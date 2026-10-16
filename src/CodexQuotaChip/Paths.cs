namespace CodexQuotaChip;

// Where this app keeps its own files. Nothing here is shared with, or written into, the Codex
// installation: the CLI's own files are only ever read.
public sealed class Paths
{
    public const string HomeOverrideVariable = "CODEX_QUOTA_CHIP_HOME";

    public Paths(string rootDirectory)
    {
        Root = Path.GetFullPath(rootDirectory);
        Settings = Path.Combine(Root, "settings.json");
        Accounts = Path.Combine(Root, "accounts.json");
        LogPath = Path.Combine(Root, "logs", "app.log");
    }

    public string Root { get; }
    public string Settings { get; }
    public string Accounts { get; }
    public string LogPath { get; }

    // A source checkout keeps its state in the repository (so runs stay side by side); an
    // installed copy keeps it under %LOCALAPPDATA%.
    public static Paths Discover(string[] args)
    {
        var fromArgs = Value(args, "--home");
        if (!string.IsNullOrWhiteSpace(fromArgs))
        {
            return new Paths(fromArgs);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(HomeOverrideVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new Paths(fromEnvironment);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new Paths(Path.Combine(string.IsNullOrWhiteSpace(local) ? "." : local, "CodexQuotaChip"));
    }

    // Codex home: --codex-home, then %CODEX_HOME%, then ~/.codex.
    public static string CodexHome(string[] args)
    {
        var explicitPath = Value(args, "--codex-home");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath));
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(fromEnvironment));
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    private static string? Value(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
