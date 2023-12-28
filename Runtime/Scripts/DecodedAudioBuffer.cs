/*
 * AudioDecodingBuffer
 * Receives decoded audio buffers, and copies them into the
 * array passed via Read()
 */
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Mumble
{
    public class DecodedAudioBuffer : IDisposable
    {
        public long NumPacketsLost { get; private set; }
        public bool HasFilledInitialBuffer { get; private set; }
        /// <summary>
        /// How many samples have been decoded
        /// </summary>
        private int _decodedCount;
        private DecodedPacket _currentPacket;
        /// <summary>
        /// Name of the speaker being decoded
        /// Only used for debugging
        /// </summary>
        private string _name;
        private uint _session;

        /// <summary>
        /// The audio DSP time when we last dequeued a buffer
        /// </summary>
        private double _lastBufferTime;
        private byte[] _previousPosData;
        private byte[] _nextPosData;
        private readonly object _posLock = new();

        private readonly AudioDecodeThread _audioDecodeThread;
        private readonly object _bufferLock = new();
        private readonly Queue<DecodedPacket> _decodedBuffer = new();

        /// <summary>
        /// How many incoming packets to buffer before audio begins to be played
        /// Higher values increase stability and latency
        /// </summary>
        const int InitialSampleBuffer = 3;

        public DecodedAudioBuffer(AudioDecodeThread audioDecodeThread)
        {
            _audioDecodeThread = audioDecodeThread;
        }

        public void Init(string name, uint session)
        {
            Debug.Log("Init decoding buffer for: " + name);
            _name = name;
            _session = session;
            _audioDecodeThread.AddDecoder(_session);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // Don't send audio until we've filled our initial buffer of packets
            if (!HasFilledInitialBuffer)
            {
                Array.Clear(buffer, offset, count);
                return 0;
            }

            int readCount = 0;
            while (readCount < count && _decodedCount > 0)
                readCount += ReadFromBuffer(buffer, offset + readCount, count - readCount);

            // Return silence if there was no data available
            if (readCount == 0)
            {
                Array.Clear(buffer, offset, count);
            }
            else if (readCount < count)
            {
                Array.Clear(buffer, offset + readCount, count - readCount);
            }

            return readCount;
        }

        public void GetPreviousNextPositionData(out byte[] previousPos, out byte[] nextPos,
            out double previousAudioDSP)
        {
            lock (_posLock)
            {
                previousPos = _previousPosData;
                nextPos = _nextPosData;
                previousAudioDSP = _lastBufferTime;
            }
        }

        /// <summary>
        /// Read data that has already been decoded
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="dstOffset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private int ReadFromBuffer(float[] dst, int dstOffset, int count)
        {
            // Get the next DecodedPacket to use
            int numInSample = _currentPacket.PcmLength - _currentPacket.ReadOffset;
            if (numInSample == 0)
            {
                lock (_bufferLock)
                {
                    if (_decodedBuffer.Count == 0)
                    {
                        Debug.LogError("No available decode buffers!");
                        return 0;
                    }
                    _currentPacket = _decodedBuffer.Dequeue();

                    // If we have a packet, let's update the positions
                    lock (_posLock)
                    {
                        _lastBufferTime = AudioSettings.dspTime;
                        _previousPosData = _currentPacket.PosData;
                        _nextPosData = null;
                        // Try to load the next buffer
                        if (_decodedBuffer.Count > 0)
                            _nextPosData = _decodedBuffer.Peek().PosData;
                    }
                    numInSample = _currentPacket.PcmLength - _currentPacket.ReadOffset;
                }
            }

            int readCount = Math.Min(numInSample, count);
            Array.Copy(_currentPacket.PcmData, _currentPacket.ReadOffset, dst, dstOffset, readCount);

            Interlocked.Add(ref _decodedCount, -readCount);
            _currentPacket.ReadOffset += readCount;
            return readCount;
        }

        internal void AddDecodedAudio(float[] pcmData, int pcmLength, byte[] posData, bool reevaluateInitialBuffer)
        {
            DecodedPacket decodedPacket = new()
            {
                PcmData = pcmData,
                PosData = posData,
                PcmLength = pcmLength,
                ReadOffset = 0
            };

            int count = 0;
            lock (_bufferLock)
            {
                count = _decodedBuffer.Count;
                if (count > MumbleConstants.RECEIVED_PACKET_BUFFER_SIZE)
                {
                    // TODO this seems to happen at times
                    Debug.LogWarning("Max recv buffer size reached, dropping for user " + _name);
                }
                else
                {
                    _decodedBuffer.Enqueue(decodedPacket);
                    Interlocked.Add(ref _decodedCount, pcmLength);

                    // this is set if the previous received packet was a last packet
                    // or if there was an abrupt change in sequence number
                    if (reevaluateInitialBuffer)
                        HasFilledInitialBuffer = false;

                    if (!HasFilledInitialBuffer && (count + 1 >= InitialSampleBuffer))
                        HasFilledInitialBuffer = true;
                }
            }

            // Make sure the next position data is loaded
            lock (_posLock)
            {
                _nextPosData ??= posData;
            }
        }

        public void Reset()
        {
            lock (_bufferLock)
            {
                _name = null;
                if (_session != 0)
                    _audioDecodeThread.RemoveDecoder(_session);
                NumPacketsLost = 0;
                HasFilledInitialBuffer = false;
                _decodedCount = 0;
                _decodedBuffer.Clear();
                _currentPacket = new DecodedPacket { };
                _session = 0;
            }
            lock (_posLock)
            {
                _previousPosData = null;
                _nextPosData = null;
                _lastBufferTime = 0;
            }
        }

        public void Dispose()
        {
        }

        private struct DecodedPacket
        {
            public float[] PcmData;
            public byte[] PosData;
            public int PcmLength;
            public int ReadOffset;
        }
    }
}