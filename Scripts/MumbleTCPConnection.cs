using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using MumbleProto;
using UnityEngine;
using ProtoBuf;
using System.Timers;
using System.Threading;
using System.Text;
using Version = MumbleProto.Version;

namespace Mumble
{
    public class MumbleTcpConnection
    {
        private readonly UpdateOcbServerNonce _updateOcbServerNonce;
        private readonly IPEndPoint _host;
        private readonly string _hostname;

        private readonly MumbleClient _mumbleClient;
        private readonly TcpClient _tcpClient;
        private BinaryReader _reader;
        private SslStream _ssl;
        private MumbleUdpConnection _udpConnection;
        private bool _validConnection;
        private BinaryWriter _writer;
        private System.Timers.Timer _tcpTimer;
        private Thread _processThread;
        private string _username;
        private string _password;

        internal MumbleTcpConnection(IPEndPoint host, string hostname, UpdateOcbServerNonce updateOcbServerNonce,
            MumbleUdpConnection udpConnection, MumbleClient mumbleClient)
        {
            _host = host;
            _hostname = hostname;
            _mumbleClient = mumbleClient;
            _udpConnection = udpConnection;
            _tcpClient = new TcpClient();
            _updateOcbServerNonce = updateOcbServerNonce;
            
            _processThread = new Thread(ProcessTcpData)
            {
                IsBackground = true
            };
        }

        internal void StartClient(string username, string password)
        {
            _username = username;
            _password = password;
            _tcpClient.BeginConnect(_host.Address, _host.Port, new AsyncCallback(OnTcpConnected), null);
            //Debug.Log("Attempting to connect to " + _host);
        }
        private void OnTcpConnected(IAsyncResult connectionResult)
        {
            if (!_tcpClient.Connected)
            {
                Debug.LogError("Connection failed! Please confirm that you have internet access, and that the hostname is correct");
                throw new Exception("Failed to connect");
            }

            NetworkStream networkStream = _tcpClient.GetStream();
            _ssl = new SslStream(networkStream, false, ValidateCertificate);
            _ssl.AuthenticateAsClient(_hostname);
            _reader = new BinaryReader(_ssl);
            _writer = new BinaryWriter(_ssl);

            DateTime startWait = DateTime.Now;
            while (!_ssl.IsAuthenticated)
            {
                if (DateTime.Now - startWait > TimeSpan.FromSeconds(2))
                    throw new TimeoutException("Time out waiting for SSL authentication");
            }
            SendVersion();
            StartPingTimer();
        }
        private void SendVersion()
        {
            var version = new Version
            {
                Release = MumbleClient.ReleaseName,
                version = (MumbleClient.Major << 16) | (MumbleClient.Minor << 8) | (MumbleClient.Patch),
                Os = Environment.OSVersion.ToString(),
                OsVersion = Environment.OSVersion.VersionString,
            };
            SendMessage(MessageType.Version, version);
        }
        private void StartPingTimer()
        {
            // Keepalive, if the Mumble server doesn't get a message 
            // for 30 seconds it will close the connection
            _tcpTimer = new System.Timers.Timer(MumbleConstants.PING_INTERVAL_MS);
            _tcpTimer.Elapsed += SendPing;
            _tcpTimer.Enabled = true; 
            _processThread.Start();
        }

        internal void SendMessage<T>(MessageType mt, T message)
        {
            lock (_ssl)
            {
                //if(mt != MessageType.Ping && mt != MessageType.UDPTunnel)
                    //Debug.Log("Sending " + mt + " message");
                //_writer.Write(IPAddress.HostToNetworkOrder((Int16) mt));
                //Serializer.SerializeWithLengthPrefix(_ssl, message, PrefixStyle.Fixed32BigEndian);
                Int16 messageType = (Int16)mt;

                // UDP Tunnels have their own way in which they handle serialization
                if(mt == MessageType.UDPTunnel)
                {
                    UDPTunnel udpTunnel = message as UDPTunnel;
                    Int32 messageSize = (Int32)udpTunnel.Packet.Length;
                    _writer.Write(IPAddress.HostToNetworkOrder(messageType));
                    _writer.Write(IPAddress.HostToNetworkOrder(messageSize));
                    _writer.Write(udpTunnel.Packet);
                }
                else
                {
                    MemoryStream messageStream = new MemoryStream();
                    Serializer.NonGeneric.Serialize(messageStream, message);
                    Int32 messageSize = (Int32)messageStream.Length;
                    _writer.Write(IPAddress.HostToNetworkOrder(messageType));
                    _writer.Write(IPAddress.HostToNetworkOrder(messageSize));
                    messageStream.Position = 0;
                    _writer.Write(messageStream.ToArray());
                }
                
                /*
                StringBuilder sb = new StringBuilder();
                byte[] msgArray = messageStream.ToArray();
                for (int i = 0; i < msgArray.Length; i++)
                {
                    sb.Append(msgArray[i]);
                    sb.Append(",");
                }
                Debug.Log(sb.ToString());
                */
                _writer.Flush();
            }
        }

        //TODO implement actual certificate validation
        private bool ValidateCertificate(object sender, X509Certificate certificate, X509Chain chain,
            SslPolicyErrors errors)
        {
            return true;
        }

        private void ProcessTcpData()
        {
            try
            {
                var messageType = (MessageType) IPAddress.NetworkToHostOrder(_reader.ReadInt16());
                //Debug.Log("Processing data of type: " + messageType);

                switch (messageType)
                {
                    case MessageType.Version:
                        _mumbleClient.RemoteVersion = Serializer.DeserializeWithLengthPrefix<Version>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        //Debug.Log("Server version: " + _mc.RemoteVersion.release);
                        var authenticate = new Authenticate
                        {
                            Username = _username,
                            Password = _password,
                            Opus = true
                        };
                        SendMessage(MessageType.Authenticate, authenticate);
                        break;
                    case MessageType.CryptSetup:
                        var cryptSetup = Serializer.DeserializeWithLengthPrefix<CryptSetup>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        ProcessCryptSetup(cryptSetup);
                        //Debug.Log("Got crypt");
                        break;
                    case MessageType.CodecVersion:
                        _mumbleClient.CodecVersion = Serializer.DeserializeWithLengthPrefix<CodecVersion>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        //Debug.Log("Got codec version");
                        break;
                    case MessageType.ChannelState:
                        ChannelState ChannelState = Serializer.DeserializeWithLengthPrefix<ChannelState>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        /*
                        Debug.Log("Channel state Name = " + ChannelState.name);
                        Debug.Log("Channel state ID = " + ChannelState.channel_id);
                        Debug.Log("Channel state Position = " + ChannelState.position);
                        Debug.Log("Channel state Temporary = " + ChannelState.temporary);
                        Debug.Log("Channel state Parent = " + ChannelState.parent);
                        Debug.Log("Channel state Description = " + ChannelState.description);
                        */
                        _mumbleClient.AddChannel(ChannelState);
                        break;
                    case MessageType.PermissionQuery:
                        _mumbleClient.PermissionQuery = Serializer.DeserializeWithLengthPrefix<PermissionQuery>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        //Debug.Log("Permission Query = " + _mumbleClient.PermissionQuery.permissions);
                        //Debug.Log("Permission Channel = " + _mumbleClient.PermissionQuery.channel_id);
                        break;
                    case MessageType.UserState:
                        //This is called for every user in the room, including us
                        UserState user = Serializer.DeserializeWithLengthPrefix<UserState>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        
                        //Debug.Log("Name: " + user.Name);
                        //Debug.Log("Session: " + user.Session);
                        //Debug.Log("actor: " + user.Actor);
                        //Debug.Log("Chan: " + user.ChannelId);
                        //Debug.Log("ID: " + user.UserId);
                        _mumbleClient.AddUser(user);
                        break;
                    case MessageType.ServerSync:
                        //This is where we get our session Id
                        //Debug.Log("Will server sync!");
                        _mumbleClient.SetServerSync(Serializer.DeserializeWithLengthPrefix<ServerSync>(_ssl,
                            PrefixStyle.Fixed32BigEndian));
                        //Debug.Log("Server Sync Session= " + _mumbleClient.ServerSync.session);
                        _mumbleClient.ConnectionSetupFinished = true;
                        break;
                    case MessageType.ServerConfig:
                        _mumbleClient.ServerConfig = Serializer.DeserializeWithLengthPrefix<ServerConfig>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        //Debug.Log("Sever config = " + _mumbleClient.ServerConfig);
                        Debug.Log("Mumble is Connected!");
                        _validConnection = true; // handshake complete
                        break;
                    case MessageType.SuggestConfig:
                        //Contains suggested configuratio options from the server
                        //like whether to send positional data, client version, etc.
                        Serializer.DeserializeWithLengthPrefix<SuggestConfig>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        break;
                    case MessageType.TextMessage:
                        TextMessage textMessage = Serializer.DeserializeWithLengthPrefix<TextMessage>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        
                        Debug.Log("Text message = " + textMessage.Message);
                        Debug.Log("Text actor = " + textMessage.Actor);
                        //Debug.Log("Text channel = " + textMessage.channel_id[0]);
                        //Debug.Log("Text session Length = " + textMessage.Sessions.Length);
                        //Debug.Log("Text Tree Length = " + textMessage.TreeIds.Length);
                        break;
                    case MessageType.UDPTunnel:
                        var length = IPAddress.NetworkToHostOrder(_reader.ReadInt32());
                        //Debug.Log("Received UDP tunnel of length: " + length);
                        //At this point the message is already decrypted
                        _udpConnection.UnpackOpusVoicePacket(_reader.ReadBytes(length));
                        /*
                        //var udpTunnel = Serializer.DeserializeWithLengthPrefix<UDPTunnel>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        */
                        break;
                    case MessageType.Ping:
                        Serializer.DeserializeWithLengthPrefix<MumbleProto.Ping>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        break;
                    case MessageType.Reject:
                        // This is called, for example, when the max number of users has been hit
                        var reject = Serializer.DeserializeWithLengthPrefix<Reject>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        _validConnection = false;
                        Debug.LogError("Mumble server reject: " + reject.Reason);
                        // The server connection is over, so we return
                        return;
                    case MessageType.UserRemove:
                        var removal = Serializer.DeserializeWithLengthPrefix<UserRemove>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        Debug.Log("Removing " + removal.Session);
                        _mumbleClient.RemoveUser(removal.Session);
                        break;
                    case MessageType.ChannelRemove:
                        var removedChan = Serializer.DeserializeWithLengthPrefix<ChannelRemove>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        _mumbleClient.RemoveChannel(removedChan.ChannelId);
                        Debug.Log("Removing channel " + removedChan.ChannelId);
                        break;
                    case MessageType.PermissionDenied:
                        var denial = Serializer.DeserializeWithLengthPrefix<PermissionDenied>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        Debug.LogError("Permission denied with fields Name: " + denial.Name +", Type: " + denial.Type + ", Reason: " + denial.Reason);
                        break;
                    default:
                        Debug.LogError("Message type " + messageType + " not implemented");
                        break;
                }
            }
            catch (Exception ex)
            {
                if (ex is EndOfStreamException)
                    Debug.LogError("EOS Exception: " + ex);//This happens when we connect again with the same username
                //These just means the app stopped, it's ok
                else if (ex is ObjectDisposedException) { }
                else if (ex is ThreadAbortException) { }
                else
                    Debug.LogError("Unhandled error: " + ex);
                return;
            }

            //Get the next response
            ProcessTcpData();
        }

        private void ProcessCryptSetup(CryptSetup cryptSetup)
        {
            if (cryptSetup.Key != null && cryptSetup.ClientNonce != null && cryptSetup.ServerNonce != null)
            {
                _mumbleClient.CryptSetup = cryptSetup;
                SendMessage(MessageType.CryptSetup, new CryptSetup {ClientNonce = cryptSetup.ClientNonce});
                _mumbleClient.ConnectUdp();
            }
            else if(cryptSetup.ServerNonce != null)
                _updateOcbServerNonce(cryptSetup.ServerNonce);
            else
                SendMessage(MessageType.CryptSetup, new CryptSetup { ClientNonce = _mumbleClient.GetLatestClientNonce() });
        }

        internal void Close()
        {
            if(_ssl != null)
                _ssl.Close();
            if(_tcpTimer != null)
                _tcpTimer.Close();
            if(_processThread != null)
                _processThread.Abort();
            if(_reader != null)
                _reader.Close();
            if(_writer != null)
                _writer.Close();
            if(_tcpClient != null)
                _tcpClient.Close();
        }

        internal void SendPing(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            if (_validConnection)
            {
                var ping = new MumbleProto.Ping();
                ping.Timestamp = (ulong) (DateTime.UtcNow.Ticks - DateTime.Parse("01/01/1970 00:00:00").Ticks);
                //Debug.Log("Sending ping");
                SendMessage(MessageType.Ping, new MumbleProto.Ping());
            }
        }
    }
}