using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wsg.OpenXR;

public enum ProviderId { Off, Djules, Elliott, VectorXR, Tig3r }
public sealed record ProviderInfo(ProviderId Id, string Name, string Repository)
{
    public static readonly ProviderInfo[] All = [
        new(ProviderId.Djules, "djules OFXR", "djules75/OFXR-Bridge"),
        new(ProviderId.Elliott, "elliott OFXR", "elliotttate/OFXR-Bridge"),
        new(ProviderId.VectorXR, "VectorXR", "DienerTech/vectorxr"),
        new(ProviderId.Tig3r, "tig3rmast3r OFXR", "tig3rmast3r/OFXR-Bridge")];
    public static string NameOf(ProviderId id) => All.FirstOrDefault(p => p.Id == id)?.Name ?? "Off";
    public static bool IsOfxr(ProviderId id) => id is ProviderId.Djules or ProviderId.Elliott or ProviderId.Tig3r;
    public static ProviderId[] DisplayOrder => [ProviderId.Djules, ProviderId.Elliott, ProviderId.Tig3r, ProviderId.VectorXR, ProviderId.Off];
}
public sealed record EngineSettings
{
    public string Method { get; set; } = "nvidia";
    public string Multiplier { get; set; } = "2x";
    public string Quality { get; set; } = "medium";
    public int FlowResolution { get; set; } = 50;
    public bool PreferFps { get; set; } = true;
    public bool LowerVram { get; set; } = true;
    public bool Overlay { get; set; } = true;
    public string OverlayPosition { get; set; } = "upper_right";
    public bool Hybrid { get; set; }
    public bool HybridDepthSearch { get; set; }
    public string OverlaySize { get; set; } = "big";
    public bool OverlayLatency { get; set; } = true;
    public bool OverlayMultiplier { get; set; } = true;
    public bool OverlayHybridStatus { get; set; } = true;
    public static EngineSettings Defaults(ProviderId id) => id == ProviderId.VectorXR
        ? new() { Method = "interpolate", Multiplier = "dynamic", Quality = "balanced" }
        : id == ProviderId.Djules ? new() { Method = "fidelityfx" }
        : id == ProviderId.Tig3r ? new() { Method = "ofxr_new", LowerVram = false, OverlayPosition = "elliott_bottom" } : new();
}
public sealed record GamePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New game";
    public string Executable { get; set; } = "";
    public ProviderId Provider { get; set; }
    public bool KeepVectorFeatures { get; set; }
    public bool ExperimentalAcknowledged { get; set; }
    public Dictionary<ProviderId, EngineSettings> Settings { get; set; } = new();
    public Dictionary<ProviderId, string> Versions { get; set; } = new();
    public Dictionary<string, EngineSettings> Tig3rRoutes { get; set; } = new();
    public EngineSettings For(ProviderId id) { if (!Settings.TryGetValue(id, out var value)) Settings[id] = value = EngineSettings.Defaults(id); return value; }
    public void SelectTig3rRoute(string route) {
        if (!Tig3rConfiguration.Routes.Contains(route)) throw new InvalidDataException("Unsupported tig3rmast3r generation mode.");
        var current = For(ProviderId.Tig3r);
        Tig3rRoutes[current.Method] = current with { };
        var next = Tig3rRoutes.GetValueOrDefault(route) ?? EngineSettings.Defaults(ProviderId.Tig3r) with { Method = route };
        Settings[ProviderId.Tig3r] = next with { Overlay = current.Overlay, OverlayPosition = current.OverlayPosition };
    }
    public override string ToString() => Name;
}
public sealed record Installation(ProviderId Provider, string Version, string Directory, string Executable, string CatalogKey);
public sealed record AppState
{
    public int Schema { get; set; } = 1;
    public List<GamePreset> Games { get; set; } = [];
    public List<GamePreset> DuplicateGamePresets { get; set; } = [];
    public string? ProviderRoot { get; set; }
    public WindowPlacement? Window { get; set; }
    public List<Installation> Installations { get; set; } = [];
    public string? SelectedGameId { get; set; }
    public bool LightTheme { get; set; }
    public string? ActiveGameId { get; set; }
    public ProviderId ActiveProvider { get; set; }
    public string? LastAppliedFingerprint { get; set; }
    public string? LastReviewedFingerprint { get; set; }
    public GamePreset? AppliedSetup { get; set; }
    public StartupPreferences Startup { get; set; } = new();
}
public sealed record StartupPreferences
{
    public string? GameId { get; set; }
    public ProviderId Provider { get; set; }
    public bool ApplyOnLaunch { get; set; }
    public bool StartMinimized { get; set; }
}
public sealed record ComparisonNote(DateTimeOffset Time, string Game, string Executable, ProviderId Provider,
    string Version, EngineSettings Settings, bool KeepVectorFeatures, string Runtime, string RenderFps, string DisplayFps, string Notes)
{
    public string SetupSource { get; init; } = "Selected preset (legacy note)";
}
public static class Json
{
    public static readonly JsonSerializerOptions Options = new() {
        WriteIndented = true, PropertyNameCaseInsensitive = true, MaxDepth = 64,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string text) => JsonSerializer.Deserialize<T>(text, Options) ?? throw new InvalidDataException("Empty JSON document.");
}
