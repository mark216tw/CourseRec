using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Com.VibeCode.CourseRec.Services.Settings;

/// <summary>
/// 管理所有已開啟視窗的顯示模式與主題顏色切換實作。
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly Dictionary<AppColorTheme, string> ThemeSources = new()
    {
        [AppColorTheme.Ocean] = "ms-appx:///Styles/Themes/OceanTheme.xaml",
        [AppColorTheme.EyeCareYellow] = "ms-appx:///Styles/Themes/EyeCareYellowTheme.xaml",
        [AppColorTheme.Forest] = "ms-appx:///Styles/Themes/ForestTheme.xaml",
        [AppColorTheme.Sunset] = "ms-appx:///Styles/Themes/SunsetTheme.xaml",
        [AppColorTheme.Lavender] = "ms-appx:///Styles/Themes/LavenderTheme.xaml",
    };

    private readonly List<WindowThemeState> _windows = [];

    public AppDisplayMode CurrentDisplayMode { get; private set; } = AppDisplayMode.System;

    public AppColorTheme CurrentColorTheme { get; private set; } = AppColorTheme.Ocean;

    public void RegisterWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        RemoveClosedWindows();
        WindowThemeState? existing = _windows.FirstOrDefault(state =>
            state.Window.TryGetTarget(out Window? target) && ReferenceEquals(target, window));

        if (existing is null)
        {
            existing = new WindowThemeState(window);
            _windows.Add(existing);
        }

        ApplyToWindow(existing, CurrentDisplayMode, CurrentColorTheme);
    }

    public void UnregisterWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _windows.RemoveAll(state =>
            !state.Window.TryGetTarget(out Window? target) || ReferenceEquals(target, window));
    }

    public void ApplyTheme(AppDisplayMode displayMode, AppColorTheme colorTheme)
    {
        CurrentDisplayMode = displayMode;
        CurrentColorTheme = colorTheme;

        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            ApplyToWindow(_windows[i], displayMode, colorTheme);
            if (!_windows[i].Window.TryGetTarget(out _))
            {
                _windows.RemoveAt(i);
            }
        }
    }

    private static void ApplyToWindow(
        WindowThemeState state,
        AppDisplayMode displayMode,
        AppColorTheme colorTheme)
    {
        if (!state.Window.TryGetTarget(out Window? window) || window.Content is not FrameworkElement rootElement)
        {
            return;
        }

        bool paletteChanged = state.AppliedColorTheme != colorTheme || state.AppliedCustomTheme is null;
        if (paletteChanged)
        {
            if (state.AppliedCustomTheme is not null)
            {
                rootElement.Resources.MergedDictionaries.Remove(state.AppliedCustomTheme);
                state.AppliedCustomTheme = null;
            }

            if (ThemeSources.TryGetValue(colorTheme, out string? sourceUri))
            {
                var customTheme = new ResourceDictionary
                {
                    Source = new Uri(sourceUri, UriKind.Absolute),
                };
                rootElement.Resources.MergedDictionaries.Add(customTheme);
                state.AppliedCustomTheme = customTheme;
            }

            state.AppliedColorTheme = colorTheme;
        }

        ElementTheme requestedTheme = displayMode switch
        {
            AppDisplayMode.Light => ElementTheme.Light,
            AppDisplayMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        rootElement.RequestedTheme = requestedTheme;

        if (paletteChanged)
        {
            ElementTheme oppositeTheme = rootElement.ActualTheme == ElementTheme.Light
                ? ElementTheme.Dark
                : ElementTheme.Light;
            rootElement.RequestedTheme = oppositeTheme;
            rootElement.RequestedTheme = requestedTheme;
        }

        TryApplyTitleBarTheme(window, rootElement.ActualTheme == ElementTheme.Dark);
    }

    private static void TryApplyTitleBarTheme(Window window, bool isDark)
    {
        try
        {
            nint windowHandle = WindowNative.GetWindowHandle(window);
            WindowNativeMethods.TrySetImmersiveDarkMode(windowHandle, isDark);
        }
        catch
        {
            // Windows 10 1809 等不支援或視窗尚未建立時維持系統預設標題列。
        }
    }

    private void RemoveClosedWindows()
    {
        _windows.RemoveAll(state => !state.Window.TryGetTarget(out _));
    }

    private sealed class WindowThemeState
    {
        public WindowThemeState(Window window)
        {
            Window = new WeakReference<Window>(window);
        }

        public WeakReference<Window> Window { get; }

        public ResourceDictionary? AppliedCustomTheme { get; set; }

        public AppColorTheme? AppliedColorTheme { get; set; }
    }
}
