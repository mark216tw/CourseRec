using System.Globalization;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Com.VibeCode.CourseRec.Controls;

/// <summary>
/// 播放器下方的剪輯標記時間軸：顯示保留範圍、剪除片段、待設定剪除點與書籤，並支援點擊／拖曳跳轉。
/// </summary>
public sealed partial class EditorTimeline : UserControl
{
    private const double TrackHeight = 28;
    private const double BookmarkDotSize = 9;
    private const double MinimumMarkerWidth = 2;

    private TimeSpan _duration;
    private TimeSpan _trimStart;
    private TimeSpan _trimEnd;
    private TimeSpan? _pendingCutStart;
    private TimeSpan _position;
    private IReadOnlyList<TrimSegment> _cutSegments = [];
    private IReadOnlyList<BookmarkItem> _bookmarks = [];
    private bool _isScrubbing;
    private bool _hasTimeline;

    private string _seekToolTipFormat = "{0}";
    private string _trimStartToolTipFormat = "{0}";
    private string _trimEndToolTipFormat = "{0}";
    private string _pendingCutToolTipFormat = "{0}";
    private string _bookmarkToolTipFormat = "{0}";
    private string _cutSegmentToolTipFormat = "{0} ～ {1}";
    private string? _lastTrackToolTip;

    public EditorTimeline()
    {
        InitializeComponent();
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

        TrackHost.SizeChanged += OnTrackHostSizeChanged;
        TrackHost.PointerPressed += OnTrackPointerPressed;
        TrackHost.PointerMoved += OnTrackPointerMoved;
        TrackHost.PointerReleased += OnTrackPointerReleased;
        TrackHost.PointerCaptureLost += OnTrackPointerCaptureLost;
        TrackHost.PointerExited += OnTrackPointerExited;
        ActualThemeChanged += (_, _) => RenderMarkers();
    }

    public event EventHandler<TimeSpan>? SeekRequested;

    /// <summary>
    /// 套用本地化提示文字格式，皆以 <see cref="string.Format(string, object[])"/> 語法表示。
    /// </summary>
    public void ApplyToolTipFormats(
        string seekFormat,
        string trimStartFormat,
        string trimEndFormat,
        string pendingCutFormat,
        string bookmarkFormat,
        string cutSegmentFormat)
    {
        _seekToolTipFormat = seekFormat;
        _trimStartToolTipFormat = trimStartFormat;
        _trimEndToolTipFormat = trimEndFormat;
        _pendingCutToolTipFormat = pendingCutFormat;
        _bookmarkToolTipFormat = bookmarkFormat;
        _cutSegmentToolTipFormat = cutSegmentFormat;
        RenderMarkers();
    }

    /// <summary>
    /// 更新時間軸上的標記與目前播放位置。
    /// </summary>
    public void Update(
        TimeSpan duration,
        TimeSpan trimStart,
        TimeSpan trimEnd,
        TimeSpan? pendingCutStart,
        TimeSpan position,
        IReadOnlyList<TrimSegment> cutSegments,
        IReadOnlyList<BookmarkItem> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(cutSegments);
        ArgumentNullException.ThrowIfNull(bookmarks);

        _duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;
        _hasTimeline = _duration > TimeSpan.Zero;
        _trimStart = Clamp(trimStart);
        _trimEnd = Clamp(trimEnd);
        if (_trimEnd < _trimStart)
        {
            _trimEnd = _trimStart;
        }

        _pendingCutStart = pendingCutStart.HasValue ? Clamp(pendingCutStart.Value) : null;
        _cutSegments = cutSegments;
        _bookmarks = bookmarks;

        TrimStartText.Text = _hasTimeline ? FormatHelper.FormatDuration(_trimStart) : string.Empty;
        TotalDurationText.Text = _hasTimeline ? FormatHelper.FormatDuration(_duration) : string.Empty;

        RenderMarkers();
        UpdatePlayhead(position);
    }

    /// <summary>
    /// 只更新播放頭位置（播放中高頻呼叫）。
    /// </summary>
    public void UpdatePlayhead(TimeSpan position)
    {
        _position = Clamp(position);
        PositionText.Text = _hasTimeline ? FormatHelper.FormatDuration(_position) : string.Empty;
        UpdatePlayheadLayout();
    }

    private TimeSpan Clamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return _duration > TimeSpan.Zero && value > _duration ? _duration : value;
    }

    private double ToX(TimeSpan time)
    {
        double width = TrackHost.ActualWidth;
        if (!_hasTimeline || width <= 0)
        {
            return 0;
        }

        double ratio = Math.Clamp((double)time.Ticks / _duration.Ticks, 0d, 1d);
        return ratio * width;
    }

    private void RenderMarkers()
    {
        MarkerLayer.Children.Clear();

        double width = TrackHost.ActualWidth;
        if (!_hasTimeline || width <= 0)
        {
            KeptRangeBar.Visibility = Visibility.Collapsed;
            return;
        }

        double startX = ToX(_trimStart);
        double endX = ToX(_trimEnd);
        KeptRangeBar.Visibility = Visibility.Visible;
        KeptRangeBar.Width = Math.Max(MinimumMarkerWidth, endX - startX);
        Canvas.SetLeft(KeptRangeBar, startX);

        Brush? cutBrush = CutBrushSource.Background;
        Brush? trimBrush = TrimBrushSource.Background;
        Brush? pendingBrush = PendingBrushSource.Background;
        Brush? bookmarkBrush = BookmarkBrushSource.Background;
        Brush? bookmarkOutline = BookmarkOutlineBrushSource.BorderBrush;

        foreach (TrimSegment cut in _cutSegments)
        {
            if (cutBrush is null)
            {
                break;
            }

            double cutX = ToX(cut.Start);
            var block = new Rectangle
            {
                Width = Math.Max(MinimumMarkerWidth, ToX(cut.End) - cutX),
                Height = TrackHeight,
                Fill = cutBrush,
                Opacity = 0.55,
                RadiusX = 2,
                RadiusY = 2,
            };
            Canvas.SetLeft(block, cutX);
            Canvas.SetTop(block, 0);

            string toolTip = string.Format(
                CultureInfo.CurrentCulture,
                _cutSegmentToolTipFormat,
                FormatHelper.FormatDuration(cut.Start),
                FormatHelper.FormatDuration(cut.End));
            ToolTipService.SetToolTip(block, toolTip);
            AutomationProperties.SetName(block, toolTip);
            MarkerLayer.Children.Add(block);
        }

        AddVerticalMarker(
            startX,
            trimBrush,
            string.Format(CultureInfo.CurrentCulture, _trimStartToolTipFormat, FormatHelper.FormatDuration(_trimStart)));

        AddVerticalMarker(
            endX,
            trimBrush,
            string.Format(CultureInfo.CurrentCulture, _trimEndToolTipFormat, FormatHelper.FormatDuration(_trimEnd)));

        if (_pendingCutStart is { } pending)
        {
            AddVerticalMarker(
                ToX(pending),
                pendingBrush,
                string.Format(CultureInfo.CurrentCulture, _pendingCutToolTipFormat, FormatHelper.FormatDuration(pending)));
        }

        foreach (BookmarkItem bookmark in _bookmarks)
        {
            if (bookmarkBrush is null)
            {
                break;
            }

            double dotX = ToX(bookmark.Timestamp);
            var dot = new Ellipse
            {
                Width = BookmarkDotSize,
                Height = BookmarkDotSize,
                Fill = bookmarkBrush,
                Stroke = bookmarkOutline,
                StrokeThickness = 1,
            };
            Canvas.SetLeft(dot, Math.Clamp(dotX - (BookmarkDotSize / 2), 0, Math.Max(0, width - BookmarkDotSize)));
            Canvas.SetTop(dot, (TrackHeight - BookmarkDotSize) / 2);

            string toolTip = string.Format(
                CultureInfo.CurrentCulture,
                _bookmarkToolTipFormat,
                bookmark.FormattedTimestamp,
                bookmark.Note);
            ToolTipService.SetToolTip(dot, toolTip);
            AutomationProperties.SetName(dot, toolTip);
            MarkerLayer.Children.Add(dot);
        }
    }

    private void AddVerticalMarker(double x, Brush? brush, string toolTip)
    {
        if (brush is null)
        {
            return;
        }

        var line = new Rectangle
        {
            Width = 3,
            Height = TrackHeight,
            Fill = brush,
            RadiusX = 1.5,
            RadiusY = 1.5,
        };
        Canvas.SetLeft(line, Math.Clamp(x - 1.5, 0, Math.Max(0, TrackHost.ActualWidth - 3)));
        ToolTipService.SetToolTip(line, toolTip);
        AutomationProperties.SetName(line, toolTip);
        MarkerLayer.Children.Add(line);
    }

    private void UpdatePlayheadLayout()
    {
        double width = TrackHost.ActualWidth;
        if (!_hasTimeline || width <= 0)
        {
            Playhead.Visibility = Visibility.Collapsed;
            PlayheadThumb.Visibility = Visibility.Collapsed;
            return;
        }

        double x = ToX(_position);
        Playhead.Visibility = Visibility.Visible;
        Canvas.SetLeft(Playhead, Math.Clamp(x - 1, 0, Math.Max(0, width - 2)));
        PlayheadThumb.Visibility = Visibility.Visible;
        Canvas.SetLeft(PlayheadThumb, Math.Clamp(x - 5, 0, Math.Max(0, width - 10)));
    }

    private void OnTrackHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TimelineCanvas.Width = e.NewSize.Width;
        TimelineCanvas.Height = TrackHeight;
        RenderMarkers();
        UpdatePlayheadLayout();
    }

    private void OnTrackPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_hasTimeline)
        {
            return;
        }

        _isScrubbing = TrackHost.CapturePointer(e.Pointer);
        SeekToPointer(e);
        e.Handled = true;
    }

    private void OnTrackPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_hasTimeline)
        {
            return;
        }

        double width = TrackHost.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        Windows.Foundation.Point point = e.GetCurrentPoint(TrackHost).Position;
        double ratio = Math.Clamp(point.X / width, 0d, 1d);
        TimeSpan hoverPosition = TimeSpan.FromTicks((long)(_duration.Ticks * ratio));
        string toolTip = string.Format(
            CultureInfo.CurrentCulture,
            _seekToolTipFormat,
            FormatHelper.FormatDuration(hoverPosition));
        if (!string.Equals(toolTip, _lastTrackToolTip, StringComparison.Ordinal))
        {
            _lastTrackToolTip = toolTip;
            ToolTipService.SetToolTip(TrackHost, toolTip);
        }

        if (!_isScrubbing)
        {
            return;
        }

        SeekToPointer(e);
        e.Handled = true;
    }

    private void OnTrackPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isScrubbing)
        {
            return;
        }

        SeekToPointer(e);
        TrackHost.ReleasePointerCapture(e.Pointer);
        _isScrubbing = false;
        e.Handled = true;
    }

    private void OnTrackPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isScrubbing = false;
    }

    private void OnTrackPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _lastTrackToolTip = null;
    }

    private void SeekToPointer(PointerRoutedEventArgs e)
    {
        double width = TrackHost.ActualWidth;
        if (!_hasTimeline || width <= 0)
        {
            return;
        }

        double x = e.GetCurrentPoint(TrackHost).Position.X;
        double ratio = Math.Clamp(x / width, 0d, 1d);
        TimeSpan target = TimeSpan.FromTicks((long)(_duration.Ticks * ratio));
        UpdatePlayhead(target);
        SeekRequested?.Invoke(this, target);
    }
}
