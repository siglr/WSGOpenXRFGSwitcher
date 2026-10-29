using System.Runtime.InteropServices;
using Wsg.OpenXR;

namespace Wsg.Switcher;

public sealed partial class MainForm
{
    internal async Task VerifyStartupFailurePreview(string output) {
        if (!preview) throw new InvalidOperationException("Startup checks require isolated preview mode.");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        try {
            var fixture = new Storage(Path.Combine(output, "command-fixture"));
            var game = new GamePreset { Name = "Fixture", Executable = @"C:\fixture.exe", Provider = ProviderId.Off };
            var saved = new AppState { Games = [game], Startup = new() { GameId = game.Id, Provider = ProviderId.Off, ApplyOnLaunch = true, StartMinimized = true } };
            var calls = 0;
            foreach (var flag in new[] { "--tray", "--minimized", "--start-minimized" }) {
                var options = LaunchOptions.Parse(["--apply-default", flag]);
                foreach (var message in new[] { "Setup changed outside WSG. Open WSG and Rescan setup before automatic application.", "The saved provider version is not installed.", "Close FlightSimulator2024 before changing setup.", "Recover the pending operation in WSG before automatic application." }) {
                    var code = Program.ApplyCommand(fixture, saved, options, _ => Task.FromException(new IOException(message)), shown => {
                        calls++;
                        var result = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Root, "startup-result.json")))!;
                        Check(!result["success"]!.GetValue<bool>() && result["exitCode"]!.GetValue<int>() == 1 && shown == message,
                            flag + ": failure is recorded before showing " + message);
                    });
                    Check(code == 1, flag + ": acknowledgement preserves the failed exit code");
                }
                var success = Program.ApplyCommand(fixture, saved, options, _ => Task.CompletedTask,
                    _ => throw new Exception("Success must remain silent"));
                Check(success == 0, flag + ": successful one-shot still exits without a window");
            }
            Check(calls == 12 && saved.Startup.ApplyOnLaunch && saved.Startup.StartMinimized,
                "Failure reporting does not alter saved startup choices or automatically retry");

            using var recovery = new MainForm(fixture, saved, host, providers.Catalog, preview: true, startMinimized: true,
                startupFailure: "Setup changed outside WSG. Use Rescan setup before automatic application.");
            var observedDialog = false;
            using var dismiss = new System.Windows.Forms.Timer { Interval = 100 };
            dismiss.Tick += (_, _) => {
                var popup = GetLastActivePopup(recovery.Handle);
                if (popup == recovery.Handle || popup == IntPtr.Zero) return;
                observedDialog = recovery.Visible && recovery.Opacity == 1 && recovery.ShowInTaskbar
                    && recovery.WindowState != FormWindowState.Minimized && IsWindowVisible(recovery.Handle);
                PostMessage(popup, 0x0111 /* WM_COMMAND */, new IntPtr(1 /* IDOK */), IntPtr.Zero);
            };
            dismiss.Start(); recovery.Show(); await Task.Delay(500); dismiss.Stop();
            Check(observedDialog, "Failed one-shot opens the full switcher before its standard error dialog");
            Check(recovery.Visible && recovery.status.Text.Contains("Rescan setup"), "Actionable error remains visible after acknowledgement");
            using (var bitmap = new Bitmap(recovery.Width, recovery.Height)) {
                recovery.DrawToBitmap(bitmap, new Rectangle(Point.Empty, recovery.Size));
                bitmap.Save(Path.Combine(output, "startup-error-visible.png"));
            }
            recovery.Close();
            Check(recovery.IsDisposed, "Closing a failed command exits instead of hiding in the tray");
            File.WriteAllText(Path.Combine(output, "checks.json"), Json.Write(new { passed = true, checks }));
        } catch (Exception ex) {
            File.WriteAllText(Path.Combine(output, "checks.json"), Json.Write(new { passed = false, checks, error = ex.ToString() }));
        } finally { AllowClose = true; Close(); }
        await Task.CompletedTask;
    }
    [DllImport("user32.dll")] static extern IntPtr GetLastActivePopup(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
