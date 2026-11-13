namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 已成功建立的擷取來源快照，不代表來源當下必然有聲音。
/// </summary>
public sealed record AudioCaptureStatus(
    SystemAudioSource SystemSource,
    uint? ProcessId,
    bool IsSystemAudioFallback,
    bool IsMicrophoneAvailable);
