namespace Com.VibeCode.CourseRec.Services.Capture;

/// <summary>
/// 單幀 BGRA32 影像快照（供即時預覽、PNG 投影片快拍與最小化時重送最後一幀使用）。
/// </summary>
public sealed class CapturedFrameData : IDisposable
{
    private readonly object _sync = new();
    private SharedBuffer? _buffer;

    public CapturedFrameData(byte[] bgraPixels, int width, int height, long systemRelativeTicks, Action? onReleased = null)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        _buffer = new SharedBuffer(bgraPixels, onReleased);
        Width = width;
        Height = height;
        SystemRelativeTicks = systemRelativeTicks;
    }

    private CapturedFrameData(SharedBuffer buffer, int width, int height, long systemRelativeTicks)
    {
        _buffer = buffer;
        Width = width;
        Height = height;
        SystemRelativeTicks = systemRelativeTicks;
    }

    public byte[] BgraPixels => _buffer?.Pixels ?? throw new ObjectDisposedException(nameof(CapturedFrameData));

    public int Width { get; }

    public int Height { get; }

    public long SystemRelativeTicks { get; }

    /// <summary>取得獨立使用權；所有使用權釋放後才可覆寫原始緩衝區。</summary>
    public CapturedFrameData Retain()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            Interlocked.Increment(ref _buffer.ReferenceCount);
            return new CapturedFrameData(_buffer, Width, Height, SystemRelativeTicks);
        }
    }

    public void Dispose()
    {
        SharedBuffer? buffer;
        lock (_sync)
        {
            buffer = _buffer;
            _buffer = null;
        }

        if (buffer is not null && Interlocked.Decrement(ref buffer.ReferenceCount) == 0)
        {
            buffer.OnReleased?.Invoke();
        }
    }

    private sealed class SharedBuffer(byte[] pixels, Action? onReleased)
    {
        public readonly byte[] Pixels = pixels;
        public readonly Action? OnReleased = onReleased;
        public int ReferenceCount = 1;
    }
}
