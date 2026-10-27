using Wsg.OpenXR;

static class DjulesFourXTests
{
    const string Vector = "{\"version\":3,\"core\":{\"enabled\":false},\"modules\":{\"framegen\":{\"enabled\":false,\"profiles\":[]}},\"applications\":[]}";
    public static async Task Run(string root, Action<bool, string> check, Func<Func<Task>, string, Task> reject) {
        var release = new CatalogRelease { Provider = ProviderId.Djules, Version = "0.2.14.3", DjulesHybrid = true,
            DjulesFourX = true, DjulesHudFix = true, MinimumSwitcherVersion = "0.1.4" };
        var previous = release with { Version = "0.2.14.2", DjulesFourX = false, DjulesHudFix = false, MinimumSwitcherVersion = "0.1.1" };
        var adapter = new DjulesAdapter();
        var game = new GamePreset { Provider = ProviderId.Djules, Executable = "game.exe" };
        game.Settings[ProviderId.Djules] = new() { Method = "fidelityfx", Multiplier = "4x", PreferFps = false, HudFix = true };
        const string custom = "[tray]\nframe_multiplier=2\ntriple_frame_gen=0\nhud_fix=0\nmultiplier_hotkey=keep\nadmin_games=0\nexcluded_processes=keep.exe\n[other]\nvalue=keep\n";
        foreach (var backend in new[] { "fidelityfx", "nvidia" })
        foreach (var hybrid in new[] { false, true }) {
            game.For(ProviderId.Djules).Method = backend; game.For(ProviderId.Djules).Hybrid = hybrid;
            var text = adapter.PrepareConfiguration(custom, game, true, false, release);
            check(text.Contains("frame_multiplier=4\r\n") && text.Contains("triple_frame_gen=1\r\n") && text.Contains("deep_pipeline=1\r\n"),
                "4X writes V577's explicit multiplier and required pipeline: " + backend + ", Hybrid=" + hybrid);
            check(text.Contains("hud_fix=1\r\n") && text.Contains("multiplier_hotkey=keep") && text.Contains("admin_games=0")
                && text.Contains("excluded_processes=keep.exe") && text.Contains("value=keep"), "4X/HUD changes preserve native hotkeys and unrelated settings");
        }
        var settings = game.For(ProviderId.Djules);
        await reject(() => Task.Run(() => adapter.ValidateCapabilities(previous, settings)), "Older djules cannot select 4X or HUD fix");
        await reject(() => Task.Run(() => adapter.PrepareConfiguration("", game, true, false)), "4X requires reviewed release metadata");
        await reject(() => Task.Run(() => OfxrConfiguration.Patch("", ProviderId.Elliott, settings, release)), "4X cannot leak into Elliott");
        settings.Multiplier = "2x";
        await reject(() => Task.Run(() => adapter.ValidateCapabilities(previous, settings)), "HUD fix alone requires a supported release");
        foreach (var rate in new[] { "2x", "3x", "4x" }) {
            settings.Multiplier = rate;
            var text = adapter.PrepareConfiguration("[tray]\nframe_multiplier=4\ntriple_frame_gen=1\n", game, true, false, release);
            check(text.Contains("frame_multiplier=" + rate[..1] + "\r\n") && text.Contains("triple_frame_gen=" + (rate == "2x" ? "0" : "1")),
                "Switching from 4X explicitly selects " + rate + " without a stale override");
        }
        var legacy = OfxrConfiguration.Patch("[tray]\nframe_multiplier=4\nhud_fix=1\n", ProviderId.Elliott, new());
        check(legacy.Contains("frame_multiplier=2") && legacy.Contains("hud_fix=0"), "Returning to another fork clears shared 4X/HUD overrides");
        check(!Json.Read<EngineSettings>("{}").HudFix && Json.Read<EngineSettings>(Json.Write(settings)) == settings,
            "HUD remains off for existing presets and new options round-trip");
        var anotherGame = new GamePreset();
        check(anotherGame.For(ProviderId.Djules).Multiplier == "2x" && !anotherGame.For(ProviderId.Djules).HudFix,
            "4X/HUD choices do not change another game's defaults");

        var fixture = new Fixture(Path.Combine(root, "djules-four-x"), Vector);
        var coordinator = new SwitchCoordinator(fixture.Storage, fixture.Host, _ => { }, resolveRelease: _ => release);
        fixture.Game.Provider = ProviderId.Djules; fixture.Game.Settings[ProviderId.Djules] = settings with { Multiplier = "4x", HudFix = true };
        await coordinator.Apply(fixture.State, fixture.Game, fixture.Selected, coordinator.Fingerprint());
        check(fixture.State.AppliedSetup!.For(ProviderId.Djules).Multiplier == "4x"
            && fixture.Host.LastVerifiedRelease?.DjulesFourX == true, "Apply saves 4X and passes signed package metadata into layer verification");
        var startup = LaunchOptions.Parse(["--game", fixture.Game.Id, "--provider", "djules"]).Resolve(fixture.State);
        check(startup.For(ProviderId.Djules).Multiplier == "4x" && startup.For(ProviderId.Djules).HudFix, "Launch scripts retain the game's 4X/HUD choices");
        fixture.Game.Provider = ProviderId.Elliott; await fixture.Apply();
        check(File.ReadAllText(fixture.Host.TraySettingsPath).Contains("frame_multiplier=2"), "Switching away applies the other provider's own multiplier");
        fixture.Game.Provider = ProviderId.Djules;
        await coordinator.Apply(fixture.State, fixture.Game, fixture.Selected, coordinator.Fingerprint());
        var before = File.ReadAllText(fixture.Host.TraySettingsPath);
        fixture.Game.For(ProviderId.Djules).HudFix = false; fixture.Host.Fail = "verify";
        await reject(() => coordinator.Apply(fixture.State, fixture.Game, fixture.Selected, coordinator.Fingerprint()), "4X activation failures recover the prior setup");
        check(File.ReadAllText(fixture.Host.TraySettingsPath) == before && fixture.State.AppliedSetup!.For(ProviderId.Djules).HudFix
            && !coordinator.RecoveryPending, "4X rollback restores byte-exact settings and applied HUD state");

        CheckLayers(root, release, check);
        CheckPreviousClient(check);
        await CheckOfficialPackage(root, check);
    }
    static void CheckPreviousClient(Action<bool, string> check) {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var marker = Path.Combine(repo, "dist/latest-package.txt");
        if (!File.Exists(marker)) return;
        var previousCore = Path.Combine(File.ReadAllText(marker).Trim(), "Wsg.OpenXR.Core.dll");
        if (!File.Exists(previousCore) || System.Reflection.AssemblyName.GetAssemblyName(previousCore).Version != new Version(0, 1, 3, 0)) return;
        var context = new System.Runtime.Loader.AssemblyLoadContext("ReleasedClient013", isCollectible: true);
        try {
            var assembly = context.LoadFromAssemblyPath(previousCore);
            var type = assembly.GetType("Wsg.OpenXR.Catalog", true)!;
            var instance = Activator.CreateInstance(type, File.ReadAllText(Path.Combine(repo, "assets/catalog.signed.json")),
                File.ReadAllText(Path.Combine(repo, "assets/trust-public.pem")))!;
            var refused = false;
            try { type.GetMethod("Get")!.Invoke(instance, ["Djules:0.2.14.3"]); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { refused = true; }
            check(refused, "Released v0.1.3 refuses V577 until its required switcher update");
            var provider = Enum.Parse(assembly.GetType("Wsg.OpenXR.ProviderId", true)!, "Djules");
            var latest = type.GetMethod("Latest")!.Invoke(instance, [provider])!;
            check((string)latest.GetType().GetProperty("Version")!.GetValue(latest)! == "0.2.14.2",
                "Released v0.1.3 keeps its compatible djules download available");
        } finally { context.Unload(); }
    }
    static void CheckLayers(string root, CatalogRelease release, Action<bool, string> check) {
        var folder = Path.Combine(root, "activation-inventory"); Directory.CreateDirectory(folder);
        var x64 = Path.Combine(folder, "cache-x64.dll"); File.WriteAllText(x64, "reviewed x64");
        var x86 = Path.Combine(folder, "cache-x86.dll"); File.WriteAllText(x86, "reviewed x86");
        var vk = Path.Combine(folder, "cache-vulkan.dll"); File.WriteAllText(vk, "reviewed Vulkan");
        release = release with { Files = new() { ["ofxr/XR_APILAYER_XRFrameBridge_diagnostic.dll"] = SafeFiles.HashFile(x64)!,
            ["ofxr_x86/XR_APILAYER_XRFrameBridge_diagnostic.dll"] = SafeFiles.HashFile(x86)!, ["ofxr/OFXR_vulkan_queue_layer.dll"] = SafeFiles.HashFile(vk)! } };
        var install = new Installation(ProviderId.Djules, release.Version, folder, Path.Combine(folder, "tray.exe"), release.Key);
        var main = new LayerRegistration("User", 64, OpenXrRegistry.OpenXr, "main.json", 0, "XR_APILAYER_XRFrameBridge", x64);
        var companion = main with { Manifest = "x86.json", Library = x86 };
        var vulkan = main with { Key = OpenXrRegistry.Vulkan, Manifest = "vk.json", Library = vk };
        check(OfxrActivation.Matches(install, [main, companion, vulkan], release), "Both architectures verify under the shared HKCU registry view");
        check(OfxrActivation.Matches(install, [main], release), "The native tray's optional x86 registration does not invalidate a verified x64 arm");
        var singleArchitecture = release with { Files = new() { ["ofxr/XR_APILAYER_XRFrameBridge_diagnostic.dll"] = SafeFiles.HashFile(x64)! } };
        check(OfxrActivation.Matches(install, [main], singleArchitecture), "Single-architecture provider catalogs still verify normally");
        check(!OfxrActivation.Matches(install, [companion], release), "A companion alone cannot pass activation verification");
        check(!OfxrActivation.Matches(install, [main, main with { Manifest = "duplicate.json" }], release), "Duplicate x64 arms are rejected");
        check(!OfxrActivation.Matches(install, [main, companion, companion with { Manifest = "duplicate.json" }], release), "Duplicate x86 arms are rejected");
        check(!OfxrActivation.Matches(install, [main, companion], release with { Files = new() { ["ofxr/XR_APILAYER_XRFrameBridge_diagnostic.dll"] = SafeFiles.HashFile(x64)! } }),
            "An unreviewed companion cannot pass by being added to an installation folder");
        check(!OfxrActivation.Matches(install, [main, vulkan with { Library = x86 }], release), "Wrong Vulkan cache content is rejected");
        check(!OfxrActivation.Matches(install, [main], release with { Version = "other" }), "Activation metadata must match the selected package identity");
        File.WriteAllText(x86, "stale or changed companion");
        check(!OfxrActivation.Matches(install, [main, companion], release), "A stale or modified x86 cache is rejected");
        check(OfxrActivation.Matches(install, [main, companion with { Value = 1 }], release), "Disabled stale companions do not count as active");
    }
    static async Task CheckOfficialPackage(string root, Action<bool, string> check) {
        var review = Path.Combine(AppContext.BaseDirectory, "review-v577");
        var archive = Path.Combine(review, "OFXR-Bridge-v0.2.14.3.zip");
        if (!File.Exists(archive)) return;
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var catalog = new Catalog(File.ReadAllText(Path.Combine(repo, "assets/catalog.signed.json")), File.ReadAllText(Path.Combine(repo, "assets/trust-public.pem")));
        var release = catalog.Get("Djules:0.2.14.3");
        check(SafeFiles.HashFile(archive) == release.Sha256 && release.Files.Count == 18 && release.MinimumSwitcherVersion == "0.1.4",
            "Real V577 ZIP and complete x64/x86 inventory match the signed catalog");
        var storage = new Storage(Path.Combine(root, "real V577 install"));
        var host = new FakeHost(storage.Root, Vector); var state = new AppState { ProviderRoot = Path.Combine(storage.Root, "mods with spaces") };
        var manager = new ProviderManager(storage, catalog, host);
        void StageDownload() => SafeFiles.AtomicWrite(Path.Combine(storage.Root, "downloads", "Djules-" + release.Sha256 + ".zip"), File.ReadAllBytes(archive));
        StageDownload(); var installed = await manager.Install(release, state, _ => { }); catalog.Verify(installed);
        check(manager.Identify(installed.Directory).CatalogKey == release.Key, "Real V577 extraction and identification include both architecture DLLs");
        StageDownload(); check(await manager.Install(release, state, _ => { }) == installed, "Real V577 verified repair reuses the installed package");
        check(host.RunningTray() == null && host.Writes == 0, "Real-package validation never executes upstream binaries or changes registrations");
        await manager.Remove(installed, state, new SwitchCoordinator(storage, host, catalog.Verify).Fingerprint(), _ => { });
        check(!Directory.Exists(installed.Directory) && !manager.RemovalRecoveryPending && state.Installations.Count == 0,
            "Real V577 removal deletes its x86 DLL, prunes empty folders and clears recovery state");
        check(!Directory.Exists(Path.Combine(storage.Root, "downloads")), "Real V577 install/repair/removal leaves no cached download");
    }
}
