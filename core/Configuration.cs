using System.Text.Json.Nodes;

namespace Wsg.OpenXR;

public static class OfxrConfiguration
{
    // Preserve unknown keys and sections. The adapter owns only the controls exposed by WSG.
    public static string Patch(string existing, ProviderId provider, EngineSettings settings) {
        Validate(provider, settings);
        var values = new Dictionary<string, string> {
            ["backend"] = settings.Method == "fidelityfx" ? "fidelityfx" : "nvidia",
            ["nvidia_preset"] = settings.Quality,
            ["nvidia_input_scale"] = settings.FlowResolution.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["deep_pipeline"] = settings.PreferFps || settings.Multiplier is "3x" or "auto" ? "1" : "0",
            ["triple_frame_gen"] = settings.Multiplier == "3x" ? "1" : "0",
            ["single_swapchain_rings"] = settings.LowerVram ? "1" : "0",
            ["overlay_position"] = settings.Overlay ? settings.OverlayPosition : "off"
        };
        if (provider == ProviderId.Elliott) {
            values["frame_generation"] = settings.Method == "dlss" ? "dlss" : "ofxr";
            values["motion_vectors"] = settings.Method is "dlss" or "vectors" or "hybrid" or "extrapolate" ? "dlss" : "off";
            values["dlss_hybrid"] = settings.Method == "hybrid" ? "1" : "0";
            values["extrapolate"] = settings.Method == "extrapolate" ? "1" : "0";
            values["frames_auto"] = settings.Multiplier == "auto" ? "1" : "0";
        }
        return PatchSection(existing, "tray", values);
    }
    public static void Validate(ProviderId provider, EngineSettings settings) {
        string[] methods = provider == ProviderId.Elliott ? ["nvidia", "fidelityfx", "vectors", "dlss", "hybrid", "extrapolate"] : ["nvidia", "fidelityfx"];
        var rates = provider == ProviderId.Elliott ? new[] { "2x", "3x", "auto" } : ["2x", "3x"];
        if (!methods.Contains(settings.Method) || !rates.Contains(settings.Multiplier) || !new[] { "fast", "medium", "slow" }.Contains(settings.Quality) || !new[] { 50, 75, 100 }.Contains(settings.FlowResolution)
            || !new[] { "upper_left", "upper_right", "lower_left", "lower_right" }.Contains(settings.OverlayPosition)) throw new InvalidDataException("Unsupported OFXR settings.");
    }
    public static string PatchSection(string text, string section, Dictionary<string, string> changes) {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var found = false; var inside = false; var remaining = new Dictionary<string, string>(changes, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Count; i++) {
            var line = lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) {
                if (inside) { foreach (var pair in remaining) lines.Insert(i++, pair.Key + "=" + pair.Value); remaining.Clear(); }
                inside = line.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase); found |= inside;
            } else if (inside && !line.StartsWithAny(';', '#') && line.Contains('=')) {
                var key = line[..line.IndexOf('=')].Trim();
                if (changes.TryGetValue(key, out var value)) { lines[i] = key + "=" + value; remaining.Remove(key); }
            }
        }
        if (!found) lines.Add("[" + section + "]");
        foreach (var pair in remaining) lines.Add(pair.Key + "=" + pair.Value);
        return string.Join("\r\n", lines).TrimEnd() + "\r\n";
    }
}
public static class VectorConfiguration
{
    public static string Patch(string existing, GamePreset game, bool enableFg, bool keepFeatures) {
        var root = JsonNode.Parse(existing)?.AsObject() ?? throw new InvalidDataException("VectorXR settings are missing.");
        if (root["version"]?.GetValue<int>() != 3) throw new InvalidDataException("This VectorXR configuration version is not supported. Open VectorXR to initialize or upgrade it first.");
        var core = root["core"]?.AsObject() ?? throw new InvalidDataException("VectorXR core settings are missing.");
        var modules = root["modules"]?.AsObject() ?? throw new InvalidDataException("VectorXR module settings are missing.");
        var fg = modules["framegen"]?.AsObject();
        if (fg == null) { fg = new JsonObject { ["enabled"] = false, ["profiles"] = new JsonArray() }; modules["framegen"] = fg; }
        var activeProfiles = fg["profiles"]?.AsArray().OfType<JsonObject>().Where(p => p["enabled"]?.GetValue<bool>() == true).ToArray() ?? [];
        // This is the DEFAULT profile, not a master switch. Disable every override too.
        fg["enabled"] = false;
        if (fg["profiles"] is JsonArray originalProfiles)
            foreach (var p in originalProfiles.OfType<JsonObject>()) p["enabled"] = false;
        if (enableFg) {
            core["enabled"] = true;
            var settings = game.For(ProviderId.VectorXR);
            if (!new[] { "interpolate", "extrapolate" }.Contains(settings.Method) || !new[] { "dynamic", "auto", "2x", "3x", "4x" }.Contains(settings.Multiplier) || !new[] { "performance", "balanced", "quality" }.Contains(settings.Quality)) throw new InvalidDataException("Unsupported VectorXR settings.");
            var applications = root["applications"]?.AsArray() ?? throw new InvalidDataException("VectorXR application registry is missing.");
            // Upstream matches executable basenames, first application wins. Reuse that identity
            // so Pivot/Quadviews profiles targeting the existing application continue to match.
            var exe = Path.GetFileName(game.Executable);
            var app = applications.OfType<JsonObject>().FirstOrDefault(a => string.Equals(a["match"]?["exe"]?.GetValue<string>(), exe, StringComparison.OrdinalIgnoreCase));
            var id = app?["id"]?.GetValue<string>() ?? "wsg-" + game.Id;
            if (app == null) { app = new JsonObject { ["id"] = id, ["name"] = game.Name, ["match"] = new JsonObject { ["exe"] = exe } }; applications.Add(app); }
            app["enabled"] = true;
            var profiles = fg["profiles"]?.AsArray() ?? new JsonArray();
            if (fg["profiles"] == null) fg["profiles"] = profiles;
            var name = "WSG • " + game.Id;
            // WSG's exact executable profile must take precedence over overlapping upstream profiles.
            var profile = profiles.OfType<JsonObject>().FirstOrDefault(p => p["name"]?.GetValue<string>() == name);
            if (profile != null) profiles.Remove(profile);
            var tuning = fg["defaults"]?.DeepClone()?.AsObject() ?? new JsonObject();
            var inherited = profile?["settings"]?.AsObject()
                ?? activeProfiles.FirstOrDefault(p => p["applicationIds"] is JsonArray ids && ids.Any(value => value?.GetValue<string>() == id))?["settings"]?.AsObject();
            if (inherited != null) foreach (var value in inherited) tuning[value.Key] = value.Value?.DeepClone();
            tuning["synthesisMode"] = settings.Method; tuning["flowQuality"] = settings.Quality;
            tuning["frameMultiplier"] = settings.Multiplier;
            profile ??= new JsonObject();
            profile["name"] = name; profile["enabled"] = true; profile["applicationIds"] = new JsonArray(id);
            profile.Remove("mode"); profile["settings"] = tuning;
            profiles.Insert(0, profile);
            if (core["osd"] == null) core["osd"] = new JsonObject();
            if (core["osd"] is JsonObject osd) { osd["enabled"] = settings.Overlay; osd["showFramegen"] ??= true; osd["showFramegenFps"] ??= true; }
        } else if (!keepFeatures) core["enabled"] = false;
        else core["enabled"] = true;
        return root.ToJsonString(Json.Options);
    }
}
