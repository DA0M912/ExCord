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
    public string GifTalkUrl { get; set; } = string.Empty;
    public bool GifTalkEnabled { get; set; } = false;
    public int GifTalkGeometryCoordinateVersion { get; set; } = 1;
    public double GifTalkX { get; set; } = GetDefaultGifTalkX();
    public double GifTalkY { get; set; } = GetDefaultGifTalkY();
    public double GifTalkWidth { get; set; } = 300;
    public double GifTalkHeight { get; set; } = 300;
    public List<OverlayItemSettings> OverlayItems { get; set; } = [];

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName,
        "settings.json");

    public static double GetDefaultGifTalkX()
    {
        var width = Screen.PrimaryScreen?.Bounds.Width ?? FallbackScreenWidth;
        return (width - 300d) / 2d;
    }

    public void ValidateAndNormalize()
    {
        if (!double.IsFinite(GifTalkX) || Math.Abs(GifTalkX) > 100000d)
        {
            GifTalkX = GetDefaultGifTalkX();
        }

        if (!double.IsFinite(GifTalkY) || Math.Abs(GifTalkY) > 100000d)
        {
            GifTalkY = GetDefaultGifTalkY();
        }

        if (!double.IsFinite(GifTalkWidth) || GifTalkWidth < 80d || GifTalkWidth > 10000d)
        {
            GifTalkWidth = 300d;
        }

        if (!double.IsFinite(GifTalkHeight) || GifTalkHeight < 80d || GifTalkHeight > 10000d)
        {
            GifTalkHeight = 300d;
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
        SyncLegacyGifTalkFromPrimaryOverlay();
    }

    public static double GetDefaultGifTalkY()
    {
        var height = Screen.PrimaryScreen?.Bounds.Height ?? FallbackScreenHeight;
        return height - 300d;
    }

    public void ResetGifTalkToDefault()
    {
        GifTalkX = GetDefaultGifTalkX();
        GifTalkY = GetDefaultGifTalkY();
        GifTalkWidth = 300;
        GifTalkHeight = 300;
    }

    private void MigrateGifTalkGeometryToPixels()
    {
        if (GifTalkGeometryCoordinateVersion >= 1)
        {
            return;
        }

        var dpi = DisplayCoordinateHelper.GetDpiForScreen(Screen.PrimaryScreen);
        var scale = dpi / 96d;
        GifTalkX *= scale;
        GifTalkY *= scale;
        GifTalkWidth *= scale;
        GifTalkHeight *= scale;
        GifTalkGeometryCoordinateVersion = 1;
        Save();
    }

    public void EnsureGifTalkPositionIsOnScreen()
    {
        var overlayLeft = GifTalkX;
        var overlayTop = GifTalkY;
        var overlayWidth = GifTalkWidth;
        var overlayHeight = GifTalkHeight;

        if (!DisplayCoordinateHelper.IsRectangleOnAnyScreen(overlayLeft, overlayTop, overlayWidth, overlayHeight))
        {
            ResetGifTalkToDefault();
            SyncPrimaryOverlayFromLegacyGifTalk();
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

            settings.MigrateGifTalkGeometryToPixels();
            settings.EnsureOverlayItemsInitializedFromLegacy();
            settings.ValidateAndNormalize();
            settings.EnsureGifTalkPositionIsOnScreen();
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
            SyncPrimaryOverlayFromLegacyGifTalk();

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
            Url = GifTalkUrl,
            Enabled = GifTalkEnabled,
            X = GifTalkX,
            Y = GifTalkY,
            Width = GifTalkWidth,
            Height = GifTalkHeight
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

    private void SyncLegacyGifTalkFromPrimaryOverlay()
    {
        if (OverlayItems.Count == 0)
        {
            return;
        }

        var primary = OverlayItems[0];
        GifTalkUrl = primary.Url;
        GifTalkEnabled = primary.Enabled;
        GifTalkX = primary.X;
        GifTalkY = primary.Y;
        GifTalkWidth = primary.Width;
        GifTalkHeight = primary.Height;
    }

    private void SyncPrimaryOverlayFromLegacyGifTalk()
    {
        EnsureOverlayItemsInitializedFromLegacy();
        var primary = OverlayItems[0];
        primary.Url = GifTalkUrl;
        primary.Enabled = GifTalkEnabled;
        primary.X = GifTalkX;
        primary.Y = GifTalkY;
        primary.Width = GifTalkWidth;
        primary.Height = GifTalkHeight;
    }
}
