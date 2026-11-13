using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services.Audio;

/// <summary>
/// 原生 WASAPI 雙軌擷取（系統／單一視窗 Loopback ＋ 麥克風）、即時音量監控與混音輸出服務介面。
/// </summary>
public interface IAudioCaptureService : IDisposable
{
    bool IsMonitoring { get; }

    bool IsBufferingForEncoding { get; set; }

    bool MuteMicrophone { get; set; }

    AudioLevelReading CurrentLevels { get; }

    AudioCaptureStatus CurrentStatus { get; }

    event EventHandler<AudioCaptureStatus>? CaptureStatusChanged;

    event EventHandler<AudioLevelReading>? AudioLevelsUpdated;

    event EventHandler? ProcessAudioFallbackTriggered;

    void StartMonitoring(uint? targetProcessId);

    void UpdateSystemSource(uint? targetProcessId);

    void StopMonitoring();

    Task StopMonitoringAsync(CancellationToken cancellationToken = default);

    void ResetEncodingBuffers();

    void MixNextStereoFramesToPcm16(int frameCount, Span<short> destinationPcm16);
}
