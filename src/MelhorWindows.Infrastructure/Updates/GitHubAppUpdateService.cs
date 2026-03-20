using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using MelhorWindows.Application.Abstractions;
using MelhorWindows.Application.Models;

namespace MelhorWindows.Infrastructure.Updates;

public sealed class GitHubAppUpdateService : IAppUpdateService
{
    private const string Owner = "jamesmetals";
    private const string Repository = "Auralis";
    private const string RepositoryEndpoint = $"https://api.github.com/repos/{Owner}/{Repository}";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var defaultBranch = await ResolveDefaultBranchAsync(cancellationToken);
        using var response = await HttpClient.GetAsync($"{RepositoryEndpoint}/commits/{defaultBranch}", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        var latestCommitSha = root.GetProperty("sha").GetString();
        var latestCommitUrl = root.GetProperty("html_url").GetString();
        var latestCommitDateText = root.GetProperty("commit").GetProperty("committer").GetProperty("date").GetString();

        if (string.IsNullOrWhiteSpace(latestCommitSha) ||
            string.IsNullOrWhiteSpace(latestCommitUrl) ||
            string.IsNullOrWhiteSpace(latestCommitDateText) ||
            !DateTimeOffset.TryParse(latestCommitDateText, out var latestCommitDateUtc))
        {
            throw new InvalidOperationException("Nao foi possivel interpretar a resposta de atualizacao do GitHub.");
        }

        var currentVersionLabel = ResolveCurrentVersionLabel();
        var currentCommitSha = ResolveCurrentCommitSha();
        var isUpdateAvailable = !string.IsNullOrWhiteSpace(currentCommitSha) &&
            !string.Equals(currentCommitSha, latestCommitSha, StringComparison.OrdinalIgnoreCase);

        return new AppUpdateInfo(
            isUpdateAvailable,
            currentVersionLabel,
            currentCommitSha,
            latestCommitSha,
            latestCommitUrl,
            latestCommitDateUtc);
    }

    private static async Task<string> ResolveDefaultBranchAsync(CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(RepositoryEndpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        var defaultBranch = document.RootElement.GetProperty("default_branch").GetString();

        if (string.IsNullOrWhiteSpace(defaultBranch))
        {
            throw new InvalidOperationException("Nao foi possivel identificar a branch padrao do repositório.");
        }

        return defaultBranch;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Auralis/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string ResolveCurrentVersionLabel()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath) &&
            File.Exists(executablePath))
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(executablePath).FileVersion;
            if (!string.IsNullOrWhiteSpace(fileVersion))
            {
                return fileVersion;
            }
        }

        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
    }

    private static string? ResolveCurrentCommitSha()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath))
        {
            return null;
        }

        var productVersion = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
        if (string.IsNullOrWhiteSpace(productVersion))
        {
            return null;
        }

        var separatorIndex = productVersion.IndexOf('+');
        if (separatorIndex < 0 ||
            separatorIndex >= productVersion.Length - 1)
        {
            return null;
        }

        return productVersion[(separatorIndex + 1)..].Trim();
    }
}
