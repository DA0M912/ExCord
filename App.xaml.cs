using ExCord.Models;
using ExCord.Services;
using ExCord.Views;
using System.Windows;
using Application = System.Windows.Application;

namespace ExCord;

public partial class App : Application
{
    private NotifyIcon? _notifyIcon;
    private Icon? _trayIcon;
    private GlobalHotkeyService? _hotkeyService;
    private TtsService? _ttsService;
    private AudioOutputService? _audioOutputService;
    private OverlayManager? _overlayManager;
    private InputWindow? _inputWindow;
    private SettingsWindow? _settingsWindow;
    private AppSettings _settings = new();
    private AutoStartService _autoStartService = new();
    private CancellationTokenSource? _textSubmissionCancellation;
    private const int DispatcherExceptionLimit = 3;
    private static readonly TimeSpan DispatcherExceptionWindow = TimeSpan.FromSeconds(10);
    private readonly Dictionary<string, Queue<DateTime>> _dispatcherExceptionOccurrences = new();
    private bool _isShuttingDownAfterDispatcherExceptions;
    private bool _lastAppliedAutoStart;
    private bool _isShutdownInProgress;
    private static readonly TimeSpan OperationShutdownTimeout = TimeSpan.FromSeconds(3);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LoggingService.Initialize();
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        try
        {
            _settings = AppSettings.Load();
            LocalizationService.SetLanguage(_settings.Language);
            _lastAppliedAutoStart = _settings.AutoStart;
            _autoStartService.ReconcilePathIfNeeded(_settings.AutoStart);
            _settings.EnsureWebViewPositionIsOnScreen();
            _ttsService = new TtsService();
            _ttsService.SetPreferredVoice(_settings.TtsVoiceId, _settings.TtsVoiceIsOneCore);
            _audioOutputService = new AudioOutputService();
            _hotkeyService = new GlobalHotkeyService();
            _overlayManager = new OverlayManager();
            _ttsService.VoiceFallback += (_, message) => ShowTrayNotification(message);
            _audioOutputService.PlaybackIssue += (_, message) => ShowTrayNotification(message);
            _hotkeyService.HotkeyPressed += (_, _) => ShowInputOverlay();

            SetupTrayIcon();
            RegisterHotkeySafely();
            ApplyWebViewState(_settings);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "OnStartup");
            ShowTrayNotification("An error occurred while initializing the app. Check the log for details.");
            System.Windows.MessageBox.Show($"An error occurred while initializing the app.\n\n{ex.Message}", "ExCord", MessageBoxButton.OK, MessageBoxImage.Warning);
            _ = ShutdownApplicationAsync();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _settings.Save();
        }
        catch
        {
            // ignored
        }

        DisposeApplicationResources();
        base.OnExit(e);
    }

    private async Task ShutdownApplicationAsync()
    {
        if (_isShutdownInProgress)
        {
            return;
        }

        _isShutdownInProgress = true;
        CancelActiveOperations();

        var ttsWaitTask = _ttsService?.WaitForOperationsAsync(OperationShutdownTimeout) ?? Task.FromResult(true);
        var audioWaitTask = _audioOutputService?.WaitForOperationsAsync(OperationShutdownTimeout) ?? Task.FromResult(true);
        var waitResults = await Task.WhenAll(ttsWaitTask, audioWaitTask);

        if (!waitResults[0])
        {
            LoggingService.LogMessage("TTS operations did not stop within the shutdown timeout.");
        }

        if (!waitResults[1])
        {
            LoggingService.LogMessage("Audio operations did not stop within the shutdown timeout.");
        }

        Shutdown();
    }

    private void DisposeApplicationResources()
    {
        var textSubmissionCancellation = _textSubmissionCancellation;
        _textSubmissionCancellation = null;
        if (textSubmissionCancellation is not null)
        {
            TryCleanup(textSubmissionCancellation.Cancel, "Text submission cancellation");
        }

        var hotkeyService = _hotkeyService;
        _hotkeyService = null;
        if (hotkeyService is not null)
        {
            TryCleanup(hotkeyService.Dispose, "Global hotkey service disposal");
        }

        var ttsService = _ttsService;
        _ttsService = null;
        if (ttsService is not null)
        {
            TryCleanup(ttsService.Dispose, "TTS service disposal");
        }

        var audioOutputService = _audioOutputService;
        _audioOutputService = null;
        if (audioOutputService is not null)
        {
            TryCleanup(audioOutputService.Dispose, "Audio output service disposal");
        }

        var notifyIcon = _notifyIcon;
        _notifyIcon = null;
        if (notifyIcon is not null)
        {
            TryCleanup(notifyIcon.Dispose, "Notify icon disposal");
        }

        var trayIcon = _trayIcon;
        _trayIcon = null;
        if (trayIcon is not null)
        {
            TryCleanup(trayIcon.Dispose, "Tray icon disposal");
        }

        var overlayManager = _overlayManager;
        _overlayManager = null;
        if (overlayManager is not null)
        {
            TryCleanup(overlayManager.Dispose, "Overlay manager disposal");
        }
    }

    private void CancelActiveOperations()
    {
        TryCleanup(() => _textSubmissionCancellation?.Cancel(), "Text submission cancellation");
        TryCleanup(() => _audioOutputService?.CancelCurrentPlayback(), "Audio playback cancellation");
    }

    private static void TryCleanup(Action cleanup, string context)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, context);
        }
    }

    private void App_DispatcherUnhandledException(object? sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LoggingService.LogException(e.Exception, "DispatcherUnhandledException");

        if (_isShuttingDownAfterDispatcherExceptions)
        {
            e.Handled = true;
            return;
        }

        if (HasReachedDispatcherExceptionLimit(e.Exception))
        {
            _isShuttingDownAfterDispatcherExceptions = true;
            e.Handled = true;
            ShowTrayNotification("Repeated errors occurred. ExCord will close. Check the log for details.");
            _ = ShutdownApplicationAsync();
            return;
        }

        ShowTrayNotification("An error occurred. Check the log for details.");
        e.Handled = true;
    }

    private bool HasReachedDispatcherExceptionLimit(Exception exception)
    {
        var exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
        var declaringType = exception.TargetSite?.DeclaringType?.FullName ?? "UnknownType";
        var methodName = exception.TargetSite?.Name ?? "UnknownMethod";
        var fingerprint = $"{exceptionType}|{declaringType}.{methodName}";
        var now = DateTime.UtcNow;

        if (!_dispatcherExceptionOccurrences.TryGetValue(fingerprint, out var occurrences))
        {
            occurrences = new Queue<DateTime>();
            _dispatcherExceptionOccurrences.Add(fingerprint, occurrences);
        }

        occurrences.Enqueue(now);
        while (occurrences.Count > 0 && now - occurrences.Peek() > DispatcherExceptionWindow)
        {
            occurrences.Dequeue();
        }

        if (occurrences.Count == 0)
        {
            _dispatcherExceptionOccurrences.Remove(fingerprint);
            return false;
        }

        return occurrences.Count >= DispatcherExceptionLimit;
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LoggingService.LogException(ex, "AppDomain.UnhandledException");
            ShowTrayNotification("An error occurred. Check the log for details.");
        }
        else
        {
            LoggingService.LogMessage($"AppDomain.UnhandledException: {e.ExceptionObject}");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LoggingService.LogException(e.Exception, "TaskScheduler.UnobservedTaskException");
        ShowTrayNotification("An error occurred. Check the log for details.");
        e.SetObserved();
    }

    private void SetupTrayIcon()
    {
        var menu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("Settings");
        settingsItem.Click += (_, _) => OpenSettingsWindow();

        var WebViewToggleItem = new ToolStripMenuItem($"Overlay: {(_settings.WebViewEnabled ? "ON" : "OFF")}");
        WebViewToggleItem.Click += (_, _) =>
        {
            _settings.WebViewEnabled = !_settings.WebViewEnabled;
            WebViewToggleItem.Text = $"Overlay: {(_settings.WebViewEnabled ? "ON" : "OFF")}";
            WebViewToggleItem.Checked = _settings.WebViewEnabled;
            _settings.Save();
            _settingsWindow?.SyncWebViewEnabledState(_settings.WebViewEnabled);
            ApplyWebViewState(_settings);
        };
        WebViewToggleItem.CheckOnClick = true;
        WebViewToggleItem.Checked = _settings.WebViewEnabled;

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += async (_, _) => await ShutdownApplicationAsync();

        menu.Items.Add(settingsItem);
        menu.Items.Add(WebViewToggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "ExCord",
            Icon = _trayIcon = LoadTrayIcon(),
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => OpenSettingsWindow();
    }

    private static Icon LoadTrayIcon()
    {
        var resourceInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ExCord.ico"));
        if (resourceInfo is null)
        {
            return (Icon)SystemIcons.Information.Clone();
        }

        using var stream = resourceInfo.Stream;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, _overlayManager?.GetPrimaryWindow());
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
            };
            _settingsWindow.SettingsSaved += (_, updatedSettings) =>
            {
                var autoStartChanged = _lastAppliedAutoStart != updatedSettings.AutoStart;
                _settings = updatedSettings;
                _ttsService?.SetPreferredVoice(updatedSettings.TtsVoiceId, updatedSettings.TtsVoiceIsOneCore);
                _inputWindow?.ApplyLocalizedTexts();
                RegisterHotkeySafely();
                if (autoStartChanged)
                {
                    _autoStartService.SetEnabled(updatedSettings.AutoStart);
                    _lastAppliedAutoStart = updatedSettings.AutoStart;
                }

                ApplyWebViewState(updatedSettings);
                _settingsWindow?.SetWebViewOverlayWindow(_overlayManager?.GetPrimaryWindow());
            };
            _settingsWindow.WebViewGeometryChanged += (_, updatedSettings) =>
            {
                _settings = updatedSettings;
                ApplyWebViewState(updatedSettings);
                _settingsWindow?.SetWebViewOverlayWindow(_overlayManager?.GetPrimaryWindow());
            };
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplyWebViewState(AppSettings settings)
    {
        _overlayManager?.Apply(settings);
        _settingsWindow?.SetWebViewOverlayWindow(_overlayManager?.GetPrimaryWindow());
    }

    private void ShowInputOverlay()
    {
        if (_isShutdownInProgress || !_settings.TtsEnabled)
        {
            return;
        }

        if (_inputWindow is null)
        {
            _inputWindow = new InputWindow();
            _inputWindow.TextSubmitted += async (_, text) =>
            {
                if (_isShutdownInProgress || !_settings.TtsEnabled || string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                var cancellation = new CancellationTokenSource();
                var previousCancellation = _textSubmissionCancellation;
                _textSubmissionCancellation = cancellation;
                previousCancellation?.Cancel();
                _audioOutputService?.CancelCurrentPlayback();

                try
                {
                    if (_ttsService is null || _audioOutputService is null)
                    {
                        return;
                    }

                    var audioData = await _ttsService.SynthesizeAsync(text, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    await _audioOutputService.PlayAsync(audioData, _settings.OutputDeviceKeyword, _settings.MonitorEnabled, _settings.OutputVolume, cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    LoggingService.LogException(ex, "ShowInputOverlay");
                    ShowTrayNotification($"Speech playback error: {ex.Message}");
                }
                finally
                {
                    if (ReferenceEquals(_textSubmissionCancellation, cancellation))
                    {
                        _textSubmissionCancellation = null;
                    }

                    cancellation.Dispose();
                }
            };
        }

        _inputWindow.ApplyLocalizedTexts();
        _inputWindow.ShowOverlay();
    }

    private void RegisterHotkeySafely()
    {
        if (_hotkeyService is null)
        {
            return;
        }

        try
        {
            if (!_settings.TtsEnabled)
            {
                _hotkeyService.UnregisterHotkey();
                _audioOutputService?.CancelCurrentPlayback();
                _inputWindow?.Hide();
                return;
            }

            var registered = _hotkeyService.RegisterHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
            if (!registered)
            {
                ShowTrayNotification("Failed to register the global hotkey. Check your settings.");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "RegisterHotkeySafely");
            ShowTrayNotification($"Hotkey registration failed: {ex.Message}");
        }
    }

    private void ShowTrayNotification(string message)
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.ShowBalloonTip(1500);
        }
    }
}


