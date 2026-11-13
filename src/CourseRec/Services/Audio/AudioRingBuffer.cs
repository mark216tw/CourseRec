namespace Com.VibeCode.CourseRec.Services.Audio;

/// <summary>
/// 執行緒安全的浮點數音訊環形緩衝區（48kHz 雙聲道交錯 PCM 樣本對齊用）。
/// </summary>
public sealed class AudioRingBuffer
{
    private readonly float[] _buffer;
    private readonly object _sync = new();
    private int _readIndex;
    private int _writeIndex;
    private int _count;

    public AudioRingBuffer(int capacitySamples)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacitySamples, 0);
        _buffer = new float[capacitySamples];
    }

    public int Capacity => _buffer.Length;

    public int AvailableSamples
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// 寫入樣本至緩衝區；若空間不足則覆寫最舊資料以維持即時性，並回傳實際新寫入的樣本數。
    /// </summary>
    public int Write(ReadOnlySpan<float> source)
    {
        if (source.IsEmpty)
        {
            return 0;
        }

        lock (_sync)
        {
            if (source.Length >= _buffer.Length)
            {
                ReadOnlySpan<float> tail = source.Slice(source.Length - _buffer.Length);
                tail.CopyTo(_buffer);
                _readIndex = 0;
                _writeIndex = 0;
                _count = _buffer.Length;
                return _buffer.Length;
            }

            int free = _buffer.Length - _count;
            if (source.Length > free)
            {
                int drop = source.Length - free;
                _readIndex = (_readIndex + drop) % _buffer.Length;
                _count -= drop;
            }

            int firstChunk = Math.Min(source.Length, _buffer.Length - _writeIndex);
            source.Slice(0, firstChunk).CopyTo(_buffer.AsSpan(_writeIndex, firstChunk));

            int remaining = source.Length - firstChunk;
            if (remaining > 0)
            {
                source.Slice(firstChunk, remaining).CopyTo(_buffer.AsSpan(0, remaining));
            }

            _writeIndex = (_writeIndex + source.Length) % _buffer.Length;
            _count += source.Length;
            return source.Length;
        }
    }

    /// <summary>
    /// 讀取指定長度的樣本至目標緩衝區；不足處自動補 0.0f 以維持雙軌對齊不斷音。
    /// </summary>
    public int ReadWithZeroFill(Span<float> destination)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        lock (_sync)
        {
            int toRead = Math.Min(destination.Length, _count);
            if (toRead > 0)
            {
                int firstChunk = Math.Min(toRead, _buffer.Length - _readIndex);
                _buffer.AsSpan(_readIndex, firstChunk).CopyTo(destination.Slice(0, firstChunk));

                int remaining = toRead - firstChunk;
                if (remaining > 0)
                {
                    _buffer.AsSpan(0, remaining).CopyTo(destination.Slice(firstChunk, remaining));
                }

                _readIndex = (_readIndex + toRead) % _buffer.Length;
                _count -= toRead;
            }

            if (toRead < destination.Length)
            {
                destination.Slice(toRead).Clear();
            }

            return toRead;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _readIndex = 0;
            _writeIndex = 0;
            _count = 0;
            Array.Clear(_buffer);
        }
    }
}
