namespace Wsg.OpenXR;

public sealed record RemovalJournal(Installation Installation, string BackupId, Dictionary<string, string> Files);

public sealed partial class ProviderManager
{
    string RemovalJournalPath => Path.Combine(storage.Root, "removal-recovery.json");
    public bool RemovalRecoveryPending => File.Exists(RemovalJournalPath);
    public bool VectorRemovalPending => InstallationRecoveryPending && Json.Read<VectorInstallJournal>(File.ReadAllText(InstallJournalPath)).Operation == "Uninstall";

    public string RemovalDescription(Installation installation) {
        catalog.Verify(installation);
        if (installation.Provider == ProviderId.VectorXR) {
            Authenticode.RequireDienerTech(Uninstaller(installation));
            return "Open VectorXR’s official uninstaller for:\n" + installation.Directory
                + "\n\nWindows may ask for administrator permission. This removes VectorXR and its OpenXR registration. WSG presets and test notes are kept; no installation backup is retained after completion.";
        }
        return "Remove " + ProviderInfo.NameOf(installation.Provider) + " " + installation.Version + " from:\n" + installation.Directory
            + "\n\nOnly the " + catalog.Get(installation.CatalogKey).Files.Count + " verified package files will be removed. Other installed versions, unrelated files, saved presets and test notes are kept. Temporary recovery files are deleted after completion.";
    }

    static string Uninstaller(Installation installation) => SafeFiles.Child(installation.Directory, "uninstall.exe");

    public async Task Remove(Installation installation, AppState state, string observedFingerprint, Action<string> status) {
        host.RequireIdle(state.Games);
        if (RemovalRecoveryPending || InstallationRecoveryPending || File.Exists(storage.JournalPath)) throw new InvalidOperationException("Use Recover operation before removing a provider.");
        catalog.Verify(installation);
        if (installation.Provider == ProviderId.VectorXR) Authenticode.RequireDienerTech(Uninstaller(installation));
        var coordinator = new SwitchCoordinator(storage, host, catalog.Verify, catalog.ValidateCoexistence, catalog.Get);
        if (coordinator.Fingerprint() != observedFingerprint) throw new InvalidOperationException("Provider settings changed. Use Rescan setup and review before removing.");

        // Capture before cleanup so recovery of an interrupted official uninstall can restore it.
        var vectorJournal = installation.Provider == ProviderId.VectorXR
            ? new VectorInstallJournal(installation.CatalogKey, installation, ReadOptional(host.VectorSettingsPath), ReadOptional(OriginalFg), ReadOptional(LastFg), host.Layers().Where(r => r.Vector).ToList()) { Operation = "Uninstall" } : null;
        var running = host.RunningTray();
        var affectsActive = installation.Provider == ProviderId.VectorXR
            ? host.Layers().Any(r => r.Vector && r.Enabled)
            : string.Equals(running, installation.Executable, StringComparison.OrdinalIgnoreCase);
        if (affectsActive) {
            var cleanup = state.AppliedSetup == null ? new GamePreset { Name = "Provider removal" } : Json.Read<GamePreset>(Json.Write(state.AppliedSetup));
            cleanup.KeepVectorFeatures = false;
            Installation? retained = null;
            if (installation.Provider == ProviderId.VectorXR && ProviderInfo.IsOfxr(cleanup.Provider))
                retained = state.Installations.FirstOrDefault(i => i.Provider == cleanup.Provider && i.Executable.Equals(running, StringComparison.OrdinalIgnoreCase));
            if (retained == null) cleanup.Provider = ProviderId.Off;
            status("Deactivating the provider before removal…");
            await coordinator.Apply(state, cleanup, retained, observedFingerprint);
        }
        host.RequireIdle(state.Games);
        catalog.Verify(installation);
        if (installation.Provider == ProviderId.VectorXR) {
            SafeFiles.AtomicWrite(InstallJournalPath, Json.Write(vectorJournal!));
            status("Complete the official VectorXR uninstaller. WSG will verify removal afterwards.");
            // No scripts, silent switches or forced process termination.
            using var process = ProcessLaunch.StartInstaller(Uninstaller(installation), () => { }, recovery: true, uninstall: true);
            // NSIS can hand off to a temporary copy. Process exit is not proof of removal.
            status("Complete the official uninstaller. WSG will detect removal automatically. If cancelled, use Recover operation.");
            return;
        }
        if (host.Layers().Any(r => r.Ofxr && (IsWithin(r.Manifest, installation.Directory) || IsWithin(r.Library, installation.Directory))))
            throw new InvalidOperationException("This OFXR installation still has a layer registration. Close its tray and use Rescan setup before removing it. No package files were removed.");
        try { await RemoveOfxrFiles(installation, state, status, coordinator.Fingerprint()); }
        finally { storage.CleanupCompletedOperations(); }
    }

    static bool IsWithin(string path, string directory) => Path.IsPathFullyQualified(path)
        && Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public void CompleteVectorRemoval(AppState state, Action<string> status) {
        host.RequireIdle(state.Games);
        if (!VectorRemovalPending) throw new InvalidOperationException("No VectorXR removal is pending.");
        if (!FinishVectorRemoval(state, status))
            throw new IOException("VectorXR is still installed or registered. Finish its official uninstaller; WSG will detect removal automatically. If cancelled, choose Recover operation.");
    }

    // Called on refresh/startup and periodically while idle. Only WSG bookkeeping is written;
    // this never launches processes or changes provider files, settings, or registrations.
    public bool TryCompleteVectorRemoval(AppState state, Action<string> status) {
        if (!VectorRemovalPending || RemovalRecoveryPending || File.Exists(storage.JournalPath)) return false;
        try { host.RequireIdle(state.Games); }
        catch (InvalidOperationException) { return false; }
        return FinishVectorRemoval(state, status);
    }

    bool FinishVectorRemoval(AppState state, Action<string> status) {
        var journal = Json.Read<VectorInstallJournal>(File.ReadAllText(InstallJournalPath));
        var installation = journal.Previous ?? throw new InvalidDataException("Missing removal installation.");
        var release = catalog.Get(journal.RequestedKey);
        if (release.Provider != ProviderId.VectorXR || installation.CatalogKey != release.Key || installation.Provider != ProviderId.VectorXR
            || !SafeFiles.Child(installation.Directory, release.Executable).Equals(installation.Executable, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected VectorXR removal record.");
        if (File.Exists(installation.Executable) || host.Layers().Any(r => r.Vector)) return false;
        state.Installations.RemoveAll(i => i.Provider == ProviderId.VectorXR);
        state.LastAppliedFingerprint = null; storage.Save(state); CompleteInstallJournal();
        status("VectorXR uninstalled. Presets and test notes kept; no installation backup retained.");
        return true;
    }

    async Task RemoveOfxrFiles(Installation installation, AppState state, Action<string> status, string fingerprint) {
        var files = catalog.Get(installation.CatalogKey).Files;
        var backupId = Guid.NewGuid().ToString("N");
        var backup = Path.Combine(storage.Root, "backups", "removed-" + backupId);
        var journal = new RemovalJournal(installation, backupId, new(files));
        SafeFiles.AtomicWrite(Path.Combine(backup, "removal-manifest.json"), Json.Write(journal));
        status("Backing up " + ProviderInfo.NameOf(installation.Provider) + " before removal…");
        foreach (var entry in files) {
            var source = SafeFiles.Child(installation.Directory, entry.Key);
            var bytes = await File.ReadAllBytesAsync(source);
            if (SafeFiles.Hash(bytes) != entry.Value) throw new IOException("The installation changed. Nothing was removed.");
            SafeFiles.AtomicWrite(SafeFiles.Child(Path.Combine(backup, "files"), entry.Key), bytes);
        }
        SafeFiles.AtomicWrite(Path.Combine(backup, "removal-manifest.json"), Json.Write(journal));
        SafeFiles.AtomicWrite(RemovalJournalPath, Json.Write(journal));
        try {
            host.RequireIdle(state.Games); catalog.Verify(installation);
            if (new SwitchCoordinator(storage, host, catalog.Verify).Fingerprint() != fingerprint)
                throw new IOException("Provider settings or registrations changed during backup. No package files were removed.");
            foreach (var entry in files) {
                var path = SafeFiles.Child(installation.Directory, entry.Key);
                if (SafeFiles.HashFile(path) != entry.Value) throw new IOException("A package file changed during removal. Recovery copies were preserved.");
                File.Delete(path);
            }
            state.Installations.RemoveAll(i => i.Executable.Equals(installation.Executable, StringComparison.OrdinalIgnoreCase));
            storage.Save(state); File.Delete(RemovalJournalPath);
            PruneEmptyInstallationFolders(installation, state);
            storage.CleanupCompletedOperations();
            status(ProviderInfo.NameOf(installation.Provider) + " " + installation.Version + " removed. Presets, notes and unrelated files kept; no installation backup retained.");
        } catch {
            await RecoverRemoval(state);
            throw;
        }
    }

    void PruneEmptyInstallationFolders(Installation installation, AppState state) {
        var boundary = Path.GetFullPath(installation.Directory).TrimEnd(Path.DirectorySeparatorChar);
        if (boundary.Length <= (Path.GetPathRoot(boundary)?.Length ?? 0)) return;
        bool RemoveIfEmpty(string path) {
            try {
                SafeFiles.NoLinks(path);
                if (!Directory.Exists(path)) return true;
                if (Directory.EnumerateFileSystemEntries(path).Any()) return false;
                // Non-recursive deletion also refuses files added after the emptiness check.
                Directory.Delete(path, false); return true;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                storage.Log("Kept removal folder: " + path + " (" + ex.Message + ")"); return false;
            }
        }
        void Visit(string path) {
            if (!path.Equals(boundary, StringComparison.OrdinalIgnoreCase) && !IsWithin(path, boundary))
                throw new IOException("Removal cleanup escaped its installation directory.");
            SafeFiles.NoLinks(path);
            if (!Directory.Exists(path)) return;
            foreach (var child in Directory.EnumerateDirectories(path).ToArray()) {
                // Keep links and their targets. Their presence also preserves this directory.
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) Visit(child);
            }
            RemoveIfEmpty(path);
        }
        try {
            Visit(boundary);
            var root = ProviderPaths.Root(storage, state);
            var providerFolder = SafeFiles.Child(root, installation.Provider.ToString());
            // Imported folders have no authority over their parents. Climb only the
            // exact managed root/provider/version layout and never beyond the root.
            if (Path.GetDirectoryName(boundary)?.Equals(providerFolder, StringComparison.OrdinalIgnoreCase) == true
                && (Path.GetFileName(boundary) == installation.Version || Path.GetFileName(boundary).StartsWith(installation.Version + "-repair-", StringComparison.Ordinal))
                && !root.Equals(storage.Root, StringComparison.OrdinalIgnoreCase)) {
                if (RemoveIfEmpty(providerFolder)) RemoveIfEmpty(root);
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            storage.Log("Empty-folder cleanup incomplete: " + ex.Message);
        }
    }

    public Task RecoverRemoval(AppState state) {
        if (!RemovalRecoveryPending) return Task.CompletedTask;
        host.RequireIdle(state.Games);
        var journal = Json.Read<RemovalJournal>(File.ReadAllText(RemovalJournalPath));
        if (!Guid.TryParseExact(journal.BackupId, "N", out _) || !ProviderInfo.IsOfxr(journal.Installation.Provider)) throw new InvalidDataException("Unexpected removal recovery record.");
        var release = catalog.Get(journal.Installation.CatalogKey);
        if (release.Provider != journal.Installation.Provider || release.Version != journal.Installation.Version
            || !SafeFiles.Child(journal.Installation.Directory, release.Executable).Equals(journal.Installation.Executable, StringComparison.OrdinalIgnoreCase)
            || journal.Files.Count != release.Files.Count || journal.Files.Any(p => !release.Files.TryGetValue(p.Key, out var hash) || hash != p.Value))
            throw new InvalidDataException("Removal recovery does not match the signed package inventory.");
        var backup = Path.Combine(storage.Root, "backups", "removed-" + journal.BackupId);
        var manifest = Path.Combine(backup, "removal-manifest.json");
        SafeFiles.NoLinks(manifest);
        if (!File.Exists(manifest) || Json.Write(Json.Read<RemovalJournal>(File.ReadAllText(manifest))) != Json.Write(journal))
            throw new InvalidDataException("Removal recovery does not match its original backup record.");
        foreach (var entry in journal.Files) {
            var destination = SafeFiles.Child(journal.Installation.Directory, entry.Key);
            var current = SafeFiles.HashFile(destination);
            if (current != null && current != entry.Value) throw new IOException("An external edit conflicts with removal recovery. The file and backup were preserved: " + destination);
            if (SafeFiles.HashFile(SafeFiles.Child(Path.Combine(backup, "files"), entry.Key)) != entry.Value) throw new InvalidDataException("Removal backup verification failed.");
        }
        foreach (var entry in journal.Files) {
            var destination = SafeFiles.Child(journal.Installation.Directory, entry.Key);
            if (!File.Exists(destination)) SafeFiles.AtomicWrite(destination, File.ReadAllBytes(SafeFiles.Child(Path.Combine(backup, "files"), entry.Key)));
        }
        catalog.Verify(journal.Installation);
        state.Installations.RemoveAll(i => i.Executable.Equals(journal.Installation.Executable, StringComparison.OrdinalIgnoreCase));
        state.Installations.Add(journal.Installation); storage.Save(state); File.Delete(RemovalJournalPath);
        storage.CleanupCompletedOperations();
        return Task.CompletedTask;
    }
}
