namespace CodexQuotaChip;

// The panel is closed by a mouse press outside it. WPF's own click-outside handling cannot do
// this here: the overlay is a WS_EX_NOACTIVATE window that never activates, so a click in
// another application never arrives as a WPF input event. The window therefore watches the
// global mouse button state and asks this rule what to do with each press.
public static class Dismiss
{
    // pressed - a mouse button is down right now
    // wasUp   - no button was down at the previous look, so this press has just started
    // inside  - the cursor is inside the panel's own rectangle
    // A press that starts inside belongs to the panel, and a press that was already down when
    // the panel opened must not close it either.
    public static bool ShouldClose(bool pressed, bool wasUp, bool inside) => pressed && wasUp && !inside;
}
