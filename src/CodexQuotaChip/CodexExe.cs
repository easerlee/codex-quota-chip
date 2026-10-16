namespace CodexQuotaChip;

// Remembers where codex.exe was found and re-resolves it the moment that path stops existing:
// the CLI installs itself into a new bin\<hash>\ folder on update and deletes the old one, so a
// path resolved at startup cannot be trusted for the lifetime of the process.
public sealed class CodexExe
{
    private readonly Log _log;
    private readonly string? _configured;
    private string? _remembered;

    public CodexExe(string? configuredPath, Log log)
    {
        _configured = configuredPath;
        _log = log;
        _remembered = CodexLocator.Find(configuredPath);
    }

    public string? Current => _remembered;

    public string? Resolve()
    {
        var previous = _remembered;
        var resolved = CodexLocator.Fresh(previous, _configured, CodexLocator.Find);
        if (!string.IsNullOrWhiteSpace(resolved) &&
            !string.Equals(previous, resolved, StringComparison.OrdinalIgnoreCase))
        {
            _log.Info($"codex.exe re-resolved: {previous ?? "(none)"} -> {resolved}");
        }

        _remembered = resolved;
        return resolved;
    }
}
