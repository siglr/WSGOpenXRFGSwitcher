using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

// Test-only ephemeral signing key; never compiled into either shipping executable.
internal static class ReleasePublicKey
{
    internal static RSA Key = RSA.Create(3072);
    internal static string Xml => Key.ToXmlString(false);
}

internal static class UpdateTests
{
    static int checks;
    static string workspace = "";
    static string Self => Environment.ProcessPath!;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        checks++; Console.WriteLine("PASS: " + name);
    }
    static void Reject(Action action, string name)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or CryptographicException or FormatException or System.Text.Json.JsonException) { Check(true, name); return; }
        throw new Exception("FAILED: accepted " + name);
    }
    static string Fresh(string name) { var path = Path.Combine(workspace, name + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    static string Sign(byte[] data) => Convert.ToBase64String(ReleasePublicKey.Key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    static string KeyId => ReleaseTrust.Digest(Encoding.UTF8.GetBytes(ReleasePublicKey.Xml));
    static AppUpdate.Files Fixture(string path, string version, bool includeRuntime = true)
    {
        foreach (var name in AppUpdate.ManagedNames)
        {
            if (name == AppUpdate.Exe || name == AppUpdate.HelperExe) File.Copy(Self, Path.Combine(path, name));
            else File.WriteAllText(Path.Combine(path, name), name == "VERSION" ? version : name + " version " + version);
        }
        var inventory = new AppUpdate.Files { schema = 1, product = AppUpdate.Product, version = version,
            files = AppUpdate.ManagedNames.ToDictionary(n => n, n => AppUpdate.HashFile(Path.Combine(path, n))), keyId = KeyId };
        if (includeRuntime) {
            File.WriteAllText(Path.Combine(path, "System.FixtureRuntime.dll"), "runtime-" + version);
            inventory.files["System.FixtureRuntime.dll"] = AppUpdate.HashFile(Path.Combine(path, "System.FixtureRuntime.dll"));
        }
        inventory.signature = Sign(ReleaseTrust.InventoryMessage(inventory.product, inventory.version, inventory.files));
        File.WriteAllText(Path.Combine(path, AppUpdate.Inventory), AppUpdate.Json(inventory));
        return inventory;
    }
    static byte[] Zip(string path, string? extra = null)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var file in Directory.GetFiles(path)) zip.CreateEntryFromFile(file, Path.GetFileName(file));
            if (extra != null) { using var writer = new StreamWriter(zip.CreateEntry(extra).Open()); writer.Write("bad"); }
        }
        return memory.ToArray();
    }
    static AppUpdate.Release Release(byte[] zip, string version = "1.2.1")
    {
        var release = new AppUpdate.Release { schema = 1, product = AppUpdate.Product, version = version,
            size = zip.LongLength, sha256 = AppUpdate.Hash(zip), keyId = KeyId };
        release.signature = Sign(ReleaseTrust.ReleaseMessage(release.product, release.version, release.sha256, release.size));
        return release;
    }
    static Process Child(params string[] args)
    {
        var info = new ProcessStartInfo(Self) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info)!;
    }
    static async Task<int> Main(string[] args)
    {
        var keyPath = Environment.GetEnvironmentVariable("WSG_UPDATER_TEST_KEY");
        if (keyPath != null) ReleasePublicKey.Key.FromXmlString(File.ReadAllText(keyPath));
        if (args.Length > 0 && args[0] is "--apply-app-update" or "--recover-app-update") return AppUpdate.Helper(args);
        if (args.Length > 0 && args[0] == "crash") { AppUpdate.Apply(args[1], index => { if (index == int.Parse(args[2])) Environment.Exit(71); }); return 3; }
        if (args.Length > 0 && args[0] == "handoff")
        {
            using var gate = AppUpdate.Enter(args[1]);
            AppUpdate.Launch(args[1], false, Path.Combine(args[1], "custom output"), true);
            Thread.Sleep(800); // Keep ownership of the thread-affine named mutex.
            if (AppUpdate.ReadInventory(args[1], true).version != "1.2.0") throw new Exception("Updated before parent exit");
            return 0;
        }
        var reopened = Environment.GetEnvironmentVariable("WSG_UPDATER_TEST_REOPEN");
        if (reopened != null) { File.WriteAllText(reopened, string.Join("|", args)); return 0; }

        workspace = Path.Combine(Path.GetTempPath(), "WSG-updater-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        keyPath = Path.Combine(workspace, "ephemeral-test-key.xml");
        File.WriteAllText(keyPath, ReleasePublicKey.Key.ToXmlString(true));
        Environment.SetEnvironmentVariable("WSG_UPDATER_TEST_KEY", keyPath);
        try
        {
            Check(AppUpdate.ParseVersion("1.2.10") > AppUpdate.ParseVersion("1.2.9"), "numeric version ordering");
            foreach (var version in new[] { "1.2", "01.2.3", "1.2.3.4", "v1.2.3", "1.2.3\n" }) Reject(() => AppUpdate.ParseVersion(version), "invalid version " + version.Trim());
            foreach (var url in new[] { "http://wesimglide.org/otherdata/tools/WSG-OpenXR-FG-Switcher.zip", "https://example.com/update.zip", AppUpdate.Archive + "?redirect=1" })
                Reject(() => AppUpdate.CheckUrl(new Uri(url)), "reject noncanonical update URL");
            var fresh = Fresh("new"); Fixture(fresh, "1.2.1"); var zip = Zip(fresh); var release = Release(zip);
            Check(AppUpdate.ParseRelease(Encoding.UTF8.GetBytes(AppUpdate.Json(release))).version == "1.2.1", "signed release metadata verifies");
            var tampered = Release(zip); tampered.version = "1.2.2";
            Reject(() => AppUpdate.ParseRelease(Encoding.UTF8.GetBytes(AppUpdate.Json(tampered))), "altered metadata signature rejected");
            var root = Fresh("upgrade"); Fixture(root, "1.2.0");
            File.WriteAllText(Path.Combine(root, "settings.json"), "user configuration"); Directory.CreateDirectory(Path.Combine(root, "Output"));
            File.WriteAllText(Path.Combine(root, "Output", "altitude.txt"), "12,345");
            AppUpdate.Prepare(root, release, (_, _) => zip);
            Check(AppUpdate.ReadInventory(root, true).version == "1.2.0", "staging leaves installed version untouched");
            AppUpdate.Apply(root, null!);
            Check(AppUpdate.ReadInventory(root, true).version == "1.2.1", "successful update commits signed inventory");
            Check(File.ReadAllText(Path.Combine(root, "settings.json")) == "user configuration" && File.ReadAllText(Path.Combine(root, "Output", "altitude.txt")) == "12,345", "configuration and OBS output survive update");
            Check(!Directory.Exists(Path.Combine(root, AppUpdate.StageName)), "successful update cleans its staging files");

            var compact = Fresh("app-only"); Fixture(compact, "1.2.1", includeRuntime: false); var compactZip = Zip(compact);
            root = Fresh("runtime-removal"); Fixture(root, "1.2.0");
            File.WriteAllText(Path.Combine(root, "user-added.dll"), "keep");
            AppUpdate.Prepare(root, Release(compactZip), (_, _) => compactZip); AppUpdate.Apply(root, null!);
            Check(!File.Exists(Path.Combine(root, "System.FixtureRuntime.dll")) && !AppUpdate.ReadInventory(root, true).files.ContainsKey("System.FixtureRuntime.dll"), "App-only upgrade removes obsolete inventoried runtime files");
            Check(File.ReadAllText(Path.Combine(root, "user-added.dll")) == "keep", "Runtime removal preserves files outside the signed inventory");

            root = Fresh("checksum"); Fixture(root, "1.2.0"); var broken = zip.ToArray(); broken[20] ^= 1;
            Reject(() => AppUpdate.Prepare(root, release, (_, _) => broken), "bad ZIP checksum rejected");
            foreach (var entry in new[] { "../outside.txt", "C:/outside.txt", "settings.json", "Output/altitude.txt", AppUpdate.Exe.ToUpperInvariant() })
            {
                root = Fresh("unsafe"); Fixture(root, "1.2.0"); var unsafeZip = Zip(fresh, entry);
                Reject(() => AppUpdate.Prepare(root, Release(unsafeZip), (_, _) => unsafeZip), "reject unsafe/unlisted ZIP entry " + entry);
                Check(AppUpdate.ReadInventory(root, true).version == "1.2.0", "invalid package preserves installed files");
            }
            root = Fresh("modified"); Fixture(root, "1.2.0"); File.AppendAllText(Path.Combine(root, "README.md"), "user change");
            Reject(() => AppUpdate.Prepare(root, release, (_, _) => zip), "modified installed files are preserved");
            root = Fresh("locked"); Fixture(root, "1.2.0"); AppUpdate.Prepare(root, release, (_, _) => zip);
            using (var locked = new FileStream(Path.Combine(root, "WSG OpenXR FG Switcher.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { AppUpdate.Apply(root, null!); throw new Exception("Locked update unexpectedly succeeded"); } catch (IOException) { }
            }
            Check(AppUpdate.ReadInventory(root, true).version == "1.2.0", "locked-file failure rolls back complete old version");

            foreach (var crashIndex in new[] { 4, -1 })
            {
                root = Fresh("crash"); Fixture(root, "1.2.0"); AppUpdate.Prepare(root, release, (_, _) => zip);
                using var child = Child("crash", root, crashIndex.ToString());
                Check(child.WaitForExit(10000) && child.ExitCode == 71, "injected process interruption");
                AppUpdate.Recover(root);
                Check(AppUpdate.ReadInventory(root, true).version == (crashIndex == -1 ? "1.2.1" : "1.2.0"), "crash recovery honors commit boundary");
            }
            root = Fresh("defer"); Fixture(root, "1.2.0"); var messages = new List<string>(); var launched = false;
            var didRestart = await AppUpdate.CheckForUpdateAsync(root, messages.Add, () => false,
                (uri, _) => uri.AbsoluteUri == AppUpdate.Feed ? Encoding.UTF8.GetBytes(AppUpdate.Json(release)) : zip, () => launched = true);
            Check(!didRestart && !launched && messages.Any(s => s.StartsWith("Update ready")), "active-stream update is staged without restart");
            root = Fresh("offline"); Fixture(root, "1.2.0"); messages.Clear();
            Check(!await AppUpdate.CheckForUpdateAsync(root, messages.Add, () => true, (_, _) => throw new IOException("offline"), () => throw new Exception("must not launch")), "offline server does not stop the app");

            root = Fresh("handoff"); Fixture(root, "1.2.0"); AppUpdate.Prepare(root, release, (_, _) => zip);
            var marker = Path.Combine(root, "reopened.txt");
            Environment.SetEnvironmentVariable("WSG_UPDATER_TEST_REOPEN", marker);
            using (var parent = Child("handoff", root)) Check(parent.WaitForExit(10000) && parent.ExitCode == 0, "real helper waits for parent process to exit");
            for (var i = 0; i < 100 && !File.Exists(marker); i++) await Task.Delay(100);
            Check(File.Exists(marker), "real helper reopens updated app");
            Check(File.ReadAllText(marker) == "--output|" + Path.Combine(root, "custom output") + "|--minimized", "restart preserves custom output and minimized state");
            Check(AppUpdate.ReadInventory(root, true).version == "1.2.1", "real helper commits a verified upgrade");
            Environment.SetEnvironmentVariable("WSG_UPDATER_TEST_REOPEN", null);
            await Task.Delay(300);
            AppUpdate.Startup(root);
            Check(!Directory.Exists(Path.Combine(root, AppUpdate.StageName)), "next launch cleans finished helper files");
            Console.WriteLine($"All {checks} updater checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable("WSG_UPDATER_TEST_KEY", null);
            Environment.SetEnvironmentVariable("WSG_UPDATER_TEST_REOPEN", null);
            ReleasePublicKey.Key.Dispose();
            try { Directory.Delete(workspace, true); }
            catch (IOException ex) { Console.Error.WriteLine("Test cleanup postponed: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Console.Error.WriteLine("Test cleanup postponed: " + ex.Message); }
        }
    }
}
