using System;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using UnityEngine;
using System.IO;

namespace Mumble
{
    public class MumbleUdpConnection
    {
        private readonly IPEndPoint _host;
        private readonly UdpClient _udpClient;
        private readonly MumbleClient _mc;
        private readonly MumbleError _errorCallback;
        private CryptState _cryptState;
        private Timer _udpTimer;

        internal MumbleUdpConnection(IPEndPoint host, MumbleError errorCallback, MumbleClient mc)
        {
            _host = host;
            _errorCallback = errorCallback;
            _udpClient = new UdpClient();
            _mc = mc;
        }

        internal MumbleError ErrorCallback
        {
            get { return _errorCallback; }
        }

        internal void UpdateOcbServerNonce(byte[] serverNonce)
        {
            if(serverNonce != null)
                _cryptState.CryptSetup.server_nonce = serverNonce;
        }

        internal void Connect()
        {
            Debug.Log("Establishing UDP connection");
            _cryptState = new CryptState();
            _cryptState.CryptSetup = _mc.CryptSetup;
            _udpClient.Connect(_host);

            _udpTimer = new Timer(Constants.PING_INTERVAL);
            _udpTimer.Elapsed += RunPing;
            _udpTimer.Enabled = true;

            SendPing();
            _udpClient.BeginReceive(ReceiveUdpMessage, null);
        }

        private void RunPing(object sender, ElapsedEventArgs elapsedEventArgs)
        {
             SendPing();
        }

        private void ReceiveUdpMessage(IAsyncResult res)
        {
            IPEndPoint remoteIpEndPoint = _host;
            byte[] encrypted = _udpClient.EndReceive(res, ref remoteIpEndPoint);
            ReceiveUdpMessage(encrypted);
        }
        private void ReceiveUdpMessage(byte[] encrypted)
        {
            ProcessUdpMessage(encrypted);
            _udpClient.BeginReceive(ReceiveUdpMessage, null);
        }
        internal void ProcessUdpMessage(byte[] encrypted)
        {
            byte[] message = _cryptState.Decrypt(encrypted, encrypted.Length);

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
        }
        internal void UnpackOpusVoicePacket(byte[] plainTextMessage)
        {
            byte typeByte = plainTextMessage[0];
            int type = typeByte >> 5 & 0x7;
            int target = typeByte & 31;
            //Debug.Log("byte = " + typeByte + " type = " + type + " target = " + target);
            using (var reader = new UdpPacketReader(new MemoryStream(plainTextMessage, 1, plainTextMessage.Length - 1)))
            {
                UInt32 session;
                if(!MumbleClient.UseLocalLoopBack)
                    session = (uint)reader.ReadVarInt64();
                Int64 sequence = reader.ReadVarInt64();

                //We assume we mean OPUS
                int size = (int)reader.ReadVarInt64();
                //Debug.Log("Size " + size);
                bool isLast = (size & 8192) == 8192;
                if (isLast)
                    Debug.Log("Found last byte in seq");

                //Apply a bitmask to remove the bit that marks if this is the last packet
                size &= 0x1fff;

                //Debug.Log("Received sess: " + session);
                //Debug.Log(" seq: " + sequence + " size = " + size + " packetLen: " + plainTextMessage.Length);

                if (size == 0)
                    return;

                byte[] data = reader.ReadBytes(size);

                /*
                Debug.Log("terminating with: "
                    + data[data.Length - 6] + " "
                    + data[data.Length - 5] + " "
                    + data[data.Length - 4] + " "
                    + data[data.Length - 3] + " "
                    + data[data.Length - 2] + " "
                    + data[data.Length - 1] + " "
                    );
                    */

                if (data == null)
                    return;

                //Use session here
                _mc.GetUserAtTarget(17).ReceiveEncodedVoice(data, sequence);
            }
        }
        internal void SendPing()
        {
            ulong unixTimeStamp = (ulong) (DateTime.UtcNow.Ticks - DateTime.Parse("01/01/1970 00:00:00").Ticks);
            byte[] timeBytes = BitConverter.GetBytes(unixTimeStamp);
            var dgram = new byte[9];
            timeBytes.CopyTo(dgram, 1);
            dgram[0] = (1 << 5);
//            logger.Debug("Sending UDP ping with timestamp: " + unixTimeStamp);
            var encryptedData = _cryptState.Encrypt(dgram, timeBytes.Length + 1);
            //            var encryptedData = ocb.Encrypt(dgram, timeBytes.Length + 1);

            //Debug.Log("Sending UDP ping");
            _udpClient.Send(encryptedData, encryptedData.Length);
        }

        internal void Close()
        {
            _udpClient.Close();
            if(_udpTimer != null)
                _udpTimer.Close();
        }
        internal void SendVoicePacket(byte[] voicePacket)
        {
            //Debug.Log("Sending UDP packet! Length = " + voicePacket.Length);
            byte[] encrypted = _cryptState.Encrypt(voicePacket, voicePacket.Length);

            _udpClient.Send(encrypted, encrypted.Length);
        }
        internal byte[] GetLatestClientNonce()
        {
            return _cryptState.CryptSetup.client_nonce;
        }
    }
}
