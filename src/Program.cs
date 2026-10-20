using System.Reflection;
using Wsg.OpenXR;

namespace Wsg.Switcher;

internal static class Program
{
    internal static string Resource(string name) {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new IOException("Missing application resource: " + name);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    [STAThread]
    static int Main(string[] args) {
        ApplicationConfiguration.Initialize();
        LaunchOptions? launch = null;
        Storage? commandStorage = null;
        try {
            var exitTest = args.Length == 2 && args[0] is "--ui-exit-idle" or "--ui-exit-busy" or "--ui-exit-tray";
            var idleTest = args.Length == 2 && args[0] == "--ui-idle";
            var smoke = exitTest || idleTest || args.Length == 2 && args[0] is "--ui-smoke" or "--ui-preview";
            launch = smoke ? new() : LaunchOptions.Parse(args);
            if (!smoke && new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Open WSG normally, without Run as administrator. Only the VectorXR installer and layer helper request elevation when needed.");
            using var single = new Mutex(true, smoke ? "Local\\WSGOpenXRSmoke" : "Local\\WSGOpenXRFGSwitcher", out var first);
            if (!first) {
                if (launch.ApplyOnce) return WriteCommandResult(commandStorage = new Storage(), launch, 3, "WSG is already running. Exit it before running an apply command.");
                CenteredMessageBox.Show(null, "WSG OpenXR FG Switcher is already running. Restore it from the Windows notification area."); return 3;
            }
            var storage = new Storage(smoke ? Path.Combine(Path.GetFullPath(args[1]), "fixture-data", Guid.NewGuid().ToString("N")) : null);
            commandStorage = storage;
            var state = storage.Load();
            if (!smoke) storage.CleanupCompletedOperations();
            if (smoke) state.Games.Add(new GamePreset { Name = "Microsoft Flight Simulator 2024", Executable = @"C:\MSFS2024\FlightSimulator2024.exe", Provider = ProviderId.Djules });
            else if (state.Games.Count == 0) state.Games.AddRange(GameDiscovery.Discover());
            if (!smoke) GameDiscovery.Normalize(state);
            var host = new WindowsHost();
            using var updateGate = smoke ? null : AppUpdate.Enter(AppContext.BaseDirectory);
            if (!smoke && updateGate == null) throw new IOException("An app update is in progress. Reopen WSG after it finishes.");
            if (!smoke && !launch.ApplyOnce && !state.Startup.ApplyOnLaunch && !launch.Minimized && !state.Startup.StartMinimized && AppUpdate.Enabled) {
                try { host.RequireIdle(state.Games); if (AppUpdate.Startup(AppContext.BaseDirectory)) return 0; }
                catch (Exception ex) { storage.Log("Startup update deferred: " + ex.Message); }
            }
            var catalog = new Catalog(Resource("catalog.signed.json"), Resource("trust-public.pem"));
            if (!smoke) ProviderCatalogUpdates.LoadSaved(storage, catalog);
            if (launch.ApplyOnce) {
                // One-shot commands leave the applied providers running. Explicit UI Exit is a different operation.
                var preset = launch.Resolve(state);
                Task.Run(() => StartupLaunch.Apply(storage, state, host, catalog, preset)).GetAwaiter().GetResult();
                return WriteCommandResult(storage, launch, 0, "Ready for next launch", preset);
            }
            using var form = new MainForm(storage, state, host, catalog, smoke, launch.Minimized);
            if (idleTest) form.Shown += async (_, _) => await form.VerifyBackgroundPreview(Path.GetFullPath(args[1]));
            if (exitTest) form.Shown += async (_, _) => await form.VerifyExitPreview(Path.GetFullPath(args[1]), args[0]["--ui-exit-".Length..]);
            if (smoke && args[0] == "--ui-smoke") {
                form.Shown += async (_, _) => {
                    try {
                        await Task.Delay(150); Directory.CreateDirectory(Path.GetFullPath(args[1]));
                        form.SavePreview(Path.Combine(args[1], "switcher-dark.png"));
                        await form.SaveBusyPreview(Path.Combine(args[1], "switcher-dark-busy.png"));
                        form.SetLight(true); form.SavePreview(Path.Combine(args[1], "switcher-light.png"));
                        var sampleGame = state.Games[0];
                        storage.AddNote(new(DateTimeOffset.Now, sampleGame.Name, sampleGame.Executable, sampleGame.Provider,
                            "UI fixture", sampleGame.For(sampleGame.Provider), false, "UI fixture runtime", "45", "90", "UI test fixture only — no hardware measurements.") { SetupSource = "UI fixture" });
                        foreach (var light in new[] { false, true }) {
                            using var notes = new NotesForm(storage, sampleGame, "UI fixture", "UI fixture runtime", light, false);
                            notes.Show(form); notes.Refresh();
                            using var bitmap = new Bitmap(notes.Width, notes.Height);
                            notes.DrawToBitmap(bitmap, new Rectangle(Point.Empty, notes.Size));
                            bitmap.Save(Path.Combine(args[1], light ? "notes-light.png" : "notes-dark.png"));
                            notes.Close();
                        }
                        form.VerifySettingsPreview(Path.GetFullPath(args[1]));
                    } finally { form.AllowClose = true; form.Close(); }
                };
            }
            Application.Run(form); return 0;
        } catch (Exception ex) {
            if (launch?.ApplyOnce == true) return WriteCommandResult(commandStorage ?? new Storage(), launch, 1, ex.Message);
            if (launch == null && args.Length > 0) { Console.Error.WriteLine(ex.Message); return 2; }
            CenteredMessageBox.Show(null, ex.Message, "WSG OpenXR FG Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
        }
    }
    static int WriteCommandResult(Storage storage, LaunchOptions options, int code, string message, GamePreset? preset = null) {
        try {
            var result = Json.Write(new { success = code == 0, exitCode = code, message, time = DateTimeOffset.Now,
                game = preset?.Name, gameId = preset?.Id, provider = preset?.Provider.ToString(), version = preset?.Versions.GetValueOrDefault(preset.Provider) });
            var path = options.ResultPath ?? Path.Combine(storage.Root, "startup-result.json");
            SafeFiles.AtomicWrite(path, result);
            storage.Log("Startup command: " + message); Console.WriteLine(result);
            return code;
        } catch (Exception ex) { Console.Error.WriteLine("Could not write startup result: " + ex.Message); return 1; }
    }
}
