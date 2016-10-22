using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Mumble
{
    public class ManageAudioSendBuffer : IDisposable
    {
        const int SleepTimeMs = 10;

        private readonly OpusCodec _codec;
        private readonly MumbleUdpConnection _udpConnection;
        private readonly AudioEncodingBuffer _encodingBuffer;
        private readonly Thread _encodingThread;

        private bool _isEncodingThreadRunning;
        private UInt32 sequenceIndex;
        

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
        ~ManageAudioSendBuffer()
        {
            Dispose();
        }
        public void SendVoice(float[] pcm, SpeechTarget target, uint targetId)
        {
            _encodingBuffer.Add(pcm, target, targetId);

            if (!_encodingThread.IsAlive)
                _encodingThread.Start();
        }
        public void SendVoiceStop()
        {
            _encodingBuffer.Stop();
            sequenceIndex = 0;
        }
        public void Dispose()
        {
            if(_encodingThread != null)
                _encodingThread.Abort();
        }
        private void EncodingThreadEntry()
        {
            _isEncodingThreadRunning = true;
            while (_isEncodingThreadRunning)
            {
                try
                {
                    ArraySegment<byte> packet = _encodingBuffer.Encode(_codec);

                    if (packet == null)
                    {
                        Thread.Sleep(SleepTimeMs);
                        continue;
                    }
                    
                    int maxSize = 480;

                    //taken from JS port
                    for (int currentOffset = 0; currentOffset < packet.Count;)
                    {
                        int currentBlockSize = Math.Min(packet.Count - currentOffset, maxSize);

                        byte type = (byte)4;
                        //originaly [type = codec_type_id << 5 | whistep_chanel_id]. now we can talk only to normal chanel
                        type = (byte)(type << 5);
                        byte[] sequence = Var64.writeVarint64_alternative((UInt64)sequenceIndex);

                        // Client side voice header.
                        //TODO we can remove this alloc if we're clever
                        byte[] voiceHeader = new byte[1 + sequence.Length];
                        voiceHeader[0] = type;
                        sequence.CopyTo(voiceHeader, 1);

                        byte[] header = Var64.writeVarint64_alternative((UInt64)currentBlockSize);
                        byte[] packedData = new byte[voiceHeader.Length + header.Length + currentBlockSize];

                        //Packet:
                        //[Header] [segment] [header] [packet]
                        Array.Copy(voiceHeader, 0, packedData, 0, voiceHeader.Length);
                        Array.Copy(header, 0, packedData, voiceHeader.Length, header.Length);
                        Array.Copy(packet.Array, currentOffset + packet.Offset, packedData, voiceHeader.Length + header.Length, currentBlockSize);

                        _udpConnection.SendVoicePacket(packedData);

                        sequenceIndex++;
                        currentOffset += currentBlockSize;
                    }
                }
                catch (Exception e){
                    Debug.LogError("Error: " + e);
                }
            }
        }
    }
}
