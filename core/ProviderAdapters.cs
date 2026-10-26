namespace Wsg.OpenXR;

public interface IProviderAdapter
{
    ProviderId Id { get; }
    bool UsesOfficialInstaller { get; }
    string PrepareConfiguration(string existing, GamePreset game, bool enableFrameGeneration, bool keepOtherFeatures, CatalogRelease? release = null);
    void ValidateCapabilities(CatalogRelease release, EngineSettings settings);
}
public sealed class DjulesAdapter : IProviderAdapter
{
    public ProviderId Id => ProviderId.Djules;
    public bool UsesOfficialInstaller => false;
    public string PrepareConfiguration(string existing, GamePreset game, bool enableFrameGeneration, bool keepOtherFeatures, CatalogRelease? release = null) => OfxrConfiguration.Patch(existing, Id, game.For(Id), release);
    public void ValidateCapabilities(CatalogRelease release, EngineSettings settings) {
        if (release.Provider != Id) throw new InvalidDataException("Incorrect provider capabilities.");
        _ = OfxrConfiguration.Patch("", Id, settings, release);
    }
}
public sealed class ElliottAdapter : IProviderAdapter
{
    public ProviderId Id => ProviderId.Elliott;
    public bool UsesOfficialInstaller => false;
    public string PrepareConfiguration(string existing, GamePreset game, bool enableFrameGeneration, bool keepOtherFeatures, CatalogRelease? release = null) => OfxrConfiguration.Patch(existing, Id, game.For(Id), release);
    public void ValidateCapabilities(CatalogRelease release, EngineSettings settings) {
        OfxrConfiguration.Validate(Id, settings);
        if (settings.Method == "dlss" && !release.NativeDlss) throw new InvalidDataException("This Elliott release does not include native DLSS frame generation.");
    }
}
public sealed class VectorAdapter : IProviderAdapter
{
    public ProviderId Id => ProviderId.VectorXR;
    public bool UsesOfficialInstaller => true;
    public string PrepareConfiguration(string existing, GamePreset game, bool enableFrameGeneration, bool keepOtherFeatures, CatalogRelease? release = null) => VectorConfiguration.Patch(existing, game, enableFrameGeneration, keepOtherFeatures);
    public void ValidateCapabilities(CatalogRelease release, EngineSettings settings) {
        if (!new[] { "interpolate", "extrapolate" }.Contains(settings.Method)) throw new InvalidDataException("Unsupported VectorXR method.");
    }
}
public sealed class Tig3rAdapter : IProviderAdapter
{
    public ProviderId Id => ProviderId.Tig3r;
    public bool UsesOfficialInstaller => false;
    public string PrepareConfiguration(string existing, GamePreset game, bool enableFrameGeneration, bool keepOtherFeatures, CatalogRelease? release = null) {
        ValidateCapabilities(release ?? throw new InvalidDataException("Verified tig3rmast3r settings metadata is required."), game.For(Id));
        return Tig3rConfiguration.Patch(existing, game.For(Id), enableFrameGeneration);
    }
    public void ValidateCapabilities(CatalogRelease release, EngineSettings settings) {
        if (release.Provider != Id || release.SettingsContract != Tig3rConfiguration.Contract)
            throw new InvalidDataException("This tig3rmast3r settings format requires a newer WSG version.");
        if (settings.Method == "dlss" && !release.NativeDlss) throw new InvalidDataException("This release does not include DLSS frame generation.");
        Tig3rConfiguration.Validate(settings);
    }
}
public static class ProviderAdapters
{
    static readonly IProviderAdapter[] Adapters = [new DjulesAdapter(), new ElliottAdapter(), new VectorAdapter(), new Tig3rAdapter()];
    public static IProviderAdapter For(ProviderId id) => Adapters.Single(a => a.Id == id);
}
