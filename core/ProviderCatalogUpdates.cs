using System.Text;

namespace Wsg.OpenXR;

// Metadata only: a catalog refresh cannot install a mod, write its settings or change layers.
public static class ProviderCatalogUpdates
{
    public const string FileName = "WSG-OpenXR-FG-Switcher.providers.json";
    public const string Feed = "https://wesimglide.org/otherdata/tools/" + FileName;
    const int Limit = 6 * 1024 * 1024;
    static string CachePath(Storage storage) => Path.Combine(storage.Root, "provider-catalog.signed.json");
    public static void LoadSaved(Storage storage, Catalog catalog) {
        var path = CachePath(storage);
        if (!File.Exists(path)) return;
        try {
            SafeFiles.NoLinks(path);
            if (new FileInfo(path).Length > Limit) throw new InvalidDataException("Saved provider catalog is too large.");
            catalog.UseReviewedUpdate(catalog.ReviewUpdate(File.ReadAllText(path)));
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or FormatException or System.Text.Json.JsonException or InvalidOperationException) {
            storage.Log("Saved provider catalog ignored: " + ex.Message);
        }
    }
    public static async Task<bool> Refresh(Storage storage, Catalog catalog, Func<bool> canCommit, CancellationToken cancellation = default,
        Func<Uri, long, CancellationToken, Task<byte[]>>? download = null) {
        var bytes = await (download ?? Downloads.Get)(new Uri(Feed), Limit, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (bytes.Length > Limit) throw new InvalidDataException("Provider catalog download is too large.");
        var next = catalog.ReviewUpdate(Encoding.UTF8.GetString(bytes));
        if (!canCommit()) return false;
        if (next.Document.Revision == catalog.Document.Revision) return false;
        // The atomic write must succeed before replacing the in-memory catalog.
        SafeFiles.AtomicWrite(CachePath(storage), bytes);
        catalog.UseReviewedUpdate(next);
        return true;
    }
}
