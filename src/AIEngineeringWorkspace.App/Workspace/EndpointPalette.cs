using System.Windows.Media;

namespace AIEngineeringWorkspace.Workspace;

internal readonly record struct EndpointBadgeStyle(Brush Background, Brush Border, Brush Foreground);

internal static class EndpointPalette
{
    private static readonly (string Background, string Border, string Foreground)[] BrowserColors =
    {
        ("#D6E9FF", "#4A90E2", "#0B4F8A"),
        ("#DDF5E3", "#4AA564", "#246B38"),
        ("#FFE8CC", "#D88324", "#8A4C00"),
        ("#E8DDF7", "#8A63B8", "#55327D"),
        ("#D9F3F2", "#3B9E9A", "#1E6865"),
        ("#FFF2B8", "#C7A32C", "#745C00"),
        ("#F9DDEB", "#C85A91", "#7D2854"),
        ("#E1E7F0", "#71809A", "#39445A")
    };

    private static readonly (string Background, string Border, string Foreground)[] FileColors =
    {
        ("#FFF2C7", "#C79A24", "#725300"),
        ("#F2E3CF", "#B8854D", "#70461F"),
        ("#E8ECD9", "#819351", "#475427"),
        ("#E5E7EB", "#7A8391", "#404854")
    };

    internal static EndpointBadgeStyle GetBadgeStyle(PaneKind kind, int displayIndex)
    {
        var palette = kind == PaneKind.Browser ? BrowserColors : FileColors;
        var index = Math.Clamp(displayIndex - 1, 0, palette.Length - 1);
        var item = palette[index];
        return new EndpointBadgeStyle(CreateBrush(item.Background), CreateBrush(item.Border), CreateBrush(item.Foreground));
    }

    private static Brush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
