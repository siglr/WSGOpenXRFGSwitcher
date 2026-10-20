using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Wsg.OpenXR;

public sealed record CatalogRelease
{
    public ProviderId Provider { get; set; }
    public string Version { get; set; } = "";
    public string Repository { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string Executable { get; set; } = "OFXRBridgeTray.exe";
    public string ArchiveRoot { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new();
    public string Compatibility { get; set; } = "Experimental — headset validation pending";
    public bool NativeDlss { get; set; }
    public bool DjulesHybrid { get; set; }
    public string MinimumSwitcherVersion { get; set; } = "0.1.0";
    public string Key => Provider + ":" + Version;
}
public sealed record CompatibilityBlock(ProviderId Provider, string Version, string VectorVersion, string Reason);
public sealed record CompatibilityCatalog(int Schema, string Product, List<CatalogRelease> Releases, List<CompatibilityBlock>? BlockedCombinations = null, long Revision = 0);
public sealed record SignedDocument(string Payload, string Signature);
public sealed class Catalog
{
    public const string Product = "WSG-OpenXR-FG-Switcher";
    readonly string publicKey;
    public CompatibilityCatalog Document { get; private set; }
    public static Version SwitcherVersion => typeof(Catalog).Assembly.GetName().Version!;
    public Catalog(string envelope, string publicKey) {
        this.publicKey = publicKey;
        if (envelope.Length > 6 * 1024 * 1024) throw new InvalidDataException("Catalog is too large.");
        var signed = Json.Read<SignedDocument>(envelope);
        if (string.IsNullOrWhiteSpace(signed.Payload) || string.IsNullOrWhiteSpace(signed.Signature)) throw new InvalidDataException("Catalog signature is missing.");
        var payload = Convert.FromBase64String(signed.Payload);
        if (payload.Length > 4 * 1024 * 1024) throw new InvalidDataException("Catalog is too large.");
        using var rsa = RSA.Create(); rsa.ImportFromPem(publicKey);
        var message = Encoding.UTF8.GetBytes("WSG-CATALOG-v1\n" + signed.Payload);
        if (!rsa.VerifyData(message, Convert.FromBase64String(signed.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new InvalidDataException("Catalog signature is invalid.");
        Document = Json.Read<CompatibilityCatalog>(Encoding.UTF8.GetString(payload));
        if (Document.Schema != 1 || Document.Product != Product || Document.Revision < 0 || Document.Releases == null || Document.Releases.Count is 0 or > 1000) throw new InvalidDataException("Unsupported catalog.");
        var seen = new HashSet<string>();
        foreach (var r in Document.Releases) {
            if (r == null || string.IsNullOrWhiteSpace(r.Version) || r.Repository == null || r.Url == null || r.Sha256 == null || r.Files == null) throw new InvalidDataException("Incomplete catalog release.");
            var provider = ProviderInfo.All.SingleOrDefault(p => p.Id == r.Provider) ?? throw new InvalidDataException("Unknown catalog provider.");
            if (!seen.Add(r.Key) || r.Repository != provider.Repository || !r.Url.StartsWith("https://github.com/" + provider.Repository + "/releases/download/", StringComparison.Ordinal) || !IsHash(r.Sha256) || r.Size <= 0 || r.Size > 512L * 1024 * 1024) throw new InvalidDataException("Invalid catalog release.");
            if (r.DjulesHybrid && r.Provider != ProviderId.Djules) throw new InvalidDataException("Invalid djules capabilities.");
            if (!Version.TryParse(r.MinimumSwitcherVersion, out var minimum) || minimum.Major < 0 || minimum.Build < 0) throw new InvalidDataException("Invalid minimum switcher version.");
            SafeFiles.Child(Path.GetTempPath(), r.Executable);
            foreach (var pair in r.Files) { SafeFiles.Child(Path.GetTempPath(), pair.Key); if (!IsHash(pair.Value)) throw new InvalidDataException("Invalid inventory hash."); }
            if (r.Provider != ProviderId.VectorXR && !r.Files.ContainsKey(r.Executable)) throw new InvalidDataException("Missing executable inventory.");
        }
    }
    static bool IsHash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public bool Supports(CatalogRelease release) => Version.Parse(release.MinimumSwitcherVersion) <= SwitcherVersion;
    public CatalogRelease Get(string key) {
        var release = Document.Releases.SingleOrDefault(r => r.Key == key) ?? throw new InvalidDataException("This provider version has not been reviewed by WSG.");
        if (!Supports(release)) throw new InvalidDataException("This mod version needs WSG " + release.MinimumSwitcherVersion + " or newer for its settings. Update WSG first.");
        return release;
    }
    public CatalogRelease Latest(ProviderId provider) => Document.Releases.Last(r => r.Provider == provider && Supports(r));
    public Catalog ReviewUpdate(string envelope) {
        var next = new Catalog(envelope, publicKey);
        if (next.Document.Revision < Document.Revision) throw new InvalidDataException("Provider catalog rollback refused.");
        foreach (var previous in Document.Releases) {
            var incoming = next.Document.Releases.SingleOrDefault(r => r.Key == previous.Key);
            if (incoming == null || incoming.Sha256 != previous.Sha256 || incoming.Size != previous.Size || incoming.Url != previous.Url
                || incoming.Executable != previous.Executable || incoming.ArchiveRoot != previous.ArchiveRoot
                || incoming.Files.Count != previous.Files.Count || previous.Files.Any(p => incoming.Files.GetValueOrDefault(p.Key) != p.Value))
                throw new InvalidDataException("A provider catalog must preserve published package identities.");
        }
        if (next.Document.Revision == Document.Revision && Json.Write(next.Document) != Json.Write(Document))
            throw new InvalidDataException("Changed provider catalog needs a newer revision.");
        return next;
    }
    public void UseReviewedUpdate(Catalog next) {
        if (!string.Equals(next.publicKey, publicKey, StringComparison.Ordinal)) throw new InvalidDataException("Provider catalog identity changed.");
        Document = next.Document;
    }
    public void ValidateCoexistence(Installation provider, Installation vector) {
        var block = Document.BlockedCombinations?.FirstOrDefault(b => b.Provider == provider.Provider && b.Version == provider.Version && b.VectorVersion == vector.Version);
        if (block != null) throw new InvalidOperationException("This combination has a known conflict: " + block.Reason);
    }
    public void Verify(Installation installation) {
        var release = Get(installation.CatalogKey);
        if (release.Provider != installation.Provider || release.Version != installation.Version || !Path.GetFullPath(installation.Executable).Equals(SafeFiles.Child(installation.Directory, release.Executable), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Provider identity does not match its catalog entry.");
        if (installation.Provider == ProviderId.VectorXR) {
            Authenticode.RequireDienerTech(installation.Executable);
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(installation.Executable).ProductVersion?.Split('+')[0];
            var actual = Version.TryParse(version?.TrimStart('v'), out var av) ? new Version(av.Major, av.Minor, Math.Max(0, av.Build), Math.Max(0, av.Revision)) : null;
            var known = Version.TryParse(release.Version.TrimStart('v'), out var kv) ? new Version(kv.Major, kv.Minor, Math.Max(0, kv.Build), Math.Max(0, kv.Revision)) : null;
            if (actual == null || actual != known) throw new InvalidDataException("VectorXR's installed version is not supported by this catalog.");
        } else foreach (var pair in release.Files)
            if (SafeFiles.HashFile(SafeFiles.Child(installation.Directory, pair.Key)) != pair.Value) throw new InvalidDataException("Provider file changed or missing: " + pair.Key);
    }
}
public static class Downloads
{
    public static async Task<byte[]> Get(Uri uri, long limit, CancellationToken ct = default) {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WSG-OpenXR-FG-Switcher/0.1.0");
        for (int redirects = 0; redirects < 5; redirects++) {
            if (uri.Scheme != "https" || !new[] { "github.com", "api.github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com", "wesimglide.org" }.Contains(uri.Host) || !uri.IsDefaultPort || uri.UserInfo.Length != 0) throw new InvalidDataException("Unexpected download destination.");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400) { uri = new Uri(uri, response.Headers.Location ?? throw new InvalidDataException("Missing redirect.")); continue; }
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Invalid download size.");
            using var input = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream();
            var buffer = new byte[65536]; int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0) { if (output.Length + count > limit) throw new InvalidDataException("Download exceeds its limit."); await output.WriteAsync(buffer.AsMemory(0, count), ct); }
            return output.ToArray();
        }
        throw new InvalidDataException("Too many download redirects.");
    }
    public static void Extract(byte[] bytes, string destination) {
        using var archive = new ZipArchive(new MemoryStream(bytes));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        if (archive.Entries.Count > 10000) throw new InvalidDataException("Too many package files.");
        foreach (var entry in archive.Entries) {
            // Official OFXR ZIPs use Windows separators. Canonicalize before all
            // validation and duplicate checks; traversal/rooted paths still fail.
            var normalized = entry.FullName.Replace('\\', '/');
            var name = normalized.TrimEnd('/'); if (name.Length == 0) continue;
            var path = SafeFiles.Child(destination, name);
            if (!seen.Add(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0) throw new InvalidDataException("Duplicate or linked archive entry.");
            total += entry.Length; if (total > 1024L * 1024 * 1024) throw new InvalidDataException("Package expands beyond its limit.");
            if (normalized.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew);
            var buffer = new byte[65536]; int count; long copied = 0;
            while ((count = input.Read(buffer)) != 0) { copied += count; if (copied > entry.Length) throw new InvalidDataException("Invalid ZIP entry length."); output.Write(buffer, 0, count); }
            if (copied != entry.Length) throw new InvalidDataException("Truncated ZIP entry.");
        }
    }
}
