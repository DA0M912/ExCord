using ExCord.Models;
using ExCord.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ExCord.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<OverlayItemSettings> _overlayItems;
    private readonly Dictionary<string, WebViewOverlayWindow> _overlayWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _expanderStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _coreSettingsSaveTimer;
    private readonly CancellationTokenSource _updateCheckCancellation = new();
    private bool _isUpdatingLanguageSelection;
    private bool _isInitializingCoreFields;
    private bool _isRefreshingOverlayUi;
    private bool _isOverlayContainersGenerating;
    private OverlayItemSettings? _draggedOverlay;
    private bool _overlayOrderChanged;
    private Popup? _overlayDragPopup;
    private string? _latestReleaseUrl;

    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler<AppSettings>? WebViewGeometryChanged;

    public SettingsWindow(AppSettings settings, IReadOnlyDictionary<string, WebViewOverlayWindow>? overlayWindows = null)
    {
        _isInitializingCoreFields = true;
        InitializeComponent();
        _settings = settings;
        _overlayItems = new ObservableCollection<OverlayItemSettings>(_settings.OverlayItems);
        OverlayItemsControl.ItemsSource = _overlayItems;
        OverlayItemsControl.ItemContainerGenerator.StatusChanged += OverlayItemsControl_ItemContainerGenerator_StatusChanged;
        ApplyLocalizedTexts();

        _coreSettingsSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _coreSettingsSaveTimer.Tick += (_, _) =>
        {
            _coreSettingsSaveTimer.Stop();
            SaveSettings();
        };

        SetOverlayWindows(overlayWindows);

        Closing += (_, _) =>
        {
            _updateCheckCancellation.Cancel();
            _coreSettingsSaveTimer.Stop();
            ExitAllEditModes();
            SaveSettings();
            SetOverlayWindows(null);
        };

        KeyComboBox.ItemsSource = Enum.GetValues(typeof(Key)).Cast<Key>().Where(k => k != Key.None && k != Key.System && k != Key.LWin && k != Key.RWin).ToList();
        KeyComboBox.SelectedItem = _settings.HotkeyKey;

        InitializeLanguageOptions();

        CtrlCheckBox.IsChecked = (_settings.HotkeyModifiers & ModifierKeys.Control) == ModifierKeys.Control;
        AltCheckBox.IsChecked = (_settings.HotkeyModifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        ShiftCheckBox.IsChecked = (_settings.HotkeyModifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        var voices = LoadVoiceOptions();
        VoiceComboBox.ItemsSource = voices;

        var selected = voices.FirstOrDefault(v => string.Equals(v.Id, _settings.TtsVoiceId, StringComparison.OrdinalIgnoreCase) && v.IsOneCore == _settings.TtsVoiceIsOneCore)
            ?? voices.FirstOrDefault(v => v.IsOneCore && string.Equals(v.Id, Windows.Media.SpeechSynthesis.SpeechSynthesizer.DefaultVoice.Id, StringComparison.OrdinalIgnoreCase))
            ?? voices.FirstOrDefault();
        VoiceComboBox.SelectedItem = selected;

        var outputDevices = LoadAvailableOutputDevices();
        DeviceComboBox.ItemsSource = outputDevices;

        if (outputDevices.Count > 0)
        {
            DeviceComboBox.SelectedItem = outputDevices.FirstOrDefault(d => string.Equals(d, _settings.OutputDeviceKeyword, StringComparison.OrdinalIgnoreCase))
                ?? outputDevices.FirstOrDefault(d => d.Contains("VB", StringComparison.OrdinalIgnoreCase))
                ?? outputDevices[0];
        }
        else
        {
            DeviceComboBox.Items.Add(_settings.OutputDeviceKeyword);
            DeviceComboBox.SelectedItem = _settings.OutputDeviceKeyword;
        }

        OutputVolumeSlider.Value = _settings.OutputVolume;
        MonitorEnabledCheckBox.IsChecked = _settings.MonitorEnabled;
        AutoStartToggleButton.IsChecked = _settings.AutoStart;
        TtsToggleButton.IsChecked = _settings.TtsEnabled;
        WebViewToggleButton.IsChecked = _settings.WebViewEnabled;
        OutputVolumeLabel.Text = $"{_settings.OutputVolume:0}%";

        OutputVolumeSlider.ValueChanged += (_, _) => OutputVolumeLabel.Text = $"{OutputVolumeSlider.Value:0}%";
        VoiceComboBox.SelectionChanged += (_, _) => SaveSettingsIfInitialized();
        DeviceComboBox.SelectionChanged += (_, _) => SaveSettingsIfInitialized();
        MonitorEnabledCheckBox.Checked += (_, _) => SaveSettingsIfInitialized();
        MonitorEnabledCheckBox.Unchecked += (_, _) => SaveSettingsIfInitialized();
        AutoStartToggleButton.Checked += (_, _) => SaveSettingsIfInitialized();
        AutoStartToggleButton.Unchecked += (_, _) => SaveSettingsIfInitialized();
        TtsToggleButton.Checked += TtsToggleButton_Checked;
        TtsToggleButton.Unchecked += TtsToggleButton_Unchecked;
        WebViewToggleButton.Checked += WebViewToggleButton_Checked;
        WebViewToggleButton.Unchecked += WebViewToggleButton_Unchecked;
        OutputVolumeSlider.ValueChanged += (_, _) => RestartCoreSettingsSaveTimer();
        CtrlCheckBox.Checked += (_, _) => RestartCoreSettingsSaveTimer();
        CtrlCheckBox.Unchecked += (_, _) => RestartCoreSettingsSaveTimer();
        AltCheckBox.Checked += (_, _) => RestartCoreSettingsSaveTimer();
        AltCheckBox.Unchecked += (_, _) => RestartCoreSettingsSaveTimer();
        ShiftCheckBox.Checked += (_, _) => RestartCoreSettingsSaveTimer();
        ShiftCheckBox.Unchecked += (_, _) => RestartCoreSettingsSaveTimer();
        KeyComboBox.SelectionChanged += (_, _) => RestartCoreSettingsSaveTimer();
        VoiceComboBox.PreviewMouseLeftButtonDown += VoiceComboBox_PreviewMouseLeftButtonDown;
        DeviceComboBox.PreviewMouseLeftButtonDown += DeviceComboBox_PreviewMouseLeftButtonDown;

        _isInitializingCoreFields = false;
        Dispatcher.BeginInvoke(RefreshOverlayUi, DispatcherPriority.Loaded);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionTextBlock.Text = $"v{version?.Major}.{version?.Minor}.{version?.Build}";
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        UpdateCheckResult result;
        try
        {
            result = await new UpdateCheckService().CheckForUpdateAsync(_updateCheckCancellation.Token);
        }
        catch (OperationCanceledException) when (_updateCheckCancellation.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            return;
        }

        if (!result.IsNewVersionAvailable)
        {
            return;
        }

        _latestReleaseUrl = result.ReleaseUrl;
        NewVersionTextBlock.Visibility = Visibility.Visible;
        NewVersionTextBlock.BeginStoryboard((System.Windows.Media.Animation.Storyboard)NewVersionTextBlock.Resources["BlinkStoryboard"]);
    }

    private void NewVersionTextBlock_Click(object sender, MouseButtonEventArgs e)
    {
        if (_latestReleaseUrl is not null)
        {
            Process.Start(new ProcessStartInfo(_latestReleaseUrl) { UseShellExecute = true });
        }
    }

    private void DeviceComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DeviceComboBox.IsDropDownOpen)
        {
            return;
        }

        DeviceComboBox.IsDropDownOpen = true;
        e.Handled = true;
    }

    private void VoiceComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (VoiceComboBox.IsDropDownOpen)
        {
            return;
        }

        VoiceComboBox.IsDropDownOpen = true;
        e.Handled = true;
    }

    public void SetOverlayWindows(IReadOnlyDictionary<string, WebViewOverlayWindow>? overlayWindows)
    {
        foreach (var window in _overlayWindows.Values)
        {
            window.EditModeEnded -= WebViewOverlayWindow_EditModeEnded;
            window.EditModeRequested -= WebViewOverlayWindow_EditModeRequested;
            window.GeometryChanged -= WebViewOverlayWindow_GeometryChanged;
        }

        _overlayWindows.Clear();

        if (overlayWindows is null)
        {
            return;
        }

        foreach (var pair in overlayWindows)
        {
            _overlayWindows[pair.Key] = pair.Value;
            pair.Value.EditModeEnded += WebViewOverlayWindow_EditModeEnded;
            pair.Value.EditModeRequested += WebViewOverlayWindow_EditModeRequested;
            pair.Value.GeometryChanged += WebViewOverlayWindow_GeometryChanged;
        }
    }

    public void SyncWebViewEnabledState(bool isEnabled)
    {
        if (WebViewToggleButton.IsChecked == isEnabled)
        {
            return;
        }

        WebViewToggleButton.Checked -= WebViewToggleButton_Checked;
        WebViewToggleButton.Unchecked -= WebViewToggleButton_Unchecked;
        try
        {
            WebViewToggleButton.IsChecked = isEnabled;
        }
        finally
        {
            WebViewToggleButton.Checked += WebViewToggleButton_Checked;
            WebViewToggleButton.Unchecked += WebViewToggleButton_Unchecked;
        }
    }

    private void WebViewOverlayWindow_EditModeEnded(object? sender, bool saved)
    {
        if (sender is not WebViewOverlayWindow overlayWindow)
        {
            return;
        }

        SetOverlayEditToggleState(overlayWindow.OverlayId, false);
        if (!saved)
        {
            RefreshOverlayUi();
        }
    }

    private void WebViewOverlayWindow_EditModeRequested(object? sender, string overlayId)
    {
        ExpandOverlayExpander(overlayId);
        SetOverlayEditToggleState(overlayId, true);
    }

    private void WebViewOverlayWindow_GeometryChanged(object? sender, EventArgs e)
    {
        if (sender is not WebViewOverlayWindow overlayWindow)
        {
            return;
        }

        var overlay = _overlayItems.FirstOrDefault(item => string.Equals(item.Id, overlayWindow.OverlayId, StringComparison.OrdinalIgnoreCase));
        if (overlay is null)
        {
            return;
        }

        var bounds = overlayWindow.GetGeometryInPixels();
        overlay.X = bounds.X;
        overlay.Y = bounds.Y;
        overlay.Width = bounds.Width;
        overlay.Height = bounds.Height;
        WebViewGeometryChanged?.Invoke(this, _settings);
        RefreshOverlayUi();
    }

    private void RestartCoreSettingsSaveTimer()
    {
        if (_isInitializingCoreFields)
        {
            return;
        }

        _coreSettingsSaveTimer.Stop();
        _coreSettingsSaveTimer.Start();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TtsToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializingCoreFields)
        {
            return;
        }

        SaveSettings();
    }

    private void TtsToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializingCoreFields)
        {
            return;
        }

        SaveSettings();
    }

    private void WebViewToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializingCoreFields)
        {
            return;
        }

        _settings.WebViewEnabled = WebViewToggleButton.IsChecked == true;
        SaveSettings();
    }

    private void WebViewToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializingCoreFields)
        {
            return;
        }

        _settings.WebViewEnabled = WebViewToggleButton.IsChecked == true;
        ExitAllEditModes();
        SaveSettings();
    }

    private void OverlayNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isRefreshingOverlayUi || !TryGetOverlayFromElement(sender, out _))
        {
            return;
        }

        SaveSettings();
    }

    private void OverlayNameEditor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Escape)
        {
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }

    private void OverlayContextDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem
            || menuItem.Parent is not ContextMenu contextMenu
            || contextMenu.PlacementTarget is not FrameworkElement placementTarget
            || placementTarget.DataContext is not OverlayItemSettings overlay)
        {
            return;
        }

        DeleteOverlay(overlay);
    }

    private void ApplyOverlayUrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetOverlayFromElement(sender, out var overlay))
        {
            return;
        }

        overlay.Url = overlay.Url.Trim();
        if (string.IsNullOrWhiteSpace(overlay.Url))
        {
            return;
        }

        if (_overlayWindows.TryGetValue(overlay.Id, out var window))
        {
            window.NavigateToUrl(overlay.Url);
        }

        SaveSettings();
    }

    private void OverlayEditModeToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingOverlayUi || !TryGetOverlayFromElement(sender, out var overlay))
        {
            return;
        }

        if (!_overlayWindows.TryGetValue(overlay.Id, out var window))
        {
            SetOverlayEditToggleState(overlay.Id, false);
            return;
        }

        ExpandOverlayExpander(overlay.Id);
        if (!window.IsEditMode)
        {
            window.BeginEditMode();
            window.ApplyGeometry(overlay.X, overlay.Y, overlay.Width, overlay.Height);
        }
    }

    private void OverlayEditModeToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingOverlayUi || !TryGetOverlayFromElement(sender, out var overlay))
        {
            return;
        }

        if (_overlayWindows.TryGetValue(overlay.Id, out var window) && window.IsEditMode)
        {
            window.ExitEditMode(saveChanges: true);
        }

        SaveSettings();
    }

    private void OverlayGeometryTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isRefreshingOverlayUi || !TryGetOverlayFromElement(sender, out var overlay))
        {
            return;
        }

        if (_overlayWindows.TryGetValue(overlay.Id, out var window))
        {
            window.ApplyGeometry(overlay.X, overlay.Y, overlay.Width, overlay.Height);
        }

        SaveSettings();
    }

    private void OverlayEnabledToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        SaveSettingsIfOverlay(sender);
    }

    private void OverlayEnabledToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        SaveSettingsIfOverlay(sender);
    }

    private void OverlayTransparencyToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!TryGetOverlayFromElement(sender, out var overlay))
        {
            return;
        }

        overlay.TransparencyEnabled = true;
        SaveSettings();
        RefreshOverlayUi();
    }

    private void OverlayTransparencyToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!TryGetOverlayFromElement(sender, out _))
        {
            return;
        }

        SaveSettings();
    }

    private void OverlayInteractionToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!TryGetOverlayFromElement(sender, out var overlay))
        {
            return;
        }

        overlay.InteractionEnabled = true;
        overlay.TransparencyEnabled = false;
        SaveSettings();
        RefreshOverlayUi();
    }

    private void OverlayInteractionToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!TryGetOverlayFromElement(sender, out _))
        {
            return;
        }

        SaveSettings();
        RefreshOverlayUi();
    }

    private void OverlayShiftRightClickEditToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        SaveSettingsIfOverlay(sender);
    }

    private void OverlayShiftRightClickEditToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        SaveSettingsIfOverlay(sender);
    }

    private void OverlayDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement handle || handle.DataContext is not OverlayItemSettings overlay)
        {
            return;
        }

        _draggedOverlay = overlay;
        _overlayOrderChanged = false;
        ShowOverlayDragPopup(overlay, e.GetPosition(this));
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void OverlayDragHandle_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_draggedOverlay is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(OverlayItemsControl);
        UpdateOverlayDragPopup(e.GetPosition(this));
        var index = _overlayItems.IndexOf(_draggedOverlay);
        if (index < 0)
        {
            return;
        }

        var targetIndex = FindOverlayDropIndex(currentPoint, index);
        if (targetIndex == index)
        {
            return;
        }

        var previousPositions = CaptureOverlayItemPositions();
        _overlayItems.Move(index, targetIndex);
        AnimateOverlayItemReordering(previousPositions);
        _overlayOrderChanged = true;
    }

    private void OverlayDragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggedOverlay = null;
        CloseOverlayDragPopup();
        if (sender is UIElement element)
        {
            element.ReleaseMouseCapture();
        }

        if (_overlayOrderChanged)
        {
            SaveSettings();
        }

        _overlayOrderChanged = false;
    }

    private void MoveOverlayUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveOverlay(sender, -1);
    }

    private int FindOverlayDropIndex(System.Windows.Point currentPoint, int currentIndex)
    {
        for (var i = 0; i < _overlayItems.Count; i++)
        {
            if (OverlayItemsControl.ItemContainerGenerator.ContainerFromItem(_overlayItems[i]) is not FrameworkElement container)
            {
                continue;
            }

            var top = container.TranslatePoint(new System.Windows.Point(0, 0), OverlayItemsControl).Y;
            if (currentPoint.Y < top + container.ActualHeight / 2d)
            {
                return i > currentIndex ? i - 1 : i;
            }
        }

        return _overlayItems.Count - 1;
    }

    private Dictionary<string, double> CaptureOverlayItemPositions()
    {
        var positions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var overlay in _overlayItems)
        {
            if (OverlayItemsControl.ItemContainerGenerator.ContainerFromItem(overlay) is FrameworkElement container)
            {
                positions[overlay.Id] = container.TranslatePoint(new System.Windows.Point(0, 0), OverlayItemsControl).Y;
            }
        }

        return positions;
    }

    private void AnimateOverlayItemReordering(IReadOnlyDictionary<string, double> previousPositions)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var overlay in _overlayItems)
            {
                if (_draggedOverlay == overlay
                    || !previousPositions.TryGetValue(overlay.Id, out var previousTop)
                    || OverlayItemsControl.ItemContainerGenerator.ContainerFromItem(overlay) is not FrameworkElement container)
                {
                    continue;
                }

                var currentTop = container.TranslatePoint(new System.Windows.Point(0, 0), OverlayItemsControl).Y;
                var offset = previousTop - currentTop;
                if (Math.Abs(offset) < 1d)
                {
                    continue;
                }

                var transform = new TranslateTransform(0, offset);
                container.RenderTransform = transform;
                transform.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(offset, 0d, TimeSpan.FromMilliseconds(160))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
        }, DispatcherPriority.Render);
    }

    private void ShowOverlayDragPopup(OverlayItemSettings overlay, System.Windows.Point position)
    {
        _overlayDragPopup = new Popup
        {
            AllowsTransparency = true,
            IsHitTestVisible = false,
            PlacementTarget = this,
            Placement = PlacementMode.Relative,
            Child = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xD9, 0x2B, 0x2F, 0x36)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0x65, 0xF2)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 7, 12, 7),
                Opacity = 0.68,
                Child = new TextBlock
                {
                    Text = overlay.Name,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold
                }
            }
        };

        UpdateOverlayDragPopup(position);
        _overlayDragPopup.IsOpen = true;
    }

    private void UpdateOverlayDragPopup(System.Windows.Point position)
    {
        if (_overlayDragPopup is null)
        {
            return;
        }

        _overlayDragPopup.HorizontalOffset = position.X + 12;
        _overlayDragPopup.VerticalOffset = position.Y + 12;
    }

    private void CloseOverlayDragPopup()
    {
        if (_overlayDragPopup is null)
        {
            return;
        }

        _overlayDragPopup.IsOpen = false;
        _overlayDragPopup = null;
    }

    private void MoveOverlayDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveOverlay(sender, 1);
    }

    private void DeleteOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetOverlayFromElement(sender, out var overlay))
        {
            return;
        }

        DeleteOverlay(overlay);
    }

    private void DeleteOverlay(OverlayItemSettings overlay)
    {
        _overlayItems.Remove(overlay);
        _settings.OverlayItems.Remove(overlay);
        _expanderStates.Remove(overlay.Id);
        SaveSettings();
        RefreshOverlayUi();
    }

    private void AddOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        var overlay = new OverlayItemSettings
        {
            Name = $"Overlay {_overlayItems.Count + 1}",
            Url = string.Empty,
            X = AppSettings.GetDefaultWebViewX(),
            Y = AppSettings.GetDefaultWebViewY(),
            Width = 300d,
            Height = 300d
        };

        _overlayItems.Add(overlay);
        _settings.OverlayItems.Add(overlay);
        _expanderStates[overlay.Id] = true;
        SaveSettings();
        RefreshOverlayUi();
    }

    private void OverlayScrollViewer_Loaded(object sender, RoutedEventArgs e)
    {
        SyncOverlayScrollBar();
    }

    private void OverlayScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SyncOverlayScrollBar();
    }

    private void OverlayScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Math.Abs(OverlayScrollViewer.VerticalOffset - e.NewValue) > 0.1d)
        {
            OverlayScrollViewer.ScrollToVerticalOffset(e.NewValue);
        }
    }

    private void SyncOverlayScrollBar()
    {
        if (OverlayScrollViewer is null || OverlayScrollBar is null)
        {
            return;
        }

        var canScroll = OverlayScrollViewer.ScrollableHeight > 0;
        OverlayScrollBar.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
        OverlayScrollBar.Maximum = OverlayScrollViewer.ScrollableHeight;
        OverlayScrollBar.ViewportSize = OverlayScrollViewer.ViewportHeight;

        if (Math.Abs(OverlayScrollBar.Value - OverlayScrollViewer.VerticalOffset) > 0.1d)
        {
            OverlayScrollBar.Value = OverlayScrollViewer.VerticalOffset;
        }
    }

    private void OverlayExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander && expander.Tag is string overlayId)
        {
            _expanderStates[overlayId] = true;
            SetOverlayNameEditingState(expander, true);
        }
    }

    private void OverlayExpander_Collapsed(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander && expander.Tag is string overlayId)
        {
            _expanderStates[overlayId] = false;
            SetOverlayNameEditingState(expander, false);
        }
    }

    private static void SetOverlayNameEditingState(Expander expander, bool isEditing)
    {
        if (expander.Header is not Grid header)
        {
            return;
        }

        var nameDisplay = FindVisualChildren<TextBlock>(header)
            .FirstOrDefault(textBlock => textBlock.Tag is string tag && string.Equals(tag, "OverlayNameDisplay", StringComparison.Ordinal));
        var nameEditor = FindVisualChildren<System.Windows.Controls.TextBox>(header)
            .FirstOrDefault(textBox => textBox.Tag is string tag && string.Equals(tag, "OverlayNameEditor", StringComparison.Ordinal));
        if (nameDisplay is not null)
        {
            nameDisplay.Visibility = isEditing ? Visibility.Collapsed : Visibility.Visible;
        }

        if (nameEditor is not null)
        {
            nameEditor.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void InitializeLanguageOptions()
    {
        _isUpdatingLanguageSelection = true;
        try
        {
            LanguageComboBox.DisplayMemberPath = nameof(LanguageOption.DisplayName);
            LanguageComboBox.SelectedValuePath = nameof(LanguageOption.Language);
            LanguageComboBox.ItemsSource = BuildLanguageOptions();
            LanguageComboBox.SelectedValue = _settings.Language;
        }
        finally
        {
            _isUpdatingLanguageSelection = false;
        }
    }

    private List<LanguageOption> BuildLanguageOptions()
    {
        return
        [
            new LanguageOption(AppLanguage.English, "English"),
            new LanguageOption(AppLanguage.Korean, "한국어")
        ];
    }

    public void ApplyLocalizedTexts()
    {
        var text = LocalizationService.Text;
        Title = text.SettingsWindowTitle;
        GeneralTabItem.Header = "⚙";
        GeneralTabItem.ToolTip = text.TabGeneral;
        TtsTabItem.Header = text.TabTts;
        WebViewTabItem.Header = text.TabWebView;

        ApplicationSectionTextBlock.Text = text.SectionApplication;
        FeaturesSectionTextBlock.Text = text.SectionFeatures;
        LanguageSectionTextBlock.Text = text.SectionLanguage;
        AutoStartToggleButton.Content = text.StartWithWindows;
        TtsToggleButton.Content = text.TextToSpeech;
        WebViewToggleButton.Content = text.WebView;

        GlobalHotkeyTextBlock.Text = text.GlobalHotkey;
        TtsVoiceTextBlock.Text = text.TtsVoice;
        VirtualOutputDeviceTextBlock.Text = text.VirtualOutputDevice;
        OutputVolumeTextBlock.Text = text.OutputVolume;
        MonitorEnabledCheckBox.Content = text.MonitorSound;

        NewVersionTextBlock.Text = text.NewVersionAvailable;
        CloseButton.Content = text.Close;
        AddOverlayButton.Content = text.AddOverlay;

        InitializeLanguageOptions();
        Dispatcher.BeginInvoke(RefreshOverlayUi, DispatcherPriority.Loaded);
    }

    private void OverlayItemsControl_ItemContainerGenerator_StatusChanged(object? sender, EventArgs e)
    {
        _isOverlayContainersGenerating = OverlayItemsControl.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.GeneratingContainers;

        if (OverlayItemsControl.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
        {
            Dispatcher.BeginInvoke(RefreshOverlayUi, DispatcherPriority.Loaded);
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingLanguageSelection || _isInitializingCoreFields)
        {
            return;
        }

        if (LanguageComboBox.SelectedValue is not AppLanguage selectedLanguage)
        {
            return;
        }

        if (_settings.Language == selectedLanguage)
        {
            return;
        }

        _settings.Language = selectedLanguage;
        LocalizationService.SetLanguage(selectedLanguage);
        ApplyLocalizedTexts();
        SaveSettings();
    }

    private void SaveSettings()
    {
        var selectedVoice = VoiceComboBox.SelectedItem as VoiceOption;
        var selectedKey = KeyComboBox.SelectedItem as Key?;

        if (LanguageComboBox.SelectedValue is AppLanguage selectedLanguage)
        {
            _settings.Language = selectedLanguage;
        }

        _settings.HotkeyModifiers = BuildModifiers();
        _settings.HotkeyKey = selectedKey ?? _settings.HotkeyKey;
        _settings.TtsVoiceId = selectedVoice?.Id ?? string.Empty;
        _settings.TtsVoiceIsOneCore = selectedVoice?.IsOneCore ?? true;
        _settings.TtsEnabled = TtsToggleButton.IsChecked == true;
        _settings.OutputDeviceKeyword = DeviceComboBox.SelectedItem as string ?? _settings.OutputDeviceKeyword;
        _settings.OutputVolume = OutputVolumeSlider.Value;
        _settings.MonitorEnabled = MonitorEnabledCheckBox.IsChecked == true;
        _settings.AutoStart = AutoStartToggleButton.IsChecked == true;
        _settings.WebViewEnabled = WebViewToggleButton.IsChecked == true;
        _settings.OverlayItems = _overlayItems.ToList();
        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
    }

    private void SaveSettingsIfInitialized()
    {
        if (_isInitializingCoreFields)
        {
            return;
        }

        SaveSettings();
    }

    private void SaveSettingsIfOverlay(object sender)
    {
        if (!TryGetOverlayFromElement(sender, out _))
        {
            return;
        }

        SaveSettings();
    }

    private void RefreshOverlayUi()
    {
        if (OverlayItemsControl is null || _isOverlayContainersGenerating || _isRefreshingOverlayUi)
        {
            return;
        }

        _isRefreshingOverlayUi = true;
        try
        {
            foreach (var expander in FindVisualChildren<Expander>(OverlayItemsControl))
            {
                if (expander.Tag is string overlayId && _expanderStates.TryGetValue(overlayId, out var expanded))
                {
                    expander.IsExpanded = expanded;
                }
            }
        }
        finally
        {
            _isRefreshingOverlayUi = false;
        }

        Dispatcher.BeginInvoke(SyncOverlayScrollBar, DispatcherPriority.Loaded);
    }

    private void OverlayLocalizedElement_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var text = LocalizationService.Text;

        switch (element)
        {
            case TextBlock textBlock when textBlock.Name == "WebUrlTextBlock":
                textBlock.Text = text.WebUrl;
                break;
            case TextBlock textBlock when textBlock.Name == "WebViewXTextBlock":
                textBlock.Text = text.AxisX;
                break;
            case TextBlock textBlock when textBlock.Name == "WebViewYTextBlock":
                textBlock.Text = text.AxisY;
                break;
            case TextBlock textBlock when textBlock.Name == "WebViewWidthTextBlock":
                textBlock.Text = text.SizeWidth;
                break;
            case TextBlock textBlock when textBlock.Name == "WebViewHeightTextBlock":
                textBlock.Text = text.SizeHeight;
                break;
            case ToggleButton toggle when toggle.Name == "TransparencyToggleButton":
                toggle.Content = text.OverlayTransparency;
                if (toggle.DataContext is OverlayItemSettings overlay)
                {
                    toggle.IsEnabled = !overlay.InteractionEnabled;
                }
                break;
            case ToggleButton toggle when toggle.Name == "InteractionToggleButton":
                toggle.Content = text.OverlayInteractionMode;
                break;
            case ToggleButton toggle when toggle.Name == "ShiftRightClickEditToggleButton":
                toggle.Content = text.OverlayShiftRightClickEdit;
                break;
            case ToggleButton toggle when toggle.Tag is string:
                toggle.Content = text.EditMode;
                break;
            case System.Windows.Controls.Button button when button.Width == 70:
                button.Content = text.Apply;
                break;
        }
    }

    private void ExpandOverlayExpander(string overlayId)
    {
        _expanderStates[overlayId] = true;
        RefreshOverlayUi();
    }

    private void SetOverlayEditToggleState(string overlayId, bool isChecked)
    {
        foreach (var toggle in FindVisualChildren<ToggleButton>(OverlayItemsControl))
        {
            if (toggle.Tag is string currentId && string.Equals(currentId, overlayId, StringComparison.OrdinalIgnoreCase))
            {
                toggle.IsChecked = isChecked;
            }
        }
    }

    private void MoveOverlay(object sender, int direction)
    {
        if (!TryGetOverlayFromElement(sender, out var overlay))
        {
            return;
        }

        var currentIndex = _overlayItems.IndexOf(overlay);
        if (currentIndex < 0)
        {
            return;
        }

        var targetIndex = currentIndex + direction;
        if (targetIndex < 0 || targetIndex >= _overlayItems.Count)
        {
            return;
        }

        _overlayItems.Move(currentIndex, targetIndex);
        SaveSettings();
        RefreshOverlayUi();
    }

    private void ExitAllEditModes()
    {
        foreach (var window in _overlayWindows.Values.Where(window => window.IsEditMode))
        {
            window.ExitEditMode(saveChanges: true);
        }
    }

    private bool TryGetOverlayFromElement(object? element, out OverlayItemSettings overlay)
    {
        overlay = null!;

        if (element is not FrameworkElement frameworkElement)
        {
            return false;
        }

        overlay = frameworkElement.DataContext as OverlayItemSettings
            ?? FindVisualParent<FrameworkElement>(frameworkElement)?.DataContext as OverlayItemSettings
            ?? null!;

        return overlay is not null;
    }

    private static TParent? FindVisualParent<TParent>(DependencyObject child)
        where TParent : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is TParent matched)
            {
                return matched;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static IEnumerable<TChild> FindVisualChildren<TChild>(DependencyObject parent)
        where TChild : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TChild matched)
            {
                yield return matched;
            }

            foreach (var descendant in FindVisualChildren<TChild>(child))
            {
                yield return descendant;
            }
        }
    }

    private static List<VoiceOption> LoadVoiceOptions()
    {
        var voices = new List<VoiceOption>();

        foreach (var voice in Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices)
        {
            voices.Add(new VoiceOption(voice.Id, $"{voice.DisplayName} ({voice.Language})", voice.Language, true));
        }

        try
        {
            using var sapiSynthesizer = new System.Speech.Synthesis.SpeechSynthesizer();
            foreach (var installedVoice in sapiSynthesizer.GetInstalledVoices().Where(v => v.Enabled))
            {
                var voiceInfo = installedVoice.VoiceInfo;
                var voiceId = voiceInfo.Id;
                if (voices.Any(v => v.IsOneCore == false && string.Equals(v.Id, voiceId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                voices.Add(new VoiceOption(voiceId, $"{voiceInfo.Name} ({voiceInfo.Culture.Name})", voiceInfo.Culture.Name, false));
            }
        }
        catch
        {
        }

        return voices
            .OrderBy(v => v.Language, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> LoadAvailableOutputDevices()
    {
        using var outputService = new AudioOutputService();
        return outputService.GetAvailableOutputDeviceNames().ToList();
    }

    private ModifierKeys BuildModifiers()
    {
        var modifiers = ModifierKeys.None;

        if (CtrlCheckBox.IsChecked == true)
        {
            modifiers |= ModifierKeys.Control;
        }

        if (AltCheckBox.IsChecked == true)
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (ShiftCheckBox.IsChecked == true)
        {
            modifiers |= ModifierKeys.Shift;
        }

        return modifiers;
    }

    public sealed class VoiceOption
    {
        public VoiceOption(string id, string displayName, string language, bool isOneCore)
        {
            Id = id;
            DisplayName = displayName;
            Language = language;
            IsOneCore = isOneCore;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Language { get; }
        public bool IsOneCore { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    private sealed class LanguageOption
    {
        public LanguageOption(AppLanguage language, string displayName)
        {
            Language = language;
            DisplayName = displayName;
        }

        public AppLanguage Language { get; }
        public string DisplayName { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
