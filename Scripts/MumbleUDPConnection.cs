using System;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using UnityEngine;
using System.IO;
using MumbleProto;

namespace Mumble
{
    public class MumbleUdpConnection
    {
        private readonly IPEndPoint _host;
        private readonly UdpClient _udpClient;
        private readonly MumbleClient _mumbleClient;
        private MumbleTcpConnection _tcpConnection;
        private CryptState _cryptState;
        private Timer _udpTimer;
        private bool _isConnected = false;
        internal volatile bool _isSending = false;
        internal volatile int NumPacketsSent = 0;
        internal volatile int NumPacketsRecv = 0;
        internal volatile bool _useTcp = false;
        // These are used for switching to TCP audio and back. Don't rely on them for anything else
        private volatile int _numPingsOutstanding = 0;

        internal MumbleUdpConnection(IPEndPoint host, MumbleClient mumbleClient)
        {
            _host = host;
            _udpClient = new UdpClient();
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
            _cryptState = new CryptState();
            _cryptState.CryptSetup = _mumbleClient.CryptSetup;
            _udpClient.Connect(_host);
            _isConnected = true;

            _udpTimer = new Timer(MumbleConstants.PING_INTERVAL_MS);
            _udpTimer.Elapsed += RunPing;
            _udpTimer.Enabled = true;

            SendPing();
            _udpClient.BeginReceive(ReceiveUdpMessage, null);
        }

        private void RunPing(object sender, ElapsedEventArgs elapsedEventArgs)
        {
             SendPing();
        }
        private void ReceiveUdpMessage(byte[] encrypted)
        {
            ProcessUdpMessage(encrypted);
            _udpClient.BeginReceive(ReceiveUdpMessage, null);
        }
        private void ReceiveUdpMessage(IAsyncResult res)
        {
            //Debug.Log("Received message");
            IPEndPoint remoteIpEndPoint = _host;
            byte[] encrypted = _udpClient.EndReceive(res, ref remoteIpEndPoint);
            ReceiveUdpMessage(encrypted);
        }
        internal void ProcessUdpMessage(byte[] encrypted)
        {
            //Debug.Log("encrypted length: " + encrypted.Length);
            //TODO sometimes this fails and I have no idea why
            //Debug.Log(encrypted[0] + " " + encrypted[1]);
            byte[] message = _cryptState.Decrypt(encrypted, encrypted.Length);

            if (message == null)
                return;

            // figure out type of message
            int type = message[0] >> 5 & 0x7;
            //Debug.Log("UDP response received: " + Convert.ToString(message[0], 2).PadLeft(8, '0'));
            //Debug.Log("UDP response type: " + (UDPType)type);
            //Debug.Log("UDP length: " + message.Length);

            //If we get an OPUS audio packet, de-encode it
            switch ((UDPType)type)
            {
                case UDPType.Opus:
                    UnpackOpusVoicePacket(message);
                    break;
                case UDPType.Ping:
                    OnPing(message);
                    break;
                default:
                    Debug.LogError("Not implemented: " + ((UDPType)type));
                    break;
            }
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
        internal void UnpackOpusVoicePacket(byte[] plainTextMessage)
        {
            NumPacketsRecv++;
            //byte typeByte = plainTextMessage[0];
            //int target = typeByte & 31;
            //Debug.Log("len = " + plainTextMessage.Length + " typeByte = " + typeByte);

            using (var reader = new UdpPacketReader(new MemoryStream(plainTextMessage, 1, plainTextMessage.Length - 1)))
            {
                UInt32 session = 0;
                if (!_mumbleClient.UseLocalLoopBack)
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
                    Debug.LogError("empty or wrong sized packet");
                    return;
                }

                long remaining = reader.GetRemainingBytes();
                if(remaining != 0)
                {
                    Debug.LogWarning("We have " + remaining + " bytes!");
                }
                _mumbleClient.ReceiveEncodedVoice(session, data, sequence, isLast);
            }
        }
        internal void SendPing()
        {
            ulong unixTimeStamp = (ulong) (DateTime.UtcNow.Ticks - DateTime.Parse("01/01/1970 00:00:00").Ticks);
            byte[] timeBytes = BitConverter.GetBytes(unixTimeStamp);
            var dgram = new byte[9];
            timeBytes.CopyTo(dgram, 1);
            dgram[0] = (1 << 5);
            var encryptedData = _cryptState.Encrypt(dgram, timeBytes.Length + 1);

            if (!_isConnected)
            {
                Debug.LogError("Not yet connected");
                return;
            }

            while (_isSending)
                System.Threading.Thread.Sleep(1);
            _isSending = true;
            if(!_useTcp && _numPingsOutstanding >= MumbleConstants.MAX_CONSECUTIVE_MISSED_UDP_PINGS)
            {
                Debug.LogWarning("Error establishing UDP connection, will switch to TCP");
                _useTcp = true;
            }
            //Debug.Log(_numPingsSent - _numPingsReceived);
            _numPingsOutstanding++;
            _udpClient.BeginSend(encryptedData, encryptedData.Length, new AsyncCallback(OnSent), null);
        }

        internal void Close()
        {
            _udpClient.Close();
            if(_udpTimer != null)
                _udpTimer.Close();
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
                    UnpackOpusVoicePacket(voicePacket);
                //Debug.Log("Sending UDP packet! Length = " + voicePacket.Length);

                if (_useTcp)
                {
                    //Debug.Log("Using TCP!");
                    UDPTunnel udpMsg = new UDPTunnel();
                    udpMsg.Packet = voicePacket;
                    _tcpConnection.SendMessage(MessageType.UDPTunnel, udpMsg);
                    return;
                }
                else
                {
                    byte[] encrypted = _cryptState.Encrypt(voicePacket, voicePacket.Length);
                    lock (_udpClient)
                    {
                        _isSending = true;
                        _udpClient.BeginSend(encrypted, encrypted.Length, new AsyncCallback(OnSent), null);
                    }
                }
                NumPacketsSent++;
            }catch(Exception e)
            {
                Debug.LogError("Error sending packet: " + e);
            }
        }
        void OnSent(IAsyncResult result)
        {
            _isSending = false;
        }
        internal byte[] GetLatestClientNonce()
        {
            return _cryptState.CryptSetup.ClientNonce;
        }
    }
}
