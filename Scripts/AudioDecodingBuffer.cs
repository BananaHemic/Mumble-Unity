using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Mumble {
    /// <summary>
    /// Buffers up encoded audio packets and provides a constant stream of sound (silence if there is no more audio to decode)
    /// </summary>
    public class AudioDecodingBuffer
    {

        private int _decodedOffset;
        private int _decodedCount;
        private readonly byte[] _decodedBuffer = new byte[Constants.SAMPLE_RATE * (Constants.SAMPLE_BITS / 8) * 1];

        private long _nextSequenceToDecode;
        private readonly List<BufferPacket> _encodedBuffer = new List<BufferPacket>();

        private readonly OpusCodec _codec;

        public AudioDecodingBuffer(OpusCodec codec)
        {
            _codec = codec;   
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            //Debug.Log("We now have " + _encodedBuffer.Count + " packets");
            int readCount = 0;
            while (readCount < count)
            {
                readCount += ReadFromBuffer(buffer, offset + readCount, count - readCount);
                //Try to decode some more data into the buffer
                if (!FillBuffer())
                    break;
            }

            if (readCount == 0)
            {
                //Return silence
                //Debug.Log("Loading silence");
                
                Array.Clear(buffer, 0, buffer.Length);
                return count;
            }

            return readCount;
        }

        /// <summary>
        /// Add a new packet of encoded data
        /// </summary>
        /// <param name="sequence">Sequence number of this packet</param>
        /// <param name="data">The encoded audio packet</param>
        /// <param name="codec">The codec to use to decode this packet</param>
        public void AddEncodedPacket(long sequence, byte[] data)
        {
            //If the next seq we expect to decode comes after this packet we've already missed our opportunity!
            if (_nextSequenceToDecode > sequence)
                return;

            //Debug.Log("Adding encoded packet");

            _encodedBuffer.Add(new BufferPacket
            {
                Data = data,
                Sequence = sequence
            });
        }

        private BufferPacket? GetNextEncodedData()
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

        /// <summary>
        /// Read data that has already been decoded
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private int ReadFromBuffer(byte[] dst, int offset, int count)
        {
            //Copy as much data as we can from the buffer up to the limit
            int readCount = Math.Min(count, _decodedCount);
            Array.Copy(_decodedBuffer, _decodedOffset, dst, offset, readCount);
            _decodedCount -= readCount;
            _decodedOffset += readCount;

            //When the buffer is emptied, put the start offset back to index 0
            if (_decodedCount == 0)
                _decodedOffset = 0;

            //If the offset is nearing the end of the buffer then copy the data back to offset 0
            if ((_decodedOffset > _decodedCount) && (_decodedOffset + _decodedCount) > _decodedBuffer.Length * 0.9)
                Buffer.BlockCopy(_decodedBuffer, _decodedOffset, _decodedBuffer, 0, _decodedCount);

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

            ////todo: _nextSequenceToDecode calculation is wrong, which causes this to happen for almost every packet!
            ////Decode a null to indicate a dropped packet
            //if (packet.Value.Sequence != _nextSequenceToDecode)
            //    _codec.Decode(null);

            /*
            Debug.Log("packet data starts with "
                + " " + packet.Value.Data[0]
                + " " + packet.Value.Data[1]
                + " " + packet.Value.Data[2]
                + " " + packet.Value.Data[3]
                + " " + packet.Value.Data[4]
                + " " + packet.Value.Data[5]
                + " " + packet.Value.Data[6]
                + " " + packet.Value.Data[7]
                );
                */
            byte[] d = _codec.Decode(packet.Value.Data);
            /*
            Debug.Log("Post decode, packet starts with "
                + " " + d[0]
                + " " + d[1]
                + " " + d[2]
                + " " + d[3]
                + " " + d[4]
                + " " + d[5]
                + " " + d[6]
                + " " + d[7]
                );
                */
            _nextSequenceToDecode = packet.Value.Sequence + d.Length / Constants.FRAME_SIZE;

            Array.Copy(d, 0, _decodedBuffer, _decodedOffset, d.Length);
            _decodedCount += d.Length;
            return true;
        }

        private struct BufferPacket
        {
            public byte[] Data;
            public long Sequence;
        }
    }
}