namespace Com.VibeCode.CourseRec.Services.Encoding;

public static class RecordingFrameTiming
{
    public static long GetFrameStart100Ns(long frameIndex, int frameRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameRate, 1);

        return (frameIndex * TimeSpan.TicksPerSecond) / frameRate;
    }

    public static long GetFrameDuration100Ns(long frameIndex, int frameRate)
    {
        return GetFrameStart100Ns(frameIndex + 1, frameRate)
            - GetFrameStart100Ns(frameIndex, frameRate);
    }
}
