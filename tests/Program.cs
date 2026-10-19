using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Wsg.OpenXR;

if (args is ["--dialogs"]) { DialogTests.Run(); return 0; }

if (args.Length == 2 && args[0] == "--launch-probe") {
    File.WriteAllText(args[1], Environment.CurrentDirectory); return 0;
}

var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); passed++; Console.WriteLine("PASS " + name); }
async Task Reject(Func<Task> action, string name) { try { await action(); } catch { Check(true, name); return; } throw new Exception("FAILED: " + name + " did not reject"); }
var root = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try {
    {
        // Launch only this local test harness, never an installer, provider, or elevated helper.
        var callerDirectory = Environment.CurrentDirectory;
        var unrelatedDirectory = Path.Combine(root, "unrelated-caller"); Directory.CreateDirectory(unrelatedDirectory);
        try {
            Environment.CurrentDirectory = unrelatedDirectory;
            foreach (var shell in new[] { false, true }) {
                var resultPath = Path.Combine(root, "launch-" + shell + ".txt");
                var info = ProcessLaunch.ForFile(Environment.ProcessPath!, shell);
                info.ArgumentList.Add("--launch-probe"); info.ArgumentList.Add(resultPath);
                using var child = System.Diagnostics.Process.Start(info)!; await child.WaitForExitAsync();
                Check(child.ExitCode == 0 && File.ReadAllText(resultPath).Equals(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase), "Explicit working directory overrides caller for shell=" + shell);
            }
        } finally { Environment.CurrentDirectory = callerDirectory; }
        await Reject(() => Task.FromResult(ProcessLaunch.ForFile("relative.exe", true)), "Relative launch paths refused");
        var notStarted = false;
        await Reject(() => Task.FromResult(ProcessLaunch.StartInstaller(Path.Combine(root, "missing-installer.exe"), () => notStarted = true)), "Missing installer launch reported");
        Check(notStarted, "Failed process creation releases the pending installation journal");
        var virtualFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG OpenXR FG Switcher.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(virtualFolder);
        var virtualFile = Path.Combine(virtualFolder, "probe.txt"); File.WriteAllText(virtualFile, "physical-path-probe");
        var physical = ProcessLaunch.PhysicalFile(virtualFile);
        Check(File.ReadAllText(physical) == "physical-path-probe", "Resolved AppData path addresses the verified file");
        var resolvedLaunch = ProcessLaunch.ForFile(virtualFile, true);
        Check(resolvedLaunch.FileName == physical && resolvedLaunch.WorkingDirectory == Path.GetDirectoryName(physical), "Shell launch uses physical AppData path instead of redirected alias");
        File.Delete(virtualFile); Directory.Delete(virtualFolder);
        Check(WindowsHost.IsVectorEditor("vectorxr-app") && WindowsHost.IsVectorEditor("VECTORXR-APP"), "Official VectorXR editor process recognized");
        Check(!WindowsHost.IsVectorEditor("some-other-app"), "Unrelated processes are not treated as VectorXR editor");
    }
    // Fixtures, never the user's registry, providers, or settings.
    {
        var gameNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FlightSimulator2024" };
        foreach (var message in new[] { "Process has exited", "Cannot process request because the process (32656) has exited.", "Le processus est terminé." }) {
            Check(ProcessInspection.IdleBlockReason(-1, () => throw new InvalidOperationException(message), () => [], gameNames) == null, "Exited process name read is tolerated: " + message);
            Check(ProcessInspection.IdleBlockReason(-1, () => "unrelated", () => throw new InvalidOperationException(message), gameNames) == null, "Exited process module read is tolerated: " + message);
        }
        Check(ProcessInspection.IdleBlockReason(-1, () => "FlightSimulator2024", () => throw new Exception("Must not inspect game modules"), gameNames)?.Contains("FlightSimulator2024") == true, "Known running game remains a blocker regardless of module access");
        Check(ProcessInspection.IdleBlockReason(-1, () => "vectorxr-app", () => [], gameNames)?.Contains("Close the VectorXR settings app") == true, "VectorXR editor remains a blocker");
        Check(ProcessInspection.IdleBlockReason(-1, () => "other-game", () => ["XR_APILAYER_DIENERTECH_VECTORXR.dll"], gameNames)?.Contains("provider is loaded") == true, "Unregistered game with a managed layer remains a blocker");
        Check(ProcessInspection.IdleBlockReason(-1, () => "system", () => throw new System.ComponentModel.Win32Exception(5), gameNames) == null, "Inaccessible unrelated process is tolerated");
    }
    var vector = """{"version":3,"core":{"enabled":true,"osd":{"enabled":false}},"applications":[{"id":"existing","name":"My Game","enabled":true,"match":{"exe":"Game.exe"}}],"modules":{"pivotxr":{"enabled":true,"custom":"keep"},"turbo":{"enabled":false},"framegen":{"enabled":true,"profiles":[{"name":"Original","enabled":true,"applicationIds":["existing"],"settings":{"synthesisMode":"extrapolate"}}]}}}""";
    var game = new GamePreset { Name = "Game", Executable = @"C:\Games\Game.exe" };
    var disabled = JsonNode.Parse(VectorConfiguration.Patch(vector, game, false, true))!;
    Check(disabled["modules"]!["framegen"]!["enabled"]!.GetValue<bool>() == false, "Vector default disabled");
    Check(disabled["modules"]!["framegen"]!["profiles"]![0]!["enabled"]!.GetValue<bool>() == false, "Vector per-game override disabled");
    Check(disabled["modules"]!["pivotxr"]!["custom"]!.GetValue<string>() == "keep", "Coexistence preserves other modules");
    var enabled = JsonNode.Parse(VectorConfiguration.Patch(vector, game, true, false))!;
    Check(enabled["applications"]!.AsArray().Count == 1, "Vector reuses matching application identity");
    Check(enabled["modules"]!["framegen"]!["profiles"]![0]!["applicationIds"]![0]!.GetValue<string>() == "existing", "Vector foreground profile references original application");
    Check(enabled["modules"]!["framegen"]!["profiles"]![0]!["enabled"]!.GetValue<bool>(), "Vector selected profile enabled");
    Check(!enabled["modules"]!["framegen"]!["enabled"]!.GetValue<bool>(), "Vector FG stays off for unselected applications");
    await Reject(() => Task.FromResult(VectorConfiguration.Patch(vector.Replace("\"version\":3", "\"version\":99"), game, true, false)), "Unknown Vector schema refused");
    var ini = OfxrConfiguration.Patch("[other]\nsecret=keep\n[tray]\nbackend=fidelityfx\ncustom=preserve\n", ProviderId.Djules, new() { Multiplier = "3x", PreferFps = false });
    Check(ini.Contains("secret=keep") && ini.Contains("custom=preserve"), "OFXR unrelated settings preserved");
    Check(ini.Contains("triple_frame_gen=1") && ini.Contains("deep_pipeline=1"), "OFXR 3x selects required latency mode");
    var elliott = OfxrConfiguration.Patch("", ProviderId.Elliott, new() { Method = "dlss" });
    Check(elliott.Contains("frame_generation=dlss") && elliott.Contains("extrapolate=0"), "Elliott method exclusivity");
    await Reject(() => Task.FromResult(OfxrConfiguration.Patch("", ProviderId.Djules, new() { Method = "dlss" })), "Unsupported provider capability refused");
    foreach (var bad in new[] { "../escape", "C:/outside", "file:stream", "NUL.txt", "a\\b", "a/../b", "file." })
        await Reject(() => Task.FromResult(SafeFiles.Child(root, bad)), "Path rejected " + bad);
    byte[] Zip(params (string name, string text)[] files) {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true)) foreach (var f in files) { using var writer = new StreamWriter(archive.CreateEntry(f.name).Open()); writer.Write(f.text); }
        return stream.ToArray();
    }
    await Reject(() => { Downloads.Extract(Zip(("../bad.dll", "bad")), Path.Combine(root, "badzip")); return Task.CompletedTask; }, "ZIP traversal rejected");
    await Reject(() => { Downloads.Extract(Zip(("A.dll", "1"), ("a.dll", "2")), Path.Combine(root, "duplicates")); return Task.CompletedTask; }, "Case-insensitive duplicate ZIP rejected");
    var windowsZip = Path.Combine(root, "OpenXR FG", "windows-archive");
    Downloads.Extract(Zip(("external\\licenses\\", ""), ("external\\licenses\\notice.txt", "notice"), ("ofxr\\layer.dll", "fixture")), windowsZip);
    Check(File.ReadAllText(Path.Combine(windowsZip, "external", "licenses", "notice.txt")) == "notice"
        && File.ReadAllText(Path.Combine(windowsZip, "ofxr", "layer.dll")) == "fixture", "Windows ZIP directory and file separators extract under a root with spaces");
    await Reject(() => { Downloads.Extract(Zip(("ofxr\\..\\..\\outside.dll", "bad")), Path.Combine(root, "backslash-traversal")); return Task.CompletedTask; }, "Backslash ZIP traversal rejected");
    await Reject(() => { Downloads.Extract(Zip(("ofxr/..\\../outside.dll", "bad")), Path.Combine(root, "mixed-traversal")); return Task.CompletedTask; }, "Mixed-separator ZIP traversal rejected");
    await Reject(() => { Downloads.Extract(Zip(("ofxr\\layer.dll", "1"), ("ofxr/layer.dll", "2")), Path.Combine(root, "mixed-duplicates")); return Task.CompletedTask; }, "Mixed-separator duplicate ZIP rejected after normalization");
    await Reject(() => { Downloads.Extract(Zip(("\\\\server\\share\\bad.dll", "bad")), Path.Combine(root, "unc-zip")); return Task.CompletedTask; }, "UNC ZIP entry rejected after normalization");
    await Reject(() => { Downloads.Extract(Zip(("C:\\bad.dll", "bad")), Path.Combine(root, "absolute-zip")); return Task.CompletedTask; }, "Drive-rooted ZIP entry rejected after normalization");
    foreach (var from in Enum.GetValues<ProviderId>())
    foreach (var to in Enum.GetValues<ProviderId>()) {
        var fixture = new Fixture(Path.Combine(root, from + "-" + to), vector);
        fixture.Game.Provider = from;
        await fixture.Apply();
        fixture.Game.For(from).FlowResolution = 75;
        fixture.Game.Provider = to;
        var unrelated = fixture.Host.Layers().Single(r => r.LayerName == "Unrelated");
        await fixture.Apply();
        Check(fixture.State.ActiveProvider == to && !fixture.Coordinator.RecoveryPending, from + " -> " + to + " commits");
        Check(fixture.Host.Layers().Contains(unrelated), from + " -> " + to + " preserves unrelated layer");
        if (to is ProviderId.Djules or ProviderId.Elliott) Check(File.ReadAllText(fixture.Host.TraySettingsPath).Contains("backend="), "OFXR settings materialized");
    }
    {
        var f = new Fixture(Path.Combine(root, "settings-isolation"), vector);
        f.Game.Provider = ProviderId.Djules; f.Game.For(ProviderId.Djules).Quality = "slow"; await f.Apply();
        f.Game.For(ProviderId.Djules).Quality = "medium";
        Check(f.State.AppliedSetup?.For(ProviderId.Djules).Quality == "slow", "Editing a preset preserves the applied settings snapshot");
        Check(f.Storage.Load().AppliedSetup?.Provider == ProviderId.Djules, "Applied setup survives reloading state");
        f.Game.For(ProviderId.Djules).Quality = "slow";
        f.Game.Provider = ProviderId.Elliott; f.Game.For(ProviderId.Elliott).Quality = "fast"; await f.Apply();
        f.Game.Provider = ProviderId.Djules; await f.Apply();
        Check(File.ReadAllText(f.Host.TraySettingsPath).Contains("nvidia_preset=slow"), "Fork-specific settings restored across switches");
    }
    {
        var f = new Fixture(Path.Combine(root, "coexist"), vector);
        f.Game.Provider = ProviderId.Elliott; f.Game.KeepVectorFeatures = true;
        await Reject(f.Apply, "Experimental coexistence requires acknowledgement");
        f.Game.ExperimentalAcknowledged = true; await f.Apply();
        Check(f.Host.Layers().Any(r => r.Vector && r.Enabled) && f.Host.RunningTray() != null, "Coexistence keeps both layers");
        var config = JsonNode.Parse(File.ReadAllText(f.Host.VectorSettingsPath))!;
        Check(!config["modules"]!["framegen"]!["enabled"]!.GetValue<bool>() && !config["modules"]!["framegen"]!["profiles"]![0]!["enabled"]!.GetValue<bool>(), "Coexistence prevents stacked FG");
    }
    {
        var f = new Fixture(Path.Combine(root, "running"), vector); f.Host.GameRunning = true;
        await Reject(f.Apply, "Running game prevents switch");
        Check(!f.Coordinator.RecoveryPending && f.Host.Writes == 0, "Running-game refusal has no mutations");
    }
    {
        var f = new Fixture(Path.Combine(root, "conflict"), vector); var observed = f.Coordinator.Fingerprint();
        File.AppendAllText(f.Host.TraySettingsPath, "; user edit");
        await Reject(() => f.Coordinator.Apply(f.State, f.Game, f.Selected, observed), "External edits require refresh");
        Check(File.ReadAllText(f.Host.TraySettingsPath).Contains("user edit"), "External edit preserved");
    }
    foreach (var failure in new[] { "start", "verify", "elevation" }) {
        var f = new Fixture(Path.Combine(root, "fail-" + failure), vector);
        f.Game.Provider = ProviderId.Djules; await f.Apply();
        var before = File.ReadAllText(f.Host.TraySettingsPath);
        f.Game.Provider = failure == "elevation" ? ProviderId.VectorXR : ProviderId.Elliott;
        f.Host.Fail = failure;
        await Reject(f.Apply, failure + " failure propagated");
        Check(f.State.ActiveProvider == ProviderId.Djules && !f.Coordinator.RecoveryPending, failure + " restores previous state");
        Check(f.State.AppliedSetup?.Provider == ProviderId.Djules, failure + " restores applied settings snapshot");
        Check(File.ReadAllText(f.Host.TraySettingsPath) == before, failure + " restores previous settings");
    }
    {
        var f = new Fixture(Path.Combine(root, "crash"), vector);
        var before = File.ReadAllBytes(f.Host.TraySettingsPath); var after = Encoding.UTF8.GetBytes("new");
        var journal = new SwitchJournal { Previous = f.State, Phase = "Applying", Files = [new(f.Host.TraySettingsPath, before, after)] };
        SafeFiles.AtomicWrite(f.Storage.JournalPath, Json.Write(journal)); File.WriteAllBytes(f.Host.TraySettingsPath, after);
        await f.Coordinator.Recover(f.State);
        Check(File.ReadAllBytes(f.Host.TraySettingsPath).SequenceEqual(before) && !f.Coordinator.RecoveryPending, "Interrupted write recovered");
        SafeFiles.AtomicWrite(f.Storage.JournalPath, Json.Write(journal)); File.WriteAllText(f.Host.TraySettingsPath, "external");
        await Reject(() => f.Coordinator.Recover(f.State), "Recovery refuses unknown external edit");
        Check(f.Coordinator.RecoveryPending && File.ReadAllText(f.Host.TraySettingsPath) == "external", "Conflict retains recovery journal and edit");
    }
    {
        var f = new Fixture(Path.Combine(root, "bad-journal"), vector);
        var outside = Path.Combine(root, "never-touch"); File.WriteAllText(outside, "keep");
        SafeFiles.AtomicWrite(f.Storage.JournalPath, Json.Write(new SwitchJournal { Previous = f.State, Files = [new(outside, null, null)] }));
        await Reject(() => f.Coordinator.Recover(f.State), "Recovery journal cannot target unrelated files");
        Check(File.ReadAllText(outside) == "keep", "Unrelated recovery target untouched");
    }
    {
        using var rsa = RSA.Create(3072);
        var document = new CompatibilityCatalog(1, Catalog.Product, [new CatalogRelease { Provider = ProviderId.Djules, Version = "test", Repository = "djules75/OFXR-Bridge", Url = "https://github.com/djules75/OFXR-Bridge/releases/download/test/tool.zip", Size = 3, Sha256 = new string('a', 64), Files = new() { ["OFXRBridgeTray.exe"] = new string('b', 64) } }]);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Write(document)));
        var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes("WSG-CATALOG-v1\n" + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var envelope = Json.Write(new SignedDocument(payload, signature));
        Check(new Catalog(envelope, rsa.ExportSubjectPublicKeyInfoPem()).Document.Releases.Count == 1, "Signed catalog accepted");
        await Reject(() => Task.FromResult(new Catalog(Json.Write(new SignedDocument(payload, Convert.ToBase64String(new byte[384]))), rsa.ExportSubjectPublicKeyInfoPem())), "Catalog tampering rejected");
        using var wrong = RSA.Create(3072);
        await Reject(() => Task.FromResult(new Catalog(envelope, wrong.ExportSubjectPublicKeyInfoPem())), "Wrong catalog identity rejected");
    }
    {
        var f = new Fixture(Path.Combine(root, "external-vector-edit"), vector);
        f.Game.Provider = ProviderId.Djules; await f.Apply();
        var edited = JsonNode.Parse(File.ReadAllText(f.Host.VectorSettingsPath))!;
        edited["modules"]!["framegen"]!["profiles"]![0]!["settings"]!["synthesisMode"] = "interpolate";
        File.WriteAllText(f.Host.VectorSettingsPath, edited.ToJsonString());
        VectorSnapshots.Reconcile(f.Storage, f.Host.VectorSettingsPath);
        var baseline = JsonNode.Parse(File.ReadAllText(Path.Combine(f.Storage.Root, "vector-framegen-original.json")))!;
        Check(baseline["profiles"]![0]!["enabled"]!.GetValue<bool>(), "Refresh retains original enable flag suppressed by WSG");
        Check(baseline["profiles"]![0]!["settings"]!["synthesisMode"]!.GetValue<string>() == "interpolate", "Refresh retains external VectorXR FG edit");
        f.Game.Provider = ProviderId.VectorXR; await f.Apply();
        var applied = JsonNode.Parse(File.ReadAllText(f.Host.VectorSettingsPath))!;
        Check(applied["modules"]!["framegen"]!["profiles"]!.AsArray().Count == 2, "Switch keeps original profile alongside selected WSG profile");
        applied["modules"]!["framegen"]!["profiles"]![0]!["settings"]!["motionEstimation"] = "hardware";
        applied["modules"]!["framegen"]!["profiles"]![0]!["settings"]!["artifactSuppression"] = 77;
        File.WriteAllText(f.Host.VectorSettingsPath, applied.ToJsonString());
        VectorSnapshots.Reconcile(f.Storage, f.Host.VectorSettingsPath);
        f.Game.Provider = ProviderId.Off; await f.Apply();
        f.Game.Provider = ProviderId.VectorXR; await f.Apply();
        var restoredTuning = JsonNode.Parse(File.ReadAllText(f.Host.VectorSettingsPath))!["modules"]!["framegen"]!["profiles"]![0]!["settings"]!;
        Check(restoredTuning["motionEstimation"]!.GetValue<string>() == "hardware" && restoredTuning["artifactSuppression"]!.GetValue<int>() == 77,
            "Native VectorXR advanced tuning survives Refresh, Off, and reapply");
    }
    {
        var store = new Storage(Path.Combine(root, "notes"));
        store.AddNote(new(DateTimeOffset.Now, "=evil", "game.exe", ProviderId.Off, "Off", new(), false, "runtime", "45", "90", "Hello,\"world\"\nnext line"));
        var csv = Path.Combine(root, "notes.csv"); store.ExportNotes(csv);
        Check(File.ReadAllText(csv).Contains("'=evil") && File.ReadAllText(csv).Contains("\"\"world\"\""), "CSV escapes formulas, quotes, and newlines");
        Check(store.ReadNotes().Count == 1 && store.ReadNotes()[0].SetupSource.Contains("legacy"), "Existing notes remain readable with their source labelled");
        store.AddNote(new(DateTimeOffset.Now, "Game", "game.exe", ProviderId.Djules, "test", new(), false, "runtime", "", "", "Fixture") { SetupSource = "Applied setup snapshot" });
        store.ExportNotes(csv);
        Check(store.ReadNotes().Count == 2 && File.ReadAllText(csv).Contains("Applied setup snapshot"), "Saved history and CSV preserve applied setup provenance");
    }
    await RemovalTests.Run(root, vector, Check, Reject);
    CleanupTests.Run(root, Check);
    await SetupTests.Run(root, Check, Reject);
    await SettingsCompatibilityTests.Run(Check, Reject);
    await StartupTests.Run(root, vector, Check, Reject);
    // Optional read-only verification of pinned upstream packages; never execute providers.
    if (args.Length == 1) {
        var repo = Path.GetFullPath(args[0]); var catalog = new Catalog(File.ReadAllText(Path.Combine(repo, "assets/catalog.signed.json")), File.ReadAllText(Path.Combine(repo, "assets/trust-public.pem")));
        foreach (var r in catalog.Document.Releases.Where(r => r.Provider != ProviderId.VectorXR)) {
            var folder = Path.Combine(repo, ".research/packages", r.Version);
            catalog.Verify(new(r.Provider, r.Version, folder, Path.Combine(folder, r.Executable), r.Key));
            Check(true, "Official " + r.Provider + " release inventory verified");
            var package = Directory.GetFiles(Path.Combine(repo, ".research/packages"), "*.zip").Single(path => SafeFiles.HashFile(path) == r.Sha256);
            var fixtureStorage = new Storage(Path.Combine(root, "official-install-" + r.Provider + "-" + r.Version));
            var fixtureState = new AppState { ProviderRoot = Path.Combine(fixtureStorage.Root, "OpenXR FG") };
            var fixtureHost = new FakeHost(fixtureStorage.Root, "{\"version\":3}");
            SafeFiles.AtomicWrite(Path.Combine(fixtureStorage.Root, "downloads", r.Provider + "-" + r.Sha256 + ".zip"), File.ReadAllBytes(package));
            var fixtureManager = new ProviderManager(fixtureStorage, catalog, fixtureHost);
            var installed = await fixtureManager.Install(r, fixtureState, _ => { });
            catalog.Verify(installed);
            Check(fixtureManager.Identify(installed.Directory).CatalogKey == r.Key,
                "Official " + r.Provider + " identifies correctly against the full multi-provider catalog");
            SafeFiles.AtomicWrite(Path.Combine(fixtureStorage.Root, "downloads", r.Provider + "-" + r.Sha256 + ".zip"), File.ReadAllBytes(package));
            Check(await fixtureManager.Install(r, fixtureState, _ => { }) == installed,
                "Official " + r.Provider + " repair reuses a verified installed package");
            Check(installed.Directory == ProviderPaths.Destination(fixtureStorage, fixtureState, r),
                "Official " + r.Provider + " " + r.Version + " ZIP installs and verifies in a root with spaces");
            Check(fixtureHost.Writes == 0 && fixtureHost.RunningTray() == null && !Directory.Exists(Path.Combine(fixtureState.ProviderRoot, ".staging")),
                "Official ZIP install cleans staging without starting a provider or changing registrations");
        }
        Authenticode.RequireDienerTech(Path.Combine(repo, ".research/packages/VectorXR-0.19.2-windows-x64-setup.exe"));
        Check(true, "Official VectorXR Authenticode verified without execution");
        var liveHost = new WindowsHost();
        var manager = new ProviderManager(new Storage(Path.Combine(root, "discovery-only")), catalog, liveHost);
        var installedVector = manager.Discover(new AppState()).FirstOrDefault(i => i.Provider == ProviderId.VectorXR);
        if (installedVector != null) {
            catalog.Verify(installedVector);
            Check(Path.GetFileName(installedVector.Executable) == "vectorxr-app.exe", "Actual installed VectorXR signed executable discovered from its registered layer");
            Console.WriteLine("Verified installed version: " + installedVector.Version);
            if (File.Exists(liveHost.VectorSettingsPath)) Console.WriteLine("VectorXR settings physical path: " + ProcessLaunch.PhysicalFile(liveHost.VectorSettingsPath));
            var editorOpen = System.Diagnostics.Process.GetProcessesByName("vectorxr-app");
            try {
                if (editorOpen.Length > 0) {
                    bool refused = false;
                    try { liveHost.RequireIdle([]); } catch (InvalidOperationException ex) { refused = ex.Message.Contains("Close the VectorXR settings app"); }
                    Check(refused, "Actual running VectorXR editor blocks conflicting settings writes");
                }
            } finally { foreach (var process in editorOpen) process.Dispose(); }
        }
    }
    Console.WriteLine($"\n{passed} checks passed. Fixtures: {root}"); return 0;
} catch (Exception ex) { Console.Error.WriteLine(ex); Console.Error.WriteLine("Fixtures retained: " + root); return 1; }

sealed class Fixture
{
    public Storage Storage; public FakeHost Host; public AppState State; public GamePreset Game; public SwitchCoordinator Coordinator;
    public Installation? Selected => State.Installations.FirstOrDefault(i => i.Provider == Game.Provider);
    public Fixture(string root, string vector) {
        Storage = new(root); Host = new(root, vector); Game = new() { Name = "Game", Executable = @"C:\Game.exe" };
        State = new() { Games = [Game], Installations = ProviderInfo.All.Select(p => new Installation(p.Id, "test", root, Path.Combine(root, p.Id + ".exe"), p.Id + ":test")).ToList() };
        Coordinator = new(Storage, Host, _ => { });
    }
    public Task Apply() => Coordinator.Apply(State, Game, Selected, Coordinator.Fingerprint());
}
sealed class FakeHost : ISwitchHost
{
    public string TraySettingsPath { get; }
    public string VectorSettingsPath { get; }
    public bool GameRunning; public string? Fail; public int Writes;
    string? running;
    readonly List<LayerRegistration> layers;
    public FakeHost(string root, string vector) {
        TraySettingsPath = Path.Combine(root, "tray.ini"); VectorSettingsPath = Path.Combine(root, "vector.json");
        File.WriteAllText(TraySettingsPath, "[tray]\nbackend=nvidia\n"); File.WriteAllText(VectorSettingsPath, vector);
        layers = [new("Machine", 64, OpenXrRegistry.OpenXr, "vector.json", 0, "XR_APILAYER_DIENERTECH_VECTORXR", "vector.dll"), new("Machine", 64, OpenXrRegistry.OpenXr, "unrelated.json", 0, "Unrelated", "unrelated.dll")];
    }
    public IReadOnlyList<LayerRegistration> Layers() => layers.ToArray();
    public void AddLayer(LayerRegistration registration) => layers.Add(registration);
    public void ClearVectorLayers() => layers.RemoveAll(r => r.Vector);
    public void RequireIdle(IEnumerable<GamePreset> games) { if (GameRunning) throw new InvalidOperationException("Game running"); }
    public string? RunningTray() => running;
    public Task StopTray() { running = null; layers.RemoveAll(r => r.Ofxr); return Task.CompletedTask; }
    public Task StartTray(string executable) { Throw("start"); running = executable; layers.Add(new("User", 64, OpenXrRegistry.OpenXr, "ofxr.json", 0, "XR_APILAYER_XRFrameBridge", "layer.dll")); return Task.CompletedTask; }
    public Task SetVector(IReadOnlyList<RegistryChange> changes) { Throw("elevation"); foreach (var c in changes) { var index = layers.FindIndex(r => r.Identity == c.Before.Identity); layers[index] = layers[index] with { Value = c.After }; Writes++; } return Task.CompletedTask; }
    public Task Verify(Installation? installation, bool keepVector) { Throw("verify"); return Task.CompletedTask; }
    void Throw(string stage) { if (Fail == stage) { Fail = null; throw new IOException(stage + " failed"); } }
}
