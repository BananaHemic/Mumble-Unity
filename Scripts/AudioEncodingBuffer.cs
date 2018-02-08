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

        //TODO not certain on this
        public readonly ArraySegment<byte> EmptyByteSegment = new ArraySegment<byte>(new byte[0] {});

        private volatile bool _isWaitingToSendLastPacket = false;

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
                    Debug.Log("Marking last packet");
            }
        }

        public ArraySegment<byte> Encode(OpusEncoder encoder, out bool isStop, out bool isEmpty)
        {
            isStop = false;
            isEmpty = false;
            PcmArray nextPcmToSend = null;
            ArraySegment<byte> encoder_buffer;

            lock (_unencodedBuffer)
            {
                if (_unencodedBuffer.Count == 0)
                    isEmpty = true;
                else
                {
                    if (_unencodedBuffer.Count == 1 && _isWaitingToSendLastPacket)
                        isStop = true;

                    TargettedSpeech speech = _unencodedBuffer.Dequeue();
                    isStop = isStop || speech.IsStop;

                    nextPcmToSend = speech.PcmData;
                    if(nextPcmToSend != null)
                        nextPcmToSend.IsAvailable = true;

                    if (isStop)
                        _isWaitingToSendLastPacket = false;
                }
            }

            if (nextPcmToSend == null || nextPcmToSend.Pcm.Length == 0)
                isEmpty = true;

            encoder_buffer = isEmpty ? EmptyByteSegment : encoder.Encode(nextPcmToSend.Pcm);

            if (isStop)
            {
                Debug.Log("Resetting encoder state");
                encoder.ResetState();
            }
            return encoder_buffer;
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
            
            public TargettedSpeech(bool stop)
            {
                IsStop = stop;
                PcmData = null;
                Target = SpeechTarget.Normal;
                TargetId = 0;
            }
        }
    }
}
