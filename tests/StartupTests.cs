using System.Security.Cryptography;
using System.Text;
using Wsg.OpenXR;

static class StartupTests
{
    public static async Task Run(string root, string vector, Action<bool, string> check, Func<Func<Task>, string, Task> reject) {
        check(!LaunchOptions.Parse([]).ApplyOnce && !LaunchOptions.Parse([]).Minimized, "Normal startup retains interactive defaults");
        foreach (var flag in new[] { "--tray", "--minimized", "--start-minimized" }) {
            check(LaunchOptions.Parse([flag]).Minimized && !LaunchOptions.Parse([flag]).ApplyOnce, flag + " independently starts in the tray");
            var oneShot = LaunchOptions.Parse(["--apply-default", flag]);
            check(oneShot.ApplyOnce && oneShot.Minimized, flag + " is harmless for a one-shot command");
        }
        foreach (var provider in new[] { "off", "djules", "elliott", "vectorxr" })
            check(LaunchOptions.Parse(["--game", "My Game", "--provider", provider]).ApplyOnce, "Explicit startup provider parsed: " + provider);
        string[][] invalid = [["--game"], ["--game", "x"], ["--provider", "off"], ["--provider", "unknown", "--game", "x"],
            ["--apply-default", "--game", "x", "--provider", "off"], ["--minimized", "--start-minimized"],
            ["--tray", "--minimized"], ["--start-minimized", "--tray"], ["--tray", "--tray"], ["--result", "relative.json"],
            ["--result", Path.Combine(root, "result.json")], ["--unknown"], ["--apply-default", "--apply-default"]];
        foreach (var args in invalid) await reject(() => Task.FromResult(LaunchOptions.Parse(args)), "Invalid startup arguments rejected: " + string.Join(' ', args));
        var state = Json.Read<AppState>("{}");
        check(!state.Startup.ApplyOnLaunch && !state.Startup.StartMinimized, "Older settings never opt into automatic changes or minimizing");
        var game = new GamePreset { Name = "Game", Executable = Path.Combine(root, "Game.exe"), Provider = ProviderId.Elliott };
        game.Versions[ProviderId.Djules] = "test"; game.For(ProviderId.Djules).Multiplier = "3x";
        state.Games = [game]; state.Startup = new() { GameId = game.Id, Provider = ProviderId.Djules, StartMinimized = true };
        var preset = LaunchOptions.Parse(["--apply-default"]).Resolve(state);
        check(preset.Provider == ProviderId.Djules && game.Provider == ProviderId.Elliott && preset.For(ProviderId.Djules).Multiplier == "3x", "Startup target uses its saved settings independently of the last selected tab");
        preset.For(ProviderId.Djules).Multiplier = "2x";
        check(game.For(ProviderId.Djules).Multiplier == "3x", "Resolving a startup command does not mutate saved controls");
        check(Json.Read<AppState>(Json.Write(state)).Startup == state.Startup, "Default target and independent startup options persist");
        foreach (var selector in new[] { game.Id, game.Name, game.Executable })
            check(LaunchOptions.Parse(["--game", selector, "--provider", "off"]).Resolve(state).Id == game.Id, "Explicit game resolves by ID, name or path: " + selector);
        state.Games.Add(new() { Name = game.Name, Executable = Path.Combine(root, "Another.exe") });
        await reject(() => Task.FromResult(new LaunchOptions(Game: "Game", Provider: ProviderId.Off).Resolve(state)), "Ambiguous game names require ID or path");
        state.Startup.GameId = "missing";
        await reject(() => Task.FromResult(new LaunchOptions(ApplyDefault: true).Resolve(state)), "Deleted default never falls back to a different game");
        var duplicate = new GamePreset { Name = "Alias", Executable = game.Executable };
        state.Games.Add(duplicate); state.Startup.GameId = duplicate.Id;
        GameDiscovery.Normalize(state);
        check(state.Games.Any(g => g.Id == state.Startup.GameId && g.Executable == game.Executable), "Game normalization preserves the startup target");
        foreach (var visible in new[] { false, true }) foreach (var busy in new[] { false, true }) foreach (var removal in new[] { false, true }) {
            var expected = busy ? BackgroundCheckMode.Stopped : visible ? BackgroundCheckMode.VisibleStatus : removal ? BackgroundCheckMode.PendingRemoval : BackgroundCheckMode.Stopped;
            check(BackgroundMonitoring.Mode(visible, busy, removal) == expected, $"Background policy visible={visible}, busy={busy}, removal={removal}");
        }

        using var key = RSA.Create(2048);
        var bytes = Encoding.UTF8.GetBytes("harmless fixture, not an executable");
        var release = new CatalogRelease { Provider = ProviderId.Djules, Version = "test", Repository = ProviderInfo.All[0].Repository,
            Url = "https://github.com/" + ProviderInfo.All[0].Repository + "/releases/download/test/package.zip", Sha256 = SafeFiles.Hash(bytes), Size = bytes.Length,
            Files = new() { ["OFXRBridgeTray.exe"] = SafeFiles.Hash(bytes) } };
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Write(new CompatibilityCatalog(1, Catalog.Product, [release]))));
        var signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes("WSG-CATALOG-v1\n" + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        var catalog = new Catalog(Json.Write(new SignedDocument(payload, signature)), key.ExportSubjectPublicKeyInfoPem());
        var store = new Storage(Path.Combine(root, "startup"));
        var host = new FakeHost(store.Root, vector);
        var target = new GamePreset { Name = "Fixture", Executable = Path.Combine(store.Root, "game.exe"), Provider = ProviderId.Off };
        state = new() { Games = [target] };
        await StartupLaunch.Apply(store, state, host, catalog, target);
        check(state.AppliedSetup?.Provider == ProviderId.Off && !File.Exists(store.JournalPath), "One-shot Off commits and clears its transaction");
        var unchanged = host.Layers().Single(r => r.LayerName == "Unrelated");
        File.WriteAllBytes(target.Executable, bytes); target.Provider = ProviderId.Djules; target.Versions[ProviderId.Djules] = "test";
        await reject(() => StartupLaunch.Apply(store, state, host, catalog, target), "Startup refuses a missing saved provider version");
        var dir = Path.Combine(store.Root, "provider"); Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, release.Executable), bytes);
        state.Installations.Add(new(ProviderId.Djules, "test", dir, Path.Combine(dir, release.Executable), release.Key));
        await StartupLaunch.Apply(store, state, host, catalog, target);
        check(host.RunningTray() == state.Installations[0].Executable && state.ActiveProvider == ProviderId.Djules, "One-shot apply leaves the requested provider active for the launcher");
        check(host.Layers().Single(r => r.LayerName == "Unrelated") == unchanged, "Startup leaves unrelated layers unchanged");
        var writes = host.Writes; host.GameRunning = true;
        await reject(() => StartupLaunch.Apply(store, state, host, catalog, target), "Running game blocks automatic apply");
        check(host.Writes == writes, "Busy refusal makes no registry changes"); host.GameRunning = false;
        File.AppendAllText(host.TraySettingsPath, "\nexternal=edit\n");
        await reject(() => StartupLaunch.Apply(store, state, host, catalog, target), "Unreviewed external settings changes block startup");
        state.LastReviewedFingerprint = new SwitchCoordinator(store, host, catalog.Verify).Fingerprint();
        await StartupLaunch.Apply(store, state, host, catalog, target);
        check(state.LastAppliedFingerprint == new SwitchCoordinator(store, host, catalog.Verify).Fingerprint(), "An explicitly rescanned setup can be applied automatically");
        File.WriteAllText(store.JournalPath, "pending");
        await reject(() => StartupLaunch.Apply(store, state, host, catalog, target), "Pending recovery blocks automatic apply");
        File.Delete(store.JournalPath);
        host.Fail = "start";
        await reject(() => StartupLaunch.Apply(store, state, host, catalog, target), "Failed startup activation reports failure");
        check(!File.Exists(store.JournalPath) && host.RunningTray() == state.Installations[0].Executable, "Failed automatic apply rolls back the previous setup");
    }
}
