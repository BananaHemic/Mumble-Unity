using System;
using System.Collections.Generic;

namespace Mumble
{
    /// <summary>
    /// Decoding buffers include an opus decoder, which can be expensive to create
    /// If done frequently. So we have a pool of Decoding buffers that we re-use.
    /// </summary>
    public class DecodingBufferPool : IDisposable
    {
        private readonly Stack<DecodedAudioBuffer> _audioDecodingBuffers = new();
        private readonly AudioDecodeThread _audioDecodeThread;

        public DecodingBufferPool(AudioDecodeThread audioDecodeThread)
        {
            _audioDecodeThread = audioDecodeThread;
        }

        public DecodedAudioBuffer GetDecodingBuffer()
        {
            DecodedAudioBuffer decodingBuffer;
            if (_audioDecodingBuffers.Count != 0)
                decodingBuffer = _audioDecodingBuffers.Pop();
            else
                decodingBuffer = new DecodedAudioBuffer(_audioDecodeThread);
            return decodingBuffer;
        }

        public void ReturnDecodingBuffer(DecodedAudioBuffer decodingBuffer)
        {
            decodingBuffer.Reset();
            _audioDecodingBuffers.Push(decodingBuffer);
        }

        // Dispose of all buffers that are currently in use
        public void Dispose()
        {
            while (_audioDecodingBuffers.Count != 0)
            {
                DecodedAudioBuffer decodingBuffer = _audioDecodingBuffers.Pop();
                decodingBuffer.Dispose();
            }
        }
    }
}