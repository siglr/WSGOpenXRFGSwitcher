namespace Wsg.OpenXR;

public static class OfxrActivation
{
    const string LayerDll = "XR_APILAYER_XRFrameBridge_diagnostic.dll";
    const string X86Dll = "ofxr_x86/" + LayerDll;
    const string VulkanDll = "OFXR_vulkan_queue_layer.dll";

    // HKCU's Khronos key is shared by both registry views. V577 registers both
    // DLL architectures there, so View alone cannot identify the companion.
    // Compare each loaded cache DLL with the selected signed package inventory.
    public static bool Matches(Installation installation, IReadOnlyList<LayerRegistration> layers, CatalogRelease? release = null) {
        var xr = layers.Where(r => r.Ofxr && r.Enabled && r.Key == OpenXrRegistry.OpenXr).ToArray();
        var vk = layers.Where(r => r.Ofxr && r.Enabled && r.Key == OpenXrRegistry.Vulkan).ToArray();
        if (xr.Length is < 1 or > 2 || vk.Length > 1) return false;
        string? mainHash, companionHash = null, vulkanHash;
        if (release != null) {
            if (release.Key != installation.CatalogKey || release.Provider != installation.Provider) return false;
            var main = release.Files.Where(p => Path.GetFileName(p.Key) == LayerDll && p.Key != X86Dll).ToArray();
            var vulkan = release.Files.Where(p => Path.GetFileName(p.Key) == VulkanDll).ToArray();
            if (main.Length != 1 || vulkan.Length > 1) return false;
            mainHash = main[0].Value; vulkanHash = vulkan.SingleOrDefault().Value;
            if (release.Provider == ProviderId.Djules) companionHash = release.Files.GetValueOrDefault(X86Dll);
        } else {
            // Older callers can verify their single-layer packages as before.
            // A package with multiple architectures requires reviewed metadata.
            var main = Directory.GetFiles(installation.Directory, LayerDll, SearchOption.AllDirectories);
            var vulkan = Directory.GetFiles(installation.Directory, VulkanDll, SearchOption.AllDirectories);
            if (main.Length != 1 || vulkan.Length > 1) return false;
            mainHash = SafeFiles.HashFile(main[0]); vulkanHash = vulkan.Length == 1 ? SafeFiles.HashFile(vulkan[0]) : null;
        }
        if (mainHash == null || mainHash == companionHash) return false;
        var hashes = xr.Select(r => SafeFiles.HashFile(r.Library)).ToArray();
        if (hashes.Count(h => h == mainHash) != 1) return false;
        if (hashes.Length == 2 && (companionHash == null || hashes.Count(h => h == companionHash) != 1)) return false;
        return vk.Length == 0 || vulkanHash != null && SafeFiles.HashFile(vk[0].Library) == vulkanHash;
    }
}
