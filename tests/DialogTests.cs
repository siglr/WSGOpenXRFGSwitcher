using System.Runtime.InteropServices;
using System.Text;
using Wsg.Switcher;

// Isolated Windows UI fixtures: no app state, provider processes or game changes.
static class DialogTests
{
    public static void Run() {
        Exception? failure = null;
        var thread = new Thread(() => { try { RunOnUiThread(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Exception("Dialog checks failed", failure);
    }
    static void RunOnUiThread() {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        Check(CenteredMessageBox.Center(new(-1800, 150, 800, 600), new(400, 200), new(-1920, 0, 1920, 1080)) == new Point(-1600, 350), "Negative-coordinate monitor geometry");
        Check(CenteredMessageBox.Center(new(1800, 950, 600, 400), new(400, 200), new(0, 0, 1920, 1080)) == new Point(1520, 880), "Offscreen parent keeps message inside working area");
        var area = Screen.PrimaryScreen!.WorkingArea;
        using var owner = new Form { Text = "WSG dialog test fixture", StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(area.Left + 80, area.Top + 90, 720, 500) };
        owner.Shown += (_, _) => owner.BeginInvoke((Action)(() => {
            try {
                Test(owner, MessageBoxButtons.OK, MessageBoxDefaultButton.Button1, 1, DialogResult.OK);
                owner.Location = new Point(area.Left + 230, area.Top + 180);
                Test(owner, MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button2, 7, DialogResult.No);
                using var nested = new Form { Text = "Nested settings fixture", StartPosition = FormStartPosition.Manual,
                    Bounds = new Rectangle(area.Left + 310, area.Top + 250, 480, 330) };
                nested.Show(owner);
                Test(nested, MessageBoxButtons.OKCancel, MessageBoxDefaultButton.Button2, 2, DialogResult.Cancel);
                nested.Close();
                owner.Hide();
                Test(owner, MessageBoxButtons.OK, MessageBoxDefaultButton.Button1, 1, DialogResult.OK);
                Check(owner.Visible, "A hidden owner is restored for its message");
                owner.Location = new Point(area.Right - 150, area.Bottom - 100);
                Test(owner, MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button2, 7, DialogResult.No);
            } catch (Exception ex) { owner.Tag = ex; }
            finally { owner.Close(); }
        }));
        Application.Run(owner);
        if (owner.Tag is Exception error) throw error;
        Console.WriteLine($"{checks} dialog checks passed.");

        void Test(Form parent, MessageBoxButtons buttons, MessageBoxDefaultButton defaultButton, int click, DialogResult expectedResult) {
            var ticks = 0; Exception? failure = null; bool observed = false;
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (_, _) => {
                ticks++;
                if (ticks > 50) { Console.Error.WriteLine("Native dialog fixture timed out."); Environment.Exit(1); }
                if (observed) return;
                EnumThreadWindows(GetCurrentThreadId(), (window, _) => {
                    if (GetWindow(window, 4) != parent.Handle) return true;
                    var name = new StringBuilder(32); GetClassName(window, name, name.Capacity);
                    if (name.ToString() != "#32770") return true;
                    observed = true;
                    try {
                        Check(GetWindowRect(window, out var rect), "Native dialog rectangle available");
                        var expected = CenteredMessageBox.Center(parent.Bounds, new(rect.Right - rect.Left, rect.Bottom - rect.Top), Screen.FromControl(parent).WorkingArea);
                        Check(Math.Abs(rect.Left - expected.X) <= 1 && Math.Abs(rect.Top - expected.Y) <= 1, "Native " + buttons + " dialog centered on immediate owner");
                        Check(!IsWindowEnabled(parent.Handle), "Owner remains modal while prompt is open");
                        var defaultId = (int)(SendMessage(window, 0x0400 /* DM_GETDEFID */, IntPtr.Zero, IntPtr.Zero).ToInt64() & 0xffff);
                        Check(defaultId == click, "Original default button preserved");
                    } catch (Exception ex) { failure = ex; }
                    finally {
                        // Native dialog keyboard handling runs in its modal message loop.
                        PostMessage(window, 0x0100 /* WM_KEYDOWN */, new IntPtr(13), IntPtr.Zero);
                        PostMessage(window, 0x0101 /* WM_KEYUP */, new IntPtr(13), IntPtr.Zero);
                    }
                    return false;
                }, IntPtr.Zero);
            };
            timer.Start();
            var result = CenteredMessageBox.Show(parent, "Fixture message only. No provider or game is changed.\n\nChecks parent centering and standard buttons.", "WSG centering check", buttons, MessageBoxIcon.Information, defaultButton);
            timer.Stop();
            Check(observed, "Prompt observed and dismissed by the isolated fixture");
            if (failure != null) throw failure;
            Check(result == expectedResult, "Original " + expectedResult + " result preserved");
        }
    }
    delegate bool EnumCallback(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
