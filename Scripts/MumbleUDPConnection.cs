using System;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using UnityEngine;
using System.IO;
using MumbleProto;
using System.Threading;

namespace Mumble
{
    public class MumbleUdpConnection
    {
        const int MaxUDPSize = 0x10000;
        private readonly IPEndPoint _host;
        private readonly UdpClient _udpClient;
        private readonly MumbleClient _mumbleClient;
        private readonly AudioDecodeThread _audioDecodeThread;
        private readonly System.Object _sendLock = new System.Object();
        private MumbleTcpConnection _tcpConnection;
        private CryptState _cryptState;
        private System.Timers.Timer _udpTimer;
        private bool _isConnected = false;
        internal volatile int NumPacketsSent = 0;
        internal volatile int NumPacketsRecv = 0;
        internal volatile bool _useTcp = false;
        // These are used for switching to TCP audio and back. Don't rely on them for anything else
        private volatile int _numPingsOutstanding = 0;
        private Thread _receiveThread;
        private byte[] _recvBuffer;
        private readonly byte[] _sendPingBuffer = new byte[9];

        internal MumbleUdpConnection(IPEndPoint host, AudioDecodeThread audioDecodeThread, MumbleClient mumbleClient)
        {
            _host = host;
            _udpClient = new UdpClient();
            _audioDecodeThread = audioDecodeThread;
            _mumbleClient = mumbleClient;
        }
        internal void SetTcpConnection(MumbleTcpConnection tcpConnection)
        {
            _tcpConnection = tcpConnection;
        }
        internal void UpdateOcbServerNonce(byte[] serverNonce)
        {
            if(serverNonce != null)
                _cryptState.CryptSetup.ServerNonce = serverNonce;
        }

        internal void Connect()
        {
            //Debug.Log("Establishing UDP connection");
            _cryptState = new CryptState
            {
                CryptSetup = _mumbleClient.CryptSetup
            };
            _udpClient.Connect(_host);
            // I believe that I need to enable dontfragment in order to make
            // sure that all packets received are received as discreet datagrams
            _udpClient.DontFragment = true;

            _isConnected = true;

            _udpTimer = new System.Timers.Timer(MumbleConstants.PING_INTERVAL_MS);
            _udpTimer.Elapsed += RunPing;
            _udpTimer.Enabled = true;

            SendPing();
            _receiveThread = new Thread(ReceiveUDP)
            {
                IsBackground = true
            };
            _receiveThread.Start();
        }

        private void RunPing(object sender, ElapsedEventArgs elapsedEventArgs)
        {
             SendPing();
        }
        private void ReceiveUDP()
        {
            int prevPacketSize = 0;
            if (_recvBuffer == null)
                _recvBuffer = new byte[MaxUDPSize];

            EndPoint endPoint = (EndPoint)_host;
            while (true)
            {
                try
                {
                    // This should only happen on exit
                    if (_udpClient == null)
                        return;
                    //IPEndPoint remoteIpEndPoint = _host;
                    //byte[] encrypted = _udpClient.Receive(ref remoteIpEndPoint);
                    //int readLen = encrypted.Length;

                    // We receive the data into a pre-allocated buffer to avoid
                    // needless allocations
                    byte[] encrypted;
                    int readLen = _udpClient.Client.ReceiveFrom(_recvBuffer, ref endPoint);
                    encrypted = _recvBuffer;

                    bool didProcess = ProcessUdpMessage(encrypted, readLen);
                    if (!didProcess)
                    {
                        Debug.LogError("Failed decrypt of: " + readLen + " bytes. exclusive: "
                            + _udpClient.ExclusiveAddressUse
                            + " ttl:" + _udpClient.Ttl
                            + " avail: " + _udpClient.Available
                            + " prev pkt size:" + prevPacketSize);
                    }
                    prevPacketSize = readLen;
                }catch(Exception ex)
                {
                    if (ex is ObjectDisposedException) { }
                    else if (ex is ThreadAbortException) { }
                    else
                        Debug.LogError("Unhandled UDP receive error: " + ex);
                }
            }
        }
        internal bool ProcessUdpMessage(byte[] encrypted, int len)
        {
            //Debug.Log("encrypted length: " + len);
            //TODO sometimes this fails and I have no idea why
            //Debug.Log(encrypted[0] + " " + encrypted[1]);
            byte[] message = _cryptState.Decrypt(encrypted, len);

            if (message == null)
                return false;

            // figure out type of message
            int type = message[0] >> 5 & 0x7;
            //Debug.Log("UDP response received: " + Convert.ToString(message[0], 2).PadLeft(8, '0'));
            //Debug.Log("UDP response type: " + (UDPType)type);
            //Debug.Log("UDP length: " + message.Length);

            //If we get an OPUS audio packet, de-encode it
            switch ((UDPType)type)
            {
                case UDPType.Opus:
                    UnpackOpusVoicePacket(message, false);
                    break;
                case UDPType.Ping:
                    OnPing(message);
                    break;
                default:
                    Debug.LogError("Not implemented: " + ((UDPType)type) + " #" + type);
                    return false;
            }
            return true;
        }
        internal void OnPing(byte[] message)
        {
            //Debug.Log("Would process ping");
            _numPingsOutstanding = 0;
            // If we received a ping, that means that UDP is working
            if (_useTcp)
            {
                Debug.Log("Switching back to UDP");
                _useTcp = false;
            }
        }
        internal void UnpackOpusVoicePacket(byte[] plainTextMessage, bool isLoopback)
        {
            NumPacketsRecv++;
            //byte typeByte = plainTextMessage[0];
            //int target = typeByte & 31;
            //Debug.Log("len = " + plainTextMessage.Length + " typeByte = " + typeByte);

            using (var reader = new UdpPacketReader(new MemoryStream(plainTextMessage, 1, plainTextMessage.Length - 1)))
            {
                UInt32 session = 0;
                if (!isLoopback)
                    session = (uint)reader.ReadVarInt64();
                else
                    session = _mumbleClient.OurUserState.Session;

                Int64 sequence = reader.ReadVarInt64();

                //We assume we mean OPUS
                int size = (int)reader.ReadVarInt64();
                //Debug.Log("Seq = " + sequence + " Ses: " + session + " Size " + size + " type= " + typeByte + " tar= " + target);
                bool isLast = (size & 8192) == 8192;
                if (isLast)
                    Debug.Log("Found last byte in seq");

                //Apply a bitmask to remove the bit that marks if this is the last packet
                size &= 0x1fff;

                //Debug.Log("Received sess: " + session);
                //Debug.Log(" seq: " + sequence + " size = " + size + " packetLen: " + plainTextMessage.Length);

                byte[] data = (size != 0) ? reader.ReadBytes(size) : new byte[0];
                
                if (data == null || data.Length != size)
                {
                    Debug.LogError("empty or wrong sized packet. Recv: " + (data != null ? data.Length.ToString() : "null")
                        + " expected: " + size + " plain len: " + plainTextMessage.Length
                        + " seq: " + sequence + " isLoop: " + isLoopback);
                    return;
                }

                // All remaining bytes are assumed to be positional data
                byte[] posData = null;
                long remaining = reader.GetRemainingBytes();
                if(remaining != 0)
                {
                    //Debug.LogWarning("We have " + remaining + " bytes!");
                    posData = reader.ReadBytes((int)remaining);
                }
                //_mumbleClient.ReceiveEncodedVoice(session, data, sequence, isLast);
                _audioDecodeThread.AddCompressedAudio(session, data, posData, sequence, isLast);
            }
        }
        internal void SendPing()
        {
            ulong unixTimeStamp = (ulong) (DateTime.UtcNow.Ticks - DateTime.Parse("01/01/1970 00:00:00").Ticks);
            byte[] timeBytes = BitConverter.GetBytes(unixTimeStamp);
            timeBytes.CopyTo(_sendPingBuffer, 1);
            _sendPingBuffer[0] = (1 << 5);
            var encryptedData = _cryptState.Encrypt(_sendPingBuffer, timeBytes.Length + 1);

            if (!_isConnected)
            {
                Debug.LogError("Not yet connected");
                return;
            }

            if(!_useTcp && _numPingsOutstanding >= MumbleConstants.MAX_CONSECUTIVE_MISSED_UDP_PINGS)
            {
                Debug.LogWarning("Error establishing UDP connection, will switch to TCP");
                _useTcp = true;
            }
            //Debug.Log(_numPingsSent - _numPingsReceived);
            _numPingsOutstanding++;
            lock (_sendLock)
            {
                _udpClient.Send(encryptedData, encryptedData.Length);
            }
        }

        internal void Close()
        {
            if (_receiveThread != null)
                _receiveThread.Abort();
            _receiveThread = null;
            if(_udpTimer != null)
                _udpTimer.Close();
            _udpTimer = null;
            _udpClient.Close();
        }
        internal void SendVoicePacket(byte[] voicePacket)
        {
            if (!_isConnected)
            {
                Debug.LogError("Not yet connected");
                return;
            }
            try
            {
                if (_mumbleClient.UseLocalLoopBack)
                    UnpackOpusVoicePacket(voicePacket, true);
                //Debug.Log("Sending UDP packet! Length = " + voicePacket.Length);

                if (_useTcp)
                {
                    //Debug.Log("Using TCP!");
                    UDPTunnel udpMsg = new UDPTunnel
                    {
                        Packet = voicePacket
                    };
                    _tcpConnection.SendMessage(MessageType.UDPTunnel, udpMsg);
                    return;
                }
                else
                {
                    byte[] encrypted = _cryptState.Encrypt(voicePacket, voicePacket.Length);
                    lock (_sendLock)
                    {
                        _udpClient.Send(encrypted, encrypted.Length);
                    }
                }
                NumPacketsSent++;
            }catch(Exception e)
            {
                Debug.LogError("Error sending packet: " + e);
            }
        }
        internal byte[] GetLatestClientNonce()
        {
            return _cryptState.CryptSetup.ClientNonce;
        }
    }
}
