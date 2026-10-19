using System.Text.Json.Nodes;
using Wsg.OpenXR;

static class SettingsCompatibilityTests
{
    public static async Task Run(Action<bool, string> check, Func<Func<Task>, string, Task> reject) {
        foreach (var provider in new[] { ProviderId.Djules, ProviderId.Elliott }) {
            foreach (var position in new[] { "upper_left", "upper_right", "lower_left", "lower_right" }) {
                var text = OfxrConfiguration.Patch("[tray]\ncustom=keep\n", provider, new() { OverlayPosition = position });
                check(text.Contains("overlay_position=" + position) && text.Contains("custom=keep"), provider + " saves " + position + " overlay without changing unrelated settings");
            }
            check(OfxrConfiguration.Patch("", provider, new() { Overlay = false, OverlayPosition = "lower_left" }).Contains("overlay_position=off"), provider + " overlay Off remains independent of its remembered corner");
            await reject(() => Task.FromResult(OfxrConfiguration.Patch("", provider, new() { OverlayPosition = "invalid" })), provider + " rejects unknown overlay positions");
        }
        check(Json.Read<EngineSettings>("{\"overlay\":false}").Overlay == false && Json.Read<EngineSettings>("{}").OverlayPosition == "upper_right",
            "Old overlay checkbox presets retain their Off or upper-right behavior");
        var automatic = OfxrConfiguration.Patch("", ProviderId.Elliott, new() { Multiplier = "auto", PreferFps = false });
        check(automatic.Contains("frames_auto=1") && automatic.Contains("deep_pipeline=1"), "Elliott Auto retains its required pipeline depth");
        await reject(() => Task.FromResult(OfxrConfiguration.Patch("", ProviderId.Djules, new() { Multiplier = "auto" })), "Djules refuses unsupported Auto multiplier");

        var game = new GamePreset { Name = "Fixture", Executable = @"C:\Game\game.exe", Provider = ProviderId.VectorXR };
        var original = """{"version":3,"core":{"enabled":true},"applications":[{"id":"game","enabled":true,"match":{"exe":"game.exe"}}],"modules":{"framegen":{"enabled":false,"defaults":{"motionEstimation":"hardware","artifactSuppression":81,"motionLimitPercent":17},"profiles":[]},"turbo":{"enabled":false,"profiles":[{"enabled":true,"applicationIds":["game"]}]}}}""";
        var patched = JsonNode.Parse(VectorConfiguration.Patch(original, game, true, false))!;
        var profile = patched["modules"]!["framegen"]!["profiles"]![0]!;
        check(profile["settings"]!["motionEstimation"]!.GetValue<string>() == "hardware" && profile["settings"]!["artifactSuppression"]!.GetValue<int>() == 81,
            "New VectorXR FG profiles inherit advanced provider defaults");
        profile["settings"]!["motionLimitPercent"] = 22;
        var reapplied = JsonNode.Parse(VectorConfiguration.Patch(patched.ToJsonString(), game, true, false))!;
        check(reapplied["modules"]!["framegen"]!["profiles"]![0]!["settings"]!["motionLimitPercent"]!.GetValue<int>() == 22,
            "Reapplying keeps native VectorXR tuning that WSG does not expose");
        var merged = VectorSnapshots.PreserveManagedProfiles(new JsonObject { ["profiles"] = new JsonArray() }, reapplied["modules"]!["framegen"]!.AsObject());
        check(merged["profiles"]![0]!["settings"]!["motionLimitPercent"]!.GetValue<int>() == 22 && !merged["profiles"]![0]!["enabled"]!.GetValue<bool>(),
            "Saved managed profile retains advanced tuning with FG disabled");
        var native = JsonNode.Parse(original)!;
        native["core"]!["osd"] = new JsonObject { ["showFramegen"] = false, ["showFramegenFps"] = false, ["horizontalDegrees"] = -20 };
        native["modules"]!["framegen"]!["profiles"]!.AsArray().Add(new JsonObject {
            ["name"] = "Native game tuning", ["enabled"] = true, ["applicationIds"] = new JsonArray("game"),
            ["settings"] = new JsonObject { ["artifactSuppression"] = 88 } });
        var inheritedNative = JsonNode.Parse(VectorConfiguration.Patch(native.ToJsonString(), game, true, false))!;
        check(inheritedNative["modules"]!["framegen"]!["profiles"]![0]!["settings"]!["artifactSuppression"]!.GetValue<int>() == 88,
            "First WSG VectorXR profile inherits matching native-profile tuning");
        check(!inheritedNative["core"]!["osd"]!["showFramegen"]!.GetValue<bool>() && inheritedNative["core"]!["osd"]!["horizontalDegrees"]!.GetValue<int>() == -20,
            "VectorXR overlay detail and position choices are preserved");

        var on = new ToolkitTurboState(true, "Toolkit Turbo On.");
        var off = new ToolkitTurboState(false, "Toolkit Turbo Off.");
        check(CompatibilityChecks.ResolveToolkitProfile("Fixture", 0, 0, 1).Enabled == false, "Toolkit per-game Turbo Off overrides machine default On");
        check(CompatibilityChecks.ResolveToolkitProfile("Fixture", null, 0, 1).Enabled == true, "Toolkit missing per-game value inherits the machine default");
        check(CompatibilityChecks.ResolveToolkitProfile("Fixture", 1, 1, null).Enabled == false, "Toolkit per-game bypass prevents a false Turbo conflict");
        check(CompatibilityChecks.ResolveToolkitProfile("Fixture", "unknown", 0, null).Enabled == null, "Unknown Toolkit value types are not guessed");
        game.Provider = ProviderId.Off; game.KeepVectorFeatures = true;
        check(CompatibilityChecks.Evaluate(game, original, on).Any(i => i.BlocksApply), "Retained VectorXR custom Turbo plus Toolkit Turbo is blocked");
        check(!CompatibilityChecks.Evaluate(game, original, off).Any(i => i.BlocksApply), "Toolkit Turbo Off does not cause a conflict");
        var unrelated = original.Replace("\"applicationIds\":[\"game\"]", "\"applicationIds\":[\"other\"]");
        check(!CompatibilityChecks.Evaluate(game, unrelated, on).Any(i => i.BlocksApply), "Turbo profile for another game does not block this preset");
        check(!CompatibilityChecks.Evaluate(game, original.Replace("\"profiles\":[{\"enabled\":true", "\"profiles\":[{\"enabled\":false"), on).Any(i => i.BlocksApply), "Disabled Turbo profiles do not conflict");
        check(!CompatibilityChecks.Evaluate(game, original, new(null, "Unknown Toolkit application")).Any(i => i.BlocksApply), "Unknown Toolkit mapping is advisory, not a confirmed conflict");
        game.Provider = ProviderId.VectorXR;
        var fg = CompatibilityChecks.Evaluate(game, original, on);
        check(!fg.Any(i => i.BlocksApply) && fg.Any(i => i.Message.Contains("suppresses")), "VectorXR FG suppressing its own Turbo is explained without a false dual-Turbo block");
        game.Provider = ProviderId.Djules; game.KeepVectorFeatures = false;
        check(CompatibilityChecks.Evaluate(game, original, on).Count == 0, "Exclusive OFXR does not report a disabled VectorXR conflict");
        game.KeepVectorFeatures = true;
        check(CompatibilityChecks.Evaluate(game, original.Replace("\"version\":3", "\"version\":999"), on).Any(i => i.Message.Contains("Unknown")), "Unknown VectorXR format is reported as unchecked");
        check(CompatibilityChecks.Evaluate(game, "{broken", on).Any(i => i.Message.Contains("incomplete")), "Malformed settings do not crash compatibility reporting");
    }
}
