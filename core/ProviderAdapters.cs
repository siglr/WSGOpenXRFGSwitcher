namespace Wsg.OpenXR;

public interface IProviderAdapter
{
    ProviderId Id { get; }
    bool UsesOfficialInstaller { get; }
    string PrepareConfiguration(string existing, GamePreset game, bool enableFrameGeneration, bool keepOtherFeatures);
    void ValidateCapabilities(CatalogRelease release, EngineSettings settings);
}
public sealed class DjulesAdapter : IProviderAdapter
{
    public ProviderId Id => ProviderId.Djules;
    public bool UsesOfficialInstaller => false;
    public string PrepareConfiguration(string existing, GamePreset game, bool enableFrameGeneration, bool keepOtherFeatures) => OfxrConfiguration.Patch(existing, Id, game.For(Id));
    public void ValidateCapabilities(CatalogRelease release, EngineSettings settings) => OfxrConfiguration.Validate(Id, settings);
}
public sealed class ElliottAdapter : IProviderAdapter
{
    public ProviderId Id => ProviderId.Elliott;
    public bool UsesOfficialInstaller => false;
    public string PrepareConfiguration(string existing, GamePreset game, bool enableFrameGeneration, bool keepOtherFeatures) => OfxrConfiguration.Patch(existing, Id, game.For(Id));
    public void ValidateCapabilities(CatalogRelease release, EngineSettings settings) {
        OfxrConfiguration.Validate(Id, settings);
        if (settings.Method == "dlss" && !release.NativeDlss) throw new InvalidDataException("This Elliott release does not include native DLSS frame generation.");
    }
}
public sealed class VectorAdapter : IProviderAdapter
{
    public ProviderId Id => ProviderId.VectorXR;
    public bool UsesOfficialInstaller => true;
    public string PrepareConfiguration(string existing, GamePreset game, bool enableFrameGeneration, bool keepOtherFeatures) => VectorConfiguration.Patch(existing, game, enableFrameGeneration, keepOtherFeatures);
    public void ValidateCapabilities(CatalogRelease release, EngineSettings settings) {
        if (!new[] { "interpolate", "extrapolate" }.Contains(settings.Method)) throw new InvalidDataException("Unsupported VectorXR method.");
    }
}
public static class ProviderAdapters
{
    static readonly IProviderAdapter[] Adapters = [new DjulesAdapter(), new ElliottAdapter(), new VectorAdapter()];
    public static IProviderAdapter For(ProviderId id) => Adapters.Single(a => a.Id == id);
}
