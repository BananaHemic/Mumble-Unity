/*
 * This puts data from the mics taken on the main thread
 * Then another thread pulls frame data out
 * 
 * We now assume that each mic packet placed into the buffer is an acceptable size
 */
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mumble
{
    public class AudioEncodingBuffer
    {
        private readonly Queue<TargettedSpeech> _unencodedBuffer = new Queue<TargettedSpeech>();

        public readonly ArraySegment<byte> EmptyByteSegment = new ArraySegment<byte> { };

        private bool _isWaitingToSendLastPacket = false;

        /// <summary>
        /// Add some raw PCM data to the buffer to send
        /// </summary>
        /// <param name="pcm"></param>
        /// <param name="target"></param>
        /// <param name="targetId"></param>
        public void Add(PcmArray pcm, SpeechTarget target, uint targetId)
        {
            lock (_unencodedBuffer)
            {
                _unencodedBuffer.Enqueue(new TargettedSpeech(pcm, target, targetId));
            }
        }

        public void Stop()
        {
            lock (_unencodedBuffer)
            {
                //If we still have an item in the queue, mark the last one as last
                _isWaitingToSendLastPacket = true;
                if (_unencodedBuffer.Count == 0)
                {
                    Debug.Log("Adding stop packet");
                    _unencodedBuffer.Enqueue(new TargettedSpeech(stop: true));
                }
                else
                    Debug.LogWarning("Marking last packet");
            }
        }

        public ArraySegment<byte> Encode(OpusCodec codec, out bool isStop, out bool isEmpty)
        {
            isStop = false;
            isEmpty = false;
            PcmArray nextPcmToSend = null;

            lock (_unencodedBuffer)
            {
                if (_unencodedBuffer.Count == 0)
                    isEmpty = true;
                else
                {
                    if (_unencodedBuffer.Count == 1 && _isWaitingToSendLastPacket)
                    {
                        isStop = true;
                        _isWaitingToSendLastPacket = false;
                    }

                    TargettedSpeech speech = _unencodedBuffer.Dequeue();
                    isStop = isStop || speech.IsStop;
                    if (!isStop)
                    {
                        nextPcmToSend = speech.PcmData;
                        nextPcmToSend.IsAvailable = true;
                    }
                }
            }

            if (nextPcmToSend == null || nextPcmToSend.Pcm.Length == 0)
                isEmpty = true;

            if (isEmpty)
                return EmptyByteSegment;

            //Debug.Log("Will encode: " + nextPcmToSend.Length);
            return codec.Encode(nextPcmToSend.Pcm);
        }

        /// <summary>
        /// PCM data targetted at a specific person
        /// </summary>
        private struct TargettedSpeech
        {
            public readonly PcmArray PcmData;
            public readonly SpeechTarget Target;
            public readonly uint TargetId;

            public bool IsStop;

            public TargettedSpeech(PcmArray pcm, SpeechTarget target, uint targetId)
            {
                TargetId = targetId;
                Target = target;
                PcmData = pcm;

                IsStop = false;
            }
            
            public TargettedSpeech(bool stop = true)
            {
                IsStop = stop;
                PcmData = null;
                Target = SpeechTarget.Normal;
                TargetId = 0;
            }
        }
    }
}
