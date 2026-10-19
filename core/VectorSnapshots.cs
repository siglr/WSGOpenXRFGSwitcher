using System.Text.Json.Nodes;

namespace Wsg.OpenXR;

public static class VectorSnapshots
{
    public static JsonObject PreserveManagedProfiles(JsonObject baseline, JsonObject current) {
        var result = baseline.DeepClone().AsObject();
        if (current["profiles"] is not JsonArray profiles) return result;
        if (result["profiles"] == null) result["profiles"] = new JsonArray();
        var saved = result["profiles"]!.AsArray();
        foreach (var p in profiles.OfType<JsonObject>()) {
            var name = p["name"]?.GetValue<string>();
            if (name?.StartsWith("WSG • ", StringComparison.Ordinal) != true) continue;
            foreach (var old in saved.OfType<JsonObject>().Where(n => n["name"]?.GetValue<string>() == name).ToArray()) saved.Remove(old);
            var copy = p.DeepClone().AsObject(); copy["enabled"] = false; saved.Add(copy);
        }
        return result;
    }
    // Reconcile changes made in VectorXR without losing the enable flags temporarily
    // suppressed by WSG. Only these two local snapshots are written by Refresh.
    public static void Reconcile(Storage storage, string configPath) {
        var originalPath = Path.Combine(storage.Root, "vector-framegen-original.json");
        var lastPath = Path.Combine(storage.Root, "vector-framegen-last.json");
        if (!File.Exists(configPath) || !File.Exists(originalPath) || !File.Exists(lastPath)) return;
        var root = JsonNode.Parse(File.ReadAllText(configPath));
        if (root?["version"]?.GetValue<int>() != 3) return;
        var current = root["modules"]?["framegen"]?.DeepClone()?.AsObject();
        if (current == null) return;
        var last = JsonNode.Parse(File.ReadAllText(lastPath))!.AsObject();
        if (JsonNode.DeepEquals(current, last)) return;
        var original = JsonNode.Parse(File.ReadAllText(originalPath))!.AsObject();
        if (JsonNode.DeepEquals(current["enabled"], last["enabled"])) current["enabled"] = original["enabled"]?.DeepClone() ?? JsonValue.Create(false);
        if (current["profiles"] is JsonArray profiles) {
            foreach (var p in profiles.OfType<JsonObject>().ToArray()) {
                var name = p["name"]?.GetValue<string>();
                if (name?.StartsWith("WSG • ", StringComparison.Ordinal) == true) { p["enabled"] = false; continue; }
                var oldProfile = original["profiles"]?.AsArray().OfType<JsonObject>().FirstOrDefault(n => n["name"]?.GetValue<string>() == name);
                var lastProfile = last["profiles"]?.AsArray().OfType<JsonObject>().FirstOrDefault(n => n["name"]?.GetValue<string>() == name);
                if (oldProfile != null && lastProfile != null && JsonNode.DeepEquals(p["enabled"], lastProfile["enabled"])) p["enabled"] = oldProfile["enabled"]?.DeepClone();
            }
        }
        SafeFiles.AtomicWrite(originalPath, current.ToJsonString(Json.Options));
    }
}
