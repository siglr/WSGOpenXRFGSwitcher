using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed partial class MainForm
{
    // Uses the real WinForms buttons, operation gate, timer, and close handler.
    // Preview mode guarantees no provider activation or real settings writes.
    internal async Task VerifyExitPreview(string output, string scenario) {
        if (!preview) throw new InvalidOperationException("Exit UI tests require preview mode.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        var closed = new TaskCompletionSource();
        var closes = 0;
        FormClosed += (_, _) => { closes++; closed.TrySetResult(); };
        void Check(bool value, string message) {
            if (!value) throw new InvalidOperationException(message);
            checks.Add(message);
        }
        void ClickExit() {
            if (scenario == "tray") exitMenu.PerformClick();
            else exitButton!.PerformClick();
        }
        try {
            await Task.Delay(100);
            Check(exitButton!.Enabled, "Exit is enabled on the displayed window");
            if (scenario != "idle") {
                await Run(async () => {
                    Check(exitButton.Enabled && !apply.Enabled, "Exit remains enabled while other actions are disabled");
                    ClickExit(); ClickExit();
                    Check(exitRequested && exitButton.Text == "Exit queued…", "Repeated Exit clicks are acknowledged and queued");
                    Check(!closed.Task.IsCompleted, "Exit does not interrupt an ongoing operation");
                    SavePreview(Path.Combine(output, scenario + "-queued.png"));
                    await Task.Delay(150);
                    Check(!closed.Task.IsCompleted, "Operation completes before the window closes");
                });
            } else ClickExit();
            await Task.WhenAny(closed.Task, Task.Delay(7000));
            Check(closed.Task.IsCompleted && closes == 1, "Exit closes the window exactly once");
            File.WriteAllText(Path.Combine(output, scenario + ".json"), Json.Write(new { passed = true, checks }));
        } catch (Exception ex) {
            File.WriteAllText(Path.Combine(output, scenario + ".json"), Json.Write(new { passed = false, checks, error = ex.ToString() }));
            AllowClose = true; Close();
        }
    }
}
