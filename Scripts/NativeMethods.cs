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
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Mumble
{
    /// <summary>
    /// Wraps the Opus API.
    /// </summary>
    internal class NativeMethods
    {
#if UNITY_IPHONE && !UNITY_EDITOR
        const string pluginName = "__Internal";
#else
        const string pluginName = "opus-1.3";
#endif

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int opus_encoder_get_size(int numChannels);

		[DllImport(pluginName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int opus_decoder_get_size(int numChannels);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern OpusErrors opus_encoder_init(IntPtr encoder, int sampleRate, int channelCount, int application);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern OpusErrors opus_decoder_init(IntPtr decoder, int sampleRate, int channelCount);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int opus_encode_float(IntPtr st, float[] pcm, int frame_size, byte[] data, int max_data_bytes);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int opus_packet_get_nb_channels(byte[] encodedData);

        //Control the encoder
        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int opus_encoder_ctl(IntPtr encoder, OpusCtl request, out int value);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int opus_encoder_ctl(IntPtr encoder, OpusCtl request, int value);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr opus_decoder_create(int sampleRate, int channelCount, out IntPtr error);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int opus_decode(IntPtr decoder, IntPtr data, int len, IntPtr pcm, int frameSize, int decodeFec);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int opus_decode_float(IntPtr decoder, byte[] data, int len, float[] pcm, int frameSize, int decodeFec);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void opus_decoder_destroy(IntPtr decoder);


        #region internal methods
        internal static IntPtr opus_encoder_create(int sampleRate, int channelCount, OpusApplication application, out OpusErrors error)
        {
            int size = opus_encoder_get_size(channelCount);
            IntPtr ptr = Marshal.AllocHGlobal(size);

            error = opus_encoder_init(ptr, sampleRate, channelCount, (int)application);
            
            if(error != OpusErrors.Ok)
                if (ptr != IntPtr.Zero)
                {
                    destroy_opus(ptr);
                    ptr = IntPtr.Zero;
                }

            return ptr;
        }

        internal static int opus_encode(IntPtr encoder, float[] pcmData, int frameSize, byte[] encodedData)
        {
            if(encoder == IntPtr.Zero)
            {
                Debug.LogError("Encoder empty??");
                return 0;
            }

            int byteLength = opus_encode_float(encoder, pcmData, frameSize, encodedData, encodedData.Length);

            if (byteLength <= 0)
            {
                Debug.LogError("Encoding error: " + (OpusErrors)byteLength);
                Debug.Log("Input = " + pcmData.Length);
            }

            return byteLength;
        }
        internal static void destroy_opus(IntPtr ptr)
        {
            Marshal.FreeHGlobal(ptr);
        }
        internal static IntPtr opus_decoder_create(int sampleRate, int channelCount, out OpusErrors error)
        {
            IntPtr p;
            IntPtr res =  NativeMethods.opus_decoder_create(sampleRate, channelCount, out p);
            error = (OpusErrors)p;
            return res;
        }
        internal static int opus_decode(IntPtr decoder, byte[] encodedData, float[] outputPcm, int frameSize, int channelCount)
        {
            if (decoder == IntPtr.Zero)
            {
                Debug.LogError("Encoder empty??");
                return 0;
            }
            //TODO use null to signify empty packet
            if(encodedData == null)
            {
                Debug.LogError("Empty encoded packet?");
                return 0;
            }

            int length = NativeMethods.opus_decode_float(decoder, encodedData, encodedData.Length, outputPcm, outputPcm.Length / channelCount, MumbleConstants.USE_FORWARD_ERROR_CORRECTION);

            if (length <= 0)
                Debug.LogError("Encoding error: " + (OpusErrors)length);

            return length;
        }
        #endregion
    }
}