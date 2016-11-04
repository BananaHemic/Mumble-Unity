using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mumble
{
    public class Constants
    {
        public const int SAMPLE_RATE = 48000;
        public const int FRAME_SIZE = SAMPLE_RATE / 100;
        public const int SAMPLE_BITS = 16;
        public const float MAX_LATENCY_SECONDS = 0.1f;
        public const bool IS_LITTLE_ENDIAN = false;
        public const int PING_INTERVAL = 5000;//5 seconds
        //TODO experiment with this
        public const int USE_FORWARD_ERROR_CORRECTION = 0;
        public const int MAX_FRAMES_PER_PACKET = 6;//Should probably be 12, but I'm hesitant to double the buffer for everyone to support a rare case
        public const int NUM_CHANNELS = 1;
    }
}
