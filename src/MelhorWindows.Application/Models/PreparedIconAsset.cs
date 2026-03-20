namespace MelhorWindows.Application.Models;

public sealed record PreparedIconAsset(
    string OriginalFileName,
    string SuggestedFileName,
    byte[] IconBytes,
    byte[] PreviewPngBytes);

