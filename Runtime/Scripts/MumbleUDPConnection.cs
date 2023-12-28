using MumbleProto;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Timers;
using UnityEngine;

namespace Mumble
{
    public class MumbleUdpConnection
    {
        const int MaxUDPSize = 0x10000;
        private readonly IPEndPoint _host;
        private readonly UdpClient _udpClient;
        private readonly MumbleClient _mumbleClient;
        private readonly AudioDecodeThread _audioDecodeThread;
        private readonly object _sendLock = new();
        private MumbleTcpConnection _tcpConnection;
        private CryptState _cryptState;
        private System.Timers.Timer _udpTimer;
        private bool _isConnected = false;
        internal volatile int NumPacketsSent = 0;
        internal volatile int NumPacketsRecv = 0;
        internal volatile bool _useTcp = false;
        // These are used for switching to TCP audio and back. Don't rely on them for anything else
        private bool _running; // This is to signal threads to shut down safely
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
            if (serverNonce != null)
                _cryptState.CryptSetup.ServerNonce = serverNonce;
        }

        internal void Connect()
        {
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
            // Before starting our thread, set running to true
            _running = true;
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
            _recvBuffer ??= new byte[MaxUDPSize];

            EndPoint endPoint = _host;
            while (_running)
            {
                try
                {
                    // This should only happen on exit
                    if (_udpClient == null)
                        return;

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
                }
                catch (Exception ex)
                {
                    if (ex is ObjectDisposedException) { return; }
                    else if (ex is ThreadAbortException) { return; }
                    else
                        Debug.LogError("Unhandled UDP receive error: " + ex);
                }
            }
            // This probably isn't needed but go ahead and set running back to true
            // here just to ensure we're always set up for the next thread
            _running = true;
        }
        internal bool ProcessUdpMessage(byte[] encrypted, int len)
        {
            // TODO sometimes this fails and I have no idea why
            byte[] message = _cryptState.Decrypt(encrypted, len);

            if (message == null)
                return false;

            // Figure out type of message
            int type = message[0] >> 5 & 0x7;

            // If we get an OPUS audio packet, de-encode it
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

        internal void OnPing(byte[] _)
        {
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

            using var reader = new UdpPacketReader(new MemoryStream(plainTextMessage, 1, plainTextMessage.Length - 1));

            uint session = 0;

            if (!isLoopback)
                session = (uint)reader.ReadVarInt64();
            else
                session = _mumbleClient.OurUserState.Session;

            long sequence = reader.ReadVarInt64();

            // We assume we mean OPUS
            int size = (int)reader.ReadVarInt64();

            bool isLast = (size & 8192) == 8192;
            if (isLast)
                Debug.Log("Found last byte in seq");

            // Apply a bitmask to remove the bit that marks if this is the last packet
            size &= 0x1fff;

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
            if (remaining != 0)
            {
                posData = reader.ReadBytes((int)remaining);
            }
            _audioDecodeThread.AddCompressedAudio(session, data, posData, sequence, isLast);
        }

        internal void SendPing()
        {
            ulong unixTimeStamp = (ulong)(DateTime.UtcNow.Ticks - DateTime.Parse("01/01/1970 00:00:00").Ticks);
            byte[] timeBytes = BitConverter.GetBytes(unixTimeStamp);
            timeBytes.CopyTo(_sendPingBuffer, 1);
            _sendPingBuffer[0] = (1 << 5);
            var encryptedData = _cryptState.Encrypt(_sendPingBuffer, timeBytes.Length + 1);

            if (!_isConnected)
            {
                Debug.LogError("Not yet connected");
                return;
            }

            if (!_useTcp && _numPingsOutstanding >= MumbleConstants.MAX_CONSECUTIVE_MISSED_UDP_PINGS)
            {
                Debug.LogWarning("Error establishing UDP connection, will switch to TCP");
                _useTcp = true;
            }
            _numPingsOutstanding++;
            lock (_sendLock)
            {
                _udpClient.Send(encryptedData, encryptedData.Length);
            }
        }

        internal void Close()
        {
            // Signal thread that it's time to shut down
            _running = false;
            _receiveThread?.Interrupt();
            _receiveThread = null;
            _udpTimer?.Close();
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

                if (_useTcp)
                {
                    UDPTunnel udpMsg = new()
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
            }
            catch (Exception e)
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
