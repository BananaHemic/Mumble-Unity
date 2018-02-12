// 
// Author: John Carruthers (johnc@frag-labs.com)
// 
// Copyright (C) 2013 John Carruthers
// 
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//  
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//  
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// 

using System;
using System.Linq;
using UnityEngine;

namespace Mumble
{
    /// <summary>
    /// Opus encoder.
    /// </summary>
    public class OpusEncoder
        : IDisposable
    {
        /// <summary>
        /// Opus encoder.
        /// </summary>
        private IntPtr _encoder;

        /// <summary>
        /// Size of each sample in bytes.
        /// </summary>
        private readonly int _sampleSize;

        /// <summary>
        /// Permitted frame sizes in ms.
        /// </summary>
        private readonly float[] _permittedFrameSizes = {
            2.5f, 5, 10,
            20, 40, 60
        };
        /// <summary>
        /// Permitted frame sizes in samples per channel.
        /// </summary>
        public int[] PermittedFrameSizes { get; private set; }

        /// <summary>
        /// Gets or sets the bitrate setting of the encoding.
        /// </summary>
        public int Bitrate
        {
            get
            {
                if (_encoder == IntPtr.Zero)
                    throw new ObjectDisposedException("OpusEncoder");
                int bitrate;
                var ret = NativeMethods.opus_encoder_ctl(_encoder, OpusCtl.GET_BITRATE_REQUEST, out bitrate);
                if (ret < 0)
                    throw new Exception("Encoder error - " + ((OpusErrors)ret));
                return bitrate;
            }
            set
            {
                if (_encoder == IntPtr.Zero)
                    throw new ObjectDisposedException("OpusEncoder");
                var ret = NativeMethods.opus_encoder_ctl(_encoder, OpusCtl.SET_BITRATE_REQUEST, out value);
                if (ret < 0)
                    throw new Exception("Encoder error - " + ((OpusErrors)ret));
            }
        }

        /// <summary>
        /// Gets or sets if Forward Error Correction encoding is enabled.
        /// </summary>
        public bool EnableForwardErrorCorrection
        {
            get
            {
                if (_encoder == IntPtr.Zero)
                    throw new ObjectDisposedException("OpusEncoder");
                int fec;
                var ret = NativeMethods.opus_encoder_ctl(_encoder, OpusCtl.GET_INBAND_FEC_REQUEST, out fec);
                if (ret < 0)
                    throw new Exception("Encoder error - " + ((OpusErrors)ret));
                return fec > 0;
            }
            set
            {
                if (_encoder == IntPtr.Zero)
                    throw new ObjectDisposedException("OpusEncoder");
                int req = Convert.ToInt32(value);
                var ret = NativeMethods.opus_encoder_ctl(_encoder, OpusCtl.SET_INBAND_FEC_REQUEST, req);
                if (ret < 0)
                    throw new Exception("Encoder error - " + ((OpusErrors)ret));
            }
        }

        /// <summary>
        /// Max number of bytes per packet
        /// from the Mumble protocol
        /// </summary>
        const int MaxPacketSize = 1020;

        private byte[] _encodedPacket = new byte[MaxPacketSize];
        
        /// <summary>
        /// Creates a new Opus encoder.
        /// </summary>
        /// <param name="srcSamplingRate">The sampling rate of the input stream.</param>
        /// <param name="srcChannelCount">The number of channels in the input stream.</param>
        public OpusEncoder(int srcSamplingRate, int srcChannelCount)
        {
            if (srcSamplingRate != 8000 &&
                srcSamplingRate != 12000 &&
                srcSamplingRate != 16000 &&
                srcSamplingRate != 24000 &&
                srcSamplingRate != 48000)
                throw new ArgumentOutOfRangeException("srcSamplingRate");
            if (srcChannelCount != 1 && srcChannelCount != 2)
                throw new ArgumentOutOfRangeException("srcChannelCount");

            OpusErrors error;
            var encoder = NativeMethods.opus_encoder_create(srcSamplingRate, srcChannelCount, OpusApplication.Voip, out error);
            if (error != OpusErrors.Ok)
            {
                throw new Exception("Exception occured while creating encoder");
            }
            _encoder = encoder;

            const int BIT_DEPTH = 16;
            _sampleSize = SampleSize(BIT_DEPTH, srcChannelCount);

            PermittedFrameSizes = new int[_permittedFrameSizes.Length];
            for (var i = 0; i < _permittedFrameSizes.Length; i++)
                PermittedFrameSizes[i] = (int)(srcSamplingRate / 1000f * _permittedFrameSizes[i]);
        }

        private static int SampleSize(int bitDepth, int channelCount)
        {
            return (bitDepth / 8) * channelCount;
        }

        ~OpusEncoder()
        {
            Dispose();
        }

        public ArraySegment<byte> Encode(float[] pcmSamples)
        {
            int size = NativeMethods.opus_encode(_encoder, pcmSamples, pcmSamples.Length, _encodedPacket);

            if (size <= 1)
            {
                Debug.LogError("Negative size in encoded packet?");
                return new ArraySegment<byte> { };
            }
            else
                return new ArraySegment<byte>(_encodedPacket, 0, size);
        }

        /// <summary>
        /// Calculates the size of a frame in bytes.
        /// </summary>
        /// <param name="frameSizeInSamples">Size of the frame in samples per channel.</param>
        /// <returns>The size of a frame in bytes.</returns>
        public int FrameSizeInBytes(int frameSizeInSamples)
        {
            return frameSizeInSamples * _sampleSize;
        }

        /// <summary>
        /// Resets the encoder to an ambient state
        /// This should be called after any discontinuties in the stream
        /// </summary>
        public void ResetState()
        {
            NativeMethods.opus_reset_encoder(_encoder);
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            if (_encoder != IntPtr.Zero)
            {
                NativeMethods.destroy_opus(_encoder);
                _encoder = IntPtr.Zero;
            }
        }
    }
}