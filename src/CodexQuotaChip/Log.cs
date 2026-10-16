namespace CodexQuotaChip;

// Append-only text log next to the state files. Logging must never be able to take the monitor
// down, so every failure here is swallowed.
public sealed class Log
{
    private readonly object _gate = new();
    private readonly string _path;

    public Log(string path)
    {
        _path = path;
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? error = null) =>
        Write("ERROR", error is null ? message : $"{message}: {error.GetType().Name}: {error.Message}");

    private void Write(string level, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
            lock (_gate)
            {
                File.AppendAllText(_path, line);
            }
        }
        catch (Exception)
        {
            // A monitor with no log is still a working monitor.
        }
    }
}
