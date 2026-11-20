using System.Runtime.InteropServices;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Microsoft.Extensions.Logging;

namespace Com.VibeCode.CourseRec.Services.Encoding;

/// <summary>
/// 基於 <c>IMFSinkWriter</c> 的 H.264 + AAC MP4 安全編碼器實作。
/// </summary>
public sealed class Mp4EncoderService : IMp4EncoderService
{
    private readonly ILogger<Mp4EncoderService> _logger;
    private readonly object _writerLock = new();
    private readonly object _mfLock = new();
    private readonly (nint Buffer, uint Capacity)[] _videoBufferPool = new (nint, uint)[6];
    private readonly (nint Buffer, uint Capacity)[] _audioBufferPool = new (nint, uint)[4];

    private bool _mfInitializationAttempted;
    private bool _mfInitialized;
    private nint _sinkWriter;
    private uint _videoStreamIndex;
    private uint _audioStreamIndex;
    private int _configuredInputWidth;
    private int _configuredInputHeight;
    private byte[] _scaledFrameBuffer = [];
    private bool _isDisposed;

    public Mp4EncoderService(ILogger<Mp4EncoderService> logger)
    {
        _logger = logger;
    }

    public bool IsOpen => _sinkWriter != 0;

    private bool EnsureMediaFoundationInitialized()
    {
        lock (_mfLock)
        {
            if (_mfInitializationAttempted)
            {
                return _mfInitialized;
            }

            _mfInitializationAttempted = true;
            int hr = MediaFoundationNativeMethods.MFStartup(
                MediaFoundationNativeMethods.MfVersion,
                MediaFoundationNativeMethods.MfStartupNosocket);
            _mfInitialized = hr >= 0;
            if (!_mfInitialized)
            {
                _logger.LogError("MFStartup 初始化失敗，HRESULT=0x{Hr:X8}", hr);
            }

            return _mfInitialized;
        }
    }

    public RecordingProfile? ActiveProfile { get; private set; }

    public string? OutputFilePath { get; private set; }

    public bool PreferFragmentedMp4 { get; set; } = true;

    public void Open(string outputFilePath, RecordingProfile profile, int sourceWidth = 0, int sourceHeight = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);
        ArgumentNullException.ThrowIfNull(profile);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_writerLock)
        {
            if (!EnsureMediaFoundationInitialized())
            {
                throw new InvalidOperationException("Media Foundation 初始化失敗，無法開始編碼。");
            }

            FinalizeRecording();

            string? dir = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            int reqW = sourceWidth > 0 ? sourceWidth : profile.Width;
            int reqH = sourceHeight > 0 ? sourceHeight : profile.Height;

            int hr = MediaFoundationNativeMethods.CreateConfiguredMp4SinkWriter(
                outputFilePath,
                profile,
                reqW,
                reqH,
                PreferFragmentedMp4,
                out _sinkWriter,
                out _videoStreamIndex,
                out _audioStreamIndex,
                out _configuredInputWidth,
                out _configuredInputHeight);

            if (hr < 0 || _sinkWriter == 0)
            {
                throw new InvalidOperationException($"無法建立 MP4 硬體編碼管線（HRESULT=0x{hr:X8}）。");
            }

            ActiveProfile = profile;
            OutputFilePath = outputFilePath;
        }
    }

    public void WriteVideoFrameBgra(
        ReadOnlySpan<byte> bgraPixels,
        int sourceWidth,
        int sourceHeight,
        long sampleTime100Ns,
        long duration100Ns)
    {
        lock (_writerLock)
        {
            if (_sinkWriter == 0 || ActiveProfile is null || bgraPixels.IsEmpty)
            {
                return;
            }

            int dstW = _configuredInputWidth > 0 ? _configuredInputWidth : ActiveProfile.Width;
            int dstH = _configuredInputHeight > 0 ? _configuredInputHeight : ActiveProfile.Height;
            int requiredBytes = dstW * dstH * 4;

            ReadOnlySpan<byte> payload;
            if (sourceWidth == dstW && sourceHeight == dstH && bgraPixels.Length >= requiredBytes)
            {
                payload = bgraPixels.Slice(0, requiredBytes);
            }
            else
            {
                if (_scaledFrameBuffer.Length < requiredBytes)
                {
                    _scaledFrameBuffer = new byte[requiredBytes];
                }

                ScaleBgraIntoBuffer(bgraPixels, sourceWidth, sourceHeight, _scaledFrameBuffer, dstW, dstH);
                payload = _scaledFrameBuffer.AsSpan(0, requiredBytes);
            }

            int hr = MediaFoundationNativeMethods.WriteBufferSample(
                _sinkWriter,
                _videoStreamIndex,
                payload,
                sampleTime100Ns,
                duration100Ns,
                _videoBufferPool);

            if (hr < 0)
            {
                throw new COMException("Video sample write failed.", hr);
            }
        }
    }

    public void WriteAudioPcm16(
        ReadOnlySpan<short> stereoPcm16,
        long sampleTime100Ns,
        long duration100Ns)
    {
        lock (_writerLock)
        {
            if (_sinkWriter == 0 || stereoPcm16.IsEmpty)
            {
                return;
            }

            ReadOnlySpan<byte> byteSpan = MemoryMarshal.AsBytes(stereoPcm16);
            int hr = MediaFoundationNativeMethods.WriteBufferSample(
                _sinkWriter,
                _audioStreamIndex,
                byteSpan,
                sampleTime100Ns,
                duration100Ns,
                _audioBufferPool);

            if (hr < 0)
            {
                throw new COMException("Audio sample write failed.", hr);
            }
        }
    }

    public void FinalizeRecording()
    {
        lock (_writerLock)
        {
            int hr = 0;
            try
            {
                if (_sinkWriter != 0)
                {
                    hr = MediaFoundationNativeMethods.FinalizeAndReleaseSinkWriter(ref _sinkWriter);
                }
            }
            finally
            {
                MediaFoundationNativeMethods.ReleaseBufferPool(_videoBufferPool);
                MediaFoundationNativeMethods.ReleaseBufferPool(_audioBufferPool);
                _scaledFrameBuffer = [];
                _configuredInputWidth = 0;
                _configuredInputHeight = 0;
                ActiveProfile = null;
            }

            if (hr < 0)
            {
                throw new COMException("MP4 finalization failed.", hr);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            FinalizeRecording();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "釋放編碼器時封裝失敗。");
        }
        finally
        {
            if (_mfInitialized)
            {
                _ = MediaFoundationNativeMethods.MFShutdown();
            }
        }
    }

    public static void ScaleBgraIntoBuffer(
        ReadOnlySpan<byte> src,
        int srcW,
        int srcH,
        byte[] dst,
        int dstW,
        int dstH)
    {
        BgraScaler.ScaleBgraIntoBuffer(src, srcW, srcH, dst, dstW, dstH);
    }
}
