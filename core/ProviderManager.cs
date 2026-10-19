using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Wsg.OpenXR;

public sealed record VectorInstallJournal(string RequestedKey, Installation? Previous, byte[]? Configuration, byte[]? OriginalFramegen, byte[]? LastFramegen, List<LayerRegistration> Registrations)
{
    public string Operation { get; init; } = "Install";
    public string? RequestedDirectory { get; init; }
}
public sealed partial class ProviderManager(Storage storage, Catalog catalog, ISwitchHost host)
{
    public Catalog Catalog => catalog;
    string InstallJournalPath => Path.Combine(storage.Root, "installation-recovery.json");
    public bool InstallationRecoveryPending => File.Exists(InstallJournalPath);
    public IEnumerable<Installation> Discover(AppState state) {
        foreach (var existing in state.Installations) {
            try { catalog.Verify(existing); } catch { continue; }
            yield return existing;
        }
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\OFXRBridge", @"C:\OFXRBridge_elliott" };
        foreach (var layer in host.Layers().Where(r => r.Vector)) {
            var directory = Path.GetDirectoryName(layer.Manifest);
            for (int i = 0; directory != null && i < 4; i++, directory = Path.GetDirectoryName(directory))
                if (catalog.Document.Releases.Where(r => r.Provider == ProviderId.VectorXR).Any(r => File.Exists(SafeFiles.Child(directory, r.Executable)))) folders.Add(directory);
        }
        foreach (var dir in folders) {
            Installation? found = null; try { found = Identify(dir); } catch { }
            if (found != null) yield return found;
        }
    }
    public Installation Identify(string directory) {
        directory = Path.GetFullPath(directory); SafeFiles.NoLinks(directory);
        foreach (var release in catalog.Document.Releases.AsEnumerable().Reverse()) {
            var executable = SafeFiles.Child(directory, release.Executable); if (!File.Exists(executable)) continue;
            var installation = new Installation(release.Provider, release.Version, directory, executable, release.Key);
            // A mismatching candidate is expected while checking multiple forks
            // that use the same executable name. Continue to the next inventory.
            try { catalog.Verify(installation); return installation; } catch (Exception ex) when (ex is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException) { }
        }
        throw new InvalidDataException("This folder does not match a reviewed release. WSG has not changed it. Install a supported version from its provider card.");
    }
    public async Task<Installation> Install(CatalogRelease release, AppState state, Action<string> status, CancellationToken cancellation = default) {
        host.RequireIdle(state.Games);
        if (RemovalRecoveryPending) throw new InvalidOperationException("Use Recover operation to restore the interrupted removal first.");
        if (File.Exists(storage.JournalPath)) throw new InvalidOperationException("Recover the interrupted switch before installing.");
        if (InstallationRecoveryPending) {
            var pending = Json.Read<VectorInstallJournal>(File.ReadAllText(InstallJournalPath));
            if (pending.Operation == "Install" && release.Provider == ProviderId.VectorXR && pending.RequestedKey == release.Key) return await CompleteVectorInstallation(release, state, status);
            throw new InvalidOperationException("Recover the interrupted VectorXR installation first.");
        }
        var downloadDir = Path.Combine(storage.Root, "downloads"); Directory.CreateDirectory(downloadDir);
        var watcher = new SwitchCoordinator(storage, host, catalog.Verify);
        var beforeDownload = watcher.Fingerprint();
        var fileName = release.Provider + "-" + release.Sha256 + (release.Provider == ProviderId.VectorXR ? ".exe" : ".zip");
        var package = Path.Combine(downloadDir, fileName);
        status("Downloading " + ProviderInfo.NameOf(release.Provider) + " " + release.Version + "…");
        if (!File.Exists(package) || SafeFiles.HashFile(package) != release.Sha256) {
            var bytes = await Downloads.Get(new Uri(release.Url), release.Size, cancellation);
            if (bytes.LongLength != release.Size || SafeFiles.Hash(bytes) != release.Sha256) throw new InvalidDataException("Provider download does not match the signed catalog.");
            SafeFiles.AtomicWrite(package, bytes);
        }
        host.RequireIdle(state.Games);
        Installation installation;
        if (watcher.Fingerprint() != beforeDownload) throw new InvalidOperationException("Setup changed while downloading. Rescan setup before installing.");
        if (ProviderAdapters.For(release.Provider).UsesOfficialInstaller) {
            Authenticode.RequireDienerTech(package);
            var vectorConfig = host.VectorSettingsPath;
            var previous = Discover(state).FirstOrDefault(i => i.Provider == ProviderId.VectorXR);
            var destination = previous != null || state.ProviderRoot != null ? ProviderPaths.Destination(storage, state, release, previous) : null;
            var journal = new VectorInstallJournal(release.Key, previous, ReadOptional(vectorConfig), ReadOptional(OriginalFg), ReadOptional(LastFg), host.Layers().Where(r => r.Vector).ToList()) { RequestedDirectory = destination };
            SafeFiles.AtomicWrite(InstallJournalPath, Json.Write(journal));
            status("Complete the official VectorXR installer. Windows may request administrator permission. Close VectorXR afterwards.");
            using var process = ProcessLaunch.StartInstaller(package, CompleteInstallJournal, installDirectory: destination);
            await process.WaitForExitAsync(cancellation);
            if (process.ExitCode != 0) throw new IOException("VectorXR installation was cancelled or failed. Use Recover operation; temporary recovery data is retained until this is resolved.");
            return await CompleteVectorInstallation(release, state, status);
        } else {
            var final = ProviderPaths.Destination(storage, state, release);
            Installation? existing = null;
            if (Directory.Exists(final)) {
                try { existing = Identify(final); }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { final += "-repair-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"); }
            }
            if (existing != null) installation = existing;
            else {
                // Stage beside the destination so a custom root on another drive works.
                var stagingRoot = SafeFiles.Child(ProviderPaths.Root(storage, state), ".staging");
                var staging = SafeFiles.Child(stagingRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
                try {
                    await Task.Run(() => Downloads.Extract(File.ReadAllBytes(package), staging), cancellation);
                    var contents = release.ArchiveRoot.Length == 0 ? staging : SafeFiles.Child(staging, release.ArchiveRoot);
                    var candidate = new Installation(release.Provider, release.Version, contents, SafeFiles.Child(contents, release.Executable), release.Key);
                    catalog.Verify(candidate);
                    Directory.CreateDirectory(Path.GetDirectoryName(final)!); Directory.Move(contents, final);
                    installation = candidate with { Directory = final, Executable = SafeFiles.Child(final, release.Executable) };
                } finally {
                    SafeFiles.NoLinks(staging);
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    if (Directory.Exists(stagingRoot) && !Directory.EnumerateFileSystemEntries(stagingRoot).Any()) Directory.Delete(stagingRoot);
                }
            }
        }
        catalog.Verify(installation);
        state.Installations.RemoveAll(i => i.Provider == installation.Provider && (i.Version == installation.Version || installation.Provider == ProviderId.VectorXR));
        state.Installations.Add(installation); storage.Save(state);
        if (release.Provider == ProviderId.VectorXR) CompleteInstallJournal();
        storage.CleanupCompletedOperations();
        status(ProviderInfo.NameOf(release.Provider) + " " + release.Version + " installed. Apply a setup when games are closed.");
        return installation;
    }
    string OriginalFg => Path.Combine(storage.Root, "vector-framegen-original.json");
    string LastFg => Path.Combine(storage.Root, "vector-framegen-last.json");
    static byte[]? ReadOptional(string path) { SafeFiles.NoLinks(path); return File.Exists(path) ? File.ReadAllBytes(path) : null; }
    static void WriteOptional(string path, byte[]? bytes) { SafeFiles.NoLinks(path); if (bytes == null) { if (File.Exists(path)) File.Delete(path); } else SafeFiles.AtomicWrite(path, bytes); }
    void CompleteInstallJournal() {
        if (!File.Exists(InstallJournalPath)) return;
        File.Delete(InstallJournalPath);
        storage.CleanupCompletedOperations();
    }
    async Task<Installation> CompleteVectorInstallation(CatalogRelease release, AppState state, Action<string> status) {
        host.RequireIdle(state.Games);
        var journal = Json.Read<VectorInstallJournal>(File.ReadAllText(InstallJournalPath));
        if (journal.RequestedKey != release.Key) throw new InvalidDataException("Installation target changed.");
        var installation = Discover(new AppState()).FirstOrDefault(i => i.Provider == ProviderId.VectorXR && i.CatalogKey == release.Key) ?? throw new IOException("Installation is incomplete. Finish the official installer, close VectorXR, and choose Finish setup. Use Recover operation if the installer was cancelled.");
        if (journal.RequestedDirectory != null && !Path.GetFullPath(installation.Directory).TrimEnd('\\').Equals(Path.GetFullPath(journal.RequestedDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new IOException("VectorXR was installed at " + installation.Directory + ", instead of the requested " + journal.RequestedDirectory + ". Use Recover operation before continuing; WSG has not moved its files.");
        if (!File.Exists(host.VectorSettingsPath)) SafeFiles.AtomicWrite(host.VectorSettingsPath, "{\"version\":3,\"core\":{\"enabled\":true,\"logLevel\":\"info\",\"logRetentionFiles\":7,\"trackSeenApps\":true},\"applications\":[],\"modules\":{}}");
        // Installation alone must not enable another provider. Apply setup owns activation.
        await host.SetVector(host.Layers().Where(r => r.Vector && r.Enabled).Select(r => new RegistryChange(r, 1)).ToArray());
        catalog.Verify(installation);
        state.Installations.RemoveAll(i => i.Provider == ProviderId.VectorXR); state.Installations.Add(installation);
        state.LastAppliedFingerprint = null; storage.Save(state); CompleteInstallJournal();
        status("VectorXR " + release.Version + " installed and verified. Select VectorXR and Apply setup to prepare this game's profile.");
        return installation;
    }
    public async Task RecoverInstallation(AppState state, Action<string> status) {
        if (!InstallationRecoveryPending) return;
        host.RequireIdle(state.Games);
        var journal = Json.Read<VectorInstallJournal>(File.ReadAllText(InstallJournalPath));
        if (catalog.Get(journal.RequestedKey).Provider != ProviderId.VectorXR) throw new InvalidDataException("Unexpected installation recovery record.");
        var installed = Discover(new AppState()).FirstOrDefault(i => i.Provider == ProviderId.VectorXR);
        if (journal.Previous != null) {
            var release = catalog.Get(journal.Previous.CatalogKey);
            if (release.Provider != ProviderId.VectorXR) throw new InvalidDataException("Unexpected recovery provider.");
            if (installed?.CatalogKey != journal.Previous.CatalogKey) {
                status("Restoring the previous VectorXR version through its official installer…");
                var package = Path.Combine(storage.Root, "downloads", "VectorXR-" + release.Sha256 + ".exe");
                if (!File.Exists(package) || SafeFiles.HashFile(package) != release.Sha256) {
                    var bytes = await Downloads.Get(new Uri(release.Url), release.Size);
                    if (bytes.LongLength != release.Size || SafeFiles.Hash(bytes) != release.Sha256) throw new InvalidDataException("Recovery installer hash mismatch.");
                    SafeFiles.AtomicWrite(package, bytes);
                }
                Authenticode.RequireDienerTech(package);
                // Recovery must retain its original journal if its installer cannot start.
                using var process = ProcessLaunch.StartInstaller(package, () => { }, recovery: true, installDirectory: journal.Previous.Directory);
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new IOException("Recovery installer did not complete. Originals remain saved.");
                host.RequireIdle(state.Games); catalog.Verify(journal.Previous);
            }
        }
        var changes = new List<RegistryChange>();
        foreach (var current in host.Layers().Where(r => r.Vector)) {
            var old = journal.Registrations.FirstOrDefault(r => r.Identity == current.Identity);
            var desired = old?.Value ?? 1;
            if (desired is not (0 or 1)) throw new InvalidDataException("Invalid recovery flag.");
            if (current.Value != desired) changes.Add(new(current, desired));
        }
        await host.SetVector(changes);
        WriteOptional(host.VectorSettingsPath, journal.Configuration); WriteOptional(OriginalFg, journal.OriginalFramegen); WriteOptional(LastFg, journal.LastFramegen);
        state.Installations.RemoveAll(i => i.Provider == ProviderId.VectorXR);
        state.Installations.AddRange(Discover(new AppState()).Where(i => i.Provider == ProviderId.VectorXR));
        storage.Save(state); CompleteInstallJournal();
        status(journal.Previous == null ? "Installation recovered. Any newly installed VectorXR layer is disabled; its official uninstaller remains available." : "Previous VectorXR version, settings, and enable flags restored.");
    }
    public async Task<List<string>> CheckReleases(CancellationToken cancellation = default) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var output = await Task.WhenAll(ProviderInfo.All.Select(async provider => {
            try {
                var bytes = await Downloads.Get(new Uri("https://api.github.com/repos/" + provider.Repository + "/releases/latest"), 2 * 1024 * 1024, timeout.Token);
                var tag = JsonNode.Parse(bytes)?["tag_name"]?.GetValue<string>();
                var reviewed = catalog.Latest(provider.Id).Version;
                return provider.Name + ": " + (tag == reviewed ? reviewed + " is the reviewed release." : "Upstream " + tag + "; reviewed " + reviewed + ". New versions need a signed WSG catalog update.");
            } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException) { return provider.Name + ": update check unavailable. Installed versions remain usable."; }
        }));
        cancellation.ThrowIfCancellationRequested();
        return output.ToList();
    }
    public void OpenAdvanced(Installation installation) {
        catalog.Verify(installation);
        if (installation.Provider != ProviderId.VectorXR) {
            if (!string.Equals(host.RunningTray(), installation.Executable, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Apply this OFXR provider first, then use its icon in the Windows notification area.");
            throw new InvalidOperationException("Right-click the active OFXR icon in the Windows notification area for advanced settings. Refresh WSG after making changes.");
        }
        Process.Start(new ProcessStartInfo(installation.Executable) { UseShellExecute = false, WorkingDirectory = installation.Directory });
    }
}
public static partial class GameDiscovery
{
    public static List<GamePreset> Discover() {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] roots = [@"C:\MSFS2024", @"C:\XboxGames\Microsoft Flight Simulator 2024\Content", @"C:\Program Files (x86)\Steam\steamapps\common\Limitless"];
        foreach (var root in roots) {
            foreach (var relative in new[] { "FlightSimulator2024.exe", "Content/FlightSimulator2024.exe" }) { var path = Path.Combine(root, relative); if (File.Exists(path)) files.Add(path); }
        }
        using var apps = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        if (apps != null) foreach (var name in apps.GetSubKeyNames().Where(n => n.Contains("Limitless", StringComparison.OrdinalIgnoreCase))) {
            using var key = apps.OpenSubKey(name); if (key?.GetValue("PackageRootFolder") is string dir) { var path = Path.Combine(dir, "FlightSimulator2024.exe"); if (File.Exists(path)) files.Add(path); }
        }
        return files.GroupBy(Identity, StringComparer.OrdinalIgnoreCase).Select(group => PreferredPath(group))
            .Select(path => new GamePreset { Name = "Microsoft Flight Simulator 2024", Executable = path }).ToList();
    }
}
