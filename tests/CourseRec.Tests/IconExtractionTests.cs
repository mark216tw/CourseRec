using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Xunit;

namespace Com.VibeCode.CourseRec.Tests;

public sealed class IconExtractionTests
{
    [Fact]
    public void MonitorIcon_IsDecodedToValidBgraBuffer()
    {
        IconData? icon = WindowIconHelper.GetMonitorIcon();

        Assert.NotNull(icon);
        Assert.True(icon.Width > 0);
        Assert.True(icon.Height > 0);
        Assert.Equal(icon.ExpectedLength, icon.Bgra.Length);
    }
}
