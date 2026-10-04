using ExCord.Models;
using ExCord.Services;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ExCord.Views;

public partial class GifTalkOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const double MinimumResizeSize = 80d;
    private const double MinimumHoverOpacity = 0.20d;
    private const double FullHoverOpacity = 1.0d;
    private const double FullTransparencyDistanceRatio = 0.50d;
    private const int HoverOpacitySteps = 10;
    private const int HoverUpdateIntervalMilliseconds = 50;
    private const int TopmostEnforceIntervalMilliseconds = 500;
    private const int NavigationWatchdogTimeoutMilliseconds = 15000;
    private const int NavigationRetryDelayMilliseconds = 2000;
    private const int MaxNavigationRetries = 5;

    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _topmostEnforceTimer;
    private readonly DispatcherTimer _navigationWatchdogTimer;
    private readonly DispatcherTimer _navigationRetryTimer;
    private readonly OverlayItemSettings _overlay;
    private readonly Action? _persistOverlayChanges;
    private bool _isEditMode;
    private bool _isClosed;
    private string? _pendingUrl;
    private string? _lastRequestedUrl;
    private string? _activeNavigationUrl;
    private bool _isNavigationInProgress;
    private bool _isRetryPending;
    private int _navigationRetryCount;
    private Rect? _pendingGeometry;
    private int _startupHoverGraceTicks;
    private Rect _editModeStartGeometry;
    private double _editModeStartSettingsX;
    private double _editModeStartSettingsY;
    private double _editModeStartSettingsWidth;
    private double _editModeStartSettingsHeight;

    private bool _isClickThroughEnabled;
    private double _webViewOpacity = FullHoverOpacity;
    private DateTime _lastWebViewOpacityUpdate = DateTime.MinValue;
    private bool _isApplyingOpacity;
    private double? _pendingWebViewOpacity;
    private bool _isEditorDragInProgress;
    private POINT _editorDragStartCursorPosition;
    private int _editorDragStartLeft;
    private int _editorDragStartTop;

    public event EventHandler<bool>? EditModeEnded;

    public event EventHandler? GeometryChanged;

    public bool IsEnabledOverlay => _overlay.Enabled;
    public string OverlayId => _overlay.Id;

    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (_isEditMode == value)
            {
                return;
            }

            _isEditMode = value;

            if (_isEditMode)
            {
                _editModeStartGeometry = GetGeometryInPixels();
                _editModeStartSettingsX = _overlay.X;
                _editModeStartSettingsY = _overlay.Y;
                _editModeStartSettingsWidth = _overlay.Width;
                _editModeStartSettingsHeight = _overlay.Height;
                DisableClickThrough();
                UpdateEditorVisualState();
                _hoverTimer.Stop();
                return;
            }

            UpdateEditorVisualState();
            if (_overlay.Enabled)
            {
                _startupHoverGraceTicks = 3;
                _hoverTimer.Start();
            }
        }
    }

    public GifTalkOverlayWindow(OverlayItemSettings overlay, Action? persistOverlayChanges = null)
    {
        InitializeComponent();
        _overlay = overlay;
        _persistOverlayChanges = persistOverlayChanges;
        ApplyLocalizedTexts();
        MinWidth = MinimumResizeSize;
        MinHeight = MinimumResizeSize;
        ResizeMode = ResizeMode.CanResize;
        _hoverTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(HoverUpdateIntervalMilliseconds)
        };
        _hoverTimer.Tick += HoverTimer_Tick;
        _topmostEnforceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TopmostEnforceIntervalMilliseconds)
        };
        _topmostEnforceTimer.Tick += (_, _) => EnsureTopmost();
        _navigationWatchdogTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(NavigationWatchdogTimeoutMilliseconds)
        };
        _navigationWatchdogTimer.Tick += (_, _) => HandleNavigationWatchdogTimeout();
        _navigationRetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(NavigationRetryDelayMilliseconds)
        };
        _navigationRetryTimer.Tick += (_, _) => RetryPendingNavigation();
        LocationChanged += (_, _) => NotifyGeometryChanged();
        SizeChanged += (_, _) => NotifyGeometryChanged();
        Activated += (_, _) => EnsureTopmost();
        Deactivated += (_, _) => Dispatcher.BeginInvoke(EnsureTopmost, DispatcherPriority.Background);

        WebView.CoreWebView2InitializationCompleted += (_, _) =>
        {
            if (WebView.CoreWebView2 is not null)
            {
                WebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                WebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                ApplyWebViewOpacity();
            }

            if (!string.IsNullOrWhiteSpace(_pendingUrl))
            {
                var pendingUrl = _pendingUrl;
                _pendingUrl = null;
                NavigateToUrl(pendingUrl);
            }
        };

        Loaded += (_, _) =>
        {
            UpdateWindowStyle();
            EnsureTopmost();
        };

    }

    public void ApplyLocalizedTexts()
    {
        var text = LocalizationService.Text;
        ConfirmEditButton.ToolTip = text.EditConfirmToolTip;
        CancelEditButton.ToolTip = text.EditCancelToolTip;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyWindowExStyle();
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowProc);
        }

        if (_pendingGeometry is Rect geometry)
        {
            ApplyGeometry(geometry.X, geometry.Y, geometry.Width, geometry.Height);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _hoverTimer.Stop();
        _topmostEnforceTimer.Stop();
        _navigationWatchdogTimer.Stop();
        _navigationRetryTimer.Stop();
        DisposeWebViewResources();
        base.OnClosed(e);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WM_NCHITTEST || !IsEditMode || !GetWindowRect(hwnd, out var windowRect))
        {
            return IntPtr.Zero;
        }

        var screenX = unchecked((short)(long)lParam);
        var screenY = unchecked((short)((long)lParam >> 16));
        const int resizeBorderThickness = 10;

        var isLeft = screenX < windowRect.Left + resizeBorderThickness;
        var isRight = screenX >= windowRect.Right - resizeBorderThickness;
        var isTop = screenY < windowRect.Top + resizeBorderThickness;
        var isBottom = screenY >= windowRect.Bottom - resizeBorderThickness;

        var hitTest = isTop
            ? isLeft ? HTTOPLEFT : isRight ? HTTOPRIGHT : HTTOP
            : isBottom ? isLeft ? HTBOTTOMLEFT : isRight ? HTBOTTOMRIGHT : HTBOTTOM
            : isLeft ? HTLEFT : isRight ? HTRIGHT : 0;

        if (hitTest == 0)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(hitTest);
    }

    public void BeginEditMode()
    {
        _editModeStartGeometry = GetGeometryInPixels();
        _editModeStartSettingsX = _overlay.X;
        _editModeStartSettingsY = _overlay.Y;
        _editModeStartSettingsWidth = _overlay.Width;
        _editModeStartSettingsHeight = _overlay.Height;
        IsEditMode = true;
    }

    public void ExitEditMode(bool saveChanges)
    {
        if (!IsEditMode)
        {
            return;
        }

        if (!saveChanges)
        {
            ApplyGeometry(_editModeStartGeometry.X, _editModeStartGeometry.Y, _editModeStartGeometry.Width, _editModeStartGeometry.Height);
            _overlay.X = _editModeStartSettingsX;
            _overlay.Y = _editModeStartSettingsY;
            _overlay.Width = _editModeStartSettingsWidth;
            _overlay.Height = _editModeStartSettingsHeight;
            _persistOverlayChanges?.Invoke();
        }
        else
        {
            SaveCurrentGeometryToSettings();
        }

        IsEditMode = false;
        EditModeEnded?.Invoke(this, saveChanges);
    }

    public void ApplyGeometry(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            _pendingGeometry = new Rect(x, y, width, height);
            return;
        }

        SetWindowPos(hwnd, IntPtr.Zero, ToPixelCoordinate(x), ToPixelCoordinate(y), Math.Max(1, ToPixelCoordinate(width)), Math.Max(1, ToPixelCoordinate(height)), SwpNoZOrder | SwpNoActivate);
        _pendingGeometry = null;
    }

    public void UpdateFromSettings()
    {
        ApplyGeometry(_overlay.X, _overlay.Y, _overlay.Width, _overlay.Height);
    }

    public void SaveCurrentGeometryToSettings()
    {
        var bounds = GetGeometryInPixels();
        _overlay.X = bounds.X;
        _overlay.Y = bounds.Y;
        _overlay.Width = bounds.Width;
        _overlay.Height = bounds.Height;
        _persistOverlayChanges?.Invoke();
    }

    public Rect GetGeometryInPixels()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var windowRect))
        {
            return new Rect(windowRect.Left, windowRect.Top, windowRect.Right - windowRect.Left, windowRect.Bottom - windowRect.Top);
        }

        if (_pendingGeometry is Rect pendingGeometry)
        {
            return pendingGeometry;
        }

        return new Rect(Left, Top, Width, Height);
    }

    public void NavigateToUrl(string? url)
    {
        if (_isClosed || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _lastRequestedUrl = url;
        _navigationRetryCount = 0;
        _isRetryPending = false;
        _navigationRetryTimer.Stop();
        _pendingUrl = url;

        if (WebView.CoreWebView2 is not null)
        {
            _pendingUrl = null;
            if (!TryNavigate(url))
            {
                ScheduleNavigationRetry(url, "Initial navigation failed.");
            }

            return;
        }

        _ = EnsureCoreWebView2Async();
    }

    private bool TryNavigate(string url)
    {
        if (WebView.CoreWebView2 is null)
        {
            return false;
        }

        try
        {
            WebView.CoreWebView2.Navigate(url);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "GifTalkOverlayWindow.TryNavigate");
            try
            {
                WebView.CoreWebView2.Navigate("about:blank");
            }
            catch
            {
                // ignored
            }

            return false;
        }
    }

    private void CoreWebView2_NavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        _isNavigationInProgress = true;
        _activeNavigationUrl = string.IsNullOrWhiteSpace(e.Uri) ? _lastRequestedUrl : e.Uri;
        _navigationWatchdogTimer.Stop();
        _navigationWatchdogTimer.Start();
    }

    private void CoreWebView2_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        _isNavigationInProgress = false;
        _navigationWatchdogTimer.Stop();
        ApplyWebViewOpacity();

        if (e.IsSuccess)
        {
            _navigationRetryCount = 0;
            _isRetryPending = false;
            _navigationRetryTimer.Stop();
            return;
        }

        if (e.WebErrorStatus == Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.OperationCanceled && _isRetryPending)
        {
            return;
        }

        ScheduleNavigationRetry(_activeNavigationUrl ?? _lastRequestedUrl, $"Navigation failed: {e.WebErrorStatus}");
    }

    private void HandleNavigationWatchdogTimeout()
    {
        _navigationWatchdogTimer.Stop();

        if (!_isNavigationInProgress)
        {
            return;
        }

        _isNavigationInProgress = false;

        try
        {
            WebView.CoreWebView2?.Stop();
        }
        catch
        {
            // ignored
        }

        ScheduleNavigationRetry(_activeNavigationUrl ?? _lastRequestedUrl, "Navigation timed out.");
    }

    private void ScheduleNavigationRetry(string? url, string reason)
    {
        if (_isClosed || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (_navigationRetryCount >= MaxNavigationRetries)
        {
            LoggingService.LogMessage($"GifTalk overlay navigation retry limit reached: {url}");
            return;
        }

        _lastRequestedUrl = url;

        if (_navigationRetryTimer.IsEnabled)
        {
            return;
        }

        _isRetryPending = true;
        LoggingService.LogMessage($"GifTalk overlay navigation retry scheduled ({_navigationRetryCount + 1}/{MaxNavigationRetries}). Reason: {reason}");
        _navigationRetryTimer.Start();
    }

    private void RetryPendingNavigation()
    {
        _navigationRetryTimer.Stop();

        if (_isClosed || string.IsNullOrWhiteSpace(_lastRequestedUrl))
        {
            _isRetryPending = false;
            return;
        }

        _navigationRetryCount++;
        _isRetryPending = false;

        if (WebView.CoreWebView2 is null)
        {
            _pendingUrl = _lastRequestedUrl;
            _ = EnsureCoreWebView2Async();
            return;
        }

        if (!TryNavigate(_lastRequestedUrl))
        {
            ScheduleNavigationRetry(_lastRequestedUrl, "Retry navigation failed.");
        }
    }

    private void UpdateWindowStyle()
    {
        if (_overlay.Enabled)
        {
            _topmostEnforceTimer.Start();
            EnsureTopmost();
        }
        else
        {
            _topmostEnforceTimer.Stop();
        }

        if (_overlay.Enabled && !IsEditMode)
        {
            _hoverTimer.Start();
        }
    }

    private void EnsureTopmost()
    {
        if (_isClosed || !_overlay.Enabled)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void HoverTimer_Tick(object? sender, EventArgs e)
    {
        if (IsEditMode || !_overlay.Enabled)
        {
            _hoverTimer.Stop();
            return;
        }

        if (_startupHoverGraceTicks > 0)
        {
            _startupHoverGraceTicks--;
            SetWebViewOpacity(FullHoverOpacity);
            DisableClickThrough();
            return;
        }

        if (new WindowInteropHelper(this).Handle == IntPtr.Zero)
        {
            return;
        }

        if (!GetCursorPos(out var point))
        {
            return;
        }

        var mousePoint = new System.Windows.Point(point.X, point.Y);
        var isInside = mousePoint.X >= _overlay.X && mousePoint.X <= _overlay.X + _overlay.Width
            && mousePoint.Y >= _overlay.Y && mousePoint.Y <= _overlay.Y + _overlay.Height;

        if (isInside)
        {
            SetWebViewOpacity(ComputeWebViewOpacity(mousePoint));
            EnableClickThrough();
        }
        else
        {
            SetWebViewOpacity(FullHoverOpacity);
            DisableClickThrough();
        }
    }

    private double ComputeWebViewOpacity(System.Windows.Point mousePoint)
    {
        var halfHeight = _overlay.Height / 2.0;
        if (halfHeight <= 0)
        {
            return FullHoverOpacity;
        }

        var distanceFromNearestEdge = Math.Min(
            Math.Min(mousePoint.X - _overlay.X, _overlay.X + _overlay.Width - mousePoint.X),
            Math.Min(mousePoint.Y - _overlay.Y, _overlay.Y + _overlay.Height - mousePoint.Y));
        var fullTransparencyDepth = halfHeight * (1.0 - FullTransparencyDistanceRatio);

        if (distanceFromNearestEdge >= fullTransparencyDepth)
        {
            return MinimumHoverOpacity;
        }

        var opacityProgress = Math.Clamp(distanceFromNearestEdge / fullTransparencyDepth, 0.0, 1.0);
        return FullHoverOpacity + (MinimumHoverOpacity - FullHoverOpacity) * opacityProgress;
    }

    private void SetWebViewOpacity(double opacity)
    {
        var opacityStep = 1d / (HoverOpacitySteps - 1);
        var quantizedOpacity = Math.Round(Math.Clamp(opacity, MinimumHoverOpacity, FullHoverOpacity) / opacityStep) * opacityStep;
        if (Math.Abs(_webViewOpacity - quantizedOpacity) < opacityStep / 2d)
        {
            return;
        }

        if (DateTime.UtcNow - _lastWebViewOpacityUpdate < TimeSpan.FromMilliseconds(HoverUpdateIntervalMilliseconds))
        {
            return;
        }

        _webViewOpacity = quantizedOpacity;
        _lastWebViewOpacityUpdate = DateTime.UtcNow;
        ApplyWebViewOpacity();
    }

    private void ApplyWebViewOpacity()
    {
        _pendingWebViewOpacity = _webViewOpacity;
        if (_isApplyingOpacity)
        {
            return;
        }

        _ = ApplyPendingWebViewOpacityAsync();
    }

    private async Task EnsureCoreWebView2Async()
    {
        if (_isClosed)
        {
            return;
        }

        try
        {
            await WebView.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "GifTalkOverlayWindow.EnsureCoreWebView2Async");
        }
    }

    private async Task ApplyPendingWebViewOpacityAsync()
    {
        _isApplyingOpacity = true;
        try
        {
            while (!_isClosed && _pendingWebViewOpacity is double opacity)
            {
                _pendingWebViewOpacity = null;
                if (WebView.CoreWebView2 is null)
                {
                    return;
                }

                var opacityText = opacity.ToString(CultureInfo.InvariantCulture);
                await WebView.CoreWebView2.ExecuteScriptAsync($"document.documentElement.style.transition = 'opacity 120ms ease-in-out'; document.documentElement.style.opacity = '{opacityText}';");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "GifTalkOverlayWindow.ApplyPendingWebViewOpacityAsync");
        }
        finally
        {
            _isApplyingOpacity = false;

            if (!_isClosed && _pendingWebViewOpacity is not null)
            {
                ApplyWebViewOpacity();
            }
        }
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditMode || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (!GetCursorPos(out _editorDragStartCursorPosition))
        {
            return;
        }

        var geometry = GetGeometryInPixels();
        _editorDragStartLeft = ToPixelCoordinate(geometry.X);
        _editorDragStartTop = ToPixelCoordinate(geometry.Y);
        _isEditorDragInProgress = true;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void DragArea_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isEditorDragInProgress || e.LeftButton != MouseButtonState.Pressed || !GetCursorPos(out var currentCursorPosition))
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var geometry = GetGeometryInPixels();
        var offsetX = currentCursorPosition.X - _editorDragStartCursorPosition.X;
        var offsetY = currentCursorPosition.Y - _editorDragStartCursorPosition.Y;
        SetWindowPos(hwnd, IntPtr.Zero, _editorDragStartLeft + offsetX, _editorDragStartTop + offsetY, ToPixelCoordinate(geometry.Width), ToPixelCoordinate(geometry.Height), SwpNoZOrder | SwpNoActivate);
        NotifyGeometryChanged();
    }

    private static int ToPixelCoordinate(double value)
    {
        return (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue);
    }

    private void DragArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isEditorDragInProgress = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void NotifyGeometryChanged()
    {
        if (!IsEditMode)
        {
            return;
        }

        GeometryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateWindowStyle();
    }

    private void ConfirmEditButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ExitEditMode(saveChanges: true);
    }

    private void CancelEditButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ExitEditMode(saveChanges: false);
    }

    private void UpdateEditorVisualState()
    {
        if (IsEditMode)
        {
            WebView.Margin = new Thickness(0);
            WebView.Visibility = Visibility.Collapsed;
            EditModeCard.Visibility = Visibility.Visible;
            EditModeCard.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0xA0, 0xFF));
            EditModeCard.StrokeThickness = 1.5;
            EditModeCard.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x4D, 0x1F, 0x23, 0x28));
            EditModeCard.RadiusX = 8;
            EditModeCard.RadiusY = 8;
            DragArea.Visibility = Visibility.Visible;
            EditModeActionButtons.Visibility = Visibility.Visible;
        }
        else
        {
            WebView.Margin = new Thickness(0);
            WebView.Visibility = Visibility.Visible;
            EditModeCard.Visibility = Visibility.Collapsed;
            EditModeCard.Fill = System.Windows.Media.Brushes.Transparent;
            DragArea.Visibility = Visibility.Collapsed;
            EditModeActionButtons.Visibility = Visibility.Collapsed;
        }
    }

    private void AnimateOpacity(double targetOpacity)
    {
        SetWebViewOpacity(targetOpacity);
    }

    private void EnableClickThrough()
    {
        if (_isClickThroughEnabled)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT);
        _isClickThroughEnabled = true;
    }

    private void DisableClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT);
        _isClickThroughEnabled = false;
    }

    private void ApplyWindowExStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    private static long GetWindowLong(IntPtr hwnd, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr(hwnd, index)
            : GetWindowLong32(hwnd, index);
    }

    private static void SetWindowLong(IntPtr hwnd, int index, long value)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr(hwnd, index, value);
        }
        else
        {
            SetWindowLong32(hwnd, index, (int)value);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern long GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern long SetWindowLongPtr(IntPtr hwnd, int index, long value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private void DisposeWebViewResources()
    {
        try
        {
            WebView?.CoreWebView2?.Stop();
        }
        catch
        {
            // ignored
        }

        try
        {
            WebView?.Dispose();
        }
        catch
        {
            // ignored
        }
    }
}
