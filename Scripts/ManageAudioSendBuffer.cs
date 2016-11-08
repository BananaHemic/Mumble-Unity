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
                        //Debug.LogWarning("Empty Packet");
                        continue;
                    }
                    if (isLastPacket)
                        Debug.Log("Will send last packet");

                    if (packet.Array.Length == 0 || packet.Count == 0)
                        Debug.LogError("Empty packet?");

                    //Make the header
                    byte type = (byte)4;
                    //originaly [type = codec_type_id << 5 | whistep_chanel_id]. now we can talk only to normal chanel
                    type = (byte)(type << 5);
                    byte[] sequence = Var64.writeVarint64_alternative((UInt64)sequenceIndex);

                    //Write header to show how long the encoded data is
                    byte[] opusHeader = Var64.writeVarint64_alternative((UInt64)packet.Count);
                    //Mark the leftmost bit if this is the last packet
                    if (isLastPacket)
                    {
                        opusHeader[0] = (byte)(opusHeader[0] | 128);
                        Debug.LogWarning("Adding end flag");
                    }
                    //Packet:
                    //[type/target] [sequence] [opus length header] [packet data]
                    byte[] finalPacket = new byte[1 + sequence.Length + opusHeader.Length + packet.Count];
                    finalPacket[0] = type;
                    Array.Copy(sequence, 0, finalPacket, 1, sequence.Length);
                    Array.Copy(opusHeader, 0, finalPacket, 1 + sequence.Length, opusHeader.Length);
                    Array.Copy(packet.Array, packet.Offset, finalPacket, 1 + sequence.Length + opusHeader.Length, packet.Count);

                    while (_udpConnection._isSending)
                    {
                        //Debug.Log("waiting");
                        Thread.Sleep(1);
                    }
                    Debug.Log("seq: " + sequenceIndex + " | " + finalPacket.Length);
                    _udpConnection.SendVoicePacket(finalPacket);
                    sequenceIndex += 2;
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
