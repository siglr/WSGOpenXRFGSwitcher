using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Wsg.OpenXR;

static class SetupTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string name, string existing, IntPtr reserved);

    public static async Task Run(string root, Action<bool, string> check, Func<Func<Task>, string, Task> reject) {
        var desktop = new System.Drawing.Rectangle(0, 0, 1920, 1040);
        var minimum = new System.Drawing.Size(1050, 800);
        var placement = new WindowPlacement(80, 90, 1200, 850, true);
        check(placement.Fit(desktop, minimum) == new System.Drawing.Rectangle(80, 90, 1200, 850), "Saved window position and size remain unchanged on the same monitor");
        check((placement with { X = 5000, Y = -3000 }).Fit(desktop, minimum) == new System.Drawing.Rectangle(720, 0, 1200, 850), "Disconnected-monitor position is brought fully onto the remaining monitor");
        check((placement with { Width = 4000, Height = 3000 }).Fit(desktop, minimum) == desktop, "Oversized saved window fits the available desktop");
        check(placement.Fit(new(-1280, 0, 1280, 1024), minimum).X == -1200, "Negative-coordinate secondary monitors remain supported");
        check(placement.Fit(new(0, 0, 800, 600), minimum) == new System.Drawing.Rectangle(0, 0, 800, 600), "Small monitors keep the saved window fully visible");
        check(Json.Read<AppState>(Json.Write(new AppState { Window = placement })).Window == placement, "Position, normal size, and maximized preference persist together");
        var folder = Path.Combine(root, "game-and-folder-fixtures"); Directory.CreateDirectory(folder);
        var actual = Path.Combine(folder, "Xbox.exe"); var alias = Path.Combine(folder, "WindowsApps.exe"); var different = Path.Combine(folder, "Other.exe");
        File.WriteAllText(actual, "fixture"); File.WriteAllText(different, "fixture");
        if (!CreateHardLink(alias, actual, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        check(GameDiscovery.Identity(actual) == GameDiscovery.Identity(alias), "Xbox/WindowsApps hard-link aliases have one game identity");
        check(GameDiscovery.Identity(actual) != GameDiscovery.Identity(different), "Separate installations with identical bytes remain separate games");
        var expectedIdentity = GameDiscovery.Identity(actual);
        var denied = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny);
        var fileInfo = new FileInfo(actual); var permissions = fileInfo.GetAccessControl();
        permissions.AddAccessRule(denied); fileInfo.SetAccessControl(permissions);
        try {
            var contentBlocked = false;
            try { using var stream = File.OpenRead(actual); } catch (UnauthorizedAccessException) { contentBlocked = true; }
            check(contentBlocked, "Store-style fixture blocks executable content reads");
            check(GameDiscovery.Identity(actual) == expectedIdentity && GameDiscovery.Identity(alias) == expectedIdentity,
                "Metadata identity still deduplicates aliases when executable content reads are denied");
        } finally {
            permissions.RemoveAccessRuleSpecific(denied); fileInfo.SetAccessControl(permissions);
        }
        var primary = new GamePreset { Name = "MSFS 2024", Executable = actual, Provider = ProviderId.Djules };
        var duplicate = new GamePreset { Name = "MSFS 2024", Executable = alias, Provider = ProviderId.Elliott };
        duplicate.For(ProviderId.Elliott).Multiplier = "3x";
        var state = new AppState { Games = [primary, duplicate], ActiveGameId = primary.Id, SelectedGameId = duplicate.Id, AppliedSetup = Json.Read<GamePreset>(Json.Write(primary)) };
        check(GameDiscovery.Normalize(state) == 1 && state.Games.Count == 1 && state.Games[0].Id == primary.Id, "Saved alias duplicates collapse while retaining the active preset identity");
        check(state.SelectedGameId == primary.Id && state.AppliedSetup.Id == primary.Id, "Selected game maps to the retained active game");
        check(state.Games[0].For(ProviderId.Elliott).Multiplier == "3x" && state.DuplicateGamePresets.Single().Provider == ProviderId.Elliott, "Duplicate preset settings remain archived and missing fork settings are retained");
        check(GameDiscovery.Normalize(state) == 0 && File.Exists(alias), "Repeated normalization is harmless and never deletes a game alias");
        state.Games.Add(new() { Name = "MSFS 2024", Executable = different });
        check(GameDiscovery.Normalize(state) == 0 && GameDiscovery.Labels(state.Games).Values.Distinct().Count() == 2, "Separate installations keep distinct path-labelled choices");

        var xbox = Path.Combine(folder, "XboxGames", "Microsoft Flight Simulator 2024", "Content", "FlightSimulator2024.exe");
        var windows = Path.Combine(folder, "WindowsApps", "Microsoft.Limitless_fixture", "FlightSimulator2024.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(xbox)!); Directory.CreateDirectory(Path.GetDirectoryName(windows)!);
        File.WriteAllText(xbox, "fixture");
        if (!CreateHardLink(windows, xbox, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var activeAlias = new GamePreset { Name = "MSFS 2024", Executable = windows };
        var xboxPreset = new GamePreset { Name = "MSFS 2024", Executable = xbox };
        var aliases = new AppState { Games = [activeAlias, xboxPreset], ActiveGameId = activeAlias.Id, SelectedGameId = activeAlias.Id };
        check(GameDiscovery.Normalize(aliases) == 1 && aliases.Games.Single().Executable == xbox, "XboxGames content path is displayed even when WindowsApps was active");
        check(aliases.Games[0].Id == activeAlias.Id && aliases.ActiveGameId == activeAlias.Id, "Preferred Xbox path retains active preset identity");
        check(aliases.DuplicateGamePresets.Any(g => g.Id == activeAlias.Id && g.Executable == windows), "Original active alias preset is archived before changing its display path");
        check(GameDiscovery.PreferredPath(new[] { windows, xbox }) == xbox, "Fresh discovery prefers XboxGames regardless of discovery order");

        foreach (var provider in Enum.GetValues<ProviderId>()) {
            var selected = new GamePreset { Name = "Game", Provider = provider }; selected.Versions[provider] = "v1";
            var applied = new AppState { ActiveProvider = provider, AppliedSetup = Json.Read<GamePreset>(Json.Write(selected)) };
            check(SetupSelection.MatchesApplied(applied, selected, true), provider + " unchanged verified selection needs no Apply");
            check(!SetupSelection.MatchesApplied(applied, selected, false), provider + " external change requires Apply again");
            selected.KeepVectorFeatures = true;
            check(!SetupSelection.MatchesApplied(applied, selected, true), provider + " coexistence change requires Apply");
            selected.KeepVectorFeatures = false;
            if (provider != ProviderId.Off) {
                selected.For(provider).Overlay = false;
                check(!SetupSelection.MatchesApplied(applied, selected, true), provider + " edited settings require Apply");
                selected.Settings.Clear(); selected.Versions[provider] = "v2";
                check(!SetupSelection.MatchesApplied(applied, selected, true), provider + " different version requires Apply");
            }
            selected.Id = Guid.NewGuid().ToString("N");
            check(!SetupSelection.MatchesApplied(applied, selected, true), provider + " another game requires its own Apply");
        }

        var store = new Storage(Path.Combine(folder, "data")); state.ProviderRoot = Path.Combine(folder, "My VR Mods");
        ProviderPaths.CheckWritable(state.ProviderRoot);
        check(!Directory.EnumerateFiles(state.ProviderRoot).Any(), "Folder writability check leaves no probe file");
        var release = new CatalogRelease { Provider = ProviderId.Djules, Version = "1.2.3" };
        var destination = ProviderPaths.Destination(store, state, release);
        check(destination == Path.Combine(state.ProviderRoot, "Djules", "1.2.3"), "OFXR installation uses selected root plus fork and version");
        check(ProviderPaths.Destination(store, state, release with { Provider = ProviderId.Elliott }) != destination, "OFXR forks never share an installation folder");
        var vector = release with { Provider = ProviderId.VectorXR };
        check(ProviderPaths.Destination(store, state, vector) == Path.Combine(state.ProviderRoot, "VectorXR"), "Fresh VectorXR installation targets its custom-root subfolder");
        var oldVector = new Installation(ProviderId.VectorXR, "old", Path.Combine(folder, "previous-vector"), "unused", "unused");
        check(ProviderPaths.Destination(store, state, vector, oldVector) == oldVector.Directory, "Changing root does not relocate an installed VectorXR");
        check(ProviderPaths.NsisDirectoryArgument(Path.Combine(state.ProviderRoot, "VectorXR")) == "/D=" + Path.Combine(state.ProviderRoot, "VectorXR"), "NSIS directory argument preserves spaces without unsupported quotes");
        foreach (var bad in new[] { "relative", "C:\\", "\\\\server\\share\\mods", "C:\\Mods\" /S", "C:\\Mods\n/S", "C:\\NUL\\mods" })
            await reject(() => Task.FromResult(ProviderPaths.ValidateRoot(bad)), "Unsafe mod root refused: " + bad.Replace('\n', ' '));
        store.Save(state); check(store.Load().ProviderRoot == state.ProviderRoot, "Mod root persists with app settings");
        check(ProviderPaths.Root(store, new()) == Path.Combine(store.Root, "providers"), "Existing settings retain the original default OFXR location");

        // Cached signed fixture: no network or provider execution.
        byte[] zip;
        using (var memory = new MemoryStream()) {
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true)) {
                using var writer = new StreamWriter(archive.CreateEntry("bundle/OFXRBridgeTray.exe").Open()); writer.Write("fixture-executable");
            }
            zip = memory.ToArray();
        }
        release.Repository = ProviderInfo.All.First(p => p.Id == release.Provider).Repository;
        release.Url = "https://github.com/" + release.Repository + "/releases/download/test/package.zip";
        release.ArchiveRoot = "bundle"; release.Size = zip.Length; release.Sha256 = SafeFiles.Hash(zip);
        release.Files = new() { [release.Executable] = SafeFiles.Hash(Encoding.UTF8.GetBytes("fixture-executable")) };
        using var rsa = RSA.Create(2048);
        var otherFork = release with { Provider = ProviderId.Elliott,
            Repository = ProviderInfo.All.Single(p => p.Id == ProviderId.Elliott).Repository,
            Url = "https://github.com/" + ProviderInfo.All.Single(p => p.Id == ProviderId.Elliott).Repository + "/releases/download/test/package.zip",
            Files = new() { [release.Executable] = SafeFiles.Hash(Encoding.UTF8.GetBytes("other-executable")),
                ["docs/other-release.md"] = SafeFiles.Hash(Encoding.UTF8.GetBytes("other-doc")) } };
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Write(new CompatibilityCatalog(1, Catalog.Product, [release, otherFork]))));
        var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes("WSG-CATALOG-v1\n" + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var catalog = new Catalog(Json.Write(new SignedDocument(payload, signature)), rsa.ExportSubjectPublicKeyInfoPem());
        SafeFiles.AtomicWrite(Path.Combine(store.Root, "downloads", release.Provider + "-" + release.Sha256 + ".zip"), zip);
        var host = new FakeHost(store.Root, "{\"version\":3}");
        var manager = new ProviderManager(store, catalog, host);
        var installed = await manager.Install(release, state, _ => { });
        check(installed.Directory == destination && File.ReadAllText(installed.Executable) == "fixture-executable", "Verified OFXR ZIP installs under the chosen root");
        check(!Directory.Exists(Path.Combine(state.ProviderRoot, ".staging")) && host.Writes == 0, "Custom install cleans staging without changing runtime registrations");
        check(manager.Identify(destination).CatalogKey == release.Key, "Identification skips a nonmatching fork with the same executable name");
        SafeFiles.AtomicWrite(Path.Combine(store.Root, "downloads", release.Provider + "-" + release.Sha256 + ".zip"), zip);
        var reinstalled = await manager.Install(release, state, _ => { });
        check(reinstalled == installed && state.Installations.Count == 1, "Repair of a verified installation reuses its directory with multiple forks in the catalog");
        var otherDirectory = Path.Combine(folder, "arbitrary-folder-name");
        foreach (var pair in otherFork.Files) SafeFiles.AtomicWrite(SafeFiles.Child(otherDirectory, pair.Key), pair.Key == release.Executable ? "other-executable" : "other-doc");
        check(manager.Identify(otherDirectory).Provider == ProviderId.Elliott, "Other fork identity is determined by inventory rather than folder name");
        File.Delete(SafeFiles.Child(otherDirectory, "docs/other-release.md"));
        await reject(() => Task.FromResult(manager.Identify(otherDirectory)), "Incomplete provider inventories remain rejected after checking all forks");
        state.ProviderRoot = Path.Combine(folder, "Next Mods"); store.Save(state);
        check(File.Exists(installed.Executable) && store.Load().Installations.Single().Directory == destination, "Changing the root leaves existing installs tracked and in place");
    }
}
