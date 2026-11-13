using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Microsoft.Extensions.Logging;

namespace Com.VibeCode.CourseRec.Services.Hotkeys;

/// <summary>
/// 透過 <c>RegisterHotKey</c> 與 <c>SetWindowSubclass</c> 實作的全域快捷鍵管理服務。
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const nuint SubclassId = 0xC095E1;

    private readonly ILogger<GlobalHotkeyService> _logger;
    private readonly HotkeyNativeMethods.SubclassProc _subclassDelegate;
    private readonly HashSet<int> _registeredIds = [];

    private nint _hwnd;
    private bool _isSubclassed;
    private bool _isDisposed;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger)
    {
        _logger = logger;
        _subclassDelegate = OnWindowSubclassMessage;
    }

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public void AttachWindow(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (windowHandle == 0 || _hwnd == windowHandle)
        {
            return;
        }

        DetachSubclass();
        _hwnd = windowHandle;
        _isSubclassed = HotkeyNativeMethods.SetWindowSubclass(_hwnd, _subclassDelegate, SubclassId, 0);
    }

    public IReadOnlyList<HotkeyAction> ApplyHotkeys(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_hwnd == 0)
        {
            return [];
        }

        UnregisterAll();
        List<HotkeyAction> failures = [];
        TryRegisterSingle(HotkeyAction.ToggleStartStop, settings.ToggleStartStopHotkey, failures);
        TryRegisterSingle(HotkeyAction.TogglePauseResume, settings.TogglePauseHotkey, failures);
        TryRegisterSingle(HotkeyAction.AddBookmark, settings.AddBookmarkHotkey, failures);
        TryRegisterSingle(HotkeyAction.TakeSnapshot, settings.TakeSnapshotHotkey, failures);
        return failures;
    }

    public void UnregisterAll()
    {
        if (_hwnd == 0)
        {
            return;
        }

        foreach (int id in _registeredIds)
        {
            HotkeyNativeMethods.UnregisterHotKey(_hwnd, id);
        }

        _registeredIds.Clear();
    }

    public static bool TryParseHotkeyGesture(string? gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        string[] tokens = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        foreach (string rawToken in tokens)
        {
            string upper = rawToken.ToUpperInvariant();
            switch (upper)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= HotkeyNativeMethods.ModControl;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyNativeMethods.ModShift;
                    break;
                case "ALT":
                    modifiers |= HotkeyNativeMethods.ModAlt;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= HotkeyNativeMethods.ModWin;
                    break;
                default:
                    if (!TryMapKeyToken(upper, out virtualKey))
                    {
                        return false;
                    }

                    break;
            }
        }

        if (virtualKey == 0 || modifiers == 0)
        {
            return false;
        }

        modifiers |= HotkeyNativeMethods.ModNoRepeat;
        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        UnregisterAll();
        DetachSubclass();
    }

    private void TryRegisterSingle(HotkeyAction action, string gesture, List<HotkeyAction> failures)
    {
        if (!TryParseHotkeyGesture(gesture, out uint modifiers, out uint vk))
        {
            _logger.LogWarning("快捷鍵語法無效，略過註冊：{Action} = '{Gesture}'", action, gesture);
            failures.Add(action);
            return;
        }

        int id = (int)action;
        if (HotkeyNativeMethods.RegisterHotKey(_hwnd, id, modifiers, vk))
        {
            _registeredIds.Add(id);
        }
        else
        {
            _logger.LogWarning("全域快捷鍵已被其他程式佔用：{Action} = '{Gesture}'", action, gesture);
            failures.Add(action);
        }
    }

    private void DetachSubclass()
    {
        if (_isSubclassed && _hwnd != 0)
        {
            HotkeyNativeMethods.RemoveWindowSubclass(_hwnd, _subclassDelegate, SubclassId);
            _isSubclassed = false;
        }
    }

    private nint OnWindowSubclassMessage(
        nint hWnd,
        uint uMsg,
        nuint wParam,
        nint lParam,
        nuint uIdSubclass,
        nuint dwRefData)
    {
        if (uMsg == HotkeyNativeMethods.WmHotkey)
        {
            int id = (int)wParam;
            if (Enum.IsDefined(typeof(HotkeyAction), id))
            {
                HotkeyPressed?.Invoke(this, (HotkeyAction)id);
                return 0;
            }
        }
        else if (uMsg == HotkeyNativeMethods.WmNcdestroy)
        {
            UnregisterAll();
            DetachSubclass();
        }

        return HotkeyNativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private static bool TryMapKeyToken(string upperToken, out uint vk)
    {
        vk = 0;
        if (upperToken.Length == 1)
        {
            char ch = upperToken[0];
            if (ch is >= 'A' and <= 'Z')
            {
                vk = ch;
                return true;
            }

            if (ch is >= '0' and <= '9')
            {
                vk = ch;
                return true;
            }
        }

        if (upperToken.StartsWith('F')
            && int.TryParse(upperToken.AsSpan(1), out int fnNumber)
            && fnNumber is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + (fnNumber - 1)); // VK_F1 = 0x70
            return true;
        }

        vk = upperToken switch
        {
            "SPACE" => 0x20u,
            "SNAPSHOT" or "PRINTSCREEN" => 0x2Cu,
            "INSERT" => 0x2Du,
            "DELETE" => 0x2Eu,
            "HOME" => 0x24u,
            "END" => 0x23u,
            _ => 0u,
        };

        return vk != 0;
    }
}
