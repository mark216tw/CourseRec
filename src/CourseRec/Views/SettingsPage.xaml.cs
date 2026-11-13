using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace Com.VibeCode.CourseRec.Views;

/// <summary>
/// 設定頁面。
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        InfoBarAutoDismiss.Attach(StatusInfoBar);
    }

    public SettingsViewModel ViewModel { get; }

    private async void OnBrowseOutputDirectoryClicked(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App { MainWindowInstance: MainWindow mainWindow })
        {
            return;
        }

        var folderPicker = new FolderPicker();
        folderPicker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(folderPicker, mainWindow.WindowHandle);

        StorageFolder? folder = await folderPicker.PickSingleFolderAsync();
        if (folder is not null && !string.IsNullOrWhiteSpace(folder.Path))
        {
            ViewModel.OutputDirectory = folder.Path;
        }
    }

    private void OnHotkeyTextBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
        {
            e.Handled = true;
            return;
        }

        bool ctrlDown = IsKeyDown(VirtualKey.Control);
        bool shiftDown = IsKeyDown(VirtualKey.Shift);
        bool altDown = IsKeyDown(VirtualKey.Menu);
        bool winDown = IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows);
        if (!ctrlDown && !shiftDown && !altDown && !winDown)
        {
            return;
        }

        string? keyToken = GetHotkeyKeyToken(e.Key);
        if (keyToken is null)
        {
            e.Handled = true;
            return;
        }

        var parts = new List<string>(5);
        if (ctrlDown)
        {
            parts.Add("Ctrl");
        }

        if (shiftDown)
        {
            parts.Add("Shift");
        }

        if (altDown)
        {
            parts.Add("Alt");
        }

        if (winDown)
        {
            parts.Add("Win");
        }

        parts.Add(keyToken);
        textBox.Text = string.Join('+', parts);
        textBox.SelectAll();
        e.Handled = true;
    }

    private static bool IsKeyDown(VirtualKey key)
    {
        CoreVirtualKeyStates state = InputKeyboardSource.GetKeyStateForCurrentThread(key);
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    private static string? GetHotkeyKeyToken(VirtualKey key)
    {
        int keyValue = (int)key;
        int letterA = (int)VirtualKey.A;
        int letterZ = (int)VirtualKey.Z;
        if (keyValue >= letterA && keyValue <= letterZ)
        {
            return ((char)('A' + (keyValue - letterA))).ToString();
        }

        int number0 = (int)VirtualKey.Number0;
        int number9 = (int)VirtualKey.Number9;
        if (keyValue >= number0 && keyValue <= number9)
        {
            return ((char)('0' + (keyValue - number0))).ToString();
        }

        int f1 = (int)VirtualKey.F1;
        int f24 = (int)VirtualKey.F24;
        if (keyValue >= f1 && keyValue <= f24)
        {
            return $"F{keyValue - f1 + 1}";
        }

        return key switch
        {
            VirtualKey.Space => "Space",
            VirtualKey.Snapshot => "PrintScreen",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            _ => null,
        };
    }
}
