using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mumble
{
    public class Constants
    {
        public const uint SAMPLE_RATE = 48000;
        public const uint FRAME_SIZE = SAMPLE_RATE / 100;
        public const uint SAMPLE_BITS = 16;
        public const bool IS_LITTLE_ENDIAN = false;
        public const int PING_INTERVAL = 5000;//5 seconds
    }
}
