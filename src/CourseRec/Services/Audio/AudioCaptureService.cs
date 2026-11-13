using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Microsoft.Extensions.Logging;

namespace Com.VibeCode.CourseRec.Services.Audio;

/// <summary>
/// 原生 WASAPI 雙軌擷取與端點熱插拔自動重連實作。
/// </summary>
public sealed class AudioCaptureService : IAudioCaptureService
{
    private readonly DualTrackAudioMixer _mixer = new(bufferSeconds: 6);
    private readonly ILogger<AudioCaptureService> _logger;
    private readonly object _lifecycleLock = new();

    private CancellationTokenSource? _loopCts;
    private Task? _captureTask;
    private volatile bool _isBufferingForEncoding;
    private volatile uint _desiredProcessId;
    private volatile bool _reloadSystemSourceRequested;
    private bool _isDisposed;
    private volatile AudioCaptureStatus _currentStatus = new(SystemAudioSource.Inactive, null, false, false);

    public AudioCaptureService(ILogger<AudioCaptureService> logger)
    {
        _logger = logger;
    }

    public bool IsMonitoring => _captureTask is { IsCompleted: false };

    public bool IsBufferingForEncoding
    {
        get => _isBufferingForEncoding;
        set
        {
            if (_isBufferingForEncoding != value)
            {
                if (value)
                {
                    _mixer.ResetBuffers();
                }

                _isBufferingForEncoding = value;
            }
        }
    }

    public bool MuteMicrophone
    {
        get => _mixer.MuteMicrophone;
        set => _mixer.MuteMicrophone = value;
    }

    public AudioLevelReading CurrentLevels => _mixer.CurrentLevels;

    public AudioCaptureStatus CurrentStatus => _currentStatus;

    public event EventHandler<AudioCaptureStatus>? CaptureStatusChanged;

    public event EventHandler<AudioLevelReading>? AudioLevelsUpdated;

    public event EventHandler? ProcessAudioFallbackTriggered;

    public void StartMonitoring(uint? targetProcessId)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_lifecycleLock)
        {
            _desiredProcessId = targetProcessId ?? 0u;
            if (IsMonitoring)
            {
                _reloadSystemSourceRequested = true;
                return;
            }

            _loopCts = new CancellationTokenSource();
            PublishStatus(new AudioCaptureStatus(SystemAudioSource.Connecting, null, false, false));
            CancellationToken token = _loopCts.Token;
            _captureTask = Task.Factory.StartNew(
                () => RunAudioCaptureLoop(token),
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    public void UpdateSystemSource(uint? targetProcessId)
    {
        uint nextPid = targetProcessId ?? 0u;
        if (_desiredProcessId != nextPid)
        {
            _desiredProcessId = nextPid;
            _reloadSystemSourceRequested = true;
        }
    }

    public void StopMonitoring()
    {
        StopMonitoringAsync().GetAwaiter().GetResult();
    }

    public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
    {
        Task? runningTask;
        CancellationTokenSource? cts;

        lock (_lifecycleLock)
        {
            cts = _loopCts;
            runningTask = _captureTask;
            _loopCts = null;
            _captureTask = null;
        }

        if (cts is null)
        {
            _mixer.ResetBuffers();
            PublishStatus(new AudioCaptureStatus(SystemAudioSource.Inactive, null, false, false));
            return;
        }

        bool completed = runningTask is null;
        try
        {
            cts.Cancel();
            if (runningTask is not null)
            {
                try
                {
                    await runningTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("停止音訊擷取逾時，將於背景完成清理。");
                }
                catch (OperationCanceledException)
                {
                    // 使用者或呼叫端取消等待；擷取迴圈會自行結束。
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "停止音訊擷取時發生例外。");
                }

                completed = runningTask.IsCompleted;
            }
        }
        finally
        {
            _mixer.ResetBuffers();
            PublishStatus(new AudioCaptureStatus(SystemAudioSource.Inactive, null, false, false));

            if (completed)
            {
                cts.Dispose();
            }
            else
            {
                _ = runningTask!.ContinueWith(
                    _ => cts.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    public void ResetEncodingBuffers()
    {
        _mixer.ResetBuffers();
    }

    public void MixNextStereoFramesToPcm16(int frameCount, Span<short> destinationPcm16)
    {
        _mixer.MixStereoFramesToPcm16(frameCount, destinationPcm16);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopMonitoring();
    }

    private void RunAudioCaptureLoop(CancellationToken cancellationToken)
    {
        nint sysAudioClient = 0;
        nint sysCaptureClient = 0;
        int sysChannels = 2;
        int sysSampleRate = 48_000;
        bool sysIsFloat = true;
        int sysBits = 32;
        string? activeRenderId = null;
        bool usingProcessLoopback = false;
        uint activeRequestedProcessId = _desiredProcessId;

        nint micAudioClient = 0;
        nint micCaptureClient = 0;
        int micChannels = 1;
        int micSampleRate = 48_000;
        bool micIsFloat = true;
        int micBits = 32;
        string? activeCaptureId = null;

        var rawSamples = new List<float>(4800);
        float[] conversionBuffer = new float[96_000];
        long lastEndpointCheckMs = Environment.TickCount64;
        long lastUiMeterPublishMs = 0;

        try
        {
            OpenSystemSource(
                activeRequestedProcessId,
                ref sysAudioClient,
                ref sysCaptureClient,
                ref sysChannels,
                ref sysSampleRate,
                ref sysIsFloat,
                ref sysBits,
                ref activeRenderId,
                ref usingProcessLoopback);

            WasapiNativeMethods.TryOpenEndpointCapture(
                WasapiNativeMethods.EDataFlowCapture,
                isLoopback: false,
                out micAudioClient,
                out micCaptureClient,
                out micChannels,
                out micSampleRate,
                out micIsFloat,
                out micBits,
                out activeCaptureId);

            while (!cancellationToken.IsCancellationRequested)
            {
                long nowMs = Environment.TickCount64;

                if (_reloadSystemSourceRequested)
                {
                    _reloadSystemSourceRequested = false;
                    activeRequestedProcessId = _desiredProcessId;
                    WasapiNativeMethods.StopAndReleaseAudioClient(ref sysCaptureClient, ref sysAudioClient);
                    OpenSystemSource(
                        activeRequestedProcessId,
                        ref sysAudioClient,
                        ref sysCaptureClient,
                        ref sysChannels,
                        ref sysSampleRate,
                        ref sysIsFloat,
                        ref sysBits,
                        ref activeRenderId,
                        ref usingProcessLoopback);
                }

                // Periodically detect headphone / mic endpoint switches (~1500ms)
                if (nowMs - lastEndpointCheckMs >= 1500)
                {
                    lastEndpointCheckMs = nowMs;

                    if (!usingProcessLoopback || sysCaptureClient == 0)
                    {
                        string? currentRenderId = WasapiNativeMethods.TryGetDefaultEndpointId(WasapiNativeMethods.EDataFlowRender);
                        if (sysCaptureClient == 0 || !string.Equals(currentRenderId, activeRenderId, StringComparison.Ordinal))
                        {
                            WasapiNativeMethods.StopAndReleaseAudioClient(ref sysCaptureClient, ref sysAudioClient);
                            OpenSystemSource(
                                activeRequestedProcessId,
                                ref sysAudioClient,
                                ref sysCaptureClient,
                                ref sysChannels,
                                ref sysSampleRate,
                                ref sysIsFloat,
                                ref sysBits,
                                ref activeRenderId,
                                ref usingProcessLoopback);
                        }
                    }

                    string? currentMicId = WasapiNativeMethods.TryGetDefaultEndpointId(WasapiNativeMethods.EDataFlowCapture);
                    if (micCaptureClient == 0 || !string.Equals(currentMicId, activeCaptureId, StringComparison.Ordinal))
                    {
                        WasapiNativeMethods.StopAndReleaseAudioClient(ref micCaptureClient, ref micAudioClient);
                        WasapiNativeMethods.TryOpenEndpointCapture(
                            WasapiNativeMethods.EDataFlowCapture,
                            isLoopback: false,
                            out micAudioClient,
                            out micCaptureClient,
                            out micChannels,
                            out micSampleRate,
                            out micIsFloat,
                            out micBits,
                            out activeCaptureId);
                    }
                }

                bool hadSysPackets = false;
                if (sysCaptureClient != 0)
                {
                    int hr = WasapiNativeMethods.DrainCapturePacketsToFloat(
                        sysCaptureClient,
                        sysChannels,
                        sysIsFloat,
                        sysBits,
                        rawSamples);

                    if (hr == WasapiNativeMethods.AudclntEDeviceInvalidated)
                    {
                        WasapiNativeMethods.StopAndReleaseAudioClient(ref sysCaptureClient, ref sysAudioClient);
                        activeRenderId = null;
                    }
                    else if (rawSamples.Count > 0)
                    {
                        hadSysPackets = true;
                        ReadOnlySpan<float> sysSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rawSamples);
                        if (sysChannels == DualTrackAudioMixer.TargetChannels
                            && sysSampleRate == DualTrackAudioMixer.TargetSampleRate)
                        {
                            int alignedSamples = (sysSpan.Length / DualTrackAudioMixer.TargetChannels) * DualTrackAudioMixer.TargetChannels;
                            _mixer.PushSystemSamples(sysSpan.Slice(0, alignedSamples), _isBufferingForEncoding);
                        }
                        else
                        {
                            PushConvertedSystemSamples(sysSpan, sysChannels, sysSampleRate, ref conversionBuffer);
                        }
                    }
                }

                bool hadMicPackets = false;
                if (micCaptureClient != 0)
                {
                    int hr = WasapiNativeMethods.DrainCapturePacketsToFloat(
                        micCaptureClient,
                        micChannels,
                        micIsFloat,
                        micBits,
                        rawSamples);

                    if (hr == WasapiNativeMethods.AudclntEDeviceInvalidated)
                    {
                        WasapiNativeMethods.StopAndReleaseAudioClient(ref micCaptureClient, ref micAudioClient);
                        activeCaptureId = null;
                    }
                    else if (rawSamples.Count > 0)
                    {
                        hadMicPackets = true;
                        ReadOnlySpan<float> micSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rawSamples);
                        if (micChannels == DualTrackAudioMixer.TargetChannels
                            && micSampleRate == DualTrackAudioMixer.TargetSampleRate)
                        {
                            int alignedSamples = (micSpan.Length / DualTrackAudioMixer.TargetChannels) * DualTrackAudioMixer.TargetChannels;
                            _mixer.PushMicrophoneSamples(micSpan.Slice(0, alignedSamples), _isBufferingForEncoding);
                        }
                        else
                        {
                            PushConvertedMicrophoneSamples(micSpan, micChannels, micSampleRate, ref conversionBuffer);
                        }
                    }
                }

                if (!hadSysPackets && !hadMicPackets)
                {
                    _mixer.DecayMeters();
                }

                if (nowMs - lastUiMeterPublishMs >= 50)
                {
                    lastUiMeterPublishMs = nowMs;
                    PublishStatus(new AudioCaptureStatus(
                        sysCaptureClient == 0 ? SystemAudioSource.Unavailable
                            : usingProcessLoopback ? SystemAudioSource.TargetProcess : SystemAudioSource.SystemWide,
                        sysCaptureClient != 0 && usingProcessLoopback ? activeRequestedProcessId : null,
                        sysCaptureClient != 0 && !usingProcessLoopback && activeRequestedProcessId != 0,
                        micCaptureClient != 0));
                    AudioLevelsUpdated?.Invoke(this, _mixer.CurrentLevels);
                }

                cancellationToken.WaitHandle.WaitOne(_isBufferingForEncoding ? 15 : 30);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WASAPI 音訊擷取迴圈發生非預期例外。");
        }
        finally
        {
            WasapiNativeMethods.StopAndReleaseAudioClient(ref sysCaptureClient, ref sysAudioClient);
            WasapiNativeMethods.StopAndReleaseAudioClient(ref micCaptureClient, ref micAudioClient);
            PublishStatus(new AudioCaptureStatus(SystemAudioSource.Inactive, null, false, false));
        }
    }

    private void PushConvertedSystemSamples(
        ReadOnlySpan<float> samples,
        int channels,
        int sampleRate,
        ref float[] conversionBuffer)
    {
        int requiredSamples = DualTrackAudioMixer.GetRequiredStereo48kHzSamples(samples, channels, sampleRate);
        if (requiredSamples <= 0)
        {
            return;
        }

        if (conversionBuffer.Length < requiredSamples)
        {
            conversionBuffer = new float[requiredSamples];
        }

        int written = DualTrackAudioMixer.TryConvertToStereo48kHz(
            samples,
            channels,
            sampleRate,
            conversionBuffer);
        if (written > 0)
        {
            _mixer.PushSystemSamples(conversionBuffer.AsSpan(0, written), _isBufferingForEncoding);
        }
    }

    private void PushConvertedMicrophoneSamples(
        ReadOnlySpan<float> samples,
        int channels,
        int sampleRate,
        ref float[] conversionBuffer)
    {
        int requiredSamples = DualTrackAudioMixer.GetRequiredStereo48kHzSamples(samples, channels, sampleRate);
        if (requiredSamples <= 0)
        {
            return;
        }

        if (conversionBuffer.Length < requiredSamples)
        {
            conversionBuffer = new float[requiredSamples];
        }

        int written = DualTrackAudioMixer.TryConvertToStereo48kHz(
            samples,
            channels,
            sampleRate,
            conversionBuffer);
        if (written > 0)
        {
            _mixer.PushMicrophoneSamples(conversionBuffer.AsSpan(0, written), _isBufferingForEncoding);
        }
    }

    private void OpenSystemSource(
        uint targetPid,
        ref nint sysAudioClient,
        ref nint sysCaptureClient,
        ref int sysChannels,
        ref int sysSampleRate,
        ref bool sysIsFloat,
        ref int sysBits,
        ref string? activeRenderId,
        ref bool usingProcessLoopback)
    {
        usingProcessLoopback = false;
        activeRenderId = null;

        if (targetPid != 0)
        {
            if (WasapiNativeMethods.TryOpenProcessLoopbackCapture(targetPid, out sysAudioClient, out sysCaptureClient))
            {
                sysChannels = 2;
                sysSampleRate = 48_000;
                sysIsFloat = true;
                sysBits = 32;
                usingProcessLoopback = true;
                return;
            }

            _logger.LogInformation("單一視窗音訊隔離（PID={Pid}）不支援或無法啟用，自動降級為系統全體 Loopback。", targetPid);
            ProcessAudioFallbackTriggered?.Invoke(this, EventArgs.Empty);
        }

        WasapiNativeMethods.TryOpenEndpointCapture(
            WasapiNativeMethods.EDataFlowRender,
            isLoopback: true,
            out sysAudioClient,
            out sysCaptureClient,
            out sysChannels,
            out sysSampleRate,
            out sysIsFloat,
            out sysBits,
            out activeRenderId);
    }

    private void PublishStatus(AudioCaptureStatus status)
    {
        if (_currentStatus != status)
        {
            _currentStatus = status;
            CaptureStatusChanged?.Invoke(this, status);
        }
    }
}
