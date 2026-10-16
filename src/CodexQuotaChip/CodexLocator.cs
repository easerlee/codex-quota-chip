namespace CodexQuotaChip;

// Finds the Codex CLI that is installed on this machine. The CLI installs itself into a new
// bin\<hash>\ folder on every update and deletes the old one, so a path found once cannot be
// trusted for the lifetime of the process: Fresh() re-resolves when the remembered one is gone.
public static class CodexLocator
{
    public const string ExeName = "codex.exe";

    public static string? Find(string? explicitPath = null) =>
        Find(explicitPath, LocalAppData(), PathDirectories());

    public static string? Find(string? explicitPath, string? localAppData, IEnumerable<string> pathDirectories)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        foreach (var candidate in Candidates(localAppData, pathDirectories))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // The remembered path is used while it still exists; otherwise a fresh search runs.
    public static string? Fresh(string? remembered, string? configured, Func<string?, string?> search) =>
        !string.IsNullOrWhiteSpace(remembered) && File.Exists(remembered) ? remembered : search(configured);

    private static IEnumerable<string> Candidates(string? localAppData, IEnumerable<string> pathDirectories)
    {
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            // Newest build first: an update leaves the previous folder behind for a while.
            var binRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
            if (Directory.Exists(binRoot))
            {
                foreach (var found in Directory.EnumerateFiles(binRoot, ExeName, SearchOption.AllDirectories)
                             .OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    yield return found;
                }
            }

            // The Store build keeps its files inside the package container.
            yield return Path.Combine(
                localAppData,
                "Packages",
                "OpenAI.Codex_2p2nqsd0c76g0",
                "LocalCache",
                "Local",
                "OpenAI",
                "Codex",
                "bin",
                ExeName);
        }

        foreach (var directory in pathDirectories)
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory, ExeName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static string? LocalAppData() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static IEnumerable<string> PathDirectories() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
}
