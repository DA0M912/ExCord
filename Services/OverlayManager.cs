using ExCord.Models;
using ExCord.Views;

namespace ExCord.Services;

public sealed class OverlayManager : IDisposable
{
    private readonly Dictionary<string, GifTalkOverlayWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings? _settings;

    public GifTalkOverlayWindow? GetPrimaryWindow()
    {
        if (_settings?.OverlayItems is null || _settings.OverlayItems.Count == 0)
        {
            return null;
        }

        var primaryId = _settings.OverlayItems[0].Id;
        return _windows.TryGetValue(primaryId, out var window) ? window : null;
    }

    public void Apply(AppSettings settings)
    {
        _settings = settings;

        if (!settings.GifTalkEnabled)
        {
            CloseAll();
            return;
        }

        var activeOverlayIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var overlay in settings.OverlayItems.Where(o => o.Enabled))
        {
            activeOverlayIds.Add(overlay.Id);

            if (!_windows.TryGetValue(overlay.Id, out var window))
            {
                window = CreateOverlayWindow(settings, overlay);
                _windows[overlay.Id] = window;
            }

            window.UpdateFromSettings();

            if (!string.IsNullOrWhiteSpace(overlay.Url))
            {
                window.NavigateToUrl(overlay.Url);
            }

            if (!window.IsVisible)
            {
                window.Show();
            }
        }

        var staleIds = _windows.Keys.Where(id => !activeOverlayIds.Contains(id)).ToArray();
        foreach (var staleId in staleIds)
        {
            if (_windows.TryGetValue(staleId, out var staleWindow))
            {
                _windows.Remove(staleId);
                staleWindow.Close();
            }
        }
    }

    private GifTalkOverlayWindow CreateOverlayWindow(AppSettings settings, OverlayItemSettings overlay)
    {
        var window = new GifTalkOverlayWindow(overlay, settings.Save);
        window.Closed += (_, _) =>
        {
            _windows.Remove(overlay.Id);
        };

        return window;
    }

    public void Dispose()
    {
        CloseAll();
    }

    private void CloseAll()
    {
        foreach (var window in _windows.Values.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
    }
}
