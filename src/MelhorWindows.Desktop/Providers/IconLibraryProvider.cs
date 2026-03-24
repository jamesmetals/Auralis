using System.Collections.Generic;

namespace MelhorWindows.Desktop.Providers;

public sealed record BuiltInIconItem(
    string Id,
    string Label,
    string Glyph,
    System.Windows.Media.Brush TileBackgroundBrush);

public static class IconLibraryProvider
{
    public static IReadOnlyList<BuiltInIconItem> GetBuiltInIcons()
    {
        return new[]
        {
            new BuiltInIconItem("folder", "Folder", "\uE8B7", CreateBrush("#7A6CFF")),
            new BuiltInIconItem("spark", "Spark", "\uE945", CreateBrush("#49A8FF")),
            new BuiltInIconItem("rocket", "Rocket", "\uE7C3", CreateBrush("#FF7A4F")),
            new BuiltInIconItem("database", "Database", "\uE9D2", CreateBrush("#33C5A5")),
            new BuiltInIconItem("diamond", "Diamond", "\uECAD", CreateBrush("#E5A300")),
            new BuiltInIconItem("star", "Star", "\uE734", CreateBrush("#7084FF")),
            new BuiltInIconItem("heart", "Heart", "\uEB51", CreateBrush("#E84D7A")),
            new BuiltInIconItem("gear", "Settings", "\uE713", CreateBrush("#5D89FF")),
            new BuiltInIconItem("mail", "Mail", "\uE715", CreateBrush("#5A9DFF")),
            new BuiltInIconItem("edit", "Edit", "\uE70F", CreateBrush("#31A2A2")),
            new BuiltInIconItem("doc", "Document", "\uE130", CreateBrush("#5B7BD5")),
            new BuiltInIconItem("calendar", "Calendar", "\uE787", CreateBrush("#876BFF")),
            new BuiltInIconItem("bell", "Bell", "\uE7F4", CreateBrush("#497CFB")),
            new BuiltInIconItem("person", "Person", "\uE77B", CreateBrush("#5B82D6")),
            new BuiltInIconItem("image", "Image", "\uE91B", CreateBrush("#41BFA1")),
            new BuiltInIconItem("lock", "Lock", "\uE72E", CreateBrush("#516C9A"))
        };
    }

    private static System.Windows.Media.Brush CreateBrush(string hex)
    {
        var converter = new System.Windows.Media.BrushConverter();
        var brush = (System.Windows.Media.Brush)converter.ConvertFromString(hex)!;
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }
        return brush;
    }
}
