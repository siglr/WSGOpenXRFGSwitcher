using Microsoft.Win32;
using System.Text.Json.Nodes;

namespace Wsg.OpenXR;

public sealed record CompatibilityIssue(bool BlocksApply, string Message, bool Warning = false);
public sealed record ToolkitTurboState(bool? Enabled, string Detail);

public static class CompatibilityChecks
{
    public const string ToolkitLayer = "XR_APILAYER_MBUCCHIA_toolkit";
    public const string ToolkitKey = @"SOFTWARE\OpenXR_Toolkit";

    public static ToolkitTurboState ResolveToolkitProfile(string name, object? turbo, object? bypass, object? fallback) {
        if ((bypass ?? 0) is not int skip) return new(null, "Unknown Toolkit bypass value for " + name);
        if (skip != 0) return new(false, "Toolkit profile " + name + " is bypassed.");
        var value = turbo ?? fallback ?? 0;
        return value is int flag && flag is 0 or 1
            ? new(flag == 1, "OpenXR Toolkit profile " + name + ": Turbo " + (flag == 1 ? "On." : "Off."))
            : new(null, "Unknown Toolkit Turbo value for " + name);
    }

    public static ToolkitTurboState ReadToolkit(GamePreset game, IReadOnlyList<LayerRegistration> layers) {
        if (!layers.Any(l => l.View == 64 && l.Key == OpenXrRegistry.OpenXr && l.LayerName == ToolkitLayer && l.Enabled))
            return new(false, "OpenXR Toolkit's 64-bit layer is disabled or not registered.");
        try {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var defaults = machine.OpenSubKey(ToolkitKey, false);
            if (defaults?.GetValue("safe_mode") is int safe && safe != 0) return new(false, "OpenXR Toolkit is in safe mode.");
            using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var profiles = user.OpenSubKey(ToolkitKey, false);
            var identity = GameDiscovery.Identity(game.Executable);
            var matches = new List<ToolkitTurboState>();
            if (profiles != null) foreach (var name in profiles.GetSubKeyNames()) {
                using var key = profiles.OpenSubKey(name, false);
                if (key?.GetValue("module") is not string module || !Path.IsPathFullyQualified(module)) continue;
                if (!GameDiscovery.Identity(module).Equals(identity, StringComparison.OrdinalIgnoreCase)) continue;
                matches.Add(ResolveToolkitProfile(name, key.GetValue("turbo"), key.GetValue("bypass"), defaults?.GetValue("turbo")));
            }
            return matches.Count == 1 ? matches[0] : new(null, matches.Count == 0
                ? "Cannot identify OpenXR Toolkit's application profile for this executable. Check Turbo in Toolkit."
                : "Multiple Toolkit profiles map to this executable. Check Turbo in Toolkit.");
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) {
            return new(null, "Could not read OpenXR Toolkit settings: " + ex.Message);
        }
    }

    public static List<CompatibilityIssue> Read(GamePreset game, string vectorPath, IReadOnlyList<LayerRegistration> layers) {
        if (game.Provider != ProviderId.VectorXR && !game.KeepVectorFeatures) return [];
        try {
            if (!File.Exists(vectorPath)) return [new(false, "VectorXR compatibility checks will be available after installation and setup.")];
            var result = Evaluate(game, File.ReadAllText(vectorPath), ReadToolkit(game, layers));
            if (layers.Count(l => l.Vector && l.View == 64 && l.Key == OpenXrRegistry.OpenXr) > 1)
                result.Add(new(true, "Multiple 64-bit VectorXR registrations found. Repair the installation before applying."));
            return result;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return [new(false, "Compatibility check incomplete: " + ex.Message, true)];
        }
    }

    public static List<CompatibilityIssue> Evaluate(GamePreset game, string vectorJson, ToolkitTurboState toolkit) {
        var results = new List<CompatibilityIssue>();
        if (game.Provider != ProviderId.VectorXR && !game.KeepVectorFeatures) return results;
        try {
            var root = JsonNode.Parse(vectorJson)?.AsObject();
            if (root?["version"]?.GetValue<int>() != 3)
                return [new(false, "Unknown VectorXR configuration version; Turbo compatibility could not be checked.", true)];
            var turbo = root["modules"]?["turbo"];
            var enabled = turbo?["enabled"]?.GetValue<bool>() ?? false;
            var exe = Path.GetFileName(game.Executable);
            var app = root["applications"]?.AsArray().OfType<JsonObject>().FirstOrDefault(a =>
                string.Equals(Path.GetFileName(a["match"]?["exe"]?.GetValue<string>()), exe, StringComparison.OrdinalIgnoreCase));
            var appId = app?["id"]?.GetValue<string>();
            if (appId != null && turbo?["profiles"] is JsonArray profiles)
                enabled |= profiles.OfType<JsonObject>().Any(p => p["enabled"]?.GetValue<bool>() == true
                    && p["applicationIds"] is JsonArray ids && ids.Any(id => id?.GetValue<string>() == appId));
            // Applying either VectorXR FG or retained enhancements enables VectorXR core.
            // Turbo profiles are opt-in; disabled profiles do not override a default On.
            if (toolkit.Enabled == null) results.Add(new(false, toolkit.Detail, true));
            if (game.Provider == ProviderId.VectorXR) {
                if (enabled) results.Add(new(false, "VectorXR Turbo is configured On, but VectorXR suppresses its own Turbo while its FG runs."));
                if (toolkit.Enabled == true) results.Add(new(false, toolkit.Detail + " This is another pacing override alongside VectorXR FG. Review it in Toolkit before testing; simultaneous Turbo operation is not confirmed.", true));
            } else if (enabled && toolkit.Enabled == true) {
                var disabledApp = app?["enabled"]?.GetValue<bool>() == false;
                results.Add(new(!disabledApp, disabledApp
                    ? toolkit.Detail + " VectorXR Turbo is configured On but the matching application is disabled. Review VectorXR's Application Registry; simultaneous Turbo operation is unconfirmed."
                    : toolkit.Detail + " VectorXR Turbo is also enabled for this game through retained features. Disable Turbo in one provider, save, then Rescan setup.", disabledApp));
            } else if (enabled) {
                results.Add(new(false, "VectorXR Turbo is enabled for the retained features. " + toolkit.Detail));
            } else results.Add(new(false, "VectorXR Turbo is Off for this game. " + toolkit.Detail));
        } catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) {
            results.Add(new(false, "Compatibility check incomplete: VectorXR settings could not be interpreted. Review its app.", true));
        }
        return results;
    }
}
