using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Com.VibeCode.CourseRec.Views;

/// <summary>
/// 錄影檔案庫頁面（含二次確認刪除 <see cref="ContentDialog"/>）。
/// </summary>
public sealed partial class LibraryPage : Page
{
    private readonly EditorViewModel _editorViewModel;

    public LibraryPage()
    {
        ViewModel = App.GetService<LibraryViewModel>();
        _editorViewModel = App.GetService<EditorViewModel>();
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    public LibraryViewModel ViewModel { get; }

    private void OnFocusSearchAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
        args.Handled = true;
    }

    private void OnClearSearchAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!string.IsNullOrEmpty(ViewModel.SearchText))
        {
            ViewModel.SearchText = string.Empty;
            args.Handled = true;
        }
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshLibraryAsync();
    }

    private void OnPlayItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecordingLibraryItem item })
        {
            ViewModel.PlayRecording(item);
        }
    }

    private async void OnOpenInEditorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecordingLibraryItem item })
        {
            await OpenInEditorAsync(item);
        }
    }

    private async void OnRecordingItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecordingLibraryItem item })
        {
            e.Handled = true;
            await OpenInEditorAsync(item);
        }
    }

    private async Task OpenInEditorAsync(RecordingLibraryItem item)
    {
        if (!await ConfirmReplaceEditorVideoAsync(item))
        {
            return;
        }

        ViewModel.OpenInEditor(item);
    }

    private async Task<bool> ConfirmReplaceEditorVideoAsync(RecordingLibraryItem item)
    {
        if (!_editorViewModel.HasLoadedVideo || XamlRoot is null)
        {
            return true;
        }

        (string title, string message, string confirmText, string cancelText) =
            ViewModel.GetReplaceEditorVideoDialogTexts(_editorViewModel.LoadedVideoTitle, item.FileName);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void OnRevealFileClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RecordingLibraryItem item })
        {
            ViewModel.RevealFileInExplorer(item);
        }
    }

    private async void OnRenameItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RecordingLibraryItem item } || XamlRoot is null)
        {
            return;
        }

        (string title, string message, string confirmText, string cancelText) = ViewModel.GetRenameDialogTexts();
        var nameBox = new TextBox
        {
            Header = ViewModel.RenameNameHeader,
            Text = Path.GetFileNameWithoutExtension(item.FileName),
        };
        var content = new StackPanel
        {
            Spacing = 8,
        };
        content.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.WrapWholeWords,
        });
        content.Children.Add(nameBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Opened += (_, _) =>
        {
            nameBox.Focus(FocusState.Programmatic);
            nameBox.SelectAll();
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            string? error = await ViewModel.RenameRecordingAsync(item, nameBox.Text);
            if (!string.IsNullOrEmpty(error))
            {
                ViewModel.ShowError(error);
            }
        }
    }

    private async void OnDeleteItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RecordingLibraryItem item } || XamlRoot is null)
        {
            return;
        }

        (string title, string message, string confirmText, string cancelText) = ViewModel.GetDeleteDialogTexts(item);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Close,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteRecordingConfirmedAsync(item);
        }
    }
}
