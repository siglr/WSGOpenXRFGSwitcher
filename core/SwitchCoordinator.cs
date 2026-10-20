using System.Text;
using System.Text.Json.Nodes;

namespace Wsg.OpenXR;

public sealed record FileMutation(string Path, byte[]? Before, byte[]? After);
public sealed record SwitchJournal
{
    public int Schema { get; set; } = 1;
    public string Phase { get; set; } = "Prepared";
    public AppState Previous { get; set; } = new();
    public Installation? PreviousTray { get; set; }
    public List<FileMutation> Files { get; set; } = [];
    public List<RegistryChange> Registry { get; set; } = [];
}
public sealed class SwitchCoordinator(Storage storage, ISwitchHost host, Action<Installation> verifyInstallation, Action<Installation, Installation>? verifyCoexistence = null, Func<string, CatalogRelease>? resolveRelease = null)
{
    readonly SemaphoreSlim gate = new(1, 1);
    public bool Busy { get; private set; }
    public bool RecoveryPending => File.Exists(storage.JournalPath);
    public string Fingerprint() {
        var values = host.Layers().OrderBy(r => r.Identity, StringComparer.Ordinal).Select(r => Json.Write(r));
        return SafeFiles.Hash(Encoding.UTF8.GetBytes(string.Join('\n', values) + "\n" + SafeFiles.HashFile(host.TraySettingsPath) + "\n" + SafeFiles.HashFile(host.VectorSettingsPath) + "\n" + host.RunningTray()));
    }
    public async Task Apply(AppState state, GamePreset game, Installation? selected, string observedFingerprint) {
        if (!await gate.WaitAsync(0)) throw new InvalidOperationException("Another operation is in progress.");
        Busy = true;
        try {
            if (RecoveryPending) throw new InvalidOperationException("Recover the interrupted operation before applying another setup.");
            host.RequireIdle(state.Games);
            if (Fingerprint() != observedFingerprint) throw new InvalidOperationException("Provider settings or layers changed outside WSG. Refresh and review before applying.");
            if (game.Provider != ProviderId.Off && (selected == null || selected.Provider != game.Provider)) throw new InvalidOperationException("Install or select a verified provider version first.");
            if (selected != null) verifyInstallation(selected);
            if (game.KeepVectorFeatures && !game.ExperimentalAcknowledged) throw new InvalidOperationException("Acknowledge experimental coexistence before keeping VectorXR features.");
            var layers = host.Layers();
            if (game.KeepVectorFeatures || game.Provider == ProviderId.VectorXR) {
                var vectorInstallation = state.Installations.SingleOrDefault(i => i.Provider == ProviderId.VectorXR) ?? throw new InvalidOperationException("Import or install a catalog-reviewed VectorXR version first.");
                verifyInstallation(vectorInstallation);
                if (game.KeepVectorFeatures && selected?.Provider is ProviderId.Djules or ProviderId.Elliott) verifyCoexistence?.Invoke(selected, vectorInstallation);
                if (layers.Count(r => r.Vector && r.View == 64) != 1) throw new InvalidOperationException("Install or repair VectorXR first. Exactly one 64-bit registration is required.");
                if (!File.Exists(host.VectorSettingsPath)) throw new InvalidOperationException("Open VectorXR once to initialize its settings, then close it and retry.");
            }
            var running = host.RunningTray();
            var previousTray = running == null ? null : state.Installations.SingleOrDefault(i => i.Executable.Equals(running, StringComparison.OrdinalIgnoreCase));
            if (running != null && previousTray == null) throw new InvalidOperationException("An unmanaged OFXR tray is running. Close it or import its verified installation first.");
            if (previousTray != null) verifyInstallation(previousTray);
            if (running == null && layers.Any(r => r.Ofxr && r.Enabled)) throw new InvalidOperationException("An OFXR registration is active without its tray. Disarm it with its original provider before switching.");
            var journal = new SwitchJournal { Previous = Json.Read<AppState>(Json.Write(state)), PreviousTray = previousTray };
            var enableVector = game.Provider == ProviderId.VectorXR || game.KeepVectorFeatures;
            journal.Registry = layers.Where(r => r.Vector && r.Value != (enableVector ? 0 : 1)).Select(r => new RegistryChange(r, enableVector ? 0 : 1)).ToList();
            if (File.Exists(host.VectorSettingsPath)) {
                var originalPath = Path.Combine(storage.Root, "vector-framegen-original.json");
                var current = File.ReadAllText(host.VectorSettingsPath);
                var document = JsonNode.Parse(current)!.AsObject();
                if (document["version"]?.GetValue<int>() != 3) throw new InvalidDataException("Unknown VectorXR configuration format. Settings were not changed.");
                if (!File.Exists(originalPath)) AddFile(journal, originalPath, Encoding.UTF8.GetBytes(document["modules"]?["framegen"]?.ToJsonString() ?? "{}"));
                else {
                    var baseline = JsonNode.Parse(File.ReadAllText(originalPath))!.AsObject();
                    var merged = VectorSnapshots.PreserveManagedProfiles(baseline, document["modules"]?["framegen"]?.AsObject() ?? new JsonObject());
                    AddFile(journal, originalPath, Encoding.UTF8.GetBytes(merged.ToJsonString(Json.Options)));
                    document["modules"]!["framegen"] = merged;
                }
                var patched = ProviderAdapters.For(ProviderId.VectorXR).PrepareConfiguration(document.ToJsonString(), game, game.Provider == ProviderId.VectorXR, game.KeepVectorFeatures);
                AddFile(journal, host.VectorSettingsPath, Encoding.UTF8.GetBytes(patched));
                AddFile(journal, Path.Combine(storage.Root, "vector-framegen-last.json"), Encoding.UTF8.GetBytes(JsonNode.Parse(patched)!["modules"]!["framegen"]!.ToJsonString()));
            }
            if (previousTray != null && state.ActiveGameId != null) {
                AddFile(journal, storage.PresetPath(state.ActiveGameId, previousTray.Provider, previousTray.Version), Read(host.TraySettingsPath));
            }
            if (selected?.Provider is ProviderId.Djules or ProviderId.Elliott) {
                var presetPath = storage.PresetPath(game.Id, selected.Provider, selected.Version);
                var existing = File.Exists(presetPath) ? File.ReadAllText(presetPath) : "";
                if (state.ActiveGameId == game.Id && state.ActiveProvider == selected.Provider && File.Exists(host.TraySettingsPath)) existing = File.ReadAllText(host.TraySettingsPath);
                var release = resolveRelease?.Invoke(selected.CatalogKey);
                if (release != null) ProviderAdapters.For(selected.Provider).ValidateCapabilities(release, game.For(selected.Provider));
                var patched = Encoding.UTF8.GetBytes(ProviderAdapters.For(selected.Provider).PrepareConfiguration(existing, game, true, false, release));
                AddFile(journal, host.TraySettingsPath, patched);
            } else AddFile(journal, host.TraySettingsPath, Read(host.TraySettingsPath));
            SafeFiles.AtomicWrite(storage.JournalPath, Json.Write(journal));
            try {
                await host.StopTray();
                // Tray shutdown may flush its latest settings; preserve those as the rollback origin.
                for (int i = 0; i < journal.Files.Count; i++) {
                    var f = journal.Files[i];
                    if (f.Path == host.TraySettingsPath) journal.Files[i] = f with { Before = Read(f.Path) };
                    else if (previousTray != null && state.ActiveGameId != null && f.Path == storage.PresetPath(state.ActiveGameId, previousTray.Provider, previousTray.Version)) journal.Files[i] = f with { After = Read(host.TraySettingsPath) };
                }
                journal.Phase = "Applying"; SafeFiles.AtomicWrite(storage.JournalPath, Json.Write(journal));
                host.RequireIdle(state.Games);
                foreach (var file in journal.Files) { RequireCurrent(file, false); Write(file.Path, file.After); }
                await host.SetVector(journal.Registry);
                if (selected?.Provider is ProviderId.Djules or ProviderId.Elliott) { verifyInstallation(selected); await host.StartTray(selected.Executable); }
                await host.Verify(selected, game.KeepVectorFeatures);
                state.ActiveGameId = game.Id; state.ActiveProvider = game.Provider;
                state.AppliedSetup = Json.Read<GamePreset>(Json.Write(game));
                state.LastAppliedFingerprint = Fingerprint(); storage.Save(state);
                journal.Phase = "Committed"; SafeFiles.AtomicWrite(storage.JournalPath, Json.Write(journal));
                CompleteJournal(journal); storage.Log("Applied " + game.Provider + " for " + game.Name + ". Ready for next launch; no in-game performance claim.");
            } catch (Exception ex) {
                storage.Log("Switch failed: " + ex.Message);
                try { await Rollback(journal, state); }
                catch (Exception recovery) { throw new IOException(ex.Message + " Recovery requires attention: " + recovery.Message + " The recovery journal and originals were preserved.", recovery); }
                throw new IOException(ex.Message + " The previous setup was restored.", ex);
            }
        } finally { Busy = false; gate.Release(); }
    }
    public async Task Recover(AppState state) {
        if (!await gate.WaitAsync(0)) throw new InvalidOperationException("Another operation is in progress.");
        Busy = true;
        try {
            if (!RecoveryPending) return;
            host.RequireIdle(state.Games);
            var journal = Json.Read<SwitchJournal>(File.ReadAllText(storage.JournalPath));
            ValidateJournal(journal);
            if (journal.Phase == "Committed") { CompleteJournal(journal); return; }
            await Rollback(journal, state);
        } finally { Busy = false; gate.Release(); }
    }
    async Task Rollback(SwitchJournal journal, AppState state) {
        ValidateJournal(journal); host.RequireIdle(state.Games);
        // Check all file conflicts before changing anything. Unrecognized edits stay untouched.
        foreach (var f in journal.Files) RequireCurrent(f, true);
        await host.StopTray();
        var current = host.Layers(); var reverse = new List<RegistryChange>();
        foreach (var c in journal.Registry) {
            var actual = current.SingleOrDefault(r => r.Identity == c.Before.Identity) ?? throw new IOException("A VectorXR registration disappeared during recovery.");
            if (actual.Value != c.Before.Value && actual.Value != c.After) throw new IOException("VectorXR registration was edited externally.");
            if (actual.Value != c.Before.Value) reverse.Add(new(actual, c.Before.Value));
        }
        await host.SetVector(reverse);
        foreach (var f in journal.Files.AsEnumerable().Reverse()) { RequireCurrent(f, true); Write(f.Path, f.Before); }
        if (journal.PreviousTray != null) { verifyInstallation(journal.PreviousTray); await host.StartTray(journal.PreviousTray.Executable); }
        // Original VectorXR non-FG use can be active independently of the previous WSG selection.
        if (journal.PreviousTray != null) await host.Verify(journal.PreviousTray, host.Layers().Any(r => r.Vector && r.Enabled));
        state.ActiveGameId = journal.Previous.ActiveGameId; state.ActiveProvider = journal.Previous.ActiveProvider;
        state.AppliedSetup = journal.Previous.AppliedSetup;
        state.LastAppliedFingerprint = Fingerprint(); storage.Save(state);
        journal.Phase = "Recovered"; CompleteJournal(journal);
    }
    void ValidateJournal(SwitchJournal journal) {
        if (journal.Schema != 1 || journal.Files.Count > 20 || journal.Registry.Count > 16) throw new InvalidDataException("Unknown recovery journal.");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { host.TraySettingsPath, host.VectorSettingsPath, Path.Combine(storage.Root, "vector-framegen-original.json"), Path.Combine(storage.Root, "vector-framegen-last.json") };
        foreach (var game in journal.Previous.Games) foreach (var install in journal.Previous.Installations.Where(i => i.Provider is ProviderId.Djules or ProviderId.Elliott)) allowed.Add(storage.PresetPath(game.Id, install.Provider, install.Version));
        if (journal.Files.Any(f => !allowed.Contains(f.Path)) || journal.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Files.Count) throw new InvalidDataException("Recovery contains an unexpected file.");
        if (journal.PreviousTray != null) verifyInstallation(journal.PreviousTray);
        if (journal.Registry.Any(c => !c.Before.Vector || c.Before.Key != OpenXrRegistry.OpenXr || c.Before.Value is not (0 or 1) || c.After is not (0 or 1))) throw new InvalidDataException("Recovery contains an unexpected registration.");
    }
    void CompleteJournal(SwitchJournal journal) {
        File.Delete(storage.JournalPath);
        storage.CleanupCompletedOperations();
    }
    static byte[]? Read(string path) { SafeFiles.NoLinks(path); return File.Exists(path) ? File.ReadAllBytes(path) : null; }
    static void Write(string path, byte[]? bytes) { SafeFiles.NoLinks(path); if (bytes == null) { if (File.Exists(path)) File.Delete(path); } else SafeFiles.AtomicWrite(path, bytes); }
    static void AddFile(SwitchJournal journal, string path, byte[]? after) {
        var existing = journal.Files.FindIndex(f => f.Path == path);
        if (existing >= 0) journal.Files[existing] = journal.Files[existing] with { After = after };
        else journal.Files.Add(new(path, Read(path), after));
    }
    static void RequireCurrent(FileMutation file, bool allowAfter) {
        var current = SafeFiles.HashFile(file.Path); var before = file.Before == null ? null : SafeFiles.Hash(file.Before); var after = file.After == null ? null : SafeFiles.Hash(file.After);
        if (current != before && (!allowAfter || current != after)) throw new IOException("An external edit was preserved: " + file.Path);
    }
}
