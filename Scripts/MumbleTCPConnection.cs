using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Timers;
using MumbleProto;
using UnityEngine;
using ProtoBuf;
using Version = MumbleProto.Version;

namespace Mumble
{
    public class MumbleTcpConnection
    {
        private readonly UpdateOcbServerNonce _updateOcbServerNonce;
        private readonly MumbleError _errorCallback;
        private readonly IPEndPoint _host;
        private readonly string _hostname;

        private readonly MumbleClient _mc;
        private readonly TcpClient _tcpClient;
        private BinaryReader _reader;
        private SslStream _ssl;
        private bool _validConnection;
        private BinaryWriter _writer;
        private Timer _tcpTimer;//TODO close this on application close

        internal MumbleTcpConnection(IPEndPoint host, string hostname, UpdateOcbServerNonce updateOcbServerNonce,  MumbleError errorCallback,
            MumbleClient mc)
        {
            _host = host;
            _hostname = hostname;
            _mc = mc;
            _tcpClient = new TcpClient();
            _updateOcbServerNonce = updateOcbServerNonce;
            _errorCallback = errorCallback;
        }

        internal void StartClient(string username, string password)
        {
            ConnectViaTcp();
            uint major = 1;
            uint minor = 2;
            uint patch = 8;

            var version = new Version
            {
                release = "UnityMumble",
                version = (major << 16) | (minor << 8) | (patch),
                os = Environment.OSVersion.ToString(),
                os_version = Environment.OSVersion.VersionString,
            };
            Debug.Log("version = " + version.version);
            SendMessage(MessageType.Version, version);

            var authenticate = new Authenticate
            {
                username = username,
                password = password,
                opus = true
            };
            SendMessage(MessageType.Authenticate, authenticate);

            // Keepalive, if the Mumble server doesn't get a message 
            // for 30 seconds it will close the connection
            _tcpTimer = new Timer(Constants.PING_INTERVAL);
            _tcpTimer.Elapsed += SendPing;
            _tcpTimer.Enabled = true;
        }

        internal void SendMessage<T>(MessageType mt, T message)
        {
            lock (_ssl)
            {
                if(mt != MessageType.Ping)
                    Debug.Log("Sending " + mt + " message");
                //_writer.Write(IPAddress.HostToNetworkOrder((Int16) mt));
                //Debug.Log("Can SSL read? " + _ssl.CanRead);
                //Debug.Log("Can SSL write? " + _ssl.CanWrite);
                //Debug.Log("Is authenticated? " + _ssl.IsAuthenticated);
                //Debug.Log("Is encrypted? " + _ssl.IsEncrypted);
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

        internal void ConnectViaTcp()
        {
//            _tcpClient.BeginConnect()
            _tcpClient.Connect(_host); 
            NetworkStream networkStream = _tcpClient.GetStream();
            _ssl = new SslStream(networkStream, false, ValidateCertificate);
            _ssl.AuthenticateAsClient(_hostname);
            _reader = new BinaryReader(_ssl);
            _writer = new BinaryWriter(_ssl);

            DateTime startWait = DateTime.Now;
            while (!_ssl.IsAuthenticated)
            {
                if (DateTime.Now - startWait > TimeSpan.FromSeconds(2))
                {
//                    _logger.Error("Time out waiting for SSL authentication");
                    throw new TimeoutException("Time out waiting for SSL authentication");
                }
            }
//            _logger.Debug("TCP connection established");
        }

        private bool ValidateCertificate(object sender, X509Certificate certificate, X509Chain chain,
            SslPolicyErrors errors)
        {
            return true;
        }

        internal void ProcessTcpData()
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
                        Debug.Log("Server version: " + _mc.RemoteVersion.release);
                        break;
                    case MessageType.CryptSetup:
                        var cryptSetup = Serializer.DeserializeWithLengthPrefix<CryptSetup>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        ProcessCryptSetup(cryptSetup);
                        break;
                    case MessageType.CodecVersion:
                        _mc.CodecVersion = Serializer.DeserializeWithLengthPrefix<CodecVersion>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        break;
                    case MessageType.ChannelState:
                        _mc.ChannelState = Serializer.DeserializeWithLengthPrefix<ChannelState>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        Debug.Log("Channel state ID = " + _mc.ChannelState.channel_id);
                        break;
                    case MessageType.PermissionQuery:
                        _mc.PermissionQuery = Serializer.DeserializeWithLengthPrefix<PermissionQuery>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        Debug.Log("Permission Query = " + _mc.PermissionQuery);
                        break;
                    case MessageType.UserState:
                        //This is called for every user in the room, I don't really understand why we'd be setting the
                        //Mumble Client User State each time...
                        _mc.UserState = Serializer.DeserializeWithLengthPrefix<UserState>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        Debug.Log("User State Actor= " + _mc.UserState.actor);
                        Debug.Log("User State Session= " + _mc.UserState.session);
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
                        Debug.Log("Sever config = " + _mc.ServerConfig);
                        Debug.LogWarning("Connected!");
                        _validConnection = true; // handshake complete
                        break;
                    case MessageType.TextMessage:
                        TextMessage textMessage = Serializer.DeserializeWithLengthPrefix<TextMessage>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        Debug.Log("Text message = " + textMessage.message);
                        Debug.Log("Text actor = " + textMessage.actor);
                        Debug.Log("Text channel = " + textMessage.channel_id[0]);
                        Debug.Log("Text session Length = " + textMessage.session.Count);
                        Debug.Log("Text Tree Length = " + textMessage.tree_id.Count);
                        break;
                    case MessageType.UDPTunnel:
                        //TODO this throws a "Invalid field in source data: 0
                        var udpTunnel = Serializer.DeserializeWithLengthPrefix<UDPTunnel>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        break;
                    case MessageType.Ping:
                        var ping = Serializer.DeserializeWithLengthPrefix<MumbleProto.Ping>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
//                        _logger.Debug("Received ping: " + ping.timestamp + ", udp: " + ping.udp_packets + ", tcp:" +
//                                     ping.tcp_packets);
                        break;
                    case MessageType.Reject:
                        var reject = Serializer.DeserializeWithLengthPrefix<Reject>(_ssl,
                            PrefixStyle.Fixed32BigEndian);
                        _validConnection = false;
                        _errorCallback("Mumble server reject: " + reject.reason, true);
                        break;
                    default:
                        _errorCallback("Message type " + messageType + " not implemented", true);
                        break;
                }
            }
            catch (EndOfStreamException ex)
            {
//                _logger.Error(ex);
            }
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
            {
                _updateOcbServerNonce(cryptSetup.server_nonce);
            }
            else
            {
                SendMessage(MessageType.CryptSetup, new CryptSetup { client_nonce = _mc.GetLatestClientNonce() });

            }
        }

        internal void Close()
        {
            _ssl.Close();
            _tcpTimer.Close();
        }

        internal void SendPing(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            if (_validConnection)
            {
                var ping = new MumbleProto.Ping();
                ping.timestamp = (ulong) (DateTime.UtcNow.Ticks - DateTime.Parse("01/01/1970 00:00:00").Ticks);
//                _logger.Debug("Sending TCP ping with timestamp: " + ping.timestamp);
                SendMessage(MessageType.Ping, new MumbleProto.Ping());
            }
        }
    }
}