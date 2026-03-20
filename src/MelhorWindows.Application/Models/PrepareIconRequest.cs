using MelhorWindows.Domain.Enums;

namespace MelhorWindows.Application.Models;

public sealed record PrepareIconRequest(
    string SourceImagePath,
    ImageFitMode FitMode,
    CropSelection? CropSelection = null,
    string? OutputBaseName = null);

