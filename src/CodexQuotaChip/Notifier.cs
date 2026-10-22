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

    public bool Show(string title, string text)
    {
        // Turned off in Windows is an answer, not a fault. Nothing is shown, and a balloon - a
        // second channel - is not used to get around the setting the user chose.
        if (!NotificationsAllowed())
        {
            _log.Info("notifications are off for this app, nothing shown");
            return false;
        }

        var tag = "r" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!Toast(title, text, tag))
        {
            return _balloon?.Show(title, text) ?? false;
        }

        // The shell can take a toast and then quietly drop it: the same toast sent from a short-lived
        // process reached the Action Center, sent from the running overlay it did not, and nothing
        // came back to say so. So the toast is tagged and the shell's own history is asked, a moment
        // later, whether it arrived - and when it did not, the balloon goes up instead. That check
        // waits for the shell, so it runs off the UI thread.
        _ = Task.Run(() =>
        {
            if (Delivered(tag))
            {
                return;
            }

            _log.Warn("the shell dropped the toast; a balloon was used instead");
            _balloon?.Show(title, text);
        });

        return true;
    }

    public void Dispose() => _balloon?.Dispose();

    // The two switches that decide whether a toast is allowed at all: the machine-wide one and this
    // app's own. Absent means "not set", which is the default - allowed.
    public static bool Allowed(int? toastEnabled, int? appEnabled) =>
        toastEnabled != 0 && appEnabled != 0;

    private static bool NotificationsAllowed()
    {
        using var push = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\PushNotifications");
        using var app = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\{Aumid}");

        return Allowed(push?.GetValue("ToastEnabled") as int?, app?.GetValue("Enabled") as int?);
    }

    // Did that toast reach the Action Center? Polled for a couple of seconds because the shell files
    // it asynchronously. A history that cannot be read counts as "it arrived": failing to check must
    // never mean a second notification.
    private bool Delivered(string tag)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            Thread.Sleep(400);
            try
            {
                if (Windows.UI.Notifications.ToastNotificationManager.History
                    .GetHistory(Aumid)
                    .Any(toast => toast.Tag == tag))
                {
                    return true;
                }
            }
            catch (Exception error)
            {
                _log.Warn($"cannot read the notification history: {error.Message}");
                return true;
            }
        }

        return false;
    }

    private bool Toast(string title, string text, string tag)
    {
        try
        {
            Register();
            var xml = new Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml(BuildXml(title, text));
            Windows.UI.Notifications.ToastNotificationManager
                .CreateToastNotifier(Aumid)
                .Show(new Windows.UI.Notifications.ToastNotification(xml) { Tag = tag });
            return true;
        }
        catch (Exception error)
        {
            _log.Warn($"toast not shown, trying a balloon instead: {error.Message}");
            return false;
        }
    }

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
