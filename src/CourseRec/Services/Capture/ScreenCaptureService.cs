using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Microsoft.Extensions.Logging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Com.VibeCode.CourseRec.Services.Capture;

/// <summary>
/// 基於 <see cref="Direct3D11CaptureFramePool"/> 的高效能螢幕／視窗擷取、最小化偵測與快拍實作。
/// </summary>
public sealed class ScreenCaptureService : IScreenCaptureService
{
    private const long PreviewMinIntervalTicks = TimeSpan.TicksPerMillisecond * 100; // ~10 fps UI preview
    private const long VsyncJitterMarginTicks = TimeSpan.TicksPerMillisecond * 4;
    private const int DefaultPreviewMaxDimension = 1280;
    private const int CaptureFailureThreshold = 10;
    private static readonly long RecoveryBackoffTicks = TimeSpan.TicksPerSecond * 5;

    private readonly ILogger<ScreenCaptureService> _logger;
    private readonly object _frameLock = new();
    private readonly object _lifecycleLock = new();
    private readonly byte[][] _ringPixelBuffers = [[], [], [], []];
    private readonly int[] _ringSlotInUse = new int[4];
    private readonly byte[][] _previewPixelBuffers = [[], []];
    private readonly int[] _previewSlotInUse = new int[2];

    private IDirect3DDevice? _winrtDevice;
    private nint _d3dDevice;
    private nint _d3dContext;
    private nint _stagingTexture;
    private int _stagingWidth;
    private int _stagingHeight;
    private int _ringSlotIndex;
    private int _previewSlotIndex;
    private int _previewMaxDimension = DefaultPreviewMaxDimension;
    private int _consecutiveCaptureFailures;
    private int _recoveryScheduled;
    private int _captureStopVersion;
    private long _lastRecoveryTicks;
    private volatile bool _isPreviewEnabled;

    private GraphicsCaptureItem? _captureItem;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private SizeInt32 _lastPoolSize;
    private CapturedFrameData? _latestFrame;
    private long _minCaptureIntervalTicks = PreviewMinIntervalTicks;
    private long _lastReadbackTimestamp;
    private long _lastPreviewDispatchTicks;
    private Timer? _minimizedCheckTimer;
    private bool _isDisposed;

    public ScreenCaptureService(ILogger<ScreenCaptureService> logger)
    {
        _logger = logger;
    }

    public CaptureTarget? ActiveTarget { get; private set; }

    public bool IsCapturing => _session is not null;

    public bool IsTargetMinimized { get; private set; }

    public long MinCaptureIntervalTicks
    {
        get => Interlocked.Read(ref _minCaptureIntervalTicks);
        set => Interlocked.Exchange(ref _minCaptureIntervalTicks, Math.Max(0L, value));
    }

    public bool IsPreviewEnabled
    {
        get => _isPreviewEnabled;
        set => _isPreviewEnabled = value;
    }

    public int PreviewMaxDimension
    {
        get => Volatile.Read(ref _previewMaxDimension);
        set => Volatile.Write(ref _previewMaxDimension, Math.Max(320, value));
    }

    public event EventHandler<CapturedFrameData>? PreviewFrameAvailable;

    public event EventHandler<bool>? TargetMinimizedChanged;

    public event EventHandler? TargetClosed;

    public bool StartCapture(CaptureTarget target)
    {
        lock (_lifecycleLock)
        {
            Interlocked.Increment(ref _captureStopVersion);
            return StartCaptureCore(target);
        }
    }

    private bool StartCaptureCore(CaptureTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        StopCaptureCore();

        if (!GraphicsCaptureSession.IsSupported())
        {
            _logger.LogWarning("目前系統環境不支援 Windows.Graphics.Capture。");
            return false;
        }

        if (!EnsureD3DDevice())
        {
            _logger.LogError("無法初始化 Direct3D 11 擷取裝置。");
            return false;
        }

        GraphicsCaptureItem? item = target.Kind == CaptureTargetKind.Monitor
            ? CaptureNativeMethods.TryCreateItemForMonitor(target.Handle)
            : CaptureNativeMethods.TryCreateItemForWindow(target.Handle);

        if (item is null)
        {
            _logger.LogWarning("無法為目標建立 GraphicsCaptureItem：{Title}", target.Title);
            return false;
        }

        _captureItem = item;
        _captureItem.Closed += OnCaptureItemClosed;

        try
        {
            SizeInt32 size = item.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                size = new SizeInt32(Math.Max(320, target.Width), Math.Max(240, target.Height));
            }

            _lastPoolSize = size;

            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winrtDevice!,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                size);
            _framePool.FrameArrived += OnFrameArrived;

            _session = _framePool.CreateCaptureSession(item);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                _session.IsCursorCaptureEnabled = true;
            }

            _session.StartCapture();

            ActiveTarget = target;
            UpdateMinimizedState(target.Kind == CaptureTargetKind.Window && WindowNativeMethods.IsIconic(target.Handle));

            if (target.Kind == CaptureTargetKind.Window)
            {
                _minimizedCheckTimer = new Timer(CheckWindowMinimizedCallback, null, 400, 400);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "啟動螢幕／視窗擷取失敗：{Title}", target.Title);
            StopCaptureCore();
            return false;
        }
    }

    public void StopCapture()
    {
        lock (_lifecycleLock)
        {
            Interlocked.Increment(ref _captureStopVersion);
            StopCaptureCore();
        }
    }

    public void ReleaseIdleResources()
    {
        lock (_lifecycleLock)
        {
            if (_session is not null)
            {
                return;
            }

            lock (_frameLock)
            {
                ReleaseIdleBuffersLocked();
                ReleaseGraphicsDeviceLocked();
            }
        }
    }

    private void StopCaptureCore()
    {
        _minimizedCheckTimer?.Dispose();
        _minimizedCheckTimer = null;

        GraphicsCaptureItem? item = _captureItem;
        _captureItem = null;
        if (item is not null)
        {
            item.Closed -= OnCaptureItemClosed;
        }

        Direct3D11CaptureFramePool? pool;
        GraphicsCaptureSession? session;
        lock (_frameLock)
        {
            pool = _framePool;
            _framePool = null;
            session = _session;
            _session = null;
            _latestFrame?.Dispose();
            _latestFrame = null;
            Interlocked.Exchange(ref _lastReadbackTimestamp, 0);
            D3D11NativeMethods.ReleaseCom(ref _stagingTexture);
            _stagingWidth = 0;
            _stagingHeight = 0;
            ReleaseIdleBuffersLocked();
        }

        if (pool is not null)
        {
            pool.FrameArrived -= OnFrameArrived;
            pool.Dispose();
        }

        session?.Dispose();

        ActiveTarget = null;
        UpdateMinimizedState(false);
    }

    private void ReleaseIdleBuffersLocked()
    {
        for (int i = 0; i < _ringPixelBuffers.Length; i++)
        {
            if (Volatile.Read(ref _ringSlotInUse[i]) == 0)
            {
                _ringPixelBuffers[i] = [];
            }
        }

        for (int i = 0; i < _previewPixelBuffers.Length; i++)
        {
            if (Volatile.Read(ref _previewSlotInUse[i]) == 0)
            {
                _previewPixelBuffers[i] = [];
            }
        }
    }

    public CapturedFrameData? GetLatestFrameSnapshot()
    {
        lock (_frameLock)
        {
            return _latestFrame?.Retain();
        }
    }

    public async Task<string?> SaveSnapshotPngAsync(
        string outputDirectory,
        bool copyToClipboard = true,
        CancellationToken cancellationToken = default)
    {
        using CapturedFrameData? snapshot = GetLatestFrameSnapshot();
        if (snapshot is null || snapshot.Width <= 0 || snapshot.Height <= 0)
        {
            return null;
        }

        string snapshotsDir = Path.Combine(outputDirectory, "Snapshots");
        Directory.CreateDirectory(snapshotsDir);
        string fileName = $"Slide_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        string fullPath = Path.Combine(snapshotsDir, fileName);

        await EncodeBgraToPngFileAsync(snapshot.BgraPixels, snapshot.Width, snapshot.Height, fullPath, cancellationToken)
            .ConfigureAwait(false);

        if (copyToClipboard)
        {
            try
            {
                StorageFile storageFile = await StorageFile.GetFileFromPathAsync(fullPath)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                await ClipboardHelper.SetBitmapAsync(storageFile, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "快拍已存檔，但複製至剪貼簿時發生非致命例外。");
            }
        }

        return fullPath;
    }

    public static async Task EncodeBgraToPngFileAsync(
        byte[] bgraPixels,
        int width,
        int height,
        string outputFilePath,
        CancellationToken cancellationToken = default)
    {
        using var memoryStream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memoryStream)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)width,
            (uint)height,
            96.0,
            96.0,
            bgraPixels);

        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);

        memoryStream.Seek(0);
        using Stream netStream = memoryStream.AsStreamForRead();
        using var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await netStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopCapture();

        lock (_frameLock)
        {
            ReleaseGraphicsDeviceLocked();
            _latestFrame = null;
        }
    }

    private bool EnsureD3DDevice()
    {
        lock (_frameLock)
        {
            if (_winrtDevice is not null && _d3dDevice != 0 && _d3dContext != 0)
            {
                return true;
            }

            return D3D11NativeMethods.TryCreateDirect3DDevice(out _winrtDevice, out _d3dDevice, out _d3dContext);
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            ProcessFrame(sender);
        }
        catch (ObjectDisposedException) when (!ReferenceEquals(sender, _framePool))
        {
            // 停止擷取時，已排程的舊 FrameArrived 可能晚於 FramePool.Dispose 執行。
        }
    }

    private void ProcessFrame(Direct3D11CaptureFramePool sender)
    {
        using Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        SizeInt32 contentSize = frame.ContentSize;
        bool needsRecreate = contentSize.Width > 0
            && contentSize.Height > 0
            && (contentSize.Width != _lastPoolSize.Width || contentSize.Height != _lastPoolSize.Height);

        long nowTimestamp = Stopwatch.GetTimestamp();
        long prevTimestamp = Interlocked.Read(ref _lastReadbackTimestamp);
        long minIntervalTicks = Math.Max(0L, Interlocked.Read(ref _minCaptureIntervalTicks) - VsyncJitterMarginTicks);
        if (!needsRecreate
            && prevTimestamp != 0
            && Stopwatch.GetElapsedTime(prevTimestamp, nowTimestamp).Ticks < minIntervalTicks)
        {
            return;
        }

        CapturedFrameData? captured = null;
        bool copyFailed = false;
        int copyFailureHresult = 0;
        try
        {
            lock (_frameLock)
            {
                if (!ReferenceEquals(sender, _framePool))
                {
                    return;
                }

                int nextSlot = -1;
                for (int offset = 1; offset <= _ringPixelBuffers.Length; offset++)
                {
                    int candidate = (_ringSlotIndex + offset) % _ringPixelBuffers.Length;
                    if (Interlocked.CompareExchange(ref _ringSlotInUse[candidate], 1, 0) == 0)
                    {
                        nextSlot = candidate;
                        break;
                    }
                }

                try
                {
                    if (nextSlot >= 0 && _d3dDevice != 0 && _d3dContext != 0)
                    {
                        bool copied = D3D11NativeMethods.TryCopySurfaceBgraPixels(
                            _d3dDevice,
                            _d3dContext,
                            frame.Surface,
                            ref _stagingTexture,
                            ref _stagingWidth,
                            ref _stagingHeight,
                            ref _ringPixelBuffers[nextSlot],
                            out int width,
                            out int height,
                            out copyFailureHresult);

                        if (copied)
                        {
                            int leasedSlot = nextSlot;
                            captured = new CapturedFrameData(
                                _ringPixelBuffers[nextSlot], width, height, frame.SystemRelativeTime.Ticks,
                                () => ReleaseRingSlot(leasedSlot));
                            _ringSlotIndex = nextSlot;
                            Interlocked.Exchange(ref _lastReadbackTimestamp, nowTimestamp);
                            _latestFrame?.Dispose();
                            _latestFrame = captured.Retain();
                            Interlocked.Exchange(ref _consecutiveCaptureFailures, 0);
                        }
                        else
                        {
                            copyFailed = true;
                        }
                    }
                }
                finally
                {
                    if (nextSlot >= 0 && captured is null)
                    {
                        Interlocked.Exchange(ref _ringSlotInUse[nextSlot], 0);
                    }
                }

                if (needsRecreate && _winrtDevice is not null && _framePool is not null)
                {
                    _lastPoolSize = contentSize;
                    _framePool.Recreate(
                        _winrtDevice,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        contentSize);
                }
            }

            if (copyFailed)
            {
                int failures = Interlocked.Increment(ref _consecutiveCaptureFailures);
                if (D3D11NativeMethods.IsDeviceLostHresult(copyFailureHresult)
                    || failures >= CaptureFailureThreshold)
                {
                    ScheduleCaptureRecovery();
                }
            }

            if (captured is not null && _isPreviewEnabled && PreviewFrameAvailable is not null)
            {
                long nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
                if (nowTicks - Interlocked.Read(ref _lastPreviewDispatchTicks) >= PreviewMinIntervalTicks)
                {
                    Interlocked.Exchange(ref _lastPreviewDispatchTicks, nowTicks);
                    DispatchPreviewFrame(captured);
                }
            }
        }
        finally
        {
            captured?.Dispose();
        }
    }

    private void DispatchPreviewFrame(CapturedFrameData captured)
    {
        EventHandler<CapturedFrameData>? handler = PreviewFrameAvailable;
        if (handler is null)
        {
            return;
        }

        (int targetWidth, int targetHeight) = BgraScaler.ComputePreviewSize(
            captured.Width,
            captured.Height,
            PreviewMaxDimension);

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            return;
        }

        if (targetWidth == captured.Width && targetHeight == captured.Height)
        {
            handler(this, captured);
            return;
        }

        int nextSlot = -1;
        for (int offset = 1; offset <= _previewPixelBuffers.Length; offset++)
        {
            int candidate = (_previewSlotIndex + offset) % _previewPixelBuffers.Length;
            if (Interlocked.CompareExchange(ref _previewSlotInUse[candidate], 1, 0) == 0)
            {
                nextSlot = candidate;
                break;
            }
        }

        if (nextSlot < 0)
        {
            return;
        }

        int requiredBytes = targetWidth * targetHeight * 4;
        byte[] buffer = _previewPixelBuffers[nextSlot];
        try
        {
            if (buffer.Length != requiredBytes)
            {
                buffer = new byte[requiredBytes];
                _previewPixelBuffers[nextSlot] = buffer;
            }

            BgraScaler.ScaleBgraIntoBuffer(
                captured.BgraPixels,
                captured.Width,
                captured.Height,
                buffer,
                targetWidth,
                targetHeight);
        }
        catch
        {
            Interlocked.Exchange(ref _previewSlotInUse[nextSlot], 0);
            return;
        }

        CapturedFrameData preview;
        try
        {
            int leasedPreviewSlot = nextSlot;
            preview = new CapturedFrameData(
                buffer,
                targetWidth,
                targetHeight,
                captured.SystemRelativeTicks,
                () => ReleasePreviewSlot(leasedPreviewSlot));
        }
        catch
        {
            Interlocked.Exchange(ref _previewSlotInUse[nextSlot], 0);
            return;
        }

        _previewSlotIndex = nextSlot;
        try
        {
            handler(this, preview);
        }
        finally
        {
            preview.Dispose();
        }
    }

    private void ScheduleCaptureRecovery()
    {
        if (_isDisposed || Interlocked.Exchange(ref _recoveryScheduled, 1) != 0)
        {
            return;
        }

        CaptureTarget? target = ActiveTarget;
        long nowTicks = DateTime.UtcNow.Ticks;
        long lastRecoveryTicks = Interlocked.Read(ref _lastRecoveryTicks);
        if (target is null || (lastRecoveryTicks != 0 && nowTicks - lastRecoveryTicks < RecoveryBackoffTicks))
        {
            Volatile.Write(ref _recoveryScheduled, 0);
            return;
        }

        Interlocked.Exchange(ref _lastRecoveryTicks, nowTicks);
        int stopVersion = Volatile.Read(ref _captureStopVersion);
        _ = Task.Run(() => RecoverCaptureAsync(target, stopVersion));
    }

    private async Task RecoverCaptureAsync(CaptureTarget target, int stopVersion)
    {
        const int maxAttempts = 3;
        try
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                bool started = false;
                lock (_lifecycleLock)
                {
                    if (_isDisposed || Volatile.Read(ref _captureStopVersion) != stopVersion)
                    {
                        return;
                    }

                    StopCaptureCore();
                    lock (_frameLock)
                    {
                        ReleaseGraphicsDeviceLocked();
                    }

                    started = StartCaptureCore(target);
                }

                if (started)
                {
                    Interlocked.Exchange(ref _consecutiveCaptureFailures, 0);
                    _logger.LogWarning("擷取裝置失效，已重新建立 Direct3D 與擷取工作階段：{Title}", target.Title);
                    return;
                }

                if (attempt < maxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
            }

            _logger.LogError("擷取裝置重建失敗，暫停畫面更新：{Title}", target.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "擷取裝置重建時發生例外：{Title}", target.Title);
        }
        finally
        {
            Volatile.Write(ref _recoveryScheduled, 0);
        }
    }

    private void ReleaseRingSlot(int slotIndex)
    {
        Interlocked.Exchange(ref _ringSlotInUse[slotIndex], 0);
        if (_session is null)
        {
            lock (_frameLock)
            {
                if (_session is null && Volatile.Read(ref _ringSlotInUse[slotIndex]) == 0)
                {
                    _ringPixelBuffers[slotIndex] = [];
                }
            }
        }
    }

    private void ReleasePreviewSlot(int slotIndex)
    {
        Interlocked.Exchange(ref _previewSlotInUse[slotIndex], 0);
        if (_session is null)
        {
            lock (_frameLock)
            {
                if (_session is null && Volatile.Read(ref _previewSlotInUse[slotIndex]) == 0)
                {
                    _previewPixelBuffers[slotIndex] = [];
                }
            }
        }
    }

    private void ReleaseGraphicsDeviceLocked()
    {
        D3D11NativeMethods.ReleaseCom(ref _stagingTexture);
        D3D11NativeMethods.ReleaseCom(ref _d3dContext);
        D3D11NativeMethods.ReleaseCom(ref _d3dDevice);
        _winrtDevice?.Dispose();
        _winrtDevice = null;
        _stagingWidth = 0;
        _stagingHeight = 0;
    }

    private void CheckWindowMinimizedCallback(object? state)
    {
        CaptureTarget? target = ActiveTarget;
        if (target is null || target.Kind != CaptureTargetKind.Window)
        {
            return;
        }

        if (!CaptureNativeMethods.IsWindow(target.Handle))
        {
            TargetClosed?.Invoke(this, EventArgs.Empty);
            return;
        }

        bool minimized = WindowNativeMethods.IsIconic(target.Handle);
        UpdateMinimizedState(minimized);
    }

    private void UpdateMinimizedState(bool minimized)
    {
        if (IsTargetMinimized != minimized)
        {
            IsTargetMinimized = minimized;
            TargetMinimizedChanged?.Invoke(this, minimized);
        }
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args)
    {
        TargetClosed?.Invoke(this, EventArgs.Empty);
    }
}
