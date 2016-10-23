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
        public void SendVoiceStopSignal()
        {
            _encodingBuffer.Stop();
        }
        public void Dispose()
        {
            if(_encodingThread != null)
                _encodingThread.Abort();
            _isEncodingThreadRunning = false;
        }
        private void EncodingThreadEntry()
        {
            _isEncodingThreadRunning = true;
            while (_isEncodingThreadRunning)
            {
                try
                {
                    bool isLastPacket;
                    bool isEmpty;
                    ArraySegment<byte> packet = _encodingBuffer.Encode(_codec, out isLastPacket, out isEmpty);

                    if (isEmpty)
                    {
                        //Thread.Sleep(SleepTimeMs);
                        continue;
                    }
                    if (isLastPacket)
                        Debug.LogError("Found last packet");
                    
                    int maxSize = 480;

                    //Debug.Log("Packet count = " + packet.Count);

                    for (int currentOffset = 0; currentOffset < packet.Count;)
                    {
                        int currentBlockSize = Math.Min(packet.Count - currentOffset, maxSize);

                        byte type = (byte)4;
                        //originaly [type = codec_type_id << 5 | whistep_chanel_id]. now we can talk only to normal chanel
                        type = (byte)(type << 5);
                        byte[] sequence = Var64.writeVarint64_alternative((UInt64)sequenceIndex);


                        // First header for type & sequence length
                        //TODO we can remove this alloc if we're clever
                        byte[] packetHeader = new byte[1 + sequence.Length];
                        packetHeader[0] = type;
                        sequence.CopyTo(packetHeader, 1);

                        //Debug.Log("size should be = " + currentBlockSize);
                        //Write header to show how long the encoded data is
                        byte[] opusHeader = Var64.writeVarint64_alternative((UInt64)currentBlockSize);
                        //Mark the leftmost bit if this is the last packet
                        if (isLastPacket)
                        {
                            opusHeader[0] = (byte)(opusHeader[0] | 128);
                            Debug.LogWarning("Adding end flag");
                        }
                        byte[] packedData = new byte[packetHeader.Length + opusHeader.Length + currentBlockSize];

                        //Packet:
                        //[Header] [segment] [opus header] [packet]
                        Array.Copy(packetHeader, 0, packedData, 0, packetHeader.Length);
                        Array.Copy(opusHeader, 0, packedData, packetHeader.Length, opusHeader.Length);
                        Array.Copy(packet.Array, currentOffset, packedData, packetHeader.Length + opusHeader.Length, currentBlockSize);

                        if (MumbleClient.UseLocalLoopBack)
                        	_udpConnection.UnpackOpusVoicePacket(packedData);
                        else
                            _udpConnection.SendVoicePacket(packedData);

                        sequenceIndex++;
                        currentOffset += currentBlockSize;
                    }
                    //If we've hit a stop packet, then reset the seq number
                    if (isLastPacket)
                        sequenceIndex = 0;
                }
                catch (Exception e){
                    Debug.LogError("Error: " + e);
                }
            }
        }
    }
}
