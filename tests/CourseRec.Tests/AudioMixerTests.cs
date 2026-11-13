using Com.VibeCode.CourseRec.Services.Audio;
using Xunit;

namespace Com.VibeCode.CourseRec.Tests;

public sealed class AudioMixerTests
{
    [Fact]
    public void AudioRingBuffer_WriteAndReadWithZeroFill_AlignsUnderrunAndOverflow()
    {
        var ring = new AudioRingBuffer(8);
        float[] src = [0.1f, 0.2f, 0.3f, 0.4f];
        ring.Write(src);

        float[] dst = new float[6];
        int actualRead = ring.ReadWithZeroFill(dst);

        Assert.Equal(4, actualRead);
        Assert.Equal(0.1f, dst[0], 4);
        Assert.Equal(0.4f, dst[3], 4);
        Assert.Equal(0.0f, dst[4], 4);
        Assert.Equal(0.0f, dst[5], 4);

        // Overflow drops oldest and retains newest 8 samples
        float[] burst = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f];
        ring.Write(burst);
        Assert.Equal(8, ring.AvailableSamples);

        float[] outBurst = new float[8];
        ring.ReadWithZeroFill(outBurst);
        Assert.Equal(3f, outBurst[0], 4);
        Assert.Equal(10f, outBurst[7], 4);
    }

    [Fact]
    public void DualTrackAudioMixer_SoftLimiterPreventsClippingDistortion()
    {
        var mixer = new DualTrackAudioMixer(bufferSeconds: 1)
        {
            MuteMicrophone = false,
        };

        // Both tracks near full scale (0.9f + 0.9f = 1.8f raw sum)
        float[] loudSystem = [0.9f, -0.9f, 0.5f, -0.5f];
        float[] loudMic = [0.9f, -0.9f, 0.2f, -0.2f];

        mixer.PushSystemSamples(loudSystem, enqueueForRecording: true);
        mixer.PushMicrophoneSamples(loudMic, enqueueForRecording: true);

        short[] pcm16 = new short[4];
        mixer.MixStereoFramesToPcm16(frameCount: 2, pcm16);

        // Soft limiter keeps result strictly inside [-32767, 32767] without wrap-around
        Assert.InRange(pcm16[0], (short)28000, short.MaxValue);
        Assert.InRange(pcm16[1], short.MinValue, (short)-28000);

        // Below knee (0.5 + 0.2 = 0.7 <= 0.85) stays linear
        int expectedLinear = (int)MathF.Round(0.7f * 32767f);
        Assert.InRange(pcm16[2], expectedLinear - 2, expectedLinear + 2);
    }

    [Fact]
    public void DualTrackAudioMixer_ListenOnlyModeHardMutesMicrophone()
    {
        var mixer = new DualTrackAudioMixer(bufferSeconds: 1)
        {
            MuteMicrophone = true,
        };

        float[] sys = [0.25f, -0.25f];
        float[] mic = [0.95f, -0.95f];

        mixer.PushSystemSamples(sys, enqueueForRecording: true);
        mixer.PushMicrophoneSamples(mic, enqueueForRecording: true);

        Assert.Equal(0f, mixer.CurrentLevels.MicrophonePeak);
        Assert.False(mixer.CurrentLevels.IsMicrophoneClipping);

        short[] pcm16 = new short[2];
        mixer.MixStereoFramesToPcm16(frameCount: 1, pcm16);

        int expected = (int)MathF.Round(0.25f * 32767f);
        Assert.InRange(pcm16[0], expected - 2, expected + 2);
    }

    [Fact]
    public void ConvertToStereo48kHz_ResamplesMono44100ToStereo48000()
    {
        float[] mono44k = new float[441];
        for (int i = 0; i < mono44k.Length; i++)
        {
            mono44k[i] = 0.5f;
        }

        float[] stereo48k = DualTrackAudioMixer.ConvertToStereo48kHz(mono44k, inputChannels: 1, inputSampleRate: 44_100);
        Assert.Equal(480 * 2, stereo48k.Length);
        Assert.All(stereo48k, s => Assert.InRange(s, 0.499f, 0.501f));
    }

    [Fact]
    public void ConvertToStereo48kHz_Surround51PreservesFrontCenterSpeech()
    {
        // 5.1 layout: FL=0, FR=0, FC=0.8 (speech), LFE=0.9 (ignored), BL=0.1, BR=0.2
        float[] surround51 = [0f, 0f, 0.8f, 0.9f, 0.1f, 0.2f, 0f, 0f, 0.8f, 0.9f, 0.1f, 0.2f];
        float[] stereo = DualTrackAudioMixer.ConvertToStereo48kHz(surround51, inputChannels: 6, inputSampleRate: 48_000);

        Assert.Equal(4, stereo.Length);
        float expectedLeft = (0.8f * 0.70710678f) + (0.1f * 0.5f);
        float expectedRight = (0.8f * 0.70710678f) + (0.2f * 0.5f);
        Assert.Equal(expectedLeft, stereo[0], 3);
        Assert.Equal(expectedRight, stereo[1], 3);
    }

    [Fact]
    public void TryConvertToStereo48kHz_MatchesAllocatingOverloadAndRejectsSmallDestination()
    {
        float[] mono44k = new float[441];
        Array.Fill(mono44k, 0.5f);

        int required = DualTrackAudioMixer.GetRequiredStereo48kHzSamples(mono44k, 1, 44_100);
        Assert.Equal(960, required);

        float[] destination = new float[required];
        int written = DualTrackAudioMixer.TryConvertToStereo48kHz(mono44k, 1, 44_100, destination);

        Assert.Equal(required, written);
        float[] expected = DualTrackAudioMixer.ConvertToStereo48kHz(mono44k, 1, 44_100);
        Assert.Equal(expected, destination);

        float[] tooSmall = new float[required - 1];
        Assert.Equal(0, DualTrackAudioMixer.TryConvertToStereo48kHz(mono44k, 1, 44_100, tooSmall));
    }

    [Fact]
    public void DualTrackAudioMixer_LargeStepFrameCount_MixesViaArrayPoolCleanly()
    {
        var mixer = new DualTrackAudioMixer(bufferSeconds: 1) { MuteMicrophone = false };
        float[] sys = new float[6400];
        Array.Fill(sys, 0.4f);
        mixer.PushSystemSamples(sys, enqueueForRecording: true);

        short[] pcm16 = new short[6400];
        mixer.MixStereoFramesToPcm16(frameCount: 3200, pcm16);

        int expected = (int)MathF.Round(0.4f * 32767f);
        Assert.InRange(pcm16[0], expected - 2, expected + 2);
        Assert.InRange(pcm16[6399], expected - 2, expected + 2);
    }
}
