using Wsg.OpenXR;

static class CleanupTests
{
    internal static void Run(string root, Action<bool, string> check) {
        var store = new Storage(Path.Combine(root, "retention-cleanup"));
        var backup = Path.Combine(store.Root, "backups", "20261018-123456-1234567.json");
        var download = Path.Combine(store.Root, "downloads", "Djules-" + new string('a', 64) + ".zip");
        foreach (var journal in new[] { "recovery.json", "installation-recovery.json", "removal-recovery.json" }) {
            SafeFiles.AtomicWrite(backup, "fixture"); SafeFiles.AtomicWrite(download, "fixture");
            SafeFiles.AtomicWrite(Path.Combine(store.Root, journal), "pending");
            store.CleanupCompletedOperations();
            check(File.Exists(backup) && File.Exists(download), journal + " protects temporary recovery material");
            File.Delete(Path.Combine(store.Root, journal));
        }
        foreach (var name in new[] { "vector-install-20261018-123456-1234567.json", "vector-post-installer-20261018-123456-1234567.json", "vector-before-review-20261018-123456-1234567.json", "vector-version-0123456789abcdef.json" })
            SafeFiles.AtomicWrite(Path.Combine(store.Root, "backups", name), "fixture");
        var unknown = Path.Combine(store.Root, "backups", "my-notes.txt"); SafeFiles.AtomicWrite(unknown, "keep");
        var otherDownload = Path.Combine(store.Root, "downloads", "my-file.zip"); SafeFiles.AtomicWrite(otherDownload, "keep");
        var config = Path.Combine(store.Root, "vector-framegen-original.json"); SafeFiles.AtomicWrite(config, "current working state");
        store.Save(new()); store.AddNote(new(DateTimeOffset.Now, "Game", "game.exe", ProviderId.Off, "", new(), false, "runtime", "", "", "keep"));
        store.CleanupCompletedOperations();
        check(!File.Exists(backup) && !File.Exists(download), "Completed history and provider downloads are deleted");
        check(Directory.GetFiles(Path.Combine(store.Root, "backups")).Length == 1, "Legacy VectorXR snapshot variants are deleted");
        check(File.Exists(unknown) && File.Exists(otherDownload), "Unrecognized backup and download files survive cleanup");
        check(File.Exists(config) && File.Exists(store.StatePath) && store.ReadNotes().Count == 1, "Working configuration, presets and notes survive cleanup");

        var id = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(store.Root, "backups", "removed-" + id);
        var file = Path.Combine(dir, "files", "subfolder", "provider.dll");
        SafeFiles.AtomicWrite(file, "verified");
        var removal = new RemovalJournal(new(ProviderId.Djules, "test", "unused", "unused", "unused"), id,
            new() { ["subfolder/provider.dll"] = SafeFiles.HashFile(file)! });
        SafeFiles.AtomicWrite(Path.Combine(dir, "removal-manifest.json"), Json.Write(removal));
        File.WriteAllText(file, "external edit"); store.CleanupCompletedOperations();
        check(File.ReadAllText(file) == "external edit", "Modified historical removal files are preserved");
        File.WriteAllText(file, "verified"); store.CleanupCompletedOperations();
        check(!Directory.Exists(dir), "Recognized old removal packages and their empty folders are deleted");

        // Retry a previously locked download on the next cleanup pass.
        SafeFiles.AtomicWrite(download, "fixture");
        using (var held = File.Open(download, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            store.CleanupCompletedOperations(); check(File.Exists(download), "Locked temporary downloads defer cleanup without failing an operation");
        }
        store.CleanupCompletedOperations(); check(!File.Exists(download), "Deferred download cleanup succeeds after the handle closes");
        store.CleanupCompletedOperations(); check(File.Exists(unknown), "Repeated cleanup is harmless");

        var game = new GamePreset { Name = "Keep game", Executable = "game.exe" };
        var installedExe = Path.Combine(store.Root, "provider", "tray.exe"); SafeFiles.AtomicWrite(installedExe, "fixture");
        var state = new AppState { Games = [game], ActiveProvider = ProviderId.Djules, ActiveGameId = game.Id,
            AppliedSetup = new GamePreset { Id = game.Id, Provider = ProviderId.Djules, Versions = new() { [ProviderId.Djules] = "active" } },
            Installations = [new(ProviderId.Elliott, "installed", Path.GetDirectoryName(installedExe)!, installedExe, "fixture")] };
        store.Save(state);
        var unused = store.PresetPath(game.Id, ProviderId.Djules, "removed");
        var active = store.PresetPath(game.Id, ProviderId.Djules, "active");
        var installedPreset = store.PresetPath(game.Id, ProviderId.Elliott, "installed");
        foreach (var path in new[] { unused, active, installedPreset }) SafeFiles.AtomicWrite(path, "preset fixture");
        var settingsBefore = File.ReadAllText(store.StatePath);
        var cleaned = store.CleanupCompletedOperations();
        check(!File.Exists(unused) && File.Exists(active) && File.Exists(installedPreset), "Only presets for uninstalled inactive versions are removed");
        check(settingsBefore == File.ReadAllText(store.StatePath) && File.Exists(installedExe), "Preset cleanup never resets game choices or modifies installed mods");
        check(cleaned.FilesDeleted == 1 && cleaned.BytesDeleted > 0 && !cleaned.RecoveryPending, "Cleanup reports deleted-file count and bytes");
        SafeFiles.AtomicWrite(unused, "keep during recovery"); SafeFiles.AtomicWrite(store.JournalPath, "pending");
        var pending = store.CleanupCompletedOperations();
        check(pending.RecoveryPending && pending.FilesDeleted == 0 && File.Exists(unused), "Pending recovery protects otherwise unused presets");
        File.Delete(store.JournalPath);
        state.AppliedSetup!.Versions.Clear(); store.Save(state); store.CleanupCompletedOperations();
        check(File.Exists(unused), "Unknown active version defers all preset cleanup");
    }
}
