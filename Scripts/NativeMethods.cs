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
        /*
        static NativeMethods()
        {
            IntPtr image;

            /*
            if (PlatformDetails.IsMac)
            {
                image = LibraryLoader.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio", "Codecs", "Opus", "Libs", "32bit", "libopus.dylib"));
            }
            // * /
#if UNITY_STANDALONE
            image = LibraryLoader.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio", "Codecs", "Opus", "Libs", "64bit", "opus.dll"));
#elif UNITY_ANDROID 
            image = LibraryLoader.Load("libopus.so.0");
            if (image.Equals(IntPtr.Zero))
                image = LibraryLoader.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio", "Codecs", "Opus", "Libs", "libopus.so"));
#endif
            if (image != IntPtr.Zero)
            {
                var type = typeof(NativeMethods);
                foreach (var member in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                {
                    var methodName = member.Name;
                    if (methodName == "opus_encoder_ctl_out") methodName = "opus_encoder_ctl";
                    var fieldType = member.FieldType;
                    var ptr = LibraryLoader.ResolveSymbol(image, methodName);
                    if (ptr == IntPtr.Zero)
                        throw new Exception(string.Format("Could not resolve symbol \"{0}\"", methodName));
                    member.SetValue(null, Marshal.GetDelegateForFunctionPointer(ptr, fieldType));
                }
            }
        }
*/

        // ReSharper disable InconsistentNaming
        // ReSharper disable UnassignedField.Compiler
        const string pluginName = "opus";

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr opus_encoder_create(int sampleRate, int channelCount, int application, out IntPtr error);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void opus_encoder_destroy(IntPtr encoder);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int opus_encode(IntPtr encoder, IntPtr pcm, int frameSize, IntPtr data, int maxDataBytes);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr opus_decoder_create(int sampleRate, int channelCount, out IntPtr error);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void opus_decoder_destroy(IntPtr decoder);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int opus_decode(IntPtr decoder, IntPtr data, int len, IntPtr pcm, int frameSize, int decodeFec);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int opus_packet_get_nb_channels(IntPtr data);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int opus_packet_get_nb_samples(IntPtr data, int len, int sampleRate);

        //Control the encoder
        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int opus_encoder_ctl(IntPtr encoder, Ctl request, int value);

        [DllImport(pluginName, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int opus_encoder_ctl_out(IntPtr encoder, Ctl request, out int value);

        // ReSharper restore UnassignedField.Compiler
        // ReSharper restore InconsistentNaming

        public enum Ctl
        {
            SetBitrateRequest = 4002,
            GetBitrateRequest = 4003,
            SetInbandFecRequest = 4012,
            GetInbandFecRequest = 4013
        }

        public enum OpusErrors
        {
            Ok = 0,
            BadArgument = -1,
            BufferToSmall = -2,
            InternalError = -3,
            InvalidPacket = -4,
            NotImplemented = -5,
            InvalidState = -6,
            AllocFail = -7
        }
    }
}