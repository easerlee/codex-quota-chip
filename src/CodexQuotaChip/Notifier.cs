using Microsoft.Win32;

namespace CodexQuotaChip;

// The Windows notification that says a quota window has refilled.
//
// A toast is the notification that belongs in the Action Center and honours "do not disturb", and
// a program that was never installed as a package can still send one: the shell accepts it as long
// as the app id is registered under HKCU\Software\Classes\AppUserModelId, which needs no Start Menu
// shortcut and no package reference. If the toast API is missing or the shell refuses it, the older
// notification-area balloon is used instead.
public sealed class Notifier : IDisposable
{
    public const string Aumid = "easerlee.CodexQuotaChip";
    public const string DisplayName = "Codex Quota Chip";

    private readonly Balloon? _balloon;
    private readonly Log _log;
    private bool _registered;

    public Notifier(IntPtr handle, Log log)
    {
        _log = log;
        _balloon = handle == IntPtr.Zero ? null : new Balloon(handle, log);
    }

    public bool Show(string title, string text) =>
        Toast(title, text) || (_balloon?.Show(title, text) ?? false);

    public void Dispose() => _balloon?.Dispose();

    // Kept separate from the sending so the escaping - the one part of this that has a wrong
    // answer - can be checked without a shell.
    public static string BuildXml(string title, string text) =>
        "<toast><visual><binding template=\"ToastGeneric\">" +
        $"<text>{Escape(title)}</text><text>{Escape(text)}</text>" +
        "</binding></visual></toast>";

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private bool Toast(string title, string text)
    {
        try
        {
            Register();
            var xml = new Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml(BuildXml(title, text));
            Windows.UI.Notifications.ToastNotificationManager
                .CreateToastNotifier(Aumid)
                .Show(new Windows.UI.Notifications.ToastNotification(xml));
            return true;
        }
        catch (Exception error)
        {
            _log.Warn($"toast not shown, trying a balloon instead: {error.Message}");
            return false;
        }
    }

    // Registering the app id is what makes the shell accept a toast from a program it has never
    // heard of. Once per run, and only when it is not already what it should be.
    private void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}");
        if (key?.GetValue("DisplayName") as string != DisplayName)
        {
            key?.SetValue("DisplayName", DisplayName, RegistryValueKind.String);
        }
    }
}
