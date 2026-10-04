namespace ExCord.Models;

public sealed class OverlayItemSettings
{
    private const double DefaultWidth = 300d;
    private const double DefaultHeight = 300d;
    private const double MinimumSize = 80d;
    private const double MaximumSize = 10000d;
    private const double MaximumCoordinate = 100000d;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Overlay 1";
    public string Url { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public double X { get; set; } = AppSettings.GetDefaultGifTalkX();
    public double Y { get; set; } = AppSettings.GetDefaultGifTalkY();
    public double Width { get; set; } = DefaultWidth;
    public double Height { get; set; } = DefaultHeight;

    public void ValidateAndNormalize(int index)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }

        Name = string.IsNullOrWhiteSpace(Name)
            ? $"Overlay {index + 1}"
            : Name.Trim();

        Url ??= string.Empty;

        if (!double.IsFinite(X) || Math.Abs(X) > MaximumCoordinate)
        {
            X = AppSettings.GetDefaultGifTalkX();
        }

        if (!double.IsFinite(Y) || Math.Abs(Y) > MaximumCoordinate)
        {
            Y = AppSettings.GetDefaultGifTalkY();
        }

        if (!double.IsFinite(Width) || Width < MinimumSize || Width > MaximumSize)
        {
            Width = DefaultWidth;
        }

        if (!double.IsFinite(Height) || Height < MinimumSize || Height > MaximumSize)
        {
            Height = DefaultHeight;
        }
    }
}
