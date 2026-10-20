namespace Wsg.OpenXR;

public sealed record LaunchOptions(bool ApplyDefault = false, string? Game = null, ProviderId? Provider = null,
    bool Minimized = false, string? ResultPath = null)
{
    public bool ApplyOnce => ApplyDefault || Game != null;
    public static LaunchOptions Parse(string[] args) {
        bool apply = false, minimized = false;
        string? game = null, result = null; ProviderId? provider = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++) {
            var key = args[i] is "--minimized" or "--start-minimized" ? "--tray" : args[i];
            if (!seen.Add(key)) throw new ArgumentException("Duplicate option: " + key);
            string Value() => ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(args[i])
                ? args[i] : throw new ArgumentException("Missing value for " + key);
            switch (key) {
                case "--apply-default": apply = true; break;
                case "--tray": minimized = true; break;
                case "--game": game = Value(); break;
                case "--provider":
                    provider = Value().ToLowerInvariant() switch {
                        "off" => ProviderId.Off, "djules" => ProviderId.Djules,
                        "elliott" => ProviderId.Elliott, "vectorxr" => ProviderId.VectorXR,
                        _ => throw new ArgumentException("Provider must be off, djules, elliott or vectorxr.") };
                    break;
                case "--result":
                    result = Value();
                    if (!Path.IsPathFullyQualified(result) || !result.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("--result requires an absolute .json output path.");
                    break;
                default: throw new ArgumentException("Unknown option: " + key);
            }
        }
        if (apply && (game != null || provider != null)) throw new ArgumentException("Use --apply-default OR --game with --provider.");
        if ((game != null) != (provider != null)) throw new ArgumentException("--game and --provider must be supplied together.");
        if (result != null && !apply && game == null) throw new ArgumentException("--result requires an apply command.");
        return new(apply, game, provider, minimized, result);
    }
    public GamePreset Resolve(AppState state) {
        var selector = ApplyDefault ? state.Startup.GameId : Game;
        if (string.IsNullOrWhiteSpace(selector)) throw new InvalidOperationException("Choose a default game and provider in Startup settings first.");
        var matches = state.Games.Where(g => g.Id.Equals(selector, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0 && !ApplyDefault)
            matches = state.Games.Where(g => g.Name.Equals(selector, StringComparison.OrdinalIgnoreCase)
                || g.Executable.Equals(selector, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException(matches.Length == 0 ? "The startup game preset was not found." : "Game name is ambiguous. Use its executable path or preset ID.");
        var copy = Json.Read<GamePreset>(Json.Write(matches[0]));
        copy.Provider = ApplyDefault ? state.Startup.Provider : Provider!.Value;
        if (!Enum.IsDefined(copy.Provider)) throw new InvalidDataException("Invalid startup provider.");
        return copy;
    }
}

public static class StartupLaunch
{
    // Shared by interactive startup and one-shot script commands. Never downloads or starts a game.
    public static async Task Apply(Storage storage, AppState state, ISwitchHost host, Catalog catalog, GamePreset preset) {
        host.RequireIdle(state.Games);
        var manager = new ProviderManager(storage, catalog, host);
        var coordinator = new SwitchCoordinator(storage, host, catalog.Verify, catalog.ValidateCoexistence, catalog.Get);
        if (manager.InstallationRecoveryPending || manager.RemovalRecoveryPending || coordinator.RecoveryPending)
            throw new InvalidOperationException("Recover the pending operation in WSG before automatic application.");
        var fingerprint = coordinator.Fingerprint();
        if (state.LastAppliedFingerprint != null && state.LastAppliedFingerprint != fingerprint && state.LastReviewedFingerprint != fingerprint)
            throw new InvalidOperationException("Setup changed outside WSG. Open WSG and Rescan setup before automatic application.");
        if (preset.Provider != ProviderId.Off && !File.Exists(preset.Executable)) throw new IOException("The startup game executable is missing.");
        var discovered = manager.Discover(state).DistinctBy(i => i.Executable, StringComparer.OrdinalIgnoreCase).ToList();
        Installation? selected = null;
        if (preset.Provider != ProviderId.Off) {
            var version = preset.Versions.GetValueOrDefault(preset.Provider);
            selected = discovered.FirstOrDefault(i => i.Provider == preset.Provider && i.Version == version)
                ?? throw new InvalidOperationException("The saved provider version is not installed. Install and save the desired version in WSG first.");
            ProviderAdapters.For(selected.Provider).ValidateCapabilities(catalog.Get(selected.CatalogKey), preset.For(selected.Provider));
        }
        var conflict = CompatibilityChecks.Read(preset, host.VectorSettingsPath, host.Layers()).FirstOrDefault(i => i.BlocksApply);
        if (conflict != null) throw new InvalidOperationException(conflict.Message);
        state.Installations = discovered;
        await coordinator.Apply(state, preset, selected, fingerprint);
    }
}

public enum BackgroundCheckMode { Stopped, VisibleStatus, PendingRemoval }
public static class BackgroundMonitoring
{
    public static BackgroundCheckMode Mode(bool visible, bool busy, bool pendingRemoval) => busy
        ? BackgroundCheckMode.Stopped : visible ? BackgroundCheckMode.VisibleStatus
        : pendingRemoval ? BackgroundCheckMode.PendingRemoval : BackgroundCheckMode.Stopped;
}
