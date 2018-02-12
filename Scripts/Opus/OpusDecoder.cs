//  
//  Author: John Carruthers (johnc@frag-labs.com)
//  
//  Copyright (C) 2013 John Carruthers
//  
//  Permission is hereby granted, free of charge, to any person obtaining
//  a copy of this software and associated documentation files (the
//  "Software"), to deal in the Software without restriction, including
//  without limitation the rights to use, copy, modify, merge, publish,
//  distribute, sublicense, and/or sell copies of the Software, and to
//  permit persons to whom the Software is furnished to do so, subject to
//  the following conditions:
//   
//  The above copyright notice and this permission notice shall be
//  included in all copies or substantial portions of the Software.
//   
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
//  EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//  MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//  NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
//  LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
//  OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
//  WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//  

using System;
using UnityEngine;

namespace Mumble
{
    /// <summary>
    /// Opus decoder.
    /// </summary>
    public class OpusDecoder: IDisposable
    {
        /// <summary>
        /// Opus decoder.
        /// </summary>
        private IntPtr _decoder;

        //private readonly int _outputSampleRate;

        private readonly int _outputChannelCount;

        /// <summary>
        /// Gets or sets if Forward Error Correction decoding is enabled.
        /// </summary>
        public bool EnableForwardErrorCorrection { get; set; }

        public OpusDecoder(int outputSampleRate, int outputChannelCount)
        {
            if (outputSampleRate != 8000 &&
                outputSampleRate != 12000 &&
                outputSampleRate != 16000 &&
                outputSampleRate != 24000 &&
                outputSampleRate != 48000)
                throw new ArgumentOutOfRangeException("outputSampleRate");
            if (outputChannelCount != 1 && outputChannelCount != 2)
                throw new ArgumentOutOfRangeException("outputChannelCount");

            OpusErrors error;
            _decoder = NativeMethods.opus_decoder_create(outputSampleRate, outputChannelCount, out error);
            if (error != OpusErrors.Ok)
                throw new Exception(string.Format("Exception occured while creating decoder, {0}", ((OpusErrors)error)));
            //_outputSampleRate = outputSampleRate;
            _outputChannelCount = outputChannelCount;
        }

        ~OpusDecoder()
        {
            Dispose();
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            if (_decoder != IntPtr.Zero)
            {
                NativeMethods.destroy_opus(_decoder);
                _decoder = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Resets the decoder to an ambient state
        /// This should be called after any discontinuties in the stream
        /// </summary>
        public void ResetState()
        {
            NativeMethods.opus_reset_decoder(_decoder); 
        }

        public int Decode(byte[] packetData, float[] floatBuffer)
        {
            return NativeMethods.opus_decode(_decoder, packetData, floatBuffer, _outputChannelCount);
        }

        public static int GetChannels(byte[] srcEncodedBuffer)
        {
            return NativeMethods.opus_packet_get_nb_channels(srcEncodedBuffer);
        }
    }
}
