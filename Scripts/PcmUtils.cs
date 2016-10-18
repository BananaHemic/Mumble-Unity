/*
 * Class to handle some PCM operations
 * this is primarily because Unity wants raw data between [-1, 1]
 * whereas opus gives us PCM16 data
 * 
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mumble
{
    public static class PcmUtils
    {
        const int Max_PCM = 32768;

        //TODO optimize
        public static byte[] Raw2Pcm(float[] rawData)
        {
            byte[] returnedBuffer = new byte[rawData.Length * sizeof(float)];
            int[] pcmData = new int[rawData.Length];

            for(int i = 0; i < rawData.Length; i++)
            {
                pcmData[i] = (int)(rawData[i] * Max_PCM);
            }

            Buffer.BlockCopy(pcmData, 0, returnedBuffer, 0, returnedBuffer.Length);
            return returnedBuffer;
        }
    }
}
