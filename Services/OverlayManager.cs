using ExCord.Models;
using ExCord.Views;

namespace ExCord.Services;

public sealed class OverlayManager : IDisposable
{
    private readonly Dictionary<string, WebViewOverlayWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings? _settings;
    private OverlayShiftRightClickHook? _hook;

    public WebViewOverlayWindow? GetPrimaryWindow()
    {
        if (_settings?.OverlayItems is null || _settings.OverlayItems.Count == 0)
        {
            return null;
        }

        var primaryId = _settings.OverlayItems[0].Id;
        return _windows.TryGetValue(primaryId, out var window) ? window : null;
    }

    public IReadOnlyDictionary<string, WebViewOverlayWindow> GetWindows()
    {
        return _windows;
    }

    public void Apply(AppSettings settings)
    {
        _settings = settings;

        if (!settings.WebViewEnabled)
        {
            CloseAll();
            UpdateHook();
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

            if (!string.IsNullOrWhiteSpace(overlay.Url)
                && !string.Equals(window.LastRequestedUrl, overlay.Url, StringComparison.OrdinalIgnoreCase))
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

        UpdateHook();
    }

    private WebViewOverlayWindow CreateOverlayWindow(AppSettings settings, OverlayItemSettings overlay)
    {
        var window = new WebViewOverlayWindow(overlay, settings.Save);
        window.Closed += (_, _) =>
        {
            _windows.Remove(overlay.Id);
            UpdateHook();
        };

        return window;
    }

    private WebViewOverlayWindow? FindShiftRightClickTarget(int x, int y) =>
        _windows.Values.Reverse().FirstOrDefault(w => w.IsShiftRightClickTarget(x, y));

    private void UpdateHook()
    {
        _hook ??= new OverlayShiftRightClickHook(
            (x, y) => FindShiftRightClickTarget(x, y) is not null,
            (x, y) => FindShiftRightClickTarget(x, y)?.BeginEditMode());

        if (_windows.Count > 0)
        {
            _hook.Install();
        }
        else
        {
            _hook.Uninstall();
        }
    }

    public void Dispose()
    {
        CloseAll();
        _hook?.Dispose();
        _hook = null;
    }

    private void CloseAll()
    {
        foreach (var window in _windows.Values.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
        _hook?.Dispose();
        _hook = null;
    }
}

