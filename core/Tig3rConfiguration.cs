namespace Wsg.OpenXR;

// Tagged v663 uses its own directory and seven independent preference banks.
// Do not translate this INI through either fork's shared tray.ini adapter.
public static class Tig3rConfiguration
{
    public const string Contract = "tig3r-v663";
    public static readonly string[] Routes = ["fidelityfx", "ofxr_reference", "ofxr_djules", "ofxr_new", "hybrid", "fsr3", "dlss"];
    public static readonly string[] OverlayPositions = ["off", "upper_left", "upper_right", "lower_left", "lower_right", "elliott_controller", "elliott_bottom"];
    public static bool SupportsThreeX(string route) => route is "ofxr_djules" or "dlss";
    public static bool UsesOpticalFlow(string route) => route is "fidelityfx" or "ofxr_reference" or "ofxr_djules" or "ofxr_new" or "hybrid";
    public static bool SameOutsideTray(byte[] expected, byte[] actual) {
        static IEnumerable<string> Lines(byte[] bytes) {
            var tray = false;
            foreach (var line in System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n')) {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) tray = trimmed.Equals("[tray]", StringComparison.OrdinalIgnoreCase);
                if (!tray && trimmed.Length > 0) yield return line;
            }
        }
        return Lines(expected).SequenceEqual(Lines(actual));
    }
    public static string GamePath(string globalPath, string executable) {
        var name = Path.GetFileName(executable);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("Choose a game executable for the tig3rmast3r profile.");
        return SafeFiles.Child(Path.Combine(Path.GetDirectoryName(globalPath)!, "Profiles"), name + ".ini");
    }
    public static IEnumerable<string> Profiles(string globalPath) {
        var folder = Path.Combine(Path.GetDirectoryName(globalPath)!, "Profiles");
        SafeFiles.NoLinks(folder);
        if (!Directory.Exists(folder)) return [];
        var files = Directory.GetFiles(folder, "*.ini", SearchOption.TopDirectoryOnly);
        if (files.Length > 1000) throw new InvalidDataException("Too many tig3rmast3r profiles to review.");
        foreach (var file in files) {
            SafeFiles.NoLinks(file);
            if (new FileInfo(file).Length > 1024 * 1024) throw new InvalidDataException("A tig3rmast3r profile is too large.");
        }
        return files;
    }
    public static void Validate(EngineSettings settings) {
        if (!Routes.Contains(settings.Method) || !new[] { "2x", "3x" }.Contains(settings.Multiplier)
            || settings.Multiplier == "3x" && !SupportsThreeX(settings.Method)
            || !new[] { "slow", "medium", "fast" }.Contains(settings.Quality)
            || !new[] { 25, 50, 75, 100 }.Contains(settings.FlowResolution)
            || !OverlayPositions.Contains(settings.OverlayPosition))
            throw new InvalidDataException("Unsupported tig3rmast3r settings.");
    }
    public static string Patch(string existing, EngineSettings settings, bool enabled = true) {
        Validate(settings);
        var generation = settings.Method switch { "hybrid" => 3, "fsr3" => 1, "dlss" => 2, _ => 0 };
        var backend = settings.Method switch { "ofxr_reference" => 1, "ofxr_new" => 2, "ofxr_djules" => 3, _ => 0 };
        var section = settings.Method switch { "fidelityfx" => "ofxr_fidelityfx", "fsr3" => "fsr", "dlss" => "dlssg", _ => settings.Method };
        var bank = new Dictionary<string, string> {
            ["preset"] = (settings.Quality switch { "slow" => 0, "fast" => 2, _ => 1 }).ToString(),
            ["scale"] = (settings.FlowResolution switch { 100 => 0, 75 => 1, 25 => 3, _ => 2 }).ToString(),
            ["lower_vram"] = settings.LowerVram ? "1" : "0",
            ["separate_synthesis"] = settings.LowerVram ? "1" : "0",
            ["triple_frame_gen"] = settings.Multiplier == "3x" ? "1" : "0"
        };
        // Existing route sections take precedence over flat fields in the native reader.
        // Seed missing native preference fields from that bank (or its factory defaults).
        foreach (var pair in new Dictionary<string, string> { ["motion_vectors"] = generation == 0 ? "0" : "1", ["backward"] = "0", ["prefer_fps"] = "1", ["native_scale"] = "100" })
            bank[pair.Key] = ReadValue(existing, "route." + section, pair.Key) ?? pair.Value;
        if (generation != 0) bank["motion_vectors"] = "1";
        var flat = new Dictionary<string, string>(bank) {
            ["enabled"] = enabled ? "1" : "0", ["frame_generation"] = generation.ToString(), ["backend"] = backend.ToString(),
            ["overlay_position"] = (settings.Overlay ? Array.IndexOf(OverlayPositions, settings.OverlayPosition) : 0).ToString()
        };
        return OfxrConfiguration.PatchSection(OfxrConfiguration.PatchSection(existing, "ofxr", flat), "route." + section, bank);
    }
    static string? ReadValue(string text, string section, string key) {
        var inside = false; string? result = null;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n')) {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) inside = line.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase);
            else if (inside && !line.StartsWithAny(';', '#') && line.IndexOf('=') is var equals && equals > 0
                && line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) result = line[(equals + 1)..].Trim();
        }
        return result;
    }
}
