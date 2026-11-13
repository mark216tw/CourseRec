using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services.Encoding;

/// <summary>
/// Media Foundation H.264 / AAC 硬體加速 MP4 寫入器服務介面。
/// </summary>
public interface IMp4EncoderService : IDisposable
{
    bool IsOpen { get; }

    RecordingProfile? ActiveProfile { get; }

    string? OutputFilePath { get; }

    void Open(string outputFilePath, RecordingProfile profile, int sourceWidth = 0, int sourceHeight = 0);

    void WriteVideoFrameBgra(
        ReadOnlySpan<byte> bgraPixels,
        int sourceWidth,
        int sourceHeight,
        long sampleTime100Ns,
        long duration100Ns);

    void WriteAudioPcm16(
        ReadOnlySpan<short> stereoPcm16,
        long sampleTime100Ns,
        long duration100Ns);

    /// <summary>完成封裝並釋放資源；失敗時拋出例外，不可視為成功錄影。</summary>
    void FinalizeRecording();
}
