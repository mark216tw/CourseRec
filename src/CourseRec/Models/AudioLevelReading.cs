namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 即時雙軌音量表讀數（範圍 0.0 ~ 1.0）。
/// </summary>
public readonly record struct AudioLevelReading(
    float SystemPeak,
    float MicrophonePeak,
    bool IsSystemClipping,
    bool IsMicrophoneClipping);
