using System.Runtime.InteropServices;
using System.Text;

namespace Wsg.Switcher;

// Same approach as WSG Client's Centered_MessageBox: position the native dialog
// during its modal message loop, without hooks or a background polling timer.
internal static class CenteredMessageBox
{
    internal static DialogResult Show(Form? owner, string message, string title = "WSG OpenXR FG Switcher",
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1) {
        owner ??= Form.ActiveForm ?? Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Visible && !f.IsDisposed);
        if (owner is { IsDisposed: true }) owner = null;
        if (owner != null) {
            if (!owner.Visible) owner.Show();
            if (owner.WindowState == FormWindowState.Minimized) owner.WindowState = FormWindowState.Normal;
        }
        using var placement = owner == null ? null : new Placement(owner);
        return owner == null ? MessageBox.Show(message, title, buttons, icon, defaultButton)
            : MessageBox.Show(owner, message, title, buttons, icon, defaultButton);
    }

    internal static Point Center(Rectangle parent, Size dialog, Rectangle workingArea) => new(
        Math.Clamp(parent.Left + (parent.Width - dialog.Width) / 2, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - dialog.Width)),
        Math.Clamp(parent.Top + (parent.Height - dialog.Height) / 2, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - dialog.Height)));

    sealed class Placement : IDisposable
    {
        readonly Form owner;
        readonly IntPtr ownerHandle;
        readonly uint thread;
        bool disposed;
        int attempts;
        internal Placement(Form owner) {
            this.owner = owner; ownerHandle = owner.Handle; thread = GetCurrentThreadId();
            owner.BeginInvoke((Action)FindDialog);
        }
        void FindDialog() {
            if (disposed || owner.IsDisposed || !owner.IsHandleCreated) return;
            bool found = false;
            EnumThreadWindows(thread, (window, _) => {
                // Only this owner's native dialog on this UI thread, never another app.
                if (GetWindow(window, 4 /* GW_OWNER */) != ownerHandle) return true;
                var name = new StringBuilder(32); GetClassName(window, name, name.Capacity);
                if (name.ToString() != "#32770" || !GetWindowRect(window, out var rectangle)) return true;
                var size = new Size(rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
                var location = Center(owner.Bounds, size, Screen.FromControl(owner).WorkingArea);
                SetWindowPos(window, IntPtr.Zero, location.X, location.Y, 0, 0,
                    0x0001 | 0x0004 | 0x0010 /* NOSIZE | NOZORDER | NOACTIVATE */);
                found = true; return false;
            }, IntPtr.Zero);
            if (!found && ++attempts < 10 && !disposed && !owner.IsDisposed) owner.BeginInvoke((Action)FindDialog);
        }
        public void Dispose() => disposed = true;
    }
    delegate bool EnumThreadWndProc(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, EnumThreadWndProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
