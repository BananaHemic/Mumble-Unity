namespace Mumble
{
    public enum OpusErrors
    {
        Ok = 0,
        BadArgument = -1,
        BufferTooSmall = -2,
        InternalError = -3,
        InvalidPacket = -4,
        NotImplemented = -5,
        InvalidState = -6,
        AllocFail = -7
    }
}
