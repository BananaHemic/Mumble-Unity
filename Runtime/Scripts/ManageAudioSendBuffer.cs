using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Mumble
{
    public class ManageAudioSendBuffer : IDisposable
    {
        private readonly MumbleUdpConnection _udpConnection;
        private readonly AudioEncodingBuffer _encodingBuffer;
        private readonly List<PcmArray> _pcmArrays;
        private readonly MumbleClient _mumbleClient;
        private readonly AutoResetEvent _waitHandle;
        private OpusEncoder _encoder;
        private bool _isRunning;

        private Thread _encodingThread;
        private uint sequenceIndex;
        private bool _stopSendingRequested = false;
        private readonly int _maxPositionalLength;
        private int _pendingBitrate = 0;
        /// <summary>
        /// How long of a duration, in ms should there be
        /// between sending two packets. This helps
        /// ensure that fewer udp packets are dropped
        /// </summary>
        const long MinSendingElapsedMilliseconds = 5;
        /// <summary>
        /// How many pending uncompressed buffers
        /// are too many to use any sleep. This
        /// is so that the sleep never causes us
        /// to have an uncompressed buffer overflow
        /// </summary>
        const int MaxPendingBuffersForSleep = 4;

        public ManageAudioSendBuffer(MumbleUdpConnection udpConnection, MumbleClient mumbleClient, int maxPositionalLength)
        {
            _isRunning = true;
            _udpConnection = udpConnection;
            _mumbleClient = mumbleClient;
            _pcmArrays = new List<PcmArray>();
            _encodingBuffer = new AudioEncodingBuffer();
            _waitHandle = new AutoResetEvent(false);
            _maxPositionalLength = maxPositionalLength;
        }

        internal void InitForSampleRate(int sampleRate)
        {
            if (_encoder != null)
            {
                Debug.LogError("Destroying opus encoder");
                _encoder.Dispose();
                _encoder = null;
            }
            _encoder = new OpusEncoder(sampleRate, 1) { EnableForwardErrorCorrection = false };

            if (_pendingBitrate > 0)
            {
                Debug.Log("Using pending bitrate");
                SetBitrate(_pendingBitrate);
            }

            if (_encodingThread == null)
            {
                _encodingThread = new Thread(EncodingThreadEntry)
                {
                    IsBackground = true
                };
                _encodingThread.Start();
            }
        }

        public int GetBitrate()
        {
            if (_encoder == null)
                return -1;
            return _encoder.Bitrate;
        }

        public void SetBitrate(int bitrate)
        {
            if (_encoder == null)
            {
                // We'll use the provided bitrate once we've created the encoder
                _pendingBitrate = bitrate;
                return;
            }
            _encoder.Bitrate = bitrate;
        }

        ~ManageAudioSendBuffer()
        {
            Dispose();
        }

        public PcmArray GetAvailablePcmArray()
        {
            foreach (PcmArray ray in _pcmArrays)
            {
                if (ray._refCount == 0)
                {
                    ray.Ref();

                    return ray;
                }
            }
            PcmArray newArray = new(_mumbleClient.NumSamplesPerOutgoingPacket, _pcmArrays.Count, _maxPositionalLength);
            _pcmArrays.Add(newArray);

            if (_pcmArrays.Count > 10)
            {
                Debug.LogWarning(_pcmArrays.Count + " audio buffers in-use. There may be a leak");
            }

            return newArray;
        }

        public void SendVoice(PcmArray pcm, SpeechTarget target, uint targetId)
        {
            _stopSendingRequested = false;
            _encodingBuffer.Add(pcm, target, targetId);
            _waitHandle.Set();
        }

        public void SendVoiceStopSignal()
        {
            _encodingBuffer.Stop();
            _stopSendingRequested = true;
        }

        public void Dispose()
        {
            _isRunning = false;
            _waitHandle.Set();

            _encodingThread?.Abort();
            _encodingThread = null;
            _encoder?.Dispose();
            _encoder = null;
        }

        private void EncodingThreadEntry()
        {
            // Wait for an initial voice packet
            _waitHandle.WaitOne();

            bool isLastPacket = false;

            System.Diagnostics.Stopwatch stopwatch = new();
            stopwatch.Start();

            while (true)
            {
                if (!_isRunning)
                    return;

                try
                {
                    // Keep running until a stop has been requested and we've encoded the rest of the buffer
                    // Then wait for a new voice packet
                    while (_stopSendingRequested && isLastPacket)
                        _waitHandle.WaitOne();

                    if (!_isRunning)
                        return;

                    AudioEncodingBuffer.CompressedBuffer buff = _encodingBuffer.Encode(_encoder, out isLastPacket, out bool isEmpty);

                    if (isEmpty && !isLastPacket)
                    {
                        // This should not normally occur
                        Thread.Sleep(MumbleConstants.FRAME_SIZE_MS);
                        Debug.LogWarning("Empty Packet");
                        continue;
                    }

                    if (isLastPacket)
                        Debug.Log("Will send last packet");

                    ArraySegment<byte> packet = buff.EncodedData;

                    // Make the header
                    byte type = 4;
                    // Originally [type = codec_type_id << 5 | whistep_chanel_id]. now we can talk only to normal chanel
                    type = (byte)(type << 5);
                    byte[] sequence = Var64.WriteVarint64_alternative(sequenceIndex);

                    // Write header to show how long the encoded data is
                    ulong opusHeaderNum = isEmpty ? 0 : (ulong)packet.Count;
                    // Mark the leftmost bit if this is the last packet
                    if (isLastPacket)
                    {
                        opusHeaderNum += 8192;
                        Debug.Log("Adding end flag");
                    }
                    byte[] opusHeader = Var64.WriteVarint64_alternative(opusHeaderNum);
                    // Packet:
                    //[type/target] [sequence] [opus length header] [packet data]
                    byte[] finalPacket = new byte[1 + sequence.Length + opusHeader.Length + packet.Count + buff.PositionalDataLength];
                    finalPacket[0] = type;
                    int finalOffset = 1;
                    Array.Copy(sequence, 0, finalPacket, finalOffset, sequence.Length);
                    finalOffset += sequence.Length;
                    Array.Copy(opusHeader, 0, finalPacket, finalOffset, opusHeader.Length);
                    finalOffset += opusHeader.Length;
                    Array.Copy(packet.Array, packet.Offset, finalPacket, finalOffset, packet.Count);
                    finalOffset += packet.Count;
                    // Append positional data, if it exists
                    if (buff.PositionalDataLength > 0)
                        Array.Copy(buff.PositionalData, 0, finalPacket, finalOffset, buff.PositionalDataLength);

                    stopwatch.Stop();
                    long timeSinceLastSend = stopwatch.ElapsedMilliseconds;

                    if (timeSinceLastSend < MinSendingElapsedMilliseconds
                        && _encodingBuffer.GetNumUncompressedPending() < MaxPendingBuffersForSleep)
                    {
                        Thread.Sleep((int)(MinSendingElapsedMilliseconds - timeSinceLastSend));
                    }

                    _udpConnection.SendVoicePacket(finalPacket);
                    sequenceIndex += MumbleConstants.NUM_FRAMES_PER_OUTGOING_PACKET;
                    // If we've hit a stop packet, then reset the seq number
                    if (isLastPacket)
                        sequenceIndex = 0;
                    stopwatch.Reset();
                    stopwatch.Start();
                }
                catch (Exception e)
                {
                    if (e is ThreadAbortException)
                    {
                        // This is ok
                        break;
                    }
                    else
                    {
                        Debug.LogError("Error: " + e);
                    }
                }
            }
            Debug.Log("Terminated encoding thread");
        }
    }

    /// <summary>
    /// Small class to help this script re-use float arrays after their data has become encoded
    /// Obviously, it's weird to ref-count in a managed environment, but it really
    /// Does help identify leaks and makes zero-copy buffer sharing easier
    /// </summary>
    public class PcmArray
    {
        public readonly int Index;
        public float[] Pcm;
        public byte[] PositionalData;
        public int PositionalDataLength;
        internal int _refCount;

        public PcmArray(int pcmLength, int index, int maxPositionLengthBytes)
        {
            Pcm = new float[pcmLength];
            if (maxPositionLengthBytes > 0)
                PositionalData = new byte[maxPositionLengthBytes];
            Index = index;
            _refCount = 1;
        }

        public void Ref()
        {
            _refCount++;
        }

        public void UnRef()
        {
            _refCount--;
        }
    }
}
