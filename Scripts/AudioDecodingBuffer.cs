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
        private long _lastReceivedSequence;
        private readonly Queue<BufferPacket> _encodedBuffer = new Queue<BufferPacket>();
        private readonly OpusDecoder _decoder;
        const int NumDecodedSubBuffers = (int)(MumbleConstants.MAX_LATENCY_SECONDS * (MumbleConstants.SAMPLE_RATE / MumbleConstants.FRAME_SIZE));
        const int SubBufferSize = MumbleConstants.FRAME_SIZE * MumbleConstants.MAX_FRAMES_PER_PACKET * MumbleConstants.NUM_CHANNELS;
        /// <summary>
        /// How many packets go missing before we figure they were lost
        /// Due to murmur
        /// </summary>
        const long MaxMissingPackets = 25;

        public AudioDecodingBuffer()
        {
            _decoder = new OpusDecoder(MumbleConstants.SAMPLE_RATE, MumbleConstants.NUM_CHANNELS) { EnableForwardErrorCorrection = false };
        }
        public int Read(float[] buffer, int offset, int count)
        {
            /*
            lock (_encodedBuffer)
            {
                Debug.Log("We now have " + _encodedBuffer.Count + " encoded packets");
            }
            */
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

                BufferPacket packet = _encodedBuffer.Dequeue();
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
            if (_decodedBuffer[_nextBufferToDecodeInto] == null)
                _decodedBuffer[_nextBufferToDecodeInto] = new float[SubBufferSize];

            //Debug.Log("decoding " + packet.Value.Sequence + " last=" + _lastReceivedSequence + " len=" + packet.Value.Data.Length);
            if (_nextSequenceToDecode != 0)
            {
                long seqDiff = packet.Value.Sequence - _nextSequenceToDecode;

                // If new packet is VERY late, then the sequence number has probably reset
                if(seqDiff < -MaxMissingPackets)
                {
                    Debug.Log("Sequence has possibly reset diff = " + seqDiff);
                    _decoder.ResetState();
                }
                // If the packet came before we were expecting it to, but after the last packet, the sampling has probably changed
                // unless the packet is a last packet (in which case the sequence may have only increased by 1)
                else if (packet.Value.Sequence > _lastReceivedSequence && seqDiff < 0 && !packet.Value.IsLast)
                {
                    Debug.Log("Mumble sample rate may have changed");
                }
                // If the sequence number changes abruptly (which happens with push to talk)
                else if (seqDiff > MaxMissingPackets)
                {
                    Debug.Log("Mumble packet sequence changed abruptly pkt: " + packet.Value.Sequence + " last: " + _lastReceivedSequence);
                }
                // If the packet is a bit late, drop it
                else if (seqDiff < 0 && !packet.Value.IsLast)
                {
                    Debug.LogWarning("Received old packet " + packet.Value.Sequence + " expecting " + _nextSequenceToDecode);
                    return false;
                }
                // If we missed a packet, add a null packet to tell the decoder what happened
                else if (seqDiff > 0)
                {
                    Debug.LogWarning("dropped packet, recv: " + packet.Value.Sequence + ", expected " + _nextSequenceToDecode);
                    if(packet.Value.Sequence > _nextSequenceToDecode)
                    {
                        NumPacketsLost += packet.Value.Sequence - _nextSequenceToDecode;
                        _decoder.Decode(null, _decodedBuffer[_nextBufferToDecodeInto]);
                    }
                }
            }

            int numRead = 0;
            if(packet.Value.Data.Length != 0)
                numRead = _decoder.Decode(packet.Value.Data, _decodedBuffer[_nextBufferToDecodeInto]);

            if (numRead < 0)
            {
                Debug.Log("num read is < 0");
                return false;
            }

            _decodedCount += numRead;
            _numSamplesInBuffer[_nextBufferToDecodeInto] = numRead;
            //Debug.Log("numRead = " + numRead);
            _lastReceivedSequence = packet.Value.Sequence;
            if (!packet.Value.IsLast)
                _nextSequenceToDecode = packet.Value.Sequence + numRead / (MumbleConstants.FRAME_SIZE * MumbleConstants.NUM_CHANNELS);
            else
            {
                Debug.Log("Resetting decoder");
                _nextSequenceToDecode = 0;
                _decoder.ResetState();
            }
            if(numRead > 0)
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
        public void AddEncodedPacket(long sequence, byte[] data, bool isLast)
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
                if (_encodedBuffer.Count > MumbleConstants.RECEIVED_PACKET_BUFFER_SIZE)
                {
                    Debug.LogWarning("Max recv buffer size reached, dropping");
                    return;
                }

                _encodedBuffer.Enqueue(new BufferPacket
                {
                    Data = data,
                    Sequence = sequence,
                    IsLast = isLast
                });
                //Debug.Log("Count is now: " + _encodedBuffer.Count);
            }
        }

        private struct BufferPacket
        {
            public byte[] Data;
            public long Sequence;
            public bool IsLast;
        }
    }
}