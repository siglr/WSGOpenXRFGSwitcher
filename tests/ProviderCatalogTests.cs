using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using Wsg.OpenXR;

static class ProviderCatalogTests
{
    public static async Task Run(string root, Action<bool, string> check, Func<Func<Task>, string, Task> reject) {
        using var rsa = RSA.Create(3072);
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
        byte[] Sign(CompatibilityCatalog document) {
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Write(document)));
            var signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes("WSG-CATALOG-v1\n" + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return Encoding.UTF8.GetBytes(Json.Write(new SignedDocument(payload, signature)));
        }
        var release = new CatalogRelease { Provider = ProviderId.Djules, Version = "0.2.14.1", Repository = "djules75/OFXR-Bridge",
            Url = "https://github.com/djules75/OFXR-Bridge/releases/download/0.2.14.1/provider.zip", Size = 3, Sha256 = new string('a', 64),
            Files = new() { ["OFXRBridgeTray.exe"] = new string('b', 64) }, DjulesHybrid = true };
        var initial = new CompatibilityCatalog(1, Catalog.Product, [release], Revision: 1);
        var future = release with { Version = "0.2.14.2", Url = "https://github.com/djules75/OFXR-Bridge/releases/download/0.2.14.2/provider.zip" };
        var executableBytes = Encoding.UTF8.GetBytes("Local fixture bytes — never executable or launched");
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
            using var entry = zip.CreateEntry("OFXRBridgeTray.exe").Open(); entry.Write(executableBytes);
        }
        var package = stream.ToArray();
        future = future with { Size = package.Length, Sha256 = SafeFiles.Hash(package), Files = new() { ["OFXRBridgeTray.exe"] = SafeFiles.Hash(executableBytes) } };
        var update = initial with { Revision = 2, Releases = [release, future] };
        var catalog = new Catalog(Encoding.UTF8.GetString(Sign(initial)), publicKey);
        var storage = new Storage(Path.Combine(root, "provider-catalog"));
        var requests = 0;
        Task<byte[]> Download(Uri uri, long limit, CancellationToken ct) {
            check(uri.AbsoluteUri == ProviderCatalogUpdates.Feed && limit <= 6 * 1024 * 1024, "Catalog refresh uses the fixed bounded product feed");
            requests++; return Task.FromResult(Sign(update));
        }
        check(await ProviderCatalogUpdates.Refresh(storage, catalog, () => true, download: Download)
            && catalog.Latest(ProviderId.Djules).Version == future.Version, "A signed provider-only update adds a version without rebuilding WSG");
        var savedPath = Path.Combine(storage.Root, ProviderCatalogUpdates.CacheFileName);
        var saved = File.ReadAllBytes(savedPath);
        var offline = new Catalog(Encoding.UTF8.GetString(Sign(initial)), publicKey);
        ProviderCatalogUpdates.LoadSaved(storage, offline);
        check(offline.Document.Revision == 2 && offline.Get(future.Key).Version == future.Version, "Cached provider catalog works offline for UI and launch scripts");
        var state = new AppState { ProviderRoot = Path.Combine(storage.Root, "Mods with spaces") };
        var host = new FakeHost(storage.Root, "{\"version\":3,\"core\":{\"enabled\":false},\"modules\":{\"framegen\":{\"enabled\":false,\"profiles\":[]}},\"applications\":[]}");
        SafeFiles.AtomicWrite(Path.Combine(storage.Root, "downloads", "Djules-" + future.Sha256 + ".zip"), package);
        var manager = new ProviderManager(storage, offline, host);
        var installation = await manager.Install(offline.Get(future.Key), state, _ => { });
        check(installation.Version == future.Version && manager.Identify(installation.Directory).CatalogKey == future.Key
            && host.RunningTray() == null && host.Writes == 0, "A newly fetched provider version installs in isolation without a switcher rebuild or runtime changes");
        var gameFile = Path.Combine(storage.Root, "game.exe"); File.WriteAllText(gameFile, "fixture");
        var preset = new GamePreset { Name = "Fixture game", Executable = gameFile, Provider = ProviderId.Djules };
        preset.Versions[ProviderId.Djules] = future.Version; preset.For(ProviderId.Djules).Hybrid = true; state.Games.Add(preset);
        await StartupLaunch.Apply(storage, state, host, offline, preset);
        check(state.ActiveProvider == ProviderId.Djules && File.ReadAllText(host.TraySettingsPath).Contains("hybrid=1"),
            "Launch-script application can use a newly cached provider version offline");
        check(!await ProviderCatalogUpdates.Refresh(storage, catalog, () => true, download: Download) && requests == 2, "Unchanged provider metadata does not rewrite the cache");
        async Task RejectUpdate(byte[] bytes, string name) {
            await reject(() => ProviderCatalogUpdates.Refresh(storage, catalog, () => true, download: (_, _, _) => Task.FromResult(bytes)), name);
            check(File.ReadAllBytes(savedPath).SequenceEqual(saved) && catalog.Document.Revision == 2, name + " preserves the last good catalog");
        }
        await RejectUpdate(Sign(initial), "Signed rollback is refused");
        await RejectUpdate(Sign(update with { Releases = [release] }), "Same-revision edits are refused");
        await RejectUpdate(Sign(update with { Revision = 3, Releases = [future] }), "Removing known packages is refused");
        await RejectUpdate(Sign(update with { Revision = 3, Releases = [release with { Sha256 = new string('c', 64) }, future] }), "Changing a published package hash is refused");
        await RejectUpdate(Sign(update with { Revision = 3, Product = "Another product" }), "Wrong product catalog is refused");
        await RejectUpdate(Encoding.UTF8.GetBytes("{}"), "Missing catalog signature is refused");
        var forged = Json.Read<SignedDocument>(Encoding.UTF8.GetString(Sign(update with { Revision = 3 })));
        forged = forged with { Signature = Convert.ToBase64String(new byte[384]) };
        await RejectUpdate(Encoding.UTF8.GetBytes(Json.Write(forged)), "Forged catalog signature is refused");
        await reject(() => ProviderCatalogUpdates.Refresh(storage, catalog, () => true, download: (_, _, _) => throw new IOException("offline")), "Offline refresh leaves installed versions usable");
        check(catalog.Get(future.Key).Version == future.Version && File.ReadAllBytes(savedPath).SequenceEqual(saved), "Offline refresh retains cached versions");
        var next = update with { Revision = 3 };
        check(!await ProviderCatalogUpdates.Refresh(storage, catalog, () => false, download: (_, _, _) => Task.FromResult(Sign(next)))
            && catalog.Document.Revision == 2 && File.ReadAllBytes(savedPath).SequenceEqual(saved), "Hidden or busy app defers catalog adoption without touching provider state");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await reject(() => ProviderCatalogUpdates.Refresh(storage, catalog, () => true, cancellation.Token, (_, _, _) => Task.FromResult(Sign(next))), "Cancelled refresh never commits a catalog");
        var newerApp = future with { Version = "next", MinimumSwitcherVersion = "999.0.0" };
        var requiresApp = new Catalog(Encoding.UTF8.GetString(Sign(update with { Revision = 3, Releases = [release, future, newerApp] })), publicKey);
        check(!requiresApp.Supports(newerApp) && requiresApp.Latest(ProviderId.Djules).Version == future.Version, "New settings can require a newer switcher without hiding compatible provider updates");
        await reject(() => Task.FromResult(requiresApp.Get(newerApp.Key)), "Unsupported settings contract cannot be activated automatically");
        File.WriteAllText(savedPath, "{}");
        var fallback = new Catalog(Encoding.UTF8.GetString(Sign(initial)), publicKey);
        ProviderCatalogUpdates.LoadSaved(storage, fallback);
        check(fallback.Document.Revision == 1, "Damaged local cache falls back to the embedded catalog without blocking startup");
        var nativeStorage = new Storage(Path.Combine(root, "native-catalog"));
        var nativeCatalog = new Catalog(Encoding.UTF8.GetString(Sign(initial)), publicKey);
        var native = future with { Provider = ProviderId.Tig3r, Version = "0.3.0-beta-v663", Repository = "tig3rmast3r/OFXR-Bridge",
            Url = "https://github.com/tig3rmast3r/OFXR-Bridge/releases/download/0.3.0-beta-v663/provider.zip",
            DjulesHybrid = false, SettingsContract = Tig3rConfiguration.Contract, MinimumSwitcherVersion = "0.1.3" };
        var nativeDocument = initial with { Revision = 3, Releases = [release, native] };
        var legacyCache = Path.Combine(nativeStorage.Root, "provider-catalog.signed.json");
        SafeFiles.AtomicWrite(legacyCache, Sign(initial));
        check(await ProviderCatalogUpdates.Refresh(nativeStorage, nativeCatalog, () => true, download: (_, _, _) => Task.FromResult(Sign(nativeDocument))),
            "A new provider can be added through the separate v2 feed");
        check(File.ReadAllBytes(legacyCache).SequenceEqual(Sign(initial)) && File.Exists(Path.Combine(nativeStorage.Root, ProviderCatalogUpdates.CacheFileName)),
            "The new cache leaves the old client's cache untouched");
        var nativeState = new AppState { ProviderRoot = Path.Combine(nativeStorage.Root, "Native Mods") };
        var nativeHost = new FakeHost(nativeStorage.Root, "{\"version\":3,\"core\":{\"enabled\":false},\"modules\":{\"framegen\":{\"enabled\":false,\"profiles\":[]}},\"applications\":[]}");
        var nativeManager = new ProviderManager(nativeStorage, nativeCatalog, nativeHost);
        SafeFiles.AtomicWrite(Path.Combine(nativeStorage.Root, "downloads", "Tig3r-" + native.Sha256 + ".zip"), package);
        var nativeInstall = await nativeManager.Install(nativeCatalog.Get(native.Key), nativeState, _ => { });
        var nativeGame = new GamePreset { Executable = gameFile, Name = "Native startup fixture", Provider = ProviderId.Tig3r };
        nativeGame.Versions[ProviderId.Tig3r] = native.Version; nativeState.Games.Add(nativeGame);
        await StartupLaunch.Apply(nativeStorage, nativeState, nativeHost, nativeCatalog, nativeGame);
        check(nativeState.ActiveProvider == ProviderId.Tig3r && nativeHost.RunningTray() == nativeInstall.Executable,
            "Offline launch-script application can use an installed Tig3r provider");
        var nextNative = native with { Version = "0.3.0-beta-v664", Url = "https://github.com/tig3rmast3r/OFXR-Bridge/releases/download/0.3.0-beta-v664/provider.zip" };
        check(await ProviderCatalogUpdates.Refresh(nativeStorage, nativeCatalog, () => true, download: (_, _, _) => Task.FromResult(Sign(nativeDocument with { Revision = 4, Releases = [release, native, nextNative] })))
            && nativeCatalog.Latest(ProviderId.Tig3r).Version == nextNative.Version,
            "Future native releases using the same settings contract require only a signed catalog update");
    }
}
