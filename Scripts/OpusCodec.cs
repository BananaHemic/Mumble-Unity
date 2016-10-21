using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mumble 
{
    public class OpusCodec
    {
        readonly OpusDecoder _decoder = new OpusDecoder((int)Constants.SAMPLE_RATE, Constants.NUM_CHANNELS) { EnableForwardErrorCorrection = true };
        readonly OpusEncoder _encoder = new OpusEncoder((int)Constants.SAMPLE_RATE, Constants.NUM_CHANNELS) { EnableForwardErrorCorrection = true };

        public int Decode(byte[] encodedData, float[] floatBuffer, int bufferLoadingIndex)
        {
            return _decoder.Decode(encodedData, floatBuffer, bufferLoadingIndex);
            /*
            if (encodedData == null)
            {
                _decoder.Decode(null, 0, 0, new byte[Constants.FRAME_SIZE], 0);
                return null;
            }

            int samples = OpusDecoder.GetSamples(encodedData, 0, encodedData.Length, 48000);
            if (samples < 1)
                return null;

            byte[] dst = new byte[samples * sizeof(ushort)];
            int length = _decoder.Decode(encodedData, 0, encodedData.Length, dst, 0);
            if (dst.Length != length)
                Array.Resize(ref dst, length);
            return dst;
            */
        }

        public IEnumerable<int> PermittedEncodingFrameSizes
        {
            get
            {
                return _encoder.PermittedFrameSizes;
            }
        }

        public ArraySegment<byte> Encode(float[] pcmData)
        {
            return _encoder.Encode(pcmData);
            /*
            var samples = pcm.Count / sizeof(ushort);
            var numberOfBytes = _encoder.FrameSizeInBytes(samples);

            byte[] dst = new byte[numberOfBytes];
            int encodedBytes = _encoder.Encode(pcm);

            //without it packet will have huge zero-value-tale
            Array.Resize(ref dst, encodedBytes);

            return dst;
            */
        }
    }
}
