using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Wsg.OpenXR;

static class Tig3rTests
{
    public static async Task Run(string root, Action<bool, string> check, Func<Func<Task>, string, Task> reject) {
        var adapter = new Tig3rAdapter();
        var release = new CatalogRelease { Provider = ProviderId.Tig3r, Version = "0.3.0-beta-v663", SettingsContract = Tig3rConfiguration.Contract, NativeDlss = true };
        var game = new GamePreset { Provider = ProviderId.Tig3r, Executable = @"C:\XboxGames\MSFS\FlightSimulator2024.exe" };
        var settings = game.For(ProviderId.Tig3r);
        check((int)ProviderId.VectorXR == 3 && (int)ProviderId.Tig3r == 4, "Provider enum preserves all existing stored identities");
        check(settings.Method == "ofxr_new" && settings.FlowResolution == 50 && settings.Quality == "medium" && !settings.LowerVram
            && settings.Multiplier == "2x" && settings.OverlayPosition == "elliott_bottom", "New tig3rmast3r presets match v663 defaults");
        var existing = "[ofxr]\nnr_enabled=1\nafw_only=1\ncustom=keep\n[route.ofxr_new]\npreset=2\nscale=0\nprefer_fps=0\nbackward=1\n[route.ofxr_djules]\npreset=0\ntriple_frame_gen=1\n[dlssg]\npreset=3\n[other]\ncustom=keep\n";
        foreach (var route in Tig3rConfiguration.Routes) {
            var current = EngineSettings.Defaults(ProviderId.Tig3r) with { Method = route, Quality = "fast", FlowResolution = 25, LowerVram = true,
                Multiplier = Tig3rConfiguration.SupportsThreeX(route) ? "3x" : "2x", OverlayPosition = "elliott_controller" };
            adapter.ValidateCapabilities(release, current);
            game.Settings[ProviderId.Tig3r] = current;
            var text = adapter.PrepareConfiguration(existing, game, true, false, release);
            check(text.Contains("nr_enabled=1") && text.Contains("afw_only=1") && text.Contains("[other]\r\ncustom=keep") && text.Contains("[dlssg]\r\npreset=3"),
                route + " preserves advanced, unknown and DLSS-G model settings");
            check(text.Contains("overlay_position=5") && text.Contains("scale=3") && text.Contains("separate_synthesis=1") && text.Contains("frame_generation="),
                route + " writes native numeric controls and selected preference bank");
            if (!Tig3rConfiguration.SupportsThreeX(route))
                await reject(() => Task.Run(() => adapter.ValidateCapabilities(release, current with { Multiplier = "3x" })), route + " refuses unsupported 3X");
        }
        game.Settings[ProviderId.Tig3r] = settings;
        var patched = adapter.PrepareConfiguration(existing, game, true, false, release);
        check(patched.Contains("[route.ofxr_djules]\r\npreset=0\r\ntriple_frame_gen=1") && patched.Contains("prefer_fps=0") && patched.Contains("backward=1"),
            "Active-bank patch preserves inactive modes and unowned active-bank tuning");
        await reject(() => Task.Run(() => adapter.ValidateCapabilities(release with { SettingsContract = "future-format" }, settings)), "Unknown native settings contracts are refused");
        await reject(() => Task.Run(() => adapter.ValidateCapabilities(release with { Provider = ProviderId.Djules }, settings)), "Tig3r adapter refuses another fork's metadata");
        await reject(() => Task.Run(() => Tig3rConfiguration.Validate(settings with { OverlayPosition = "arbitrary" })), "Invalid native overlay values are refused");
        game.For(ProviderId.Tig3r).Quality = "slow"; game.SelectTig3rRoute("ofxr_djules");
        game.For(ProviderId.Tig3r).Multiplier = "3x"; game.SelectTig3rRoute("ofxr_new");
        check(game.For(ProviderId.Tig3r).Quality == "slow" && game.For(ProviderId.Tig3r).Multiplier == "2x", "Mode changes restore the outgoing mode's saved settings");
        game = Json.Read<GamePreset>(Json.Write(game)); game.SelectTig3rRoute("ofxr_djules");
        check(game.For(ProviderId.Tig3r).Multiplier == "3x", "Inactive mode preferences survive JSON persistence");
        var nativePath = Tig3rConfiguration.GamePath(Path.Combine(root, "native", "ofxr-bridge.ini"), game.Executable);
        check(Path.GetFileName(nativePath) == "FlightSimulator2024.exe.ini" && Path.GetFileName(Path.GetDirectoryName(nativePath)) == "Profiles", "Native profiles use the documented executable basename");
        await reject(() => Task.FromResult(Tig3rConfiguration.GamePath(Path.Combine(root, "native.ini"), "")), "Empty game identity cannot select a native profile");

        Fixture NewFixture(string name) {
            var fixture = new Fixture(Path.Combine(root, name), "{\"version\":3,\"core\":{\"enabled\":false},\"modules\":{\"framegen\":{\"enabled\":false,\"profiles\":[]}},\"applications\":[]}");
            fixture.Game.Provider = ProviderId.Tig3r;
            ISwitchHost host = fixture.Host;
            fixture.Host.BeforeStart = executable => {
                if (!executable.EndsWith("Tig3r.exe", StringComparison.Ordinal)) return;
                var text = File.ReadAllText(host.Tig3rSettingsPath);
                SafeFiles.AtomicWrite(host.Tig3rSettingsPath, OfxrConfiguration.PatchSection(text, "tray", new() { ["pacing"] = "auto", ["pause_hotkey"] = "ctrl+alt+f7" }));
            };
            return fixture;
        }
        foreach (var target in Enum.GetValues<ProviderId>()) {
            var fixture = NewFixture("tig3r-to-" + target);
            ISwitchHost host = fixture.Host;
            SafeFiles.AtomicWrite(host.Tig3rSettingsPath, existing);
            await fixture.Apply();
            var profile = host.Tig3rGameSettingsPath(fixture.Game);
            check(File.ReadAllText(profile).Contains("backend=2") && !File.ReadAllText(host.TraySettingsPath).Contains("[route."), "Tig3r writes its own game profile, without contaminating legacy tray.ini");
            fixture.Game.Provider = target;
            await fixture.Apply();
            check(fixture.State.ActiveProvider == target && !fixture.Coordinator.RecoveryPending && File.Exists(profile), "Tig3r -> " + target + " commits and preserves native game tuning");
        }
        foreach (var failure in new[] { "start", "verify" }) {
            var fixture = NewFixture("native-failure-" + failure);
            ISwitchHost host = fixture.Host;
            fixture.Host.Fail = failure;
            await reject(fixture.Apply, "Tig3r " + failure + " failure rolls back even after its tray rewrites global configuration");
            check(!File.Exists(host.Tig3rSettingsPath) && !File.Exists(host.Tig3rGameSettingsPath(fixture.Game)) && !fixture.Coordinator.RecoveryPending,
                "Failed first native activation removes only the newly created settings and clears recovery");
        }
        {
            var fixture = NewFixture("rollback-to-native"); ISwitchHost host = fixture.Host;
            await fixture.Apply();
            var original = File.ReadAllText(host.Tig3rGameSettingsPath(fixture.Game));
            fixture.Game.Provider = ProviderId.Elliott; fixture.Host.Fail = "verify";
            await reject(fixture.Apply, "A failed switch away from Tig3r restarts its previous tray");
            check(fixture.State.ActiveProvider == ProviderId.Tig3r && fixture.Host.RunningTray()!.EndsWith("Tig3r.exe")
                && File.ReadAllText(host.Tig3rGameSettingsPath(fixture.Game)) == original && !fixture.Coordinator.RecoveryPending,
                "Cross-store rollback preserves the prior native game profile and activation");
        }
        {
            var fixture = NewFixture("native-existing-failure"); ISwitchHost host = fixture.Host;
            SafeFiles.AtomicWrite(host.Tig3rSettingsPath, existing);
            SafeFiles.AtomicWrite(host.Tig3rGameSettingsPath(fixture.Game), existing);
            fixture.Host.Fail = "verify";
            await reject(fixture.Apply, "Existing native configuration can recover a verification failure");
            check(File.ReadAllText(host.Tig3rSettingsPath) == existing && File.ReadAllText(host.Tig3rGameSettingsPath(fixture.Game)) == existing,
                "Recovery restores byte-exact existing global and per-game settings");
            var observed = fixture.Coordinator.Fingerprint();
            File.AppendAllText(host.Tig3rGameSettingsPath(fixture.Game), "; native editor change\n");
            await reject(() => fixture.Coordinator.Apply(fixture.State, fixture.Game, fixture.Selected, observed), "External native profile edits require a rescan");
            fixture.Host.SettingsEditorOpen = true;
            await reject(fixture.Apply, "An open native settings editor blocks switching");
        }
        {
            var fixture = NewFixture("native-flush"); ISwitchHost host = fixture.Host;
            SafeFiles.AtomicWrite(host.Tig3rGameSettingsPath(fixture.Game), existing);
            fixture.Host.BeforeStop = () => File.AppendAllText(host.Tig3rGameSettingsPath(fixture.Game), "flushed=keep\n");
            await fixture.Apply(); fixture.Host.BeforeStop = null;
            check(File.ReadAllText(host.Tig3rGameSettingsPath(fixture.Game)).Contains("flushed=keep"), "Apply rebases on settings flushed by the closing tray");
            var saved = fixture.State.AppliedSetup!;
            check(SetupSelection.MatchesApplied(fixture.State, fixture.Game, true), "A native applied snapshot compares by settings values");
            fixture.Game.For(ProviderId.Tig3r).Quality = "fast";
            check(!SetupSelection.MatchesApplied(fixture.State, fixture.Game, true) && saved.For(ProviderId.Tig3r).Quality == "medium", "Native setting edits invalidate Applied without modifying its snapshot");
        }
        var tags = Encoding.UTF8.GetBytes("[{\"tag_name\":\"old\",\"published_at\":\"2026-09-01T00:00:00Z\"},{\"tag_name\":\"beta\",\"prerelease\":true,\"published_at\":\"2026-10-25T00:00:00Z\"},{\"tag_name\":\"draft\",\"draft\":true,\"published_at\":\"2026-10-26T00:00:00Z\"}]");
        check(ProviderManager.ReleaseTag(ProviderId.Tig3r, tags) == "beta", "New-provider update checks include published betas and exclude drafts");
        foreach (var name in new[] { "tig3r", "tig3rmast3r" }) {
            var options = LaunchOptions.Parse(["--game", game.Id, "--provider", name, "--tray"]);
            check(options.Minimized && options.Resolve(new AppState { Games = [game] }).Provider == ProviderId.Tig3r,
                "Launch scripts accept " + name + " with the existing tray switch");
        }
        CheckCatalogFeeds(check);
    }
    static void CheckCatalogFeeds(Action<bool, string> check) {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var key = File.ReadAllText(Path.Combine(repo, "assets/trust-public.pem"));
        var modern = new Catalog(File.ReadAllText(Path.Combine(repo, "assets/catalog.signed.json")), key);
        var legacyText = File.ReadAllText(Path.Combine(repo, "assets/catalog.legacy.signed.json"));
        var legacy = new Catalog(legacyText, key);
        check(modern.Latest(ProviderId.Tig3r).SettingsContract == Tig3rConfiguration.Contract && modern.Get("Djules:0.2.14.2").Provider == ProviderId.Djules,
            "New signed catalog offers Tig3r and preserves the latest djules release");
        check(legacy.Document.Releases.All(r => r.Provider != ProviderId.Tig3r)
            && legacy.Document.Releases.All(r => modern.Get(r.Key).Sha256 == r.Sha256), "Legacy feed excludes unknown providers and preserves all package identities");
        var marker = Path.Combine(repo, "dist/latest-package.txt");
        if (!File.Exists(marker)) return;
        var oldCore = Path.Combine(File.ReadAllText(marker).Trim(), "Wsg.OpenXR.Core.dll");
        if (!File.Exists(oldCore) || AssemblyName.GetAssemblyName(oldCore).Version! > new Version(0, 1, 2, 0)) return;
        var context = new AssemblyLoadContext("ReleasedClient012", isCollectible: true);
        try {
            var assembly = context.LoadFromAssemblyPath(oldCore);
            var type = assembly.GetType("Wsg.OpenXR.Catalog", throwOnError: true)!;
            var instance = Activator.CreateInstance(type, legacyText, key)!;
            var found = type.GetMethod("Get")!.Invoke(instance, ["Djules:0.2.14.2"]);
            check(found != null, "The actual released v0.1.2 core accepts the new legacy feed");
        } finally { context.Unload(); }
    }
}
