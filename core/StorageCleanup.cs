using System.Text.RegularExpressions;

namespace Wsg.OpenXR;

public sealed record CleanupResult(int FilesDeleted, long BytesDeleted, bool RecoveryPending, int DeferredItems);

public sealed partial class Storage
{
    // Run only at startup or after an operation has finished. Pending journals take priority.
    public CleanupResult CleanupCompletedOperations() {
        if (new[] { JournalPath, Path.Combine(Root, "removal-recovery.json"), Path.Combine(Root, "installation-recovery.json") }.Any(File.Exists)) return new(0, 0, true, 0);
        int deleted = 0, deferred = 0; long bytes = 0;
        void DeleteFile(string path) {
            SafeFiles.NoLinks(path);
            var size = new FileInfo(path).Length; File.Delete(path); deleted++; bytes += size;
        }
        foreach (var folder in new[] { "backups", "downloads" }) {
            try {
                var directory = SafeFiles.Child(Root, folder);
                if (!Directory.Exists(directory)) continue;
                foreach (var path in Directory.EnumerateFileSystemEntries(directory).ToArray()) {
                    try {
                        SafeFiles.NoLinks(path);
                        var name = Path.GetFileName(path);
                        if (folder == "downloads") {
                            if (Regex.IsMatch(name, @"^(?:(?:Djules|Elliott)-[a-f0-9]{64}\.zip|VectorXR-[a-f0-9]{64}\.exe)$") && File.Exists(path)) DeleteFile(path);
                        } else if (File.Exists(path) && Regex.IsMatch(name, @"^(?:(?:vector-(?:install|post-installer|before-review)-)?\d{8}-\d{6}-\d{7}|vector-version-[a-f0-9]{16})\.json$")) {
                            DeleteFile(path);
                        } else if (Directory.Exists(path) && Regex.IsMatch(name, @"^removed-[a-f0-9]{32}$")) {
                            if (!DeleteCompletedRemoval(path, DeleteFile)) deferred++;
                        }
                    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) {
                        deferred++; Log("Deferred temporary-file cleanup: " + path + " (" + ex.Message + ")");
                    }
                }
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                deferred++; Log("Deferred temporary-folder cleanup: " + ex.Message);
            }
        }
        try { DeleteUnusedPresets(Load(), DeleteFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) {
            deferred++; Log("Deferred preset cleanup: " + ex.Message);
        }
        return new(deleted, bytes, false, deferred);
    }

    bool DeleteCompletedRemoval(string directory, Action<string> deleteFile) {
        var manifest = SafeFiles.Child(directory, "removal-manifest.json");
        if (!File.Exists(manifest)) return false;
        var journal = Json.Read<RemovalJournal>(File.ReadAllText(manifest));
        if (!Guid.TryParseExact(journal.BackupId, "N", out _) || Path.GetFileName(directory) != "removed-" + journal.BackupId
            || journal.Files == null || journal.Files.Count > 10000) return false;
        var files = new List<string>();
        foreach (var entry in journal.Files) {
            var path = SafeFiles.Child(directory, "files/" + entry.Key);
            if (!File.Exists(path)) continue;
            if (SafeFiles.HashFile(path) != entry.Value) return false; // Preserve unexpected changes.
            files.Add(path);
        }
        foreach (var path in files) deleteFile(path);
        // Prune only empty, non-linked directories. Preserve unknown files.
        void Prune(string path) {
            SafeFiles.NoLinks(path);
            foreach (var child in Directory.EnumerateDirectories(path).ToArray()) {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) Prune(child);
            }
            if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path, false);
        }
        Prune(directory);
        // Keep the manifest if unknown content remains, so future cleanup is still bounded.
        if (Directory.EnumerateFileSystemEntries(directory).All(p => p == manifest)) {
            deleteFile(manifest); Directory.Delete(directory, false); return true;
        }
        return false;
    }

    void DeleteUnusedPresets(AppState state, Action<string> deleteFile) {
        var root = SafeFiles.Child(Root, "presets");
        if (!Directory.Exists(root)) return;
        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Keep(ProviderId provider, string version) {
            retained.Add(provider + ".ini");
            retained.Add(provider + "-" + SafeFiles.Hash(System.Text.Encoding.UTF8.GetBytes(version))[..16] + ".ini");
        }
        foreach (var install in state.Installations.Where(i => File.Exists(i.Executable))) Keep(install.Provider, install.Version);
        if (state.ActiveProvider is ProviderId.Djules or ProviderId.Elliott) {
            var active = state.AppliedSetup ?? state.Games.FirstOrDefault(g => g.Id == state.ActiveGameId);
            if (active?.Versions.GetValueOrDefault(state.ActiveProvider) is string version) Keep(state.ActiveProvider, version);
            else return; // Unknown active version: preserve presets until the setup is reconciled.
        }
        foreach (var directory in Directory.EnumerateDirectories(root)) {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            SafeFiles.NoLinks(directory);
            foreach (var path in Directory.EnumerateFiles(directory)) {
                var name = Path.GetFileName(path);
                if (!Regex.IsMatch(name, @"^(Djules|Elliott)(-[a-f0-9]{16})?\.ini$") || retained.Contains(name)) continue;
                deleteFile(path);
            }
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
        }
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root, false);
    }
}
