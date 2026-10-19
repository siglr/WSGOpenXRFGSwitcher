using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed partial class MainForm
{
    internal async Task VerifyBackgroundPreview(string output) {
        if (!preview) throw new InvalidOperationException("Background tests require preview mode.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        try {
            sessionReady = monitoringPreview = true;
            statusTimer.Interval = 50; RefreshPollingMode();
            await Task.Delay(180);
            Check(periodicChecks > 0 && checkMode == BackgroundCheckMode.VisibleStatus, "Visible window processes status ticks");
            Hide(); var count = periodicChecks;
            Check(!statusTimer.Enabled, "Hiding immediately disables routine polling");
            await Task.Delay(5200);
            Check(periodicChecks == count, "No hidden polling over a full normal polling interval");
            var returns = returnChecks;
            Restore(); await Task.Delay(150);
            Check(returnChecks > returns && periodicChecks > count, "Reopening refreshes immediately and resumes visible checks");
            await Run(async () => {
                Check(!statusTimer.Enabled, "An active operation suspends status polling");
                var current = periodicChecks; await Task.Delay(130);
                Check(periodicChecks == current, "Status timer cannot overlap a change");
            });
            Hide(); RefreshPollingMode(true);
            Check(statusTimer.Enabled && checkMode == BackgroundCheckMode.PendingRemoval, "Only unfinished removal keeps its dedicated hidden check");
            RefreshPollingMode(false);
            Check(!statusTimer.Enabled, "Completing removal returns the hidden app to idle");
            Restore();
            foreach (var light in new[] { false, true }) {
                SetLight(light);
                using var dialog = CreateStartupDialog();
                dialog.Show(this); await Task.Delay(100); dialog.PerformLayout();
                var controls = dialog.Controls[0].Controls.Cast<Control>().ToArray();
                Check(controls.OfType<CheckBox>().Count() == 2, "Independent apply and minimize startup options, light=" + light);
                Check(controls.All(c => c.Bottom <= dialog.ClientSize.Height), "Startup content fits the dialog, light=" + light);
                using var bitmap = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                bitmap.Save(Path.Combine(output, light ? "startup-light.png" : "startup-dark.png"));
                dialog.Close();
            }
            File.WriteAllText(Path.Combine(output, "idle.json"), Json.Write(new { passed = true, checks }));
        } catch (Exception ex) {
            File.WriteAllText(Path.Combine(output, "idle.json"), Json.Write(new { passed = false, checks, error = ex.ToString() }));
        } finally { AllowClose = true; Close(); }
    }
}
