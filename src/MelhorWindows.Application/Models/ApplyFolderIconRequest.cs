using MelhorWindows.Domain.Enums;

namespace MelhorWindows.Application.Models;

public sealed record ApplyFolderIconRequest(
    string FolderPath,
    string SourceImagePath,
    ImageFitMode FitMode,
    CropSelection? CropSelection = null,
    bool SaveToHistory = true);

