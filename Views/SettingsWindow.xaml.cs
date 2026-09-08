using ExCord.Models;
using ExCord.Services;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ExCord.Views;

public partial class SettingsWindow : Window
{
    private const double MinimumGifTalkSize = 80d;
    private const double MaximumGifTalkSize = 10000d;
    private const double MaximumGifTalkCoordinate = 100000d;
    private readonly AppSettings _settings;
    private GifTalkOverlayWindow? _gifTalkOverlayWindow;
    private readonly DispatcherTimer _gifTalkGeometrySaveTimer;
    private readonly DispatcherTimer _coreSettingsSaveTimer;
    private bool _isSyncingGifTalkFields;
    private bool _isInitializingCoreFields;
    private string? _latestReleaseUrl;
    private readonly CancellationTokenSource _updateCheckCancellation = new();

    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler<AppSettings>? GifTalkGeometryChanged;

    public SettingsWindow(AppSettings settings, GifTalkOverlayWindow? gifTalkOverlayWindow = null)
    {
        _isSyncingGifTalkFields = true;
        _isInitializingCoreFields = true;
        InitializeComponent();
        _settings = settings;
        _gifTalkGeometrySaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _gifTalkGeometrySaveTimer.Tick += (_, _) => SaveGifTalkGeometryChanges();
        _coreSettingsSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _coreSettingsSaveTimer.Tick += (_, _) =>
        {
            _coreSettingsSaveTimer.Stop();
            SaveSettings();
        };
        SetGifTalkOverlayWindow(gifTalkOverlayWindow);
        Closing += (_, _) =>
        {
            _updateCheckCancellation.Cancel();
            _gifTalkGeometrySaveTimer.Stop();
            _coreSettingsSaveTimer.Stop();
            if (_gifTalkOverlayWindow is not null && _gifTalkOverlayWindow.IsEditMode)
            {
                _gifTalkOverlayWindow.ExitEditMode(saveChanges: true);
            }

            SaveSettings();
            SetGifTalkOverlayWindow(null);
        };

        KeyComboBox.ItemsSource = Enum.GetValues(typeof(Key)).Cast<Key>().Where(k => k != Key.None && k != Key.System && k != Key.LWin && k != Key.RWin).ToList();
        KeyComboBox.SelectedItem = _settings.HotkeyKey;

        CtrlCheckBox.IsChecked = (_settings.HotkeyModifiers & ModifierKeys.Control) == ModifierKeys.Control;
        AltCheckBox.IsChecked = (_settings.HotkeyModifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        ShiftCheckBox.IsChecked = (_settings.HotkeyModifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        // PERF: 설치된 언어팩/보이스가 매우 많은 환경에서 설정창 열기가 느리면 여기를 비동기로 전환할 것.
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
        _isInitializingCoreFields = false;

        OutputVolumeSlider.ValueChanged += (_, _) => OutputVolumeLabel.Text = $"{OutputVolumeSlider.Value:0}%";
        VoiceComboBox.SelectionChanged += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            SaveSettings();
        };
        DeviceComboBox.SelectionChanged += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            SaveSettings();
        };
        MonitorEnabledCheckBox.Checked += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            SaveSettings();
        };
        MonitorEnabledCheckBox.Unchecked += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            SaveSettings();
        };
        AutoStartToggleButton.Checked += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            SaveSettings();
        };
        AutoStartToggleButton.Unchecked += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            SaveSettings();
        };
        OutputVolumeSlider.ValueChanged += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            RestartCoreSettingsSaveTimer();
        };
        CtrlCheckBox.Checked += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            RestartCoreSettingsSaveTimer();
        };
        CtrlCheckBox.Unchecked += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            RestartCoreSettingsSaveTimer();
        };
        AltCheckBox.Checked += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            RestartCoreSettingsSaveTimer();
        };
        AltCheckBox.Unchecked += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            RestartCoreSettingsSaveTimer();
        };
        ShiftCheckBox.Checked += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            RestartCoreSettingsSaveTimer();
        };
        ShiftCheckBox.Unchecked += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            RestartCoreSettingsSaveTimer();
        };
        KeyComboBox.SelectionChanged += (_, _) =>
        {
            if (_isInitializingCoreFields)
            {
                return;
            }

            RestartCoreSettingsSaveTimer();
        };
        OutputVolumeLabel.Text = $"{_settings.OutputVolume:0}%";
        VoiceComboBox.PreviewMouseLeftButtonDown += VoiceComboBox_PreviewMouseLeftButtonDown;
        DeviceComboBox.PreviewMouseLeftButtonDown += DeviceComboBox_PreviewMouseLeftButtonDown;

        GifTalkUrlTextBox.Text = _settings.GifTalkUrl;
        GifTalkXTextBox.Text = _settings.GifTalkX.ToString(CultureInfo.InvariantCulture);
        GifTalkYTextBox.Text = _settings.GifTalkY.ToString(CultureInfo.InvariantCulture);
        GifTalkWidthTextBox.Text = _settings.GifTalkWidth.ToString(CultureInfo.InvariantCulture);
        GifTalkHeightTextBox.Text = _settings.GifTalkHeight.ToString(CultureInfo.InvariantCulture);
        GifTalkToggleButton.IsChecked = _settings.GifTalkEnabled;

        _isSyncingGifTalkFields = false;

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

    public void SetGifTalkOverlayWindow(GifTalkOverlayWindow? gifTalkOverlayWindow)
    {
        if (ReferenceEquals(_gifTalkOverlayWindow, gifTalkOverlayWindow))
        {
            return;
        }

        if (_gifTalkOverlayWindow is not null)
        {
            _gifTalkOverlayWindow.EditModeEnded -= GifTalkOverlayWindow_EditModeEnded;
            _gifTalkOverlayWindow.GeometryChanged -= GifTalkOverlayWindow_GeometryChanged;
        }

        _gifTalkOverlayWindow = gifTalkOverlayWindow;

        if (_gifTalkOverlayWindow is not null)
        {
            _gifTalkOverlayWindow.EditModeEnded += GifTalkOverlayWindow_EditModeEnded;
            _gifTalkOverlayWindow.GeometryChanged += GifTalkOverlayWindow_GeometryChanged;
        }
    }

    public void SyncGifTalkEnabledState(bool isEnabled)
    {
        if (GifTalkToggleButton.IsChecked == isEnabled)
        {
            return;
        }

        GifTalkToggleButton.Checked -= GifTalkToggleButton_Checked;
        GifTalkToggleButton.Unchecked -= GifTalkToggleButton_Unchecked;
        try
        {
            GifTalkToggleButton.IsChecked = isEnabled;
        }
        finally
        {
            GifTalkToggleButton.Checked += GifTalkToggleButton_Checked;
            GifTalkToggleButton.Unchecked += GifTalkToggleButton_Unchecked;
        }
    }

    private void GifTalkOverlayWindow_EditModeEnded(object? sender, bool saved)
    {
        if (GifTalkEditModeToggleButton.IsChecked == true)
        {
            GifTalkEditModeToggleButton.IsChecked = false;
        }

        if (!saved)
        {
            SyncGifTalkFieldsFromSettings();
        }
    }

    private void GifTalkOverlayWindow_GeometryChanged(object? sender, EventArgs e)
    {
        if (_isSyncingGifTalkFields)
        {
            return;
        }

        var activeBox = FocusManager.GetFocusedElement(this) as System.Windows.Controls.TextBox;
        if (activeBox == GifTalkXTextBox || activeBox == GifTalkYTextBox || activeBox == GifTalkWidthTextBox || activeBox == GifTalkHeightTextBox)
        {
            return;
        }

        SyncGifTalkFieldsFromOverlayGeometry();
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

    private void ApplyGifTalkUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var url = GifTalkUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _settings.GifTalkUrl = url;
        if (_gifTalkOverlayWindow is not null)
        {
            _gifTalkOverlayWindow.NavigateToUrl(url);
        }

        SaveSettings();
    }

    private void GifTalkSaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
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

    private void GifTalkToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingGifTalkFields)
        {
            return;
        }

        var isEnabled = GifTalkToggleButton.IsChecked == true;
        _settings.GifTalkEnabled = isEnabled;
        _settings.GifTalkUrl = GifTalkUrlTextBox.Text.Trim();

        if (!isEnabled && _gifTalkOverlayWindow is not null)
        {
            _gifTalkOverlayWindow.IsEditMode = false;
            GifTalkEditModeToggleButton.IsChecked = false;
        }

        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
    }

    private void GifTalkToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingGifTalkFields)
        {
            return;
        }

        var isEnabled = GifTalkToggleButton.IsChecked == true;
        _settings.GifTalkEnabled = isEnabled;
        _settings.GifTalkUrl = GifTalkUrlTextBox.Text.Trim();

        if (!isEnabled && _gifTalkOverlayWindow is not null)
        {
            _gifTalkOverlayWindow.IsEditMode = false;
            GifTalkEditModeToggleButton.IsChecked = false;
        }

        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
    }

    private void GifTalkEditModeToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingGifTalkFields)
        {
            return;
        }

        if (_gifTalkOverlayWindow is null)
        {
            GifTalkEditModeToggleButton.IsChecked = false;
            return;
        }

        if (_gifTalkOverlayWindow.IsEditMode)
        {
            GifTalkEditModeToggleButton.IsChecked = false;
            return;
        }

        SyncGifTalkFieldsFromSettings();
        _gifTalkOverlayWindow.BeginEditMode();
        _gifTalkOverlayWindow.ApplyGeometry(
            _settings.GifTalkX,
            _settings.GifTalkY,
            _settings.GifTalkWidth,
            _settings.GifTalkHeight);
    }

    private void GifTalkEditModeToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingGifTalkFields)
        {
            return;
        }

        if (_gifTalkOverlayWindow is null || !_gifTalkOverlayWindow.IsEditMode)
        {
            return;
        }

        _gifTalkOverlayWindow.ExitEditMode(saveChanges: true);
        SyncGifTalkFieldsFromSettings();
        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
    }

    private void GifTalkPositionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingGifTalkFields || _gifTalkOverlayWindow is null)
        {
            return;
        }

        if (!TryParseGifTalkGeometry(out var x, out var y, out var width, out var height))
        {
            return;
        }

        _settings.GifTalkX = x;
        _settings.GifTalkY = y;
        _settings.GifTalkWidth = width;
        _settings.GifTalkHeight = height;
        _gifTalkOverlayWindow.ApplyGeometry(x, y, width, height);
        _gifTalkGeometrySaveTimer.Stop();
        _gifTalkGeometrySaveTimer.Start();
    }

    private void SaveGifTalkGeometryChanges()
    {
        _gifTalkGeometrySaveTimer.Stop();
        _settings.Save();
        GifTalkGeometryChanged?.Invoke(this, _settings);
    }

    private void ResetGifTalkPositionButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetGifTalkToDefault();
        SyncGifTalkFieldsFromSettings();

        if (_gifTalkOverlayWindow is not null)
        {
            _gifTalkOverlayWindow.ApplyGeometry(_settings.GifTalkX, _settings.GifTalkY, _settings.GifTalkWidth, _settings.GifTalkHeight);
        }

        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
    }

    private void SaveSettings()
    {
        var selectedVoice = VoiceComboBox.SelectedItem as VoiceOption;
        var selectedKey = KeyComboBox.SelectedItem as Key?;

        _settings.HotkeyModifiers = BuildModifiers();
        _settings.HotkeyKey = selectedKey ?? _settings.HotkeyKey;
        _settings.TtsVoiceId = selectedVoice?.Id ?? string.Empty;
        _settings.TtsVoiceIsOneCore = selectedVoice?.IsOneCore ?? true;
        _settings.TtsEnabled = TtsToggleButton.IsChecked == true;
        _settings.OutputDeviceKeyword = DeviceComboBox.SelectedItem as string ?? _settings.OutputDeviceKeyword;
        _settings.OutputVolume = OutputVolumeSlider.Value;
        _settings.MonitorEnabled = MonitorEnabledCheckBox.IsChecked == true;
        _settings.AutoStart = AutoStartToggleButton.IsChecked == true;
        _settings.GifTalkUrl = GifTalkUrlTextBox.Text.Trim();
        _settings.GifTalkEnabled = GifTalkToggleButton.IsChecked == true;

        if (TryParseGifTalkGeometry(out var x, out var y, out var width, out var height))
        {
            _settings.GifTalkX = x;
            _settings.GifTalkY = y;
            _settings.GifTalkWidth = width;
            _settings.GifTalkHeight = height;
        }

        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
    }

    private void SyncGifTalkFieldsFromOverlayGeometry()
    {
        if (_gifTalkOverlayWindow is null)
        {
            return;
        }

        _isSyncingGifTalkFields = true;
        try
        {
            var bounds = _gifTalkOverlayWindow.GetGeometryInPixels();
            SetTextPreservingCaret((System.Windows.Controls.TextBox)GifTalkXTextBox, bounds.X.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)GifTalkYTextBox, bounds.Y.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)GifTalkWidthTextBox, bounds.Width.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)GifTalkHeightTextBox, bounds.Height.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            _isSyncingGifTalkFields = false;
        }
    }

    private void SyncGifTalkFieldsFromSettings()
    {
        _isSyncingGifTalkFields = true;
        try
        {
            SetTextPreservingCaret((System.Windows.Controls.TextBox)GifTalkUrlTextBox, _settings.GifTalkUrl);
            SetTextPreservingCaret((System.Windows.Controls.TextBox)GifTalkXTextBox, _settings.GifTalkX.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)GifTalkYTextBox, _settings.GifTalkY.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)GifTalkWidthTextBox, _settings.GifTalkWidth.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)GifTalkHeightTextBox, _settings.GifTalkHeight.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            _isSyncingGifTalkFields = false;
        }
    }

    private static void SetTextPreservingCaret(System.Windows.Controls.TextBox textBox, string value)
    {
        if (textBox is null)
        {
            return;
        }

        if (string.Equals(textBox.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        var caretIndex = textBox.CaretIndex;
        if (caretIndex < 0)
        {
            caretIndex = textBox.Text.Length;
        }

        textBox.Text = value;
        textBox.SelectionStart = Math.Min(caretIndex, textBox.Text.Length);
        textBox.SelectionLength = 0;
    }

    private bool TryParseGifTalkGeometry(out double x, out double y, out double width, out double height)
    {
        x = 0;
        y = 0;
        width = 300;
        height = 300;

        if (!double.TryParse(GifTalkXTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            || !double.TryParse(GifTalkYTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out y)
            || !double.TryParse(GifTalkWidthTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out width)
            || !double.TryParse(GifTalkHeightTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out height)
            || !double.IsFinite(x)
            || !double.IsFinite(y)
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || Math.Abs(x) > MaximumGifTalkCoordinate
            || Math.Abs(y) > MaximumGifTalkCoordinate
            || width < MinimumGifTalkSize
            || width > MaximumGifTalkSize
            || height < MinimumGifTalkSize
            || height > MaximumGifTalkSize)
        {
            return false;
        }

        return true;
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
            // SAPI5 음성이 없거나 접근할 수 없으면 OneCore 목록만 사용한다.
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
}
