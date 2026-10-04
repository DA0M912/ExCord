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
    private const double MinimumWebViewSize = 80d;
    private const double MaximumWebViewSize = 10000d;
    private const double MaximumWebViewCoordinate = 100000d;
    private readonly AppSettings _settings;
    private WebViewOverlayWindow? _WebViewOverlayWindow;
    private readonly DispatcherTimer _WebViewGeometrySaveTimer;
    private readonly DispatcherTimer _coreSettingsSaveTimer;
    private bool _isSyncingWebViewFields;
    private bool _isInitializingCoreFields;
    private bool _isUpdatingLanguageSelection;
    private string? _latestReleaseUrl;
    private readonly CancellationTokenSource _updateCheckCancellation = new();

    public event EventHandler<AppSettings>? SettingsSaved;
    public event EventHandler<AppSettings>? WebViewGeometryChanged;

    public SettingsWindow(AppSettings settings, WebViewOverlayWindow? WebViewOverlayWindow = null)
    {
        _isSyncingWebViewFields = true;
        _isInitializingCoreFields = true;
        InitializeComponent();
        _settings = settings;
        ApplyLocalizedTexts();
        _WebViewGeometrySaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _WebViewGeometrySaveTimer.Tick += (_, _) => SaveWebViewGeometryChanges();
        _coreSettingsSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _coreSettingsSaveTimer.Tick += (_, _) =>
        {
            _coreSettingsSaveTimer.Stop();
            SaveSettings();
        };
        SetWebViewOverlayWindow(WebViewOverlayWindow);
        Closing += (_, _) =>
        {
            _updateCheckCancellation.Cancel();
            _WebViewGeometrySaveTimer.Stop();
            _coreSettingsSaveTimer.Stop();
            if (_WebViewOverlayWindow is not null && _WebViewOverlayWindow.IsEditMode)
            {
                _WebViewOverlayWindow.ExitEditMode(saveChanges: true);
            }

            SaveSettings();
            SetWebViewOverlayWindow(null);
        };

        KeyComboBox.ItemsSource = Enum.GetValues(typeof(Key)).Cast<Key>().Where(k => k != Key.None && k != Key.System && k != Key.LWin && k != Key.RWin).ToList();
        KeyComboBox.SelectedItem = _settings.HotkeyKey;

        InitializeLanguageOptions();

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

        WebViewUrlTextBox.Text = _settings.WebViewUrl;
        WebViewXTextBox.Text = _settings.WebViewX.ToString(CultureInfo.InvariantCulture);
        WebViewYTextBox.Text = _settings.WebViewY.ToString(CultureInfo.InvariantCulture);
        WebViewWidthTextBox.Text = _settings.WebViewWidth.ToString(CultureInfo.InvariantCulture);
        WebViewHeightTextBox.Text = _settings.WebViewHeight.ToString(CultureInfo.InvariantCulture);
        WebViewToggleButton.IsChecked = _settings.WebViewEnabled;

        _isSyncingWebViewFields = false;

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

    public void SetWebViewOverlayWindow(WebViewOverlayWindow? WebViewOverlayWindow)
    {
        if (ReferenceEquals(_WebViewOverlayWindow, WebViewOverlayWindow))
        {
            return;
        }

        if (_WebViewOverlayWindow is not null)
        {
            _WebViewOverlayWindow.EditModeEnded -= WebViewOverlayWindow_EditModeEnded;
            _WebViewOverlayWindow.GeometryChanged -= WebViewOverlayWindow_GeometryChanged;
        }

        _WebViewOverlayWindow = WebViewOverlayWindow;

        if (_WebViewOverlayWindow is not null)
        {
            _WebViewOverlayWindow.EditModeEnded += WebViewOverlayWindow_EditModeEnded;
            _WebViewOverlayWindow.GeometryChanged += WebViewOverlayWindow_GeometryChanged;
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
        if (WebViewEditModeToggleButton.IsChecked == true)
        {
            WebViewEditModeToggleButton.IsChecked = false;
        }

        if (!saved)
        {
            SyncWebViewFieldsFromSettings();
        }
    }

    private void WebViewOverlayWindow_GeometryChanged(object? sender, EventArgs e)
    {
        if (_isSyncingWebViewFields)
        {
            return;
        }

        var activeBox = FocusManager.GetFocusedElement(this) as System.Windows.Controls.TextBox;
        if (activeBox == WebViewXTextBox || activeBox == WebViewYTextBox || activeBox == WebViewWidthTextBox || activeBox == WebViewHeightTextBox)
        {
            return;
        }

        SyncWebViewFieldsFromOverlayGeometry();
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

    private void ApplyWebViewUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var url = WebViewUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _settings.WebViewUrl = url;
        if (_WebViewOverlayWindow is not null)
        {
            _WebViewOverlayWindow.NavigateToUrl(url);
        }

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

    private void WebViewToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingWebViewFields)
        {
            return;
        }

        var isEnabled = WebViewToggleButton.IsChecked == true;
        _settings.WebViewEnabled = isEnabled;
        _settings.WebViewUrl = WebViewUrlTextBox.Text.Trim();

        if (!isEnabled && _WebViewOverlayWindow is not null)
        {
            _WebViewOverlayWindow.IsEditMode = false;
            WebViewEditModeToggleButton.IsChecked = false;
        }

        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
    }

    private void WebViewToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingWebViewFields)
        {
            return;
        }

        var isEnabled = WebViewToggleButton.IsChecked == true;
        _settings.WebViewEnabled = isEnabled;
        _settings.WebViewUrl = WebViewUrlTextBox.Text.Trim();

        if (!isEnabled && _WebViewOverlayWindow is not null)
        {
            _WebViewOverlayWindow.IsEditMode = false;
            WebViewEditModeToggleButton.IsChecked = false;
        }

        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
    }

    private void WebViewEditModeToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingWebViewFields)
        {
            return;
        }

        if (_WebViewOverlayWindow is null)
        {
            WebViewEditModeToggleButton.IsChecked = false;
            return;
        }

        if (_WebViewOverlayWindow.IsEditMode)
        {
            WebViewEditModeToggleButton.IsChecked = false;
            return;
        }

        SyncWebViewFieldsFromSettings();
        _WebViewOverlayWindow.BeginEditMode();
        _WebViewOverlayWindow.ApplyGeometry(
            _settings.WebViewX,
            _settings.WebViewY,
            _settings.WebViewWidth,
            _settings.WebViewHeight);
    }

    private void WebViewEditModeToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingWebViewFields)
        {
            return;
        }

        if (_WebViewOverlayWindow is null || !_WebViewOverlayWindow.IsEditMode)
        {
            return;
        }

        _WebViewOverlayWindow.ExitEditMode(saveChanges: true);
        SyncWebViewFieldsFromSettings();
        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
    }

    private void WebViewPositionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingWebViewFields || _WebViewOverlayWindow is null)
        {
            return;
        }

        if (!TryParseWebViewGeometry(out var x, out var y, out var width, out var height))
        {
            return;
        }

        _settings.WebViewX = x;
        _settings.WebViewY = y;
        _settings.WebViewWidth = width;
        _settings.WebViewHeight = height;
        _WebViewOverlayWindow.ApplyGeometry(x, y, width, height);
        _WebViewGeometrySaveTimer.Stop();
        _WebViewGeometrySaveTimer.Start();
    }

    private void SaveWebViewGeometryChanges()
    {
        _WebViewGeometrySaveTimer.Stop();
        _settings.Save();
        WebViewGeometryChanged?.Invoke(this, _settings);
    }

    private void ResetWebViewPositionButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetWebViewToDefault();
        SyncWebViewFieldsFromSettings();

        if (_WebViewOverlayWindow is not null)
        {
            _WebViewOverlayWindow.ApplyGeometry(_settings.WebViewX, _settings.WebViewY, _settings.WebViewWidth, _settings.WebViewHeight);
        }

        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
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

        WebUrlTextBlock.Text = text.WebUrl;
        ApplyWebViewUrlButton.Content = text.Apply;
        WebViewEditModeToggleButton.Content = text.EditMode;
        WebViewXTextBlock.Text = text.AxisX;
        WebViewYTextBlock.Text = text.AxisY;
        WebViewWidthTextBlock.Text = text.SizeWidth;
        WebViewHeightTextBlock.Text = text.SizeHeight;

        NewVersionTextBlock.Text = text.NewVersionAvailable;
        CloseButton.Content = text.Close;

        InitializeLanguageOptions();
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
        _settings.WebViewUrl = WebViewUrlTextBox.Text.Trim();
        _settings.WebViewEnabled = WebViewToggleButton.IsChecked == true;

        if (TryParseWebViewGeometry(out var x, out var y, out var width, out var height))
        {
            _settings.WebViewX = x;
            _settings.WebViewY = y;
            _settings.WebViewWidth = width;
            _settings.WebViewHeight = height;
        }

        _settings.Save();
        SettingsSaved?.Invoke(this, _settings);
    }

    private void SyncWebViewFieldsFromOverlayGeometry()
    {
        if (_WebViewOverlayWindow is null)
        {
            return;
        }

        _isSyncingWebViewFields = true;
        try
        {
            var bounds = _WebViewOverlayWindow.GetGeometryInPixels();
            SetTextPreservingCaret((System.Windows.Controls.TextBox)WebViewXTextBox, bounds.X.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)WebViewYTextBox, bounds.Y.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)WebViewWidthTextBox, bounds.Width.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)WebViewHeightTextBox, bounds.Height.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            _isSyncingWebViewFields = false;
        }
    }

    private void SyncWebViewFieldsFromSettings()
    {
        _isSyncingWebViewFields = true;
        try
        {
            SetTextPreservingCaret((System.Windows.Controls.TextBox)WebViewUrlTextBox, _settings.WebViewUrl);
            SetTextPreservingCaret((System.Windows.Controls.TextBox)WebViewXTextBox, _settings.WebViewX.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)WebViewYTextBox, _settings.WebViewY.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)WebViewWidthTextBox, _settings.WebViewWidth.ToString(CultureInfo.InvariantCulture));
            SetTextPreservingCaret((System.Windows.Controls.TextBox)WebViewHeightTextBox, _settings.WebViewHeight.ToString(CultureInfo.InvariantCulture));
        }
        finally
        {
            _isSyncingWebViewFields = false;
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

    private bool TryParseWebViewGeometry(out double x, out double y, out double width, out double height)
    {
        x = 0;
        y = 0;
        width = 300;
        height = 300;

        if (!double.TryParse(WebViewXTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            || !double.TryParse(WebViewYTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out y)
            || !double.TryParse(WebViewWidthTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out width)
            || !double.TryParse(WebViewHeightTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out height)
            || !double.IsFinite(x)
            || !double.IsFinite(y)
            || !double.IsFinite(width)
            || !double.IsFinite(height)
            || Math.Abs(x) > MaximumWebViewCoordinate
            || Math.Abs(y) > MaximumWebViewCoordinate
            || width < MinimumWebViewSize
            || width > MaximumWebViewSize
            || height < MinimumWebViewSize
            || height > MaximumWebViewSize)
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

