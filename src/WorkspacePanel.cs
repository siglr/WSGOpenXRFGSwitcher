using System.Runtime.InteropServices;

namespace Wsg.Switcher;

internal sealed class WorkspacePanel : Panel
{
    public WorkspacePanel() {
        AutoScroll = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); RepaintWorkspace(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); RepaintWorkspace(); }
    protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); RepaintWorkspace(); }
    void RepaintWorkspace() {
        if (!IsHandleCreated || Disposing || IsDisposed) return;
        // ScrollWindow can leave copied pixels from the old scrollbar behind after
        // a child shrinks. Repaint the client area, children and native scroll frame.
        RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0400);
    }
    [DllImport("user32.dll")]
    static extern bool RedrawWindow(IntPtr window, IntPtr updateRect, IntPtr updateRegion, uint flags);
}
