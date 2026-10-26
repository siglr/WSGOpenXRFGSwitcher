using System.Security.Cryptography;
using System.Text;
using Wsg.OpenXR;

static class RemovalTests
{
    public static async Task Run(string root, string vector, Action<bool, string> check, Func<Func<Task>, string, Task> reject) {
        using var key = RSA.Create(2048);
        var releases = new List<CatalogRelease>();
        foreach (var provider in ProviderInfo.All)
        foreach (var version in new[] { "test1", "test2" }) {
            var bytes = Encoding.UTF8.GetBytes(provider.Id + version);
            releases.Add(new() { Provider = provider.Id, Version = version, Repository = provider.Repository,
                Url = "https://github.com/" + provider.Repository + "/releases/download/test/package.zip", Sha256 = SafeFiles.Hash(bytes), Size = bytes.Length,
                SettingsContract = provider.Id == ProviderId.Tig3r ? Tig3rConfiguration.Contract : "",
                Files = new() { ["OFXRBridgeTray.exe"] = SafeFiles.Hash(bytes), ["layer.dll"] = SafeFiles.Hash(bytes) } });
        }
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Write(new CompatibilityCatalog(1, Catalog.Product, releases))));
        var signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes("WSG-CATALOG-v1\n" + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var catalog = new Catalog(Json.Write(new SignedDocument(payload, signature)), key.ExportSubjectPublicKeyInfoPem());

        (Storage store, FakeHost host, AppState state, ProviderManager manager, Installation installation) Setup(string name, ProviderId provider) {
            var store = new Storage(Path.Combine(root, name, "wsg"));
            var host = new FakeHost(store.Root, vector);
            var state = new AppState { Games = [new() { Name = "Fixture game", Provider = provider }] };
            foreach (var version in new[] { "test1", "test2" }) {
                var release = catalog.Get(provider + ":" + version);
                // Imported folder outside WSG's managed storage, still inside the isolated fixture root.
                var directory = Path.Combine(root, name, "imported-" + version); Directory.CreateDirectory(directory);
                foreach (var file in release.Files.Keys) File.WriteAllText(Path.Combine(directory, file), provider + version);
                File.WriteAllText(Path.Combine(directory, "user-notes.txt"), "keep");
                state.Installations.Add(new(provider, version, directory, Path.Combine(directory, release.Executable), release.Key));
            }
            store.AddNote(new(DateTimeOffset.Now, "Fixture game", "game.exe", provider, "test1", new(), false, "runtime", "", "", "keep"));
            store.Save(state);
            return (store, host, state, new(store, catalog, host), state.Installations[0]);
        }
        string Fingerprint(Storage store, FakeHost host) => new SwitchCoordinator(store, host, catalog.Verify).Fingerprint();
        string PrepareInterruptedRemoval(Storage store, Installation installation) {
            var id = Guid.NewGuid().ToString("N");
            var directory = Path.Combine(store.Root, "backups", "removed-" + id);
            var journal = new RemovalJournal(installation, id, new(catalog.Get(installation.CatalogKey).Files));
            foreach (var name in journal.Files.Keys)
                SafeFiles.AtomicWrite(SafeFiles.Child(Path.Combine(directory, "files"), name), installation.Provider + installation.Version);
            SafeFiles.AtomicWrite(Path.Combine(directory, "removal-manifest.json"), Json.Write(journal));
            SafeFiles.AtomicWrite(Path.Combine(store.Root, "removal-recovery.json"), Json.Write(journal));
            return directory;
        }

        foreach (var provider in new[] { ProviderId.Djules, ProviderId.Elliott, ProviderId.Tig3r }) {
            var f = Setup("empty-tree-" + provider, provider);
            f.state.ProviderRoot = Path.Combine(root, "empty-tree-" + provider, "OpenXR FG");
            var destination = ProviderPaths.Destination(f.store, f.state, catalog.Get(f.installation.CatalogKey));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(f.installation.Directory, destination);
            var moved = f.installation with { Directory = destination, Executable = Path.Combine(destination, "OFXRBridgeTray.exe") };
            f.state.Installations[0] = moved;
            File.Delete(Path.Combine(destination, "user-notes.txt"));
            Directory.CreateDirectory(Path.Combine(destination, "external", "OpenXR-SDK", "LICENSES"));
            await f.manager.Remove(moved, f.state, Fingerprint(f.store, f.host), _ => { });
            check(!Directory.Exists(f.state.ProviderRoot), provider + " removal prunes empty nested/version/provider/root folders");
            check(File.Exists(f.state.Installations.Single().Executable), provider + " pruning leaves another imported version untouched");
            check(!Directory.Exists(Path.Combine(f.store.Root, "backups")), provider + " successful removal deletes temporary backups");
            PrepareInterruptedRemoval(f.store, moved);
            await f.manager.RecoverRemoval(f.state);
            check(File.Exists(moved.Executable), provider + " recovery recreates pruned installation folders");
        }
        {
            var f = Setup("empty-import-parent", ProviderId.Djules);
            File.Delete(Path.Combine(f.installation.Directory, "user-notes.txt"));
            await f.manager.Remove(f.installation, f.state, Fingerprint(f.store, f.host), _ => { });
            check(!Directory.Exists(f.installation.Directory) && Directory.Exists(Path.GetDirectoryName(f.installation.Directory)!),
                "Empty imported installation is removed without climbing into its parent");
        }

        foreach (var provider in new[] { ProviderId.Djules, ProviderId.Elliott, ProviderId.Tig3r }) {
            var f = Setup("remove-" + provider, provider);
            var layers = f.host.Layers().ToArray();
            await f.manager.Remove(f.installation, f.state, Fingerprint(f.store, f.host), _ => { });
            check(!File.Exists(f.installation.Executable) && !File.Exists(Path.Combine(f.installation.Directory, "layer.dll")), provider + " removal deletes the verified package files");
            check(File.ReadAllText(Path.Combine(f.installation.Directory, "user-notes.txt")) == "keep", provider + " removal preserves unknown files in imported folders");
            check(f.state.Installations.Count == 1 && File.Exists(f.state.Installations[0].Executable), provider + " other installed versions survive");
            check(f.store.ReadNotes().Count == 1 && f.state.Games[0].Provider == provider, provider + " presets and test notes survive");
            check(f.host.Layers().SequenceEqual(layers), provider + " inactive removal leaves every registration unchanged");
            check(!Directory.Exists(Path.Combine(f.store.Root, "backups")), provider + " completed removal retains no package copy");

            // Simulate interruption after file removal and restart using the durable journal.
            PrepareInterruptedRemoval(f.store, f.installation);
            await f.manager.RecoverRemoval(f.state);
            check(File.Exists(f.installation.Executable) && f.state.Installations.Count == 2 && !f.manager.RemovalRecoveryPending, provider + " interrupted removal restores package and inventory");
            check(!Directory.Exists(Path.Combine(f.store.Root, "backups")), provider + " completed recovery deletes temporary files");
            PrepareInterruptedRemoval(f.store, f.installation);
            File.WriteAllText(f.installation.Executable, "external edit");
            await reject(() => f.manager.RecoverRemoval(f.state), provider + " recovery refuses external file edits");
            check(File.ReadAllText(f.installation.Executable) == "external edit" && f.manager.RemovalRecoveryPending, provider + " recovery preserves edits and journal");
        }
        {
            var f = Setup("remove-busy", ProviderId.Djules); f.host.GameRunning = true;
            await reject(() => f.manager.Remove(f.installation, f.state, Fingerprint(f.store, f.host), _ => { }), "Removal is blocked while a game is running");
            check(File.Exists(f.installation.Executable) && !f.manager.RemovalRecoveryPending, "Blocked removal leaves files and inventory intact");
            f.host.GameRunning = false;
            var fingerprint = Fingerprint(f.store, f.host); File.AppendAllText(f.host.TraySettingsPath, "external=edit");
            await reject(() => f.manager.Remove(f.installation, f.state, fingerprint, _ => { }), "Removal refuses external settings changes");
            File.WriteAllText(f.installation.Executable, "modified binary");
            await reject(() => f.manager.Remove(f.installation, f.state, Fingerprint(f.store, f.host), _ => { }), "Removal refuses modified package files");
        }
        {
            var f = Setup("remove-locked-file", ProviderId.Djules);
            using var locked = File.Open(Path.Combine(f.installation.Directory, "layer.dll"), FileMode.Open, FileAccess.Read, FileShare.Read);
            await reject(() => f.manager.Remove(f.installation, f.state, Fingerprint(f.store, f.host), _ => { }), "Locked package file aborts removal");
            check(File.Exists(f.installation.Executable) && f.state.Installations.Count == 2 && !f.manager.RemovalRecoveryPending, "Failed removal restores already-deleted package files");
        }
        {
            var f = Setup("remove-concurrent-change", ProviderId.Elliott);
            var fingerprint = Fingerprint(f.store, f.host);
            await reject(() => f.manager.Remove(f.installation, f.state, fingerprint, status => {
                if (status.StartsWith("Backing up")) File.AppendAllText(f.host.TraySettingsPath, "external=keep");
            }), "External changes during removal backup abort file deletion");
            check(File.Exists(f.installation.Executable) && File.ReadAllText(f.host.TraySettingsPath).Contains("external=keep"), "Concurrent edit and package files survive aborted removal");
        }
        {
            var f = Setup("remove-stale-registration", ProviderId.Elliott);
            f.host.AddLayer(new("User", 64, OpenXrRegistry.OpenXr, Path.Combine(f.installation.Directory, "layer.json"), 1, "XR_APILAYER_XRFrameBridge", Path.Combine(f.installation.Directory, "layer.dll")));
            await reject(() => f.manager.Remove(f.installation, f.state, Fingerprint(f.store, f.host), _ => { }), "Removal refuses stale registrations even when disabled");
            check(File.Exists(f.installation.Executable), "Registered package is not deleted");
        }
        {
            var f = Setup("remove-active", ProviderId.Djules);
            f.host.ClearVectorLayers();
            f.state.Games[0].Versions[ProviderId.Djules] = "test1";
            var coordinator = new SwitchCoordinator(f.store, f.host, catalog.Verify);
            await coordinator.Apply(f.state, f.state.Games[0], f.installation, coordinator.Fingerprint());
            await f.manager.Remove(f.installation, f.state, coordinator.Fingerprint(), _ => { });
            check(f.host.RunningTray() == null && f.state.ActiveProvider == ProviderId.Off && !f.host.Layers().Any(r => r.Ofxr), "Active OFXR removal closes tray and cleans its registrations before deleting files");
            check(f.host.Layers().Single().LayerName == "Unrelated", "Active removal preserves unrelated layer ordering and state");
        }
        {
            var f = Setup("remove-journal-target", ProviderId.Djules);
            await f.manager.Remove(f.installation, f.state, Fingerprint(f.store, f.host), _ => { });
            var backup = PrepareInterruptedRemoval(f.store, f.installation);
            var journal = Json.Read<RemovalJournal>(File.ReadAllText(Path.Combine(backup, "removal-manifest.json")));
            var redirected = Path.Combine(root, "unrelated-recovery-target");
            journal = journal with { Installation = journal.Installation with { Directory = redirected, Executable = Path.Combine(redirected, "OFXRBridgeTray.exe") } };
            File.WriteAllText(Path.Combine(f.store.Root, "removal-recovery.json"), Json.Write(journal));
            await reject(() => f.manager.RecoverRemoval(f.state), "Removal recovery rejects a redirected destination");
            check(!Directory.Exists(redirected), "Invalid recovery does not create unrelated files");
        }
        {
            var f = Setup("vector-cancelled-setup-recovery", ProviderId.VectorXR);
            f.host.ClearVectorLayers();
            var originalLayer = new LayerRegistration("Machine", 64, OpenXrRegistry.OpenXr,
                Path.Combine(f.installation.Directory, "layer.json"), 0, "XR_APILAYER_DIENERTECH_VECTORXR",
                Path.Combine(f.installation.Directory, "layer.dll"));
            f.host.AddLayer(originalLayer with { Value = 1 });
            var originalConfig = File.ReadAllBytes(f.host.VectorSettingsPath);
            var journal = new VectorInstallJournal(f.installation.CatalogKey, null, originalConfig, null, null, [originalLayer]);
            var journalPath = Path.Combine(f.store.Root, "installation-recovery.json");
            File.WriteAllText(journalPath, Json.Write(journal));
            File.WriteAllText(f.host.VectorSettingsPath, "temporary deactivated settings");
            var unrelated = f.host.Layers().Where(r => !r.Vector).ToArray();
            f.host.GameRunning = true;
            await reject(() => f.manager.RecoverInstallation(f.state, _ => { }), "VectorXR setup recovery waits for games to close");
            check(f.manager.InstallationRecoveryPending && File.ReadAllText(f.host.VectorSettingsPath) == "temporary deactivated settings",
                "Blocked VectorXR recovery preserves its journal and current configuration");
            f.host.GameRunning = false; f.host.Fail = "elevation";
            await reject(() => f.manager.RecoverInstallation(f.state, _ => { }), "Failed VectorXR registration recovery remains retryable");
            check(f.manager.InstallationRecoveryPending && f.host.Layers().Single(r => r.Vector).Value == 1,
                "Failed registration restore retains the setup journal and disabled layer");
            var reopened = new ProviderManager(f.store, catalog, f.host);
            var reloaded = f.store.Load();
            await reopened.RecoverInstallation(reloaded, _ => { });
            check(!reopened.InstallationRecoveryPending && File.ReadAllBytes(f.host.VectorSettingsPath).SequenceEqual(originalConfig),
                "Reopened VectorXR cancellation recovery restores exact settings and clears its journal");
            check(f.host.Layers().Single(r => r.Vector) == originalLayer && unrelated.SequenceEqual(f.host.Layers().Where(r => !r.Vector)),
                "VectorXR cancellation recovery restores enable flags without changing unrelated layers");
            check(!reloaded.Installations.Any(i => i.Provider == ProviderId.VectorXR) && f.store.ReadNotes().Count == 1,
                "Cancelled VectorXR setup does not claim an unverified installation and preserves notes");
            var writes = f.host.Writes;
            await reopened.RecoverInstallation(reloaded, _ => throw new Exception("Unexpected duplicate recovery"));
            check(f.host.Writes == writes, "Completed VectorXR setup recovery is harmless when repeated");
        }
        {
            var f = Setup("vector-removal-completion", ProviderId.VectorXR);
            var journal = new VectorInstallJournal(f.installation.CatalogKey, f.installation, null, null, null, []) { Operation = "Uninstall" };
            File.WriteAllText(Path.Combine(f.store.Root, "installation-recovery.json"), Json.Write(journal));
            check(!f.manager.TryCompleteVectorRemoval(f.state, _ => { }) && f.manager.VectorRemovalPending, "Automatic removal detection waits while the application exists");
            await reject(() => { f.manager.CompleteVectorRemoval(f.state, _ => { }); return Task.CompletedTask; }, "Cancelled or incomplete VectorXR uninstall is not reported as success");
            File.Delete(f.installation.Executable);
            check(!f.manager.TryCompleteVectorRemoval(f.state, _ => { }) && f.manager.VectorRemovalPending, "Automatic removal detection waits for registrations to disappear");
            await reject(() => { f.manager.CompleteVectorRemoval(f.state, _ => { }); return Task.CompletedTask; }, "VectorXR removal verifies layer registration is gone");
            f.host.ClearVectorLayers(); f.host.GameRunning = true;
            check(!f.manager.TryCompleteVectorRemoval(f.state, _ => { }) && f.manager.VectorRemovalPending, "Automatic removal verification defers quietly during a game");
            await reject(() => { f.manager.CompleteVectorRemoval(f.state, _ => { }); return Task.CompletedTask; }, "VectorXR removal completion also requires games closed");
            f.host.GameRunning = false;
            var hostFiles = File.ReadAllText(f.host.TraySettingsPath) + File.ReadAllText(f.host.VectorSettingsPath);
            var hostLayers = f.host.Layers().ToArray();
            var reopened = new ProviderManager(f.store, catalog, f.host);
            var reloadedState = f.store.Load();
            check(reopened.TryCompleteVectorRemoval(reloadedState, _ => { }), "A reopened app automatically completes an already-finished uninstall");
            f.state = reloadedState;
            check(f.host.Writes == 0 && hostLayers.SequenceEqual(f.host.Layers()) && hostFiles == File.ReadAllText(f.host.TraySettingsPath) + File.ReadAllText(f.host.VectorSettingsPath), "Automatic completion never changes provider files or registrations");
            check(!reopened.TryCompleteVectorRemoval(f.state, _ => throw new Exception("Unexpected duplicate notification")), "Repeated automatic checks are harmless after completion");
            check(!f.manager.InstallationRecoveryPending && f.state.Installations.Count == 0 && f.store.ReadNotes().Count == 1, "Verified VectorXR removal clears pending state and preserves notes");
        }
    }
}

