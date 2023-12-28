using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Mumble
{
    public class AudioDecodeThread : IDisposable
    {
        private readonly MumbleClient _mumbleClient;
        private readonly AutoResetEvent _waitHandle;
        private readonly Thread _decodeThread;
        private readonly int _outputSampleRate;
        private readonly int _outputChannelCount;
        private readonly Queue<OpusDecoder> _unusedDecoders = new();
        private readonly Dictionary<uint, DecoderState> _currentDecoders = new();
        private readonly Queue<MessageData> _messageQueue = new();

        private bool _isDisposing = false;

        /// <summary>
        /// How many packets go missing before we figure they were lost
        /// Due to murmur
        /// </summary>
        const long MaxMissingPackets = 25;
        const int SubBufferSize = MumbleConstants.OUTPUT_FRAME_SIZE * MumbleConstants.MAX_FRAMES_PER_PACKET * MumbleConstants.MAX_CHANNELS;

        public AudioDecodeThread(int outputSampleRate, int outputChannelCount, MumbleClient mumbleClient)
        {
            _mumbleClient = mumbleClient;
            _waitHandle = new AutoResetEvent(false);
            _outputSampleRate = outputSampleRate;
            _outputChannelCount = outputChannelCount;
            _decodeThread = new Thread(DecodeThread);
            _decodeThread.Start();
        }

        internal void AddDecoder(uint session)
        {
            MessageData addDecoderMsg = new()
            {
                TypeOfMessage = MessageType.AllocDecoderState,
                Session = session
            };
            lock (_messageQueue)
                _messageQueue.Enqueue(addDecoderMsg);
            _waitHandle.Set();
        }

        internal void RemoveDecoder(uint session)
        {
            MessageData removeDecoderMsg = new()
            {
                TypeOfMessage = MessageType.FreeDecoder,
                Session = session
            };
            lock (_messageQueue)
                _messageQueue.Enqueue(removeDecoderMsg);
            _waitHandle.Set();
        }

        internal void AddCompressedAudio(uint session, byte[] audioData, byte[] posData, long sequence,
            bool isLast)
        {
            if (_isDisposing)
                return;

            MessageData compressed = new()
            {
                TypeOfMessage = MessageType.DecompressData,
                Session = session,
                CompressedAudio = audioData,
                PosData = posData,
                Sequence = sequence,
                IsLast = isLast
            };

            lock (_messageQueue)
                _messageQueue.Enqueue(compressed);
            _waitHandle.Set();
        }

        private void DecodeThread()
        {
            while (!_isDisposing)
            {
                _waitHandle.WaitOne();

                // Keep looping until either disposed
                // or the message queue is depleted
                while (!_isDisposing)
                {
                    try
                    {
                        MessageData messageData;
                        lock (_messageQueue)
                        {
                            if (_messageQueue.Count == 0)
                                break;
                            messageData = _messageQueue.Dequeue();
                        }

                        OpusDecoder decoder = null;
                        DecoderState decoderState;

                        switch (messageData.TypeOfMessage)
                        {
                            case MessageType.AllocDecoderState:
                                // If we receive an alloc decoder state message
                                // then we just need make an entry for it in
                                // current decoders. We don't bother assigning
                                // an actual opus decoder until we get data
                                // this is because there may be lots of users
                                // in current decoders, but only a few of them
                                // actually are sending audio
                                _currentDecoders[messageData.Session] = new DecoderState();
                                break;
                            case MessageType.FreeDecoder:
                                if (_currentDecoders.TryGetValue(messageData.Session, out decoderState))
                                {
                                    // Return the OpusDecoder
                                    if (decoderState.Decoder != null)
                                        _unusedDecoders.Enqueue(decoderState.Decoder);
                                    _currentDecoders.Remove(messageData.Session);
                                }
                                else
                                    Debug.Log("Failed to remove decoder for session: " + messageData.Session);
                                break;
                            case MessageType.DecompressData:
                                // Drop this audio, if there's no assigned decoder ready to receive it
                                if (!_currentDecoders.TryGetValue(messageData.Session, out decoderState))
                                {
                                    Debug.LogWarning("No DecoderState for session: " + messageData.Session);
                                    break;
                                }
                                // Make an OpusDecoder if there isn't one
                                if (decoderState.Decoder == null)
                                {
                                    if (_unusedDecoders.Count > 0)
                                    {
                                        decoder = _unusedDecoders.Dequeue();
                                        decoder.ResetState();
                                    }
                                    else
                                    {
                                        decoder = new OpusDecoder(_outputSampleRate, _outputChannelCount);
                                    }
                                    decoderState.Decoder = decoder;
                                }
                                DecodeAudio(messageData.Session, decoderState, messageData.CompressedAudio, messageData.PosData, messageData.Sequence,
                                    messageData.IsLast);
                                break;
                            default:
                                Debug.LogError("Message type not implemented:" + messageData.TypeOfMessage);
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Exception in decode thread: " + e.ToString());
                    }
                }
            }
        }

        private float[] GetBufferToDecodeInto()
        {
            // TODO use an allocator
            return new float[SubBufferSize];
        }

        private void DecodeAudio(uint session, DecoderState decoderState, byte[] compressedAudio,
            byte[] posData, long sequence, bool isLast)
        {
            // We tell the decoded buffer to re-evaluate whether it needs to store
            // a few packets if the previous packet was marked last, or if there
            // was an abrupt change in sequence number
            bool reevaluateInitialBuffer = decoderState.WasPrevPacketMarkedLast;

            // Account for missing packets, out-of-order packets, & abrupt sequence changes
            if (decoderState.NextSequenceToDecode != 0)
            {
                long seqDiff = sequence - decoderState.NextSequenceToDecode;

                // If new packet is VERY late, then the sequence number has probably reset
                if (seqDiff < -MaxMissingPackets)
                {
                    Debug.Log("Sequence has possibly reset diff = " + seqDiff);
                    decoderState.Decoder.ResetState();
                    reevaluateInitialBuffer = true;
                }
                // If the packet came before we were expecting it to, but after the last packet, the sampling has probably changed
                // unless the packet is a last packet (in which case the sequence may have only increased by 1)
                else if (sequence > decoderState.LastReceivedSequence && seqDiff < 0 && !isLast)
                {
                    Debug.Log("Mumble sample rate may have changed");
                }
                // If the sequence number changes abruptly (which happens with push to talk)
                else if (seqDiff > MaxMissingPackets)
                {
                    Debug.Log("Mumble packet sequence changed abruptly pkt: " + sequence + " last: " + decoderState.LastReceivedSequence);
                    reevaluateInitialBuffer = true;
                }
                // If the packet is a bit late, drop it
                else if (seqDiff < 0 && !isLast)
                {
                    Debug.LogWarning("Received old packet " + sequence + " expecting " + decoderState.NextSequenceToDecode);
                    return;
                }
                // If we missed a packet, add a null packet to tell the decoder what happened
                else if (seqDiff > 0)
                {
                    Debug.LogWarning("dropped packet, recv: " + sequence + ", expected " + decoderState.NextSequenceToDecode);
                    float[] emptyPcmBuffer = GetBufferToDecodeInto();
                    int emptySampleNumRead = decoderState.Decoder.Decode(null, emptyPcmBuffer);
                    decoderState.NextSequenceToDecode = sequence + emptySampleNumRead / ((_outputSampleRate / 100) * _outputChannelCount);

                    // Send this decoded data to the corresponding buffer
                    _mumbleClient.ReceiveDecodedVoice(session, emptyPcmBuffer, emptySampleNumRead,
                        posData, reevaluateInitialBuffer);
                    reevaluateInitialBuffer = false;
                }
            }

            float[] pcmBuffer = GetBufferToDecodeInto();
            int numRead = 0;
            if (compressedAudio.Length != 0)
            {
                numRead = decoderState.Decoder.Decode(compressedAudio, pcmBuffer);
                // Send this decoded data to the corresponding buffer
                _mumbleClient.ReceiveDecodedVoice(session, pcmBuffer, numRead, posData,
                    reevaluateInitialBuffer);
            }

            if (numRead < 0)
            {
                Debug.LogError("num read is < 0");
                return;
            }

            decoderState.WasPrevPacketMarkedLast = isLast;
            decoderState.LastReceivedSequence = sequence;
            if (!isLast)
                decoderState.NextSequenceToDecode = sequence + numRead / ((_outputSampleRate / 100) * _outputChannelCount);
            else
            {
                Debug.Log("Resetting #" + session + " decoder");
                decoderState.NextSequenceToDecode = 0;

                decoderState.Decoder.ResetState();
            }
        }

        ~AudioDecodeThread()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_isDisposing)
                return;
            _isDisposing = true;
            _waitHandle.Set();
            _decodeThread.Join();
        }

        private enum MessageType
        {
            /// <summary>
            /// Signal that this is a request to
            /// decode some audio
            /// </summary>
            DecompressData,
            /// <summary>
            /// Signal that we need a new decoder
            /// for the given session
            /// </summary>
            AllocDecoderState,
            /// <summary>
            /// Signal that a certain decoder
            /// is not needed at the moment,
            /// and can be pooled/freed
            /// </summary>
            FreeDecoder
        }

        private struct MessageData
        {
            public MessageType TypeOfMessage;
            public uint Session;

            // Used only for CompressedData message
            public byte[] CompressedAudio;
            public byte[] PosData;
            public long Sequence;
            public bool IsLast;
        }

        private class DecoderState
        {
            // May be null
            public OpusDecoder Decoder;
            public long NextSequenceToDecode;
            public long LastReceivedSequence;
            public bool WasPrevPacketMarkedLast;
        }
    }
}
