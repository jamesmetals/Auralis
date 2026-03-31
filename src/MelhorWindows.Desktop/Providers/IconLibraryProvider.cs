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
            // Essentials
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
            new BuiltInIconItem("lock", "Lock", "\uE72E", CreateBrush("#516C9A")),

            // Media & Entertainment
            new BuiltInIconItem("music", "Music", "\uE8D6", CreateBrush("#C75BDB")),
            new BuiltInIconItem("video", "Video", "\uE714", CreateBrush("#E05A5A")),
            new BuiltInIconItem("camera", "Camera", "\uE722", CreateBrush("#4FC3C3")),
            new BuiltInIconItem("play", "Play", "\uE768", CreateBrush("#4DBD74")),
            new BuiltInIconItem("headphone", "Headphone", "\uE7F6", CreateBrush("#9B6BFF")),
            new BuiltInIconItem("microphone", "Microphone", "\uE720", CreateBrush("#FF6B8A")),

            // Development & Tools
            new BuiltInIconItem("code", "Code", "\uE943", CreateBrush("#4FC1E9")),
            new BuiltInIconItem("bug", "Bug", "\uEBE8", CreateBrush("#F0AD4E")),
            new BuiltInIconItem("terminal", "Terminal", "\uE756", CreateBrush("#2ECC71")),
            new BuiltInIconItem("wrench", "Wrench", "\uE90F", CreateBrush("#95A5A6")),
            new BuiltInIconItem("package", "Package", "\uE7B8", CreateBrush("#8E6EBF")),
            new BuiltInIconItem("shield", "Shield", "\uE83D", CreateBrush("#3498DB")),

            // Files & Data
            new BuiltInIconItem("download", "Download", "\uE896", CreateBrush("#27AE60")),
            new BuiltInIconItem("upload", "Upload", "\uE898", CreateBrush("#2980B9")),
            new BuiltInIconItem("save", "Save", "\uE74E", CreateBrush("#1ABC9C")),
            new BuiltInIconItem("clipboard", "Clipboard", "\uE77F", CreateBrush("#7F8FA6")),
            new BuiltInIconItem("archive", "Archive", "\uF12B", CreateBrush("#6C5B7B")),
            new BuiltInIconItem("link", "Link", "\uE71B", CreateBrush("#55A3F0")),

            // Devices & Hardware
            new BuiltInIconItem("desktop", "Desktop", "\uE7F4", CreateBrush("#34495E")),
            new BuiltInIconItem("phone", "Phone", "\uE8EA", CreateBrush("#2ECC71")),
            new BuiltInIconItem("print", "Print", "\uE749", CreateBrush("#636E72")),
            new BuiltInIconItem("usb", "USB", "\uE88E", CreateBrush("#D35400")),

            // Status & Communication
            new BuiltInIconItem("chat", "Chat", "\uE8F2", CreateBrush("#6C5CE7")),
            new BuiltInIconItem("globe", "Globe", "\uE774", CreateBrush("#0984E3")),
            new BuiltInIconItem("pin", "Pin", "\uE718", CreateBrush("#E17055")),
            new BuiltInIconItem("bookmark", "Bookmark", "\uE8A4", CreateBrush("#FDCB6E")),
            new BuiltInIconItem("home", "Home", "\uE80F", CreateBrush("#00B894")),
            new BuiltInIconItem("search", "Search", "\uE721", CreateBrush("#74B9FF")),
            new BuiltInIconItem("trash", "Trash", "\uE74D", CreateBrush("#D63031")),
            new BuiltInIconItem("lightning", "Lightning", "\uE945", CreateBrush("#F9CA24")),
            new BuiltInIconItem("fire", "Fire", "\uECAD", CreateBrush("#E55039")),
            new BuiltInIconItem("eye", "Eye", "\uE7B3", CreateBrush("#A29BFE"))
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
