using System.Buffers;
using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services.Audio;

/// <summary>
/// 48kHz 雙聲道雙軌即時混音器（含取樣率／聲道正規化、即時雙 VU 峰值計算與 Soft Limiter 防爆音）。
/// </summary>
public sealed class DualTrackAudioMixer
{
    public const int TargetSampleRate = 48_000;
    public const int TargetChannels = 2;

    private const float SoftKneeThreshold = 0.85f;
    private const float ClippingIndicatorThreshold = 0.98f;

    private readonly AudioRingBuffer _systemBuffer;
    private readonly AudioRingBuffer _microphoneBuffer;
    private volatile bool _muteMicrophone;
    private float _smoothedSystemPeak;
    private float _smoothedMicPeak;
    private bool _systemClipping;
    private bool _micClipping;

    public DualTrackAudioMixer(int bufferSeconds = 4)
    {
        int capacitySamples = Math.Max(1, bufferSeconds) * TargetSampleRate * TargetChannels;
        _systemBuffer = new AudioRingBuffer(capacitySamples);
        _microphoneBuffer = new AudioRingBuffer(capacitySamples);
    }

    public bool MuteMicrophone
    {
        get => _muteMicrophone;
        set
        {
            _muteMicrophone = value;
            if (value)
            {
                _microphoneBuffer.ReleaseBuffer();
                _smoothedMicPeak = 0f;
                _micClipping = false;
            }
        }
    }

    public AudioLevelReading CurrentLevels => new(
        SystemPeak: _smoothedSystemPeak,
        MicrophonePeak: _muteMicrophone ? 0f : _smoothedMicPeak,
        IsSystemClipping: _systemClipping,
        IsMicrophoneClipping: !_muteMicrophone && _micClipping);

    /// <summary>
    /// 推入已正規化為 48kHz 雙聲道交錯浮點數的課程／系統音訊，並即時更新 VU 峰值。
    /// </summary>
    public void PushSystemSamples(ReadOnlySpan<float> stereo48kSamples, bool enqueueForRecording)
    {
        UpdateMeter(stereo48kSamples, ref _smoothedSystemPeak, ref _systemClipping);
        if (enqueueForRecording && !stereo48kSamples.IsEmpty)
        {
            _systemBuffer.Write(stereo48kSamples);
        }
    }

    /// <summary>
    /// 推入已正規化為 48kHz 雙聲道交錯浮點數的麥克風音訊（純聽課模式下自動靜音拋棄）。
    /// </summary>
    public void PushMicrophoneSamples(ReadOnlySpan<float> stereo48kSamples, bool enqueueForRecording)
    {
        if (_muteMicrophone)
        {
            _smoothedMicPeak = 0f;
            _micClipping = false;
            return;
        }

        UpdateMeter(stereo48kSamples, ref _smoothedMicPeak, ref _micClipping);
        if (enqueueForRecording && !stereo48kSamples.IsEmpty)
        {
            _microphoneBuffer.Write(stereo48kSamples);
        }
    }

    /// <summary>
    /// 混音指定數量的雙聲道訊框（每訊框 2 個樣本），輸出經 Soft Limiter 保護的浮點數與 16-bit PCM。
    /// </summary>
    public void MixStereoFramesToPcm16(int frameCount, Span<short> destinationPcm16)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        int sampleCount = frameCount * TargetChannels;
        if (destinationPcm16.Length < sampleCount)
        {
            throw new ArgumentException("目標 PCM16 緩衝區長度不足。", nameof(destinationPcm16));
        }

        float[]? rentedSys = null;
        float[]? rentedMic = null;
        Span<float> sysTemp = sampleCount <= 8192
            ? stackalloc float[sampleCount]
            : (rentedSys = ArrayPool<float>.Shared.Rent(sampleCount)).AsSpan(0, sampleCount);
        Span<float> micTemp = sampleCount <= 8192
            ? stackalloc float[sampleCount]
            : (rentedMic = ArrayPool<float>.Shared.Rent(sampleCount)).AsSpan(0, sampleCount);

        try
        {
            _systemBuffer.ReadWithZeroFill(sysTemp);
            bool muteMic = _muteMicrophone;
            if (!muteMic)
            {
                _microphoneBuffer.ReadWithZeroFill(micTemp);
            }

            for (int i = 0; i < sampleCount; i++)
            {
                float sum = sysTemp[i] + (muteMic ? 0f : micTemp[i]);
                float limited = ApplySoftLimiter(sum);
                int pcm = (int)MathF.Round(limited * 32767f);
                destinationPcm16[i] = (short)Math.Clamp(pcm, short.MinValue, short.MaxValue);
            }
        }
        finally
        {
            if (rentedSys is not null)
            {
                ArrayPool<float>.Shared.Return(rentedSys);
            }

            if (rentedMic is not null)
            {
                ArrayPool<float>.Shared.Return(rentedMic);
            }
        }
    }

    /// <summary>
    /// 軟限幅函式：於 0.85 以下維持線性無失真，超過 0.85 平滑壓縮漸近至 1.0，徹底消除雙軌疊加破音。
    /// </summary>
    public static float ApplySoftLimiter(float sample)
    {
        if (float.IsNaN(sample) || float.IsInfinity(sample))
        {
            return 0f;
        }

        float abs = MathF.Abs(sample);
        if (abs <= SoftKneeThreshold)
        {
            return sample;
        }

        float headroom = 1.0f - SoftKneeThreshold;
        float excess = (abs - SoftKneeThreshold) / headroom;
        float compressed = SoftKneeThreshold + (headroom * (excess / (1.0f + excess)));
        return MathF.CopySign(Math.Clamp(compressed, 0f, 1.0f), sample);
    }

    /// <summary>
    /// 將任意聲道數與取樣率的浮點 PCM 訊框轉換為 48kHz 雙聲道交錯浮點數。
    /// </summary>
    public static float[] ConvertToStereo48kHz(
        ReadOnlySpan<float> inputSamples,
        int inputChannels,
        int inputSampleRate)
    {
        int requiredSamples = GetStereo48kHzSampleCount(inputSamples, inputChannels, inputSampleRate);
        if (requiredSamples == 0)
        {
            return [];
        }

        float[] output = new float[requiredSamples];
        int written = TryConvertToStereo48kHz(inputSamples, inputChannels, inputSampleRate, output);
        return written == requiredSamples ? output : output[..written];
    }

    public static int TryConvertToStereo48kHz(
        ReadOnlySpan<float> inputSamples,
        int inputChannels,
        int inputSampleRate,
        Span<float> destination)
    {
        int requiredSamples = GetStereo48kHzSampleCount(inputSamples, inputChannels, inputSampleRate);
        if (requiredSamples == 0 || destination.Length < requiredSamples)
        {
            return 0;
        }

        if (inputSampleRate == TargetSampleRate && inputChannels == TargetChannels)
        {
            inputSamples.Slice(0, requiredSamples).CopyTo(destination);
            return requiredSamples;
        }

        int inputFrames = inputSamples.Length / inputChannels;
        int outputFrames = requiredSamples / TargetChannels;
        double step = (double)inputSampleRate / TargetSampleRate;

        for (int outFrame = 0; outFrame < outputFrames; outFrame++)
        {
            double srcPos = outFrame * step;
            int idx0 = Math.Min((int)srcPos, inputFrames - 1);
            int idx1 = Math.Min(idx0 + 1, inputFrames - 1);
            float frac = (float)(srcPos - idx0);

            ReadOnlySpan<float> frame0 = inputSamples.Slice(idx0 * inputChannels, inputChannels);
            ReadOnlySpan<float> frame1 = inputSamples.Slice(idx1 * inputChannels, inputChannels);

            DownmixFrameToStereo(frame0, out float l0, out float r0);
            DownmixFrameToStereo(frame1, out float l1, out float r1);

            destination[outFrame * 2] = l0 + ((l1 - l0) * frac);
            destination[(outFrame * 2) + 1] = r0 + ((r1 - r0) * frac);
        }

        return requiredSamples;
    }

    public static int GetRequiredStereo48kHzSamples(
        ReadOnlySpan<float> inputSamples,
        int inputChannels,
        int inputSampleRate)
    {
        return GetStereo48kHzSampleCount(inputSamples, inputChannels, inputSampleRate);
    }

    private static int GetStereo48kHzSampleCount(
        ReadOnlySpan<float> inputSamples,
        int inputChannels,
        int inputSampleRate)
    {
        if (inputSamples.IsEmpty || inputChannels <= 0 || inputSampleRate <= 0)
        {
            return 0;
        }

        int inputFrames = inputSamples.Length / inputChannels;
        if (inputFrames == 0)
        {
            return 0;
        }

        int outputFrames = inputSampleRate == TargetSampleRate
            ? inputFrames
            : Math.Max(1, (int)Math.Round((double)inputFrames * TargetSampleRate / inputSampleRate));
        return outputFrames * TargetChannels;
    }

    private static void DownmixFrameToStereo(ReadOnlySpan<float> frame, out float left, out float right)
    {
        if (frame.Length == 1)
        {
            left = frame[0];
            right = frame[0];
            return;
        }

        left = frame[0];
        right = frame[1];
        if (frame.Length >= 3)
        {
            // Fold Front Center (-3dB = 0.7071f) so speech in 5.1/7.1 streams is preserved
            float centerMix = frame[2] * 0.70710678f;
            left += centerMix;
            right += centerMix;
        }

        if (frame.Length >= 6)
        {
            left += frame[4] * 0.5f;
            right += frame[5] * 0.5f;
        }

        if (frame.Length >= 8)
        {
            left += frame[6] * 0.5f;
            right += frame[7] * 0.5f;
        }
    }

    public void ResetBuffers()
    {
        _systemBuffer.Clear();
        _microphoneBuffer.Clear();
    }

    public void ReleaseBuffers()
    {
        _systemBuffer.ReleaseBuffer();
        _microphoneBuffer.ReleaseBuffer();
    }

    public void DecayMeters()
    {
        _smoothedSystemPeak *= 0.78f;
        _smoothedMicPeak *= 0.78f;
        if (_smoothedSystemPeak < 0.005f)
        {
            _smoothedSystemPeak = 0f;
            _systemClipping = false;
        }

        if (_smoothedMicPeak < 0.005f)
        {
            _smoothedMicPeak = 0f;
            _micClipping = false;
        }
    }

    private static void UpdateMeter(ReadOnlySpan<float> samples, ref float smoothedPeak, ref bool isClipping)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = MathF.Abs(samples[i]);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        isClipping = peak >= ClippingIndicatorThreshold;
        float clamped = Math.Clamp(peak, 0f, 1f);
        smoothedPeak = clamped >= smoothedPeak
            ? clamped
            : (smoothedPeak * 0.72f) + (clamped * 0.28f);
    }
}
