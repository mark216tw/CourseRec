namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 編碼參數組合（解析度、幀率、視訊與音訊位元率）。
/// </summary>
public sealed record RecordingProfile(
    QualityPreset Preset,
    int Width,
    int Height,
    int FrameRate,
    uint VideoBitrateBps,
    uint AudioSampleRate,
    uint AudioChannels,
    uint AudioBitrateBps)
{
    /// <summary>
    /// 預估每秒產生位元組數（含容器額外開銷 5%）。
    /// </summary>
    public double EstimatedBytesPerSecond => ((VideoBitrateBps + AudioBitrateBps) / 8.0) * 1.05;

    /// <summary>
    /// 依據來源尺寸與畫質預設建立偶數對齊的編碼設定。
    /// </summary>
    public static RecordingProfile Create(QualityPreset preset, int sourceWidth, int sourceHeight)
    {
        int clampedSourceW = Math.Max(320, sourceWidth);
        int clampedSourceH = Math.Max(240, sourceHeight);

        if (preset == QualityPreset.SlidePresentation)
        {
            double scale = 1.0;
            if (clampedSourceW > 1920 || clampedSourceH > 1080)
            {
                scale = Math.Min(1920.0 / clampedSourceW, 1080.0 / clampedSourceH);
            }

            int targetW = AlignEven((int)Math.Round(clampedSourceW * scale));
            int targetH = AlignEven((int)Math.Round(clampedSourceH * scale));
            uint bitrate = (uint)Math.Clamp(targetW * targetH * 0.72, 600_000, 1_800_000);

            return new RecordingProfile(
                QualityPreset.SlidePresentation,
                targetW,
                targetH,
                FrameRate: 15,
                VideoBitrateBps: bitrate,
                AudioSampleRate: 48_000,
                AudioChannels: 2,
                AudioBitrateBps: 128_000);
        }

        int smoothW = AlignEven(Math.Min(3840, clampedSourceW));
        int smoothH = AlignEven(Math.Min(2160, clampedSourceH));
        uint smoothBitrate = (uint)Math.Clamp(smoothW * smoothH * 2.4, 2_000_000, 12_000_000);

        return new RecordingProfile(
            QualityPreset.StandardSmooth,
            smoothW,
            smoothH,
            FrameRate: 30,
            VideoBitrateBps: smoothBitrate,
            AudioSampleRate: 48_000,
            AudioChannels: 2,
            AudioBitrateBps: 160_000);
    }

    /// <summary>
    /// 依據可用磁碟空間計算預估可錄時長。
    /// </summary>
    public TimeSpan EstimateRemainingDuration(long availableFreeBytes)
    {
        const long safetyReserveBytes = 512L * 1024L * 1024L;
        long usableBytes = Math.Max(0L, availableFreeBytes - safetyReserveBytes);
        double seconds = usableBytes / Math.Max(1.0, EstimatedBytesPerSecond);
        return TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.FromHours(999).TotalSeconds));
    }

    private static int AlignEven(int value)
    {
        int positive = Math.Max(2, value);
        return (positive & 1) == 0 ? positive : positive - 1;
    }
}
