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
using Version = MumbleProto.Version;

namespace Mumble
{
    public class MumbleTcpConnection
    {
        private readonly UpdateOcbServerNonce _updateOcbServerNonce;
        private readonly IPEndPoint _host;
        private readonly string _hostname;

        private readonly MumbleClient _mc;
        private readonly TcpClient _tcpClient;
        private BinaryReader _reader;
        private SslStream _ssl;
        private MumbleUdpConnection _muc;
        private bool _validConnection;
        private BinaryWriter _writer;
        private System.Timers.Timer _tcpTimer;
        private Thread _processThread;
        private string _username;
        private string _password;

        internal MumbleTcpConnection(IPEndPoint host, string hostname, UpdateOcbServerNonce updateOcbServerNonce,
            MumbleUdpConnection muc, MumbleClient mc)
        {
            _host = host;
            _hostname = hostname;
            _mc = mc;
            _muc = muc;
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
        }
        private void OnTcpConnected(IAsyncResult connectionResult)
        {
            if (!_tcpClient.Connected)
                throw new Exception("Failed to connect");

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
                release = MumbleClient.ReleaseName,
                version = (MumbleClient.Major << 16) | (MumbleClient.Minor << 8) | (MumbleClient.Patch),
                os = Environment.OSVersion.ToString(),
                os_version = Environment.OSVersion.VersionString,
            };
            SendMessage(MessageType.Version, version);
        }
        private void StartPingTimer()
        {
            // Keepalive, if the Mumble server doesn't get a message 
            // for 30 seconds it will close the connection
            _tcpTimer = new System.Timers.Timer(Constants.PING_INTERVAL);
            _tcpTimer.Elapsed += SendPing;
            _tcpTimer.Enabled = true; 
            _processThread.Start();
        }

        internal void SendMessage<T>(MessageType mt, T message)
        {
            lock (_ssl)
            {
                if(mt != MessageType.Ping)
                Debug.Log("Sending " + mt + " message");
                //_writer.Write(IPAddress.HostToNetworkOrder((Int16) mt));
                //Serializer.SerializeWithLengthPrefix(_ssl, message, PrefixStyle.Fixed32BigEndian);

                if (mt == MessageType.TextMessage && message is TextMessage)
                {
                    TextMessage txt = (message as TextMessage);
                    Debug.Log("Will print: " + txt.message);
                    Debug.Log("From: " + txt.actor);
                    Debug.Log("Sessions length  =" + txt.session.Count);
                    foreach(uint ses in txt.session)
                        Debug.Log("Session: " + ses);
                    foreach(uint chan in txt.channel_id)
                        Debug.Log("Channel ID = " + chan);
                    foreach(uint tree in txt.tree_id)
                        Debug.Log("tree = " + tree);
                }

                MemoryStream messageStream = new MemoryStream();
                Serializer.NonGeneric.Serialize(messageStream, message);
                Int16 messageType = (Int16)mt;
                Int32 messageSize = (Int32)messageStream.Length;
                _writer.Write(IPAddress.HostToNetworkOrder(messageType));
                _writer.Write(IPAddress.HostToNetworkOrder(messageSize));
                messageStream.Position = 0;
                _writer.Write(messageStream.ToArray());
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
                Debug.Log("Processing data of type: " + messageType);

                switch (messageType)
                {
                    case MessageType.Version:
                        _mc.RemoteVersion = Serializer.DeserializeWithLengthPrefix<Version>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        //Debug.Log("Server version: " + _mc.RemoteVersion.release);
                        var authenticate = new Authenticate
                        {
                            username = _username,
                            password = _password,
                            opus = true
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
                        _mc.CodecVersion = Serializer.DeserializeWithLengthPrefix<CodecVersion>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        //Debug.Log("Got codec version");
                        break;
                    case MessageType.ChannelState:
                        _mc.ChannelState = Serializer.DeserializeWithLengthPrefix<ChannelState>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        //Debug.Log("Channel state ID = " + _mc.ChannelState.channel_id);
                        break;
                    case MessageType.PermissionQuery:
                        _mc.PermissionQuery = Serializer.DeserializeWithLengthPrefix<PermissionQuery>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        //Debug.Log("Permission Query = " + _mc.PermissionQuery);
                        break;
                    case MessageType.UserState:
                        //This is called for every user in the room
                        //TODO add support for multiple users
                        _mc.OurUserState = Serializer.DeserializeWithLengthPrefix<UserState>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        Debug.Log("User State Actor= " + _mc.OurUserState.actor);
                        Debug.Log("User State Session= " + _mc.OurUserState.session);
                        Debug.Log("User State User ID= " + _mc.OurUserState.user_id);
                        Debug.Log("User State User ID= " + _mc.OurUserState.plugin_identity);
                        break;
                    case MessageType.ServerSync:
                        _mc.ServerSync = Serializer.DeserializeWithLengthPrefix<ServerSync>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        Debug.Log("Server Sync Session= " + _mc.ServerSync.session);
                        _mc.ConnectionSetupFinished = true;
                        break;
                    case MessageType.ServerConfig:
                        _mc.ServerConfig = Serializer.DeserializeWithLengthPrefix<ServerConfig>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        //Debug.Log("Sever config = " + _mc.ServerConfig);
                        Debug.LogWarning("Connected!");
                        _validConnection = true; // handshake complete
                        break;
                    case MessageType.SuggestConfig:
                        //Contains suggested configuratio options from the server
                        //like whether to send positional data, client version, etc.
                        var config = Serializer.DeserializeWithLengthPrefix<SuggestConfig>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        break;
                    case MessageType.TextMessage:
                        TextMessage textMessage = Serializer.DeserializeWithLengthPrefix<TextMessage>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        
                        Debug.Log("Text message = " + textMessage.message);
                        Debug.Log("Text actor = " + textMessage.actor);
                        //Debug.Log("Text channel = " + textMessage.channel_id[0]);
                        Debug.Log("Text session Length = " + textMessage.session.Count);
                        Debug.Log("Text Tree Length = " + textMessage.tree_id.Count);
                        break;
                    case MessageType.UDPTunnel:
                        var length = IPAddress.NetworkToHostOrder(_reader.ReadInt32());
                        Debug.Log("Received UDP tunnel of length: " + length);
                        //At this point the message is already decrypted
                        _muc.UnpackOpusVoicePacket(_reader.ReadBytes(length));
                        /*
                        //var udpTunnel = Serializer.DeserializeWithLengthPrefix<UDPTunnel>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        */
                        break;
                    case MessageType.Ping:
                        var ping = Serializer.DeserializeWithLengthPrefix<MumbleProto.Ping>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        break;
                    case MessageType.Reject:
                        var reject = Serializer.DeserializeWithLengthPrefix<Reject>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        _validConnection = false;
                        Debug.LogError("Mumble server reject: " + reject.reason);
                        break;
                    case MessageType.UserRemove:
                        var removal = Serializer.DeserializeWithLengthPrefix<UserRemove>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        Debug.Log("Removing " + removal.session);
                        _mc.RemoveUser(removal.session);
                        break;
                    default:
                        Debug.LogError("Message type " + messageType + " not implemented");
                        break;
                }
            }
            catch (Exception ex)
            {
                if(ex is EndOfStreamException)
                    Debug.LogError("EOS Exception: " + ex);//This happens when we connect again with the same username
                else
                    Debug.LogError("Unhandled error: " + ex);
                return;
            }

            //Get the next response
            ProcessTcpData();
        }

        private void ProcessCryptSetup(CryptSetup cryptSetup)
        {
            if (cryptSetup.key != null && cryptSetup.client_nonce != null && cryptSetup.server_nonce != null)
            {
                _mc.CryptSetup = cryptSetup;
                SendMessage(MessageType.CryptSetup, new CryptSetup {client_nonce = cryptSetup.client_nonce});
                _mc.ConnectUdp();
            }
            else if(cryptSetup.server_nonce != null)
                _updateOcbServerNonce(cryptSetup.server_nonce);
            else
                SendMessage(MessageType.CryptSetup, new CryptSetup { client_nonce = _mc.GetLatestClientNonce() });
        }

        internal void Close()
        {
            _ssl.Close();
            _tcpTimer.Close();
            _processThread.Abort();
            _reader.Close();
            _writer.Close();
            _tcpClient.Close();
        }

        internal void SendPing(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            if (_validConnection)
            {
                var ping = new MumbleProto.Ping();
                ping.timestamp = (ulong) (DateTime.UtcNow.Ticks - DateTime.Parse("01/01/1970 00:00:00").Ticks);
                //Debug.Log("Sending ping");
                SendMessage(MessageType.Ping, new MumbleProto.Ping());
            }
        }
    }
}