/*
 * This puts data from the mics taken on the main thread
 * Then another thread pulls frame data out
 * 
 * We now assume that each mic packet placed into the buffer is an acceptable size
 */
using System;
using System.Linq;
using System.Collections.Generic;

namespace Mumble
{
    public class AudioEncodingBuffer
    {
        
        //TODO! Be careful, we need to lock every use of this guy
        private readonly Queue<TargettedSpeech> _unencodedBuffer = new Queue<TargettedSpeech>();

        public readonly ArraySegment<byte> EmptyByteSegment = new ArraySegment<byte> { };

        /// <summary>
        /// Add some raw PCM data to the buffer to send
        /// </summary>
        /// <param name="pcm"></param>
        /// <param name="target"></param>
        /// <param name="targetId"></param>
        public void Add(float[] pcm, SpeechTarget target, uint targetId)
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
                _unencodedBuffer.Enqueue(new TargettedSpeech(stop: true));
            }
        }

        public ArraySegment<byte> Encode(OpusCodec codec)
        {
            TargettedSpeech nextItemToSend;
            lock (_unencodedBuffer)
            {
                if (_unencodedBuffer.Count == 0)
                    return EmptyByteSegment;

                nextItemToSend = _unencodedBuffer.Dequeue();
            }
            return codec.Encode(nextItemToSend.Pcm);
        }

        /// <summary>
        /// PCM data targetted at a specific person
        /// </summary>
        private struct TargettedSpeech
        {
            public readonly float[] Pcm;
            public readonly SpeechTarget Target;
            public readonly uint TargetId;

            public readonly bool IsStop;

            public TargettedSpeech(float[] pcm, SpeechTarget target, uint targetId)
            {
                TargetId = targetId;
                Target = target;
                Pcm = pcm;

                IsStop = false;
            }

            public TargettedSpeech(bool stop = true)
            {
                IsStop = stop;

                Pcm = new float[0];
                Target = SpeechTarget.Normal;
                TargetId = 0;
            }
        }
    }
}
