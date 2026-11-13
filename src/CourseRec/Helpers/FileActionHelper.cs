using System.Diagnostics;

namespace Com.VibeCode.CourseRec.Helpers;

internal static class FileActionHelper
{
    public static void Open(string path)
    {
        EnsureExists(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    public static void Reveal(string path)
    {
        EnsureExists(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
    }

    private static void EnsureExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(null, path);
        }
    }
}
