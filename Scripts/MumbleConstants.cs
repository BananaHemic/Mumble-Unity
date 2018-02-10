using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mumble
{
    public static class MumbleConstants
    {
        public const int SAMPLE_RATE = 48000;
        public static readonly int[] SUPPORTED_SAMPLE_RATES = new int[] {
            8000,
            12000,
            16000,
            24000,
            48000
        };
        public const int FRAME_SIZE = SAMPLE_RATE / 100;
        public const int SAMPLE_BITS = 16;
        public const float MAX_LATENCY_SECONDS = 0.2f;
        public const bool IS_LITTLE_ENDIAN = false;
        public const int PING_INTERVAL_MS = 5000;//5 seconds
        //TODO experiment with this
        public const int USE_FORWARD_ERROR_CORRECTION = 0;
        //Should probably be 12, but I'm hesitant to double the buffer for everyone to support a rare case
        //TODO make the buffer size scale
        public const int MAX_FRAMES_PER_PACKET = 6;
        //How many 10ms samples to include in each packet
        public const int NUM_FRAMES_PER_OUTGOING_PACKET = 2;
        //The length of time in each audio packet
        public const int FRAME_SIZE_MS = NUM_FRAMES_PER_OUTGOING_PACKET * 10;
        public const int NUM_CHANNELS = 2;
        //How many bytes can go into a single UDP packet
        public const int MAX_BYTES_PER_PACKET = 480;
        public const int RECEIVED_PACKET_BUFFER_SIZE = 10;
        public const int MAX_CONSECUTIVE_MISSED_UDP_PINGS = 2;
    }
}
