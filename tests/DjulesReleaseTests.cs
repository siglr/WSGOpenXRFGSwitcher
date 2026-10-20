using Wsg.OpenXR;

static class DjulesReleaseTests
{
    public static async Task Run(string root, Action<bool, string> check, Func<Func<Task>, string, Task> reject) {
        var modern = new CatalogRelease { Provider = ProviderId.Djules, Version = "0.2.14.1", DjulesHybrid = true };
        var old = new CatalogRelease { Provider = ProviderId.Djules, Version = "0.2.13.4" };
        var adapter = new DjulesAdapter();
        var settings = new EngineSettings { Method = "fidelityfx", Hybrid = true, HybridDepthSearch = true, FlowResolution = 25,
            OverlaySize = "small", OverlayLatency = false, OverlayMultiplier = false, OverlayHybridStatus = false };
        var game = new GamePreset { Provider = ProviderId.Djules }; game.Settings[ProviderId.Djules] = settings;
        var text = adapter.PrepareConfiguration("[tray]\ncustom=keep\nexcluded_processes=MyHome.exe\nframes_auto=1\n[other]\nvalue=keep\n", game, true, false, modern);
        check(text.Contains("hybrid=1\r\n") && text.Contains("hybrid_depth_search=1"), "New djules Hybrid and depth use upstream tray keys");
        check(text.Contains("backend=fidelityfx") && text.Contains("nvidia_input_scale=25"), "New djules accepts quarter-resolution FidelityFX");
        check(text.Contains("overlay_size=small") && text.Contains("overlay_latency=0") && text.Contains("overlay_multiplier=0") && text.Contains("overlay_hybrid=0"), "New djules overlay options match the provider schema");
        check(text.Contains("custom=keep") && text.Contains("excluded_processes=MyHome.exe") && text.Contains("[other]\r\nvalue=keep"), "New controls preserve unowned tuning and excluded processes");
        check(text.Contains("frames_auto=0") && text.Contains("frame_generation=ofxr") && text.Contains("motion_vectors=off"), "Djules clears incompatible shared-fork mode selections");
        adapter.ValidateCapabilities(modern, settings);
        settings.Hybrid = false;
        check(adapter.PrepareConfiguration("", game, true, false, modern).Contains("hybrid_depth_search=0"), "Depth search stays off when Hybrid is off");
        settings.Hybrid = true;
        await reject(() => Task.Run(() => adapter.ValidateCapabilities(old, settings)), "Old djules release cannot claim the new capabilities");
        await reject(() => Task.Run(() => adapter.PrepareConfiguration("", game, true, false)), "New settings require reviewed version metadata");
        await reject(() => Task.Run(() => adapter.ValidateCapabilities(modern with { Provider = ProviderId.Elliott }, settings)), "Fork capability mismatch is refused");
        foreach (var invalid in new[] { settings with { FlowResolution = 20 }, settings with { OverlaySize = "huge" } })
            await reject(() => Task.Run(() => adapter.ValidateCapabilities(modern, invalid)), "Invalid modern djules setting is refused");
        await reject(() => Task.FromResult(OfxrConfiguration.Patch("", ProviderId.Elliott, new() { FlowResolution = 25 })), "Unreviewed Elliott quarter-resolution remains refused");
        var legacy = adapter.PrepareConfiguration("[tray]\ncustom=keep\n", new(), true, false, old);
        check(!legacy.Contains("hybrid_depth_search=") && !legacy.Contains("overlay_size=") && legacy.Contains("custom=keep"), "Legacy djules settings are not assigned new controls");
        check(EngineSettings.Defaults(ProviderId.Djules).Method == "fidelityfx" && Json.Read<EngineSettings>("{\"Method\":\"nvidia\"}").Method == "nvidia", "Fresh djules presets use FidelityFX while existing NVIDIA presets retain their choice");
        var roundTrip = Json.Read<EngineSettings>(Json.Write(settings));
        check(roundTrip == settings && roundTrip != settings with { Hybrid = false }, "New settings persist and participate in applied-preset equality");

        var fixture = new Fixture(Path.Combine(root, "djules-modern"), "{\"version\":3,\"core\":{\"enabled\":false},\"modules\":{\"framegen\":{\"enabled\":false,\"profiles\":[]}},\"applications\":[]}");
        var coordinator = new SwitchCoordinator(fixture.Storage, fixture.Host, _ => { }, resolveRelease: _ => modern);
        fixture.Game.Provider = ProviderId.Djules; fixture.Game.Settings[ProviderId.Djules] = settings;
        await coordinator.Apply(fixture.State, fixture.Game, fixture.Selected, coordinator.Fingerprint());
        check(File.ReadAllText(fixture.Host.TraySettingsPath).Contains("hybrid=1"), "Switch transaction passes verified release capabilities into configuration");
        fixture.Game.For(ProviderId.Elliott).Method = "dlss";
        fixture.Game.Provider = ProviderId.Elliott;
        await fixture.Apply();
        check(!File.ReadAllText(fixture.Host.TraySettingsPath).Contains("hybrid_depth_search="), "Switching forks keeps new djules controls out of Elliott's preset");
        fixture.Game.Provider = ProviderId.Djules;
        await coordinator.Apply(fixture.State, fixture.Game, fixture.Selected, coordinator.Fingerprint());
        check(File.ReadAllText(fixture.Host.TraySettingsPath).Contains("nvidia_input_scale=25") && fixture.State.AppliedSetup!.For(ProviderId.Djules).Hybrid,
            "Switching back restores modern djules settings and applied snapshot");
        var before = File.ReadAllText(fixture.Host.TraySettingsPath);
        fixture.Host.Fail = "verify";
        await reject(() => coordinator.Apply(fixture.State, fixture.Game, fixture.Selected, coordinator.Fingerprint()), "New djules transaction still rolls back activation failures");
        check(File.ReadAllText(fixture.Host.TraySettingsPath) == before && !coordinator.RecoveryPending, "Modern-setting rollback preserves the prior configuration");
    }
}
