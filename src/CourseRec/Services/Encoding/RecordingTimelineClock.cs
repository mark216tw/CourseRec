using System.Diagnostics;

namespace Com.VibeCode.CourseRec.Services.Encoding;

/// <summary>
/// 支援暫停／繼續自動扣除時間差的錄影時間軸計時器（單位：100ns Ticks）。
/// </summary>
public sealed class RecordingTimelineClock
{
    private readonly Func<long> _utcTicksProvider;
    private readonly object _sync = new();
    private long _startTicks;
    private long _pauseBeganTicks;
    private long _accumulatedPausedTicks;
    private bool _isStarted;
    private bool _isPaused;

    public RecordingTimelineClock()
        : this(GetHighResolutionTicks)
    {
    }

    public RecordingTimelineClock(Func<long> utcTicksProvider)
    {
        ArgumentNullException.ThrowIfNull(utcTicksProvider);
        _utcTicksProvider = utcTicksProvider;
    }

    public bool IsStarted
    {
        get
        {
            lock (_sync)
            {
                return _isStarted;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _isPaused;
            }
        }
    }

    public TimeSpan ElapsedActiveTime => TimeSpan.FromTicks(GetElapsedActiveTicks());

    public void Start()
    {
        long now = _utcTicksProvider();
        lock (_sync)
        {
            _startTicks = now;
            _pauseBeganTicks = 0;
            _accumulatedPausedTicks = 0;
            _isStarted = true;
            _isPaused = false;
        }
    }

    public void Pause()
    {
        long now = _utcTicksProvider();
        lock (_sync)
        {
            if (!_isStarted || _isPaused)
            {
                return;
            }

            _pauseBeganTicks = now;
            _isPaused = true;
        }
    }

    public void Resume()
    {
        long now = _utcTicksProvider();
        lock (_sync)
        {
            if (!_isStarted || !_isPaused)
            {
                return;
            }

            long pausedDelta = Math.Max(0L, now - _pauseBeganTicks);
            _accumulatedPausedTicks += pausedDelta;
            _pauseBeganTicks = 0;
            _isPaused = false;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _startTicks = 0;
            _pauseBeganTicks = 0;
            _accumulatedPausedTicks = 0;
            _isStarted = false;
            _isPaused = false;
        }
    }

    public long GetElapsedActiveTicks()
    {
        long now = _utcTicksProvider();
        lock (_sync)
        {
            if (!_isStarted)
            {
                return 0L;
            }

            long referenceNow = _isPaused ? _pauseBeganTicks : now;
            long active = referenceNow - _startTicks - _accumulatedPausedTicks;
            return Math.Max(0L, active);
        }
    }

    private static long GetHighResolutionTicks()
    {
        double timestamp = Stopwatch.GetTimestamp();
        return (long)(timestamp * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency));
    }
}
