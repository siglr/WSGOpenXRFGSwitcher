using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed partial class MainForm
{
    bool windowPlacementReady, resizingWindow;
    FormWindowState lastVisibleWindowState = FormWindowState.Normal;

    void InitializeWindowPlacement() {
        Load += (_, _) => {
            if (preview) return;
            if (state.Window is { Width: > 0, Height: > 0 } saved) {
                StartPosition = FormStartPosition.Manual;
                RestoreWindowPlacement(saved);
            }
            lastVisibleWindowState = WindowState;
            windowPlacementReady = true;
        };
        ResizeBegin += (_, _) => resizingWindow = true;
        ResizeEnd += (_, _) => { resizingWindow = false; SaveWindowPlacement(); };
        Resize += (_, _) => {
            if (!windowPlacementReady || preview) return;
            if (WindowState != FormWindowState.Minimized) lastVisibleWindowState = WindowState;
            SaveWindowPlacement();
        };
    }

    void RestoreWindowPlacement(WindowPlacement saved) {
        // Select the nearest remaining monitor if the original monitor was disconnected.
        var area = Screen.FromPoint(new Point(saved.X, saved.Y)).WorkingArea;
        // Fit even on a smaller monitor; the workspace can scroll.
        MinimumSize = new Size(Math.Min(1050, area.Width), Math.Min(800, area.Height));
        Bounds = saved.Fit(area, MinimumSize);
        WindowState = saved.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
    }

    void SaveWindowPlacement() {
        if (preview || !windowPlacementReady || resizingWindow) return;
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (WindowState != FormWindowState.Minimized) lastVisibleWindowState = WindowState;
        var saved = new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height,
            lastVisibleWindowState == FormWindowState.Maximized);
        if (saved == state.Window) return;
        state.Window = saved;
        try { storage.Save(state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            storage.Log("Could not save window position: " + ex.Message);
        }
    }

    void Restore() {
        if (!preview && state.Window is { Width: > 0, Height: > 0 } saved) {
            windowPlacementReady = false;
            WindowState = FormWindowState.Normal;
            RestoreWindowPlacement(saved);
            lastVisibleWindowState = WindowState;
            windowPlacementReady = true;
        } else WindowState = lastVisibleWindowState;
        Show(); Activate();
    }
}
