using ExCord.Services;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace ExCord.Models;

public class AppSettings
{
    public const string AppName = "ExCord";
    private const int FallbackScreenWidth = 1920;
    private const int FallbackScreenHeight = 1080;
    private const int MaxOverlayItems = 20;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public ModifierKeys HotkeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
    public Key HotkeyKey { get; set; } = Key.T;
    public string OutputDeviceKeyword { get; set; } = "VB-Cable";
    public string TtsVoiceId { get; set; } = string.Empty;
    public bool TtsVoiceIsOneCore { get; set; } = true;
    public bool TtsEnabled { get; set; } = true;
    public bool MonitorEnabled { get; set; } = true;
    public double OutputVolume { get; set; } = 100;
    public bool AutoStart { get; set; } = false;
    public AppLanguage Language { get; set; } = LocalizationService.CurrentLanguage;
    public string WebViewUrl { get; set; } = string.Empty;
    public bool WebViewEnabled { get; set; } = false;
    public int WebViewGeometryCoordinateVersion { get; set; } = 1;
    public double WebViewX { get; set; } = GetDefaultWebViewX();
    public double WebViewY { get; set; } = GetDefaultWebViewY();
    public double WebViewWidth { get; set; } = 300;
    public double WebViewHeight { get; set; } = 300;
    public List<OverlayItemSettings> OverlayItems { get; set; } = [];

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName,
        "settings.json");

    public static double GetDefaultWebViewX()
    {
        var width = Screen.PrimaryScreen?.Bounds.Width ?? FallbackScreenWidth;
        return (width - 300d) / 2d;
    }

    public void ValidateAndNormalize()
    {
        if (!double.IsFinite(WebViewX) || Math.Abs(WebViewX) > 100000d)
        {
            WebViewX = GetDefaultWebViewX();
        }

        if (!double.IsFinite(WebViewY) || Math.Abs(WebViewY) > 100000d)
        {
            WebViewY = GetDefaultWebViewY();
        }

        if (!double.IsFinite(WebViewWidth) || WebViewWidth < 80d || WebViewWidth > 10000d)
        {
            WebViewWidth = 300d;
        }

        if (!double.IsFinite(WebViewHeight) || WebViewHeight < 80d || WebViewHeight > 10000d)
        {
            WebViewHeight = 300d;
        }

        OutputVolume = double.IsFinite(OutputVolume)
            ? Math.Clamp(OutputVolume, 0d, 100d)
            : 100d;

        if (!Enum.IsDefined(HotkeyKey))
        {
            HotkeyKey = Key.T;
        }

        const ModifierKeys validModifiers = ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows;
        if ((HotkeyModifiers & ~validModifiers) != ModifierKeys.None)
        {
            HotkeyModifiers = ModifierKeys.Control | ModifierKeys.Alt;
        }

        if (!Enum.IsDefined(Language))
        {
            Language = LocalizationService.CurrentLanguage;
        }

        ValidateAndNormalizeOverlayItems();
        SyncLegacyWebViewFromPrimaryOverlay();
    }

    public static double GetDefaultWebViewY()
    {
        var height = Screen.PrimaryScreen?.Bounds.Height ?? FallbackScreenHeight;
        return height - 300d;
    }

    public void ResetWebViewToDefault()
    {
        WebViewX = GetDefaultWebViewX();
        WebViewY = GetDefaultWebViewY();
        WebViewWidth = 300;
        WebViewHeight = 300;
    }

    private void MigrateWebViewGeometryToPixels()
    {
        if (WebViewGeometryCoordinateVersion >= 1)
        {
            return;
        }

        var dpi = DisplayCoordinateHelper.GetDpiForScreen(Screen.PrimaryScreen);
        var scale = dpi / 96d;
        WebViewX *= scale;
        WebViewY *= scale;
        WebViewWidth *= scale;
        WebViewHeight *= scale;
        WebViewGeometryCoordinateVersion = 1;
        Save();
    }

    public void EnsureWebViewPositionIsOnScreen()
    {
        var overlayLeft = WebViewX;
        var overlayTop = WebViewY;
        var overlayWidth = WebViewWidth;
        var overlayHeight = WebViewHeight;

        if (!DisplayCoordinateHelper.IsRectangleOnAnyScreen(overlayLeft, overlayTop, overlayWidth, overlayHeight))
        {
            ResetWebViewToDefault();
            SyncPrimaryOverlayFromLegacyWebView();
        }
    }

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(settings.TtsVoiceId))
            {
                settings.TtsVoiceId = string.Empty;
            }

            settings.MigrateWebViewGeometryToPixels();
            settings.EnsureOverlayItemsInitializedFromLegacy();
            settings.ValidateAndNormalize();
            settings.EnsureWebViewPositionIsOnScreen();
            return settings;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AppSettings.Load");
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            EnsureOverlayItemsInitializedFromLegacy();
            ValidateAndNormalizeOverlayItems();
            SyncPrimaryOverlayFromLegacyWebView();

            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, SerializerOptions);
            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "AppSettings.Save");
        }
    }

    private void EnsureOverlayItemsInitializedFromLegacy()
    {
        OverlayItems ??= [];
        if (OverlayItems.Count > 0)
        {
            return;
        }

        OverlayItems.Add(new OverlayItemSettings
        {
            Name = "Overlay 1",
            Url = WebViewUrl,
            Enabled = WebViewEnabled,
            X = WebViewX,
            Y = WebViewY,
            Width = WebViewWidth,
            Height = WebViewHeight
        });
    }

    private void ValidateAndNormalizeOverlayItems()
    {
        OverlayItems ??= [];

        if (OverlayItems.Count == 0)
        {
            EnsureOverlayItemsInitializedFromLegacy();
        }

        if (OverlayItems.Count > MaxOverlayItems)
        {
            OverlayItems = OverlayItems.Take(MaxOverlayItems).ToList();
        }

        var duplicateGuard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < OverlayItems.Count; i++)
        {
            var overlay = OverlayItems[i] ?? new OverlayItemSettings();
            overlay.ValidateAndNormalize(i);

            while (!duplicateGuard.Add(overlay.Id))
            {
                overlay.Id = Guid.NewGuid().ToString("N");
            }

            OverlayItems[i] = overlay;
        }
    }

    private void SyncLegacyWebViewFromPrimaryOverlay()
    {
        if (OverlayItems.Count == 0)
        {
            return;
        }

        var primary = OverlayItems[0];
        WebViewUrl = primary.Url;
        WebViewEnabled = primary.Enabled;
        WebViewX = primary.X;
        WebViewY = primary.Y;
        WebViewWidth = primary.Width;
        WebViewHeight = primary.Height;
    }

    private void SyncPrimaryOverlayFromLegacyWebView()
    {
        EnsureOverlayItemsInitializedFromLegacy();
        var primary = OverlayItems[0];
        primary.Url = WebViewUrl;
        primary.Enabled = WebViewEnabled;
        primary.X = WebViewX;
        primary.Y = WebViewY;
        primary.Width = WebViewWidth;
        primary.Height = WebViewHeight;
    }
}

