/*
 * AudioDecodingBuffer
 * Buffers up encoded audio packets and provides a constant stream of sound (silence if there is no more audio to decode)
 * This works by having a buffer with N sub-buffers, each of the size of a PCM frame. When Read, this copys the buffer data into the passed array
 * and, if there are no more decoded data, calls Opus to decode the sample
 * 
 * TODO This is decoding audio data on the main thread. We should make decoding happen in a separate thread
 * TODO Use the sequence number in error correcting
 */
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Mumble {
    public class AudioDecodingBuffer
    {
        public long NumPacketsLost { get; private set; }
        /// <summary>
        /// How many samples have been decoded
        /// </summary>
        private int _decodedCount;
        /// <summary>
        /// How far along the decoded buffer, in units of numbers of samples,
        /// have currently been read
        /// </summary>
        private int _readingOffset;
        /// <summary>
        /// The index of the next sub-buffer to decode into
        /// </summary>
        private int _nextBufferToDecodeInto;
        private readonly float[][] _decodedBuffer = new float[NumDecodedSubBuffers][];
        private readonly int[] _numSamplesInBuffer = new int[NumDecodedSubBuffers];
        private long _nextSequenceToDecode;
        private readonly List<BufferPacket> _encodedBuffer = new List<BufferPacket>();
        private readonly OpusCodec _codec;
        const int NumDecodedSubBuffers = (int)(MumbleConstants.MAX_LATENCY_SECONDS * (MumbleConstants.SAMPLE_RATE / MumbleConstants.FRAME_SIZE));
        const int SubBufferSize = MumbleConstants.FRAME_SIZE * MumbleConstants.MAX_FRAMES_PER_PACKET;

        public AudioDecodingBuffer(OpusCodec codec)
        {
            _codec = codec;
        }
        public int Read(float[] buffer, int offset, int count)
        {
            //Debug.Log("We now have " + _encodedBuffer.Count + " encoded packets");
            //Debug.LogWarning("Will read");

            int readCount = 0;
            while (readCount < count)
            {
                if(_decodedCount > 0)
                    readCount += ReadFromBuffer(buffer, offset + readCount, count - readCount);
                else if (!FillBuffer())
                    break;
            }

            //Return silence if there was no data available
            if (readCount == 0)
            {
                //Debug.Log("Returning silence");
                Array.Clear(buffer, offset, count);
            }
            return readCount;
        }

        private BufferPacket? GetNextEncodedData()
        {
            lock (_encodedBuffer)
            {
                if (_encodedBuffer.Count == 0)
                    return null;

                int minIndex = 0;
                for (int i = 1; i < _encodedBuffer.Count; i++)
                    minIndex = _encodedBuffer[minIndex].Sequence < _encodedBuffer[i].Sequence ? minIndex : i;

                var packet = _encodedBuffer[minIndex];
                _encodedBuffer.RemoveAt(minIndex);

                return packet;
            }
        }

        /// <summary>
        /// Read data that has already been decoded
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private int ReadFromBuffer(float[] dst, int offset, int count)
        {
            int currentBuffer = _readingOffset / SubBufferSize;
            int numDecodedInCurrentBuffer = _decodedCount;// % _numSamplesInBuffer[currentBuffer];// SubBufferSize - _decodedCount % SubBufferSize;
            int currentBufferOffset = _numSamplesInBuffer[currentBuffer] - numDecodedInCurrentBuffer;

            //Copy as much data as we can from the buffer up to the limit
            int readCount = Math.Min(count, numDecodedInCurrentBuffer);
           
            /* 
            Debug.Log("Reading " + readCount
                + "| starting at " + currentBufferOffset
                + "| starting at overall " + _readingOffset
                + "| current buff is " + currentBuffer
                + "| into the location " + offset
                + "| with in curr buff " + numDecodedInCurrentBuffer
                + "| out of " + _decodedBuffer[currentBuffer].Length
                + "| with " + _decodedCount);
                
            if (readCount == 0)
                return 0;
            */
            Array.Copy(_decodedBuffer[currentBuffer], currentBufferOffset, dst, offset, readCount);
            _decodedCount -= readCount;
            _readingOffset += readCount;

            //If we hit the end of a subbuffer, move the offset by all the empty samples
            if (readCount == numDecodedInCurrentBuffer)
                _readingOffset += SubBufferSize - _numSamplesInBuffer[currentBuffer];

            //If we hit the end of the buffer, lap over
            if (_readingOffset == SubBufferSize * NumDecodedSubBuffers)
                _readingOffset = 0;

            return readCount;
        }

        /// <summary>
        /// Decoded data into the buffer
        /// </summary>
        /// <returns></returns>
        private bool FillBuffer()
        {
            var packet = GetNextEncodedData();
            if (!packet.HasValue)
                return false;
            //TODO Decode a null to indicate a dropped packet
            if (packet.Value.Sequence != _nextSequenceToDecode && _nextSequenceToDecode != 0)
            {
                Debug.LogWarning("dropped packet, recv: " + packet.Value.Sequence + ", expected " + _nextSequenceToDecode);
                NumPacketsLost += packet.Value.Sequence - _nextSequenceToDecode;
            }
            else
            {
                Debug.Log("decoding " + packet.Value.Sequence);
            }

            if (_decodedBuffer[_nextBufferToDecodeInto] == null)
                _decodedBuffer[_nextBufferToDecodeInto] = new float[SubBufferSize];

            int numRead = _codec.Decode(packet.Value.Data, _decodedBuffer[_nextBufferToDecodeInto]);

            if (numRead < 0)
                return false;

            _decodedCount += numRead;
            _numSamplesInBuffer[_nextBufferToDecodeInto] = numRead;
            _nextSequenceToDecode = packet.Value.Sequence + numRead / MumbleConstants.FRAME_SIZE;
            _nextBufferToDecodeInto++;
            //Make sure we don't go over our max number of buffers
            if (_nextBufferToDecodeInto == NumDecodedSubBuffers)
                _nextBufferToDecodeInto = 0;
            return true;

        }
        /// <summary>
        /// Add a new packet of encoded data
        /// </summary>
        /// <param name="sequence">Sequence number of this packet</param>
        /// <param name="data">The encoded audio packet</param>
        /// <param name="codec">The codec to use to decode this packet</param>
        public void AddEncodedPacket(long sequence, byte[] data)
        {
            /* TODO this messes up when we hit configure in the desktop mumble app. The sequence number drops to 0
            //If the next seq we expect to decode comes after this packet we've already missed our opportunity!
            if (_nextSequenceToDecode > sequence)
            {
                Debug.LogWarning("Dropping packet number: " + sequence + " we're decoding number " + _nextSequenceToDecode);
                return;
            }
            */

            lock (_encodedBuffer)
            {
                _encodedBuffer.Add(new BufferPacket
                {
                    Data = data,
                    Sequence = sequence
                });
            }
        }

        private struct BufferPacket
        {
            public byte[] Data;
            public long Sequence;
        }
    }
}