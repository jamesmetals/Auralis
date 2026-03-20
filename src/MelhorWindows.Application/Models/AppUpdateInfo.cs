namespace MelhorWindows.Application.Models;

public sealed record AppUpdateInfo(
    bool IsUpdateAvailable,
    string CurrentVersionLabel,
    string? CurrentCommitSha,
    string LatestCommitSha,
    string LatestCommitUrl,
    DateTimeOffset LatestCommitDateUtc)
{
    public string CurrentCommitShortSha => ShortenCommitSha(CurrentCommitSha);

    public string LatestCommitShortSha => ShortenCommitSha(LatestCommitSha);

    private static string ShortenCommitSha(string? commitSha)
    {
        if (string.IsNullOrWhiteSpace(commitSha))
        {
            return "desconhecido";
        }

        return commitSha.Length <= 7
            ? commitSha
            : commitSha[..7];
    }
}
