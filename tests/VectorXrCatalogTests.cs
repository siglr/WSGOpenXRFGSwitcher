using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Wsg.OpenXR;

static class VectorXrCatalogTests
{
    // v0.19.3 additions stay under VectorXR's control, including per-profile prediction.
    const string Configuration = """
        {"version":3,"core":{"enabled":true,"osd":{"enabled":true,"showPerformance":false,
        "compactShowPerformance":true,"showFramegen":true,"showFramegenFps":true,
        "bodyOrder":["graph","turbo","framegen","pivot","modules"],
        "rowOrder":["performance","graph","framegen","turbo","pivot","modules"],
        "customPresets":[{"name":"Custom","settings":{"showPerformance":true,"rowOrder":["performance","graph"]}}]}},
        "applications":[{"id":"existing","name":"Game","enabled":true,"match":{"exe":"Game.exe"}}],
        "modules":{"pivotxr":{"enabled":true},"turbo":{"enabled":false},"framegen":{"enabled":true,
        "defaults":{"synthesisMode":"interpolate","flowQuality":"balanced","frameMultiplier":"dynamic",
        "extendHeadPrediction":true,"artifactSuppression":73,"motionLimitPercent":12},
        "profiles":[{"name":"Original","enabled":true,"applicationIds":["existing"],
        "settings":{"synthesisMode":"extrapolate","flowQuality":"quality","frameMultiplier":"2x",
        "extendHeadPrediction":false,"artifactSuppression":64,"motionLimitPercent":9}}]}}}
        """;

    public static async Task Run(string root, Action<bool, string> check, Func<Func<Task>, string, Task> reject) {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var envelope = File.ReadAllText(Path.Combine(repo, "assets/catalog.signed.json"));
        var publicKey = File.ReadAllText(Path.Combine(repo, "assets/trust-public.pem"));
        var catalog = new Catalog(envelope, publicKey);
        var release = catalog.Get("VectorXR:v0.19.3");
        check(catalog.Supports(release) && release.Size == 4113088
            && release.Sha256 == "1dd7b10950e145d702ee383b53b8b5db112c9a5aec323daf8ff6d12077aef5bc",
            "VectorXR 0.19.3 catalog pins the reviewed signed installer without a new client requirement");

        var game = new GamePreset { Name = "Game", Executable = @"C:\Games\Game.exe", Provider = ProviderId.VectorXR };
        game.For(ProviderId.VectorXR).Multiplier = "3x";
        var original = JsonNode.Parse(Configuration)!;
        var patched = JsonNode.Parse(VectorConfiguration.Patch(Configuration, game, true, false))!;
        var profile = patched["modules"]!["framegen"]!["profiles"]![0]!;
        check(profile["settings"]!["frameMultiplier"]!.GetValue<string>() == "3x"
            && !profile["settings"]!["extendHeadPrediction"]!.GetValue<bool>()
            && profile["settings"]!["artifactSuppression"]!.GetValue<int>() == 64,
            "VectorXR preset changes preserve a selected native profile's explicit prediction and advanced tuning");
        check(JsonNode.DeepEquals(original["core"]!["osd"], patched["core"]!["osd"]),
            "VectorXR 0.19.3 performance rows, backward-compatible row orders and custom OSD presets survive Apply");
        check(JsonNode.DeepEquals(original["modules"]!["framegen"]!["defaults"], patched["modules"]!["framegen"]!["defaults"]),
            "VectorXR 0.19.3 global advanced defaults remain unchanged");
        foreach (var keep in new[] { false, true }) {
            var off = JsonNode.Parse(VectorConfiguration.Patch(Configuration, game, false, keep))!;
            check(JsonNode.DeepEquals(original["core"]!["osd"], off["core"]!["osd"])
                && !off["modules"]!["framegen"]!["profiles"]![0]!["settings"]!["extendHeadPrediction"]!.GetValue<bool>(),
                "Disabling VectorXR FG preserves 0.19.3 advanced options, coexistence=" + keep);
        }
        var fresh = JsonNode.Parse(Configuration)!;
        fresh["modules"]!["framegen"]!["profiles"] = new JsonArray();
        var newProfile = JsonNode.Parse(VectorConfiguration.Patch(fresh.ToJsonString(), game, true, false))!;
        check(newProfile["modules"]!["framegen"]!["profiles"]![0]!["settings"]!["extendHeadPrediction"]!.GetValue<bool>(),
            "New VectorXR game profiles copy the user's explicit advanced defaults");

        var fixture = new Fixture(Path.Combine(root, "vectorxr-0193"), Configuration);
        fixture.Game.Provider = ProviderId.VectorXR;
        var coordinator = new SwitchCoordinator(fixture.Storage, fixture.Host, _ => { }, resolveRelease: _ => release);
        await coordinator.Apply(fixture.State, fixture.Game, fixture.Selected, coordinator.Fingerprint());
        var applied = File.ReadAllBytes(fixture.Host.VectorSettingsPath);
        fixture.Game.For(ProviderId.VectorXR).Multiplier = "4x";
        fixture.Host.Fail = "verify";
        await reject(() => coordinator.Apply(fixture.State, fixture.Game, fixture.Selected, coordinator.Fingerprint()),
            "Failed VectorXR 0.19.3 application rolls back");
        check(File.ReadAllBytes(fixture.Host.VectorSettingsPath).SequenceEqual(applied) && !coordinator.RecoveryPending,
            "VectorXR 0.19.3 rollback restores every advanced field byte for byte");

        var marker = Path.Combine(repo, "dist/latest-package.txt");
        if (File.Exists(marker)) CheckReleasedClient(File.ReadAllText(marker).Trim(), envelope, publicKey, check);
        var legacy = Path.Combine(AppContext.BaseDirectory, "review-vector-0193", "released-0.1.2");
        if (Directory.Exists(legacy)) CheckReleasedClient(legacy,
            File.ReadAllText(Path.Combine(repo, "assets/catalog.legacy.signed.json")), publicKey, check);
    }

    static void CheckReleasedClient(string folder, string envelope, string publicKey, Action<bool, string> check) {
        var context = new AssemblyLoadContext("VectorCatalog-" + Guid.NewGuid(), isCollectible: true);
        try {
            var assembly = context.LoadFromAssemblyPath(Path.Combine(folder, "Wsg.OpenXR.Core.dll"));
            var app = context.LoadFromAssemblyPath(Path.Combine(folder, "WSG OpenXR FG Switcher.dll"));
            using var resource = app.GetManifestResourceStream("catalog.signed.json")!;
            using var reader = new StreamReader(resource);
            var catalogType = assembly.GetType("Wsg.OpenXR.Catalog", true)!;
            var baseline = Activator.CreateInstance(catalogType, reader.ReadToEnd(), publicKey)!;
            var updated = catalogType.GetMethod("ReviewUpdate")!.Invoke(baseline, [envelope])!;
            var release = catalogType.GetMethod("Get")!.Invoke(updated, ["VectorXR:v0.19.3"])!;
            var provider = Enum.Parse(assembly.GetType("Wsg.OpenXR.ProviderId", true)!, "VectorXR");
            var latest = catalogType.GetMethod("Latest")!.Invoke(updated, [provider])!;
            check((string)release.GetType().GetProperty("Version")!.GetValue(release)! == "v0.19.3"
                && (string)latest.GetType().GetProperty("Version")!.GetValue(latest)! == "v0.19.3",
                "Released WSG " + assembly.GetName().Version + " accepts the signed VectorXR catalog update and exposes its download");
            var gameType = assembly.GetType("Wsg.OpenXR.GamePreset", true)!;
            var game = Activator.CreateInstance(gameType)!;
            gameType.GetProperty("Executable")!.SetValue(game, @"C:\Games\Game.exe");
            gameType.GetProperty("Name")!.SetValue(game, "Game");
            var patch = assembly.GetType("Wsg.OpenXR.VectorConfiguration", true)!.GetMethod("Patch")!;
            var output = JsonNode.Parse((string)patch.Invoke(null, [Configuration, game, true, false])!)!;
            check(JsonNode.DeepEquals(JsonNode.Parse(Configuration)!["core"]!["osd"], output["core"]!["osd"])
                && !output["modules"]!["framegen"]!["profiles"]![0]!["settings"]!["extendHeadPrediction"]!.GetValue<bool>(),
                "Released WSG " + assembly.GetName().Version + " preserves VectorXR 0.19.3 advanced settings without a rebuild");
        } finally { context.Unload(); }
    }
}
