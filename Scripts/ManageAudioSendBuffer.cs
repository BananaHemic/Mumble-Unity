using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Mumble
{
    public class ManageAudioSendBuffer
    {
        private bool _isEncodingThreadRunning;
        private AudioEncodingBuffer _encodingBuffer;
        private Thread _encodingThread;
        private UInt32 sequenceIndex;
        private OpusCodec _codec;
        private MumbleUdpConnection _udpConnection;

        

        public ManageAudioSendBuffer(OpusCodec codec, MumbleUdpConnection udpConnection)
        {
            _udpConnection = udpConnection;
            _codec = codec;
            _encodingBuffer = new AudioEncodingBuffer();

            _encodingThread = new Thread(EncodingThreadEntry)
            {
                IsBackground = true
            };
        }
        public void SendVoice(ArraySegment<byte> pcm, SpeechTarget target, uint targetId)
        {
            _encodingBuffer.Add(pcm, target, targetId);

            if (!_encodingThread.IsAlive)
                _encodingThread.Start();
        }
        public void SendVoiceStop()
        {
            _encodingBuffer.Stop();
            _encodingThread.Abort();
            sequenceIndex = 0;
        }
        private void EncodingThreadEntry()
        {
            _isEncodingThreadRunning = true;
            while (_isEncodingThreadRunning)
            {
                byte[] packet = null;
                try
                {
                    packet = _encodingBuffer.Encode(_codec);
                }
                catch { }

                if (packet != null)
                {
                    int maxSize = 480;

                    //taken from JS port
                    for (int currentOffset = 0; currentOffset < packet.Length;)
                    {
                        int currentBlockSize = Math.Min(packet.Length - currentOffset, maxSize);

                        byte type = (byte)4;
                        //originaly [type = codec_type_id << 5 | whistep_chanel_id]. now we can talk only to normal chanel
                        type = (byte)(type << 5);
                        byte[] sequence = Var64.writeVarint64_alternative((UInt64)sequenceIndex);

                        // Client side voice header.
                        byte[] voiceHeader = new byte[1 + sequence.Length];
                        voiceHeader[0] = type;
                        sequence.CopyTo(voiceHeader, 1);

                        byte[] header = Var64.writeVarint64_alternative((UInt64)currentBlockSize);
                        byte[] packedData = new byte[voiceHeader.Length + header.Length + currentBlockSize];

                        //Packet:
                        //[Header] [segment] [header] [packet]
                        Array.Copy(voiceHeader, 0, packedData, 0, voiceHeader.Length);
                        Array.Copy(header, 0, packedData, voiceHeader.Length, header.Length);
                        Array.Copy(packet, currentOffset, packedData, voiceHeader.Length + header.Length, currentBlockSize);

                        FormatVoicePacketThenSend(packedData);

                        sequenceIndex++;
                        currentOffset += currentBlockSize;
                    }
                }

                //beware! can take a lot of power, because infinite loop without sleep
            }
        }
        //TODO can I remove this?
        public void FormatVoicePacketThenSend(byte[] packedData)
        {
            //TODO format (?)
            _udpConnection.SendVoicePacket(packedData);
        }
    }
}
