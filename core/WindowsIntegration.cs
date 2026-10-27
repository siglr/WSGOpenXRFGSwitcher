using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

namespace Wsg.OpenXR;

public static class Authenticode
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileInfo { public uint Size; public string Path; public IntPtr Handle, Subject; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TrustData { public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Choice; public IntPtr File; public uint State; public IntPtr StateData; public IntPtr Url; public uint Flags, Context; }
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
    static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref TrustData data);
    public static void RequireDienerTech(string path) {
        SafeFiles.NoLinks(path);
        var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = Path.GetFullPath(path) };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try {
            Marshal.StructureToPtr(file, pointer, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = pointer, State = 1, Flags = 0x1000 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            data.State = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (result != 0) throw new InvalidDataException("VectorXR's Windows signature could not be verified. File preserved: " + path);
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            if (!certificate.GetNameInfo(X509NameType.SimpleName, false).Equals("DienerTech LLC", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected VectorXR publisher.");
        } finally { Marshal.DestroyStructure<FileInfo>(pointer); Marshal.FreeHGlobal(pointer); }
    }
}
public sealed record LayerRegistration(string Hive, int View, string Key, string Manifest, int Value, string LayerName, string Library)
{
    public bool Vector => LayerName == "XR_APILAYER_DIENERTECH_VECTORXR";
    public bool Ofxr => LayerName.Contains("XRFrameBridge", StringComparison.OrdinalIgnoreCase) || LayerName.Contains("OFXR", StringComparison.OrdinalIgnoreCase);
    public bool Enabled => Value == 0;
    public string Identity => Hive + ":" + View + ":" + Key + ":" + Manifest;
}
public sealed record RegistryChange(LayerRegistration Before, int After);
public static class OpenXrRegistry
{
    public const string OpenXr = @"SOFTWARE\Khronos\OpenXR\1\ApiLayers\Implicit";
    public const string Vulkan = @"SOFTWARE\Khronos\Vulkan\ImplicitLayers";
    public static List<LayerRegistration> Read() {
        var entries = new List<LayerRegistration>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        foreach (var name in new[] { OpenXr, Vulkan }) {
            // HKCU\Software\Khronos is shared between WOW64 views; enumerate it once.
            if (hive == RegistryHive.CurrentUser && view == RegistryView.Registry32) continue;
            using var root = RegistryKey.OpenBaseKey(hive, view); using var key = root.OpenSubKey(name);
            if (key == null) continue;
            foreach (var manifest in key.GetValueNames()) {
                var value = key.GetValue(manifest); if (value is not int number) continue;
                string layer = "Unknown", library = "";
                try {
                    if (new System.IO.FileInfo(manifest).Length > 1024 * 1024) continue;
                    var json = JsonNode.Parse(File.ReadAllText(manifest)); var data = json?["api_layer"] ?? json?["layer"];
                    layer = data?["name"]?.GetValue<string>() ?? "Unknown";
                    var relative = data?["library_path"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(relative)) library = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest)!, relative));
                } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) {
                    // Still identify known stale entries so they cannot disappear from conflict detection.
                    var file = Path.GetFileName(manifest);
                    if (file.Contains("XRFrameBridge", StringComparison.OrdinalIgnoreCase) || file.Contains("OFXR", StringComparison.OrdinalIgnoreCase)) layer = "OFXR stale registration";
                    if (file == "XR_APILAYER_DIENERTECH_VECTORXR.json") layer = "XR_APILAYER_DIENERTECH_VECTORXR";
                }
                entries.Add(new(hive == RegistryHive.CurrentUser ? "User" : "Machine", view == RegistryView.Registry64 ? 64 : 32, name, manifest, number, layer, library));
            }
        }
        return entries;
    }
    public static void SetVectorBatch(IReadOnlyList<RegistryChange> changes) {
        if (changes.Count > 16) throw new InvalidDataException("Too many layer changes.");
        foreach (var c in changes) Validate(c);
        var applied = new List<RegistryChange>();
        try {
            foreach (var c in changes) { Write(c.Before, c.Before.Value, c.After); applied.Add(c); }
        } catch {
            foreach (var c in applied.AsEnumerable().Reverse()) Write(c.Before, c.After, c.Before.Value);
            throw;
        }
    }
    static void Validate(RegistryChange c) {
        var r = c.Before;
        if (!r.Vector || r.Key != OpenXr || r.Hive is not ("Machine" or "User") || r.View is not (32 or 64) || r.Value is not (0 or 1) || c.After is not (0 or 1) || Path.GetFileName(r.Manifest) != "XR_APILAYER_DIENERTECH_VECTORXR.json") throw new InvalidDataException("The helper only changes existing VectorXR enable flags.");
        SafeFiles.NoLinks(r.Manifest);
        var layer = JsonNode.Parse(File.ReadAllText(r.Manifest))?["api_layer"];
        if (layer?["name"]?.GetValue<string>() != "XR_APILAYER_DIENERTECH_VECTORXR") throw new InvalidDataException("Invalid VectorXR manifest.");
        var library = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(r.Manifest)!, layer["library_path"]!.GetValue<string>()));
        if (!library.Equals(r.Library, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Layer changed since inspection.");
        Authenticode.RequireDienerTech(library);
        using var root = RegistryKey.OpenBaseKey(r.Hive == "Machine" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, r.View == 64 ? RegistryView.Registry64 : RegistryView.Registry32);
        using var key = root.OpenSubKey(r.Key);
        if (key?.GetValue(r.Manifest) is not int current || current != r.Value) throw new InvalidDataException("Layer registration changed. Refresh and retry.");
    }
    static void Write(LayerRegistration r, int expected, int desired) {
        using var root = RegistryKey.OpenBaseKey(r.Hive == "Machine" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, r.View == 64 ? RegistryView.Registry64 : RegistryView.Registry32);
        using var key = root.OpenSubKey(r.Key, true) ?? throw new IOException("Layer key disappeared.");
        if (key.GetValue(r.Manifest) is not int current || current != expected) throw new IOException("Layer registration changed during the operation.");
        key.SetValue(r.Manifest, desired, RegistryValueKind.DWord); key.Flush();
    }
    public static string Runtime() {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1");
        return key?.GetValue("ActiveRuntime") as string ?? "OpenXR runtime not registered";
    }
}
public interface ISwitchHost
{
    string TraySettingsPath { get; }
    string VectorSettingsPath { get; }
    string Tig3rSettingsPath => Path.Combine(Path.GetDirectoryName(TraySettingsPath)!, "tig3r", "ofxr-bridge.ini");
    string Tig3rGameSettingsPath(GamePreset game) => Tig3rConfiguration.GamePath(Tig3rSettingsPath, game.Executable);
    IEnumerable<string> Tig3rConfigurationPaths() => new[] { Tig3rSettingsPath }.Concat(Tig3rConfiguration.Profiles(Tig3rSettingsPath));
    IReadOnlyList<LayerRegistration> Layers();
    void RequireIdle(IEnumerable<GamePreset> games);
    string? RunningTray();
    Task StopTray();
    Task StartTray(string executable);
    Task SetVector(IReadOnlyList<RegistryChange> changes);
    Task Verify(Installation? installation, bool keepVector, CatalogRelease? release = null);
}
public sealed class WindowsHost : ISwitchHost
{
    Process? startingTray;
    public string TraySettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OFXR Bridge", "tray.ini");
    public string Tig3rSettingsPath {
        get {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OFXR_BRIDGE_PROFILE_DIR")))
                throw new InvalidOperationException("A custom OFXR_BRIDGE_PROFILE_DIR is set. Remove the override before WSG manages tig3rmast3r OFXR.");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ofxr-bridge", "ofxr-bridge.ini");
        }
    }
    public string VectorSettingsPath {
        get {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VECTORXR_CONFIG_PATH"))) throw new InvalidOperationException("A custom VECTORXR_CONFIG_PATH is set. Remove the override before WSG manages VectorXR.");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VectorXR", "config", "settings.json");
        }
    }
    public IReadOnlyList<LayerRegistration> Layers() => OpenXrRegistry.Read();
    public void RequireIdle(IEnumerable<GamePreset> games) {
        if (Tig3rSettingsEditorOpen()) throw new InvalidOperationException("Close tig3rmast3r's settings window and Rescan setup before applying here.");
        var names = games.Select(g => Path.GetFileNameWithoutExtension(g.Executable)).Where(n => n.Length > 0).Append("FlightSimulator2024").Append("FlightSimulator").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses()) using (process) {
            var blocker = ProcessInspection.IdleBlockReason(process.Id, () => process.ProcessName,
                () => process.Modules.Cast<ProcessModule>().Select(module => module.ModuleName).ToArray(), names);
            if (blocker != null) throw new InvalidOperationException(blocker);
        }
    }
    public string? RunningTray() {
        var processes = TrayWindows().Keys.Select(FindProcess).OfType<Process>().ToArray();
        try {
            if (processes.Length > 1) throw new InvalidOperationException("Multiple OFXR trays are running. Close them before continuing.");
            var process = processes.SingleOrDefault();
            if (process == null) return null;
            try { return process.MainModule?.FileName; }
            catch (InvalidOperationException) { return null; }
        } finally { foreach (var p in processes) p.Dispose(); }
    }
    static Process? FindProcess(int id) {
        try { return Process.GetProcessById(id); }
        catch (ArgumentException) { return null; }
    }
    public static bool IsVectorEditor(string name) => name.Equals("vectorxr-app", StringComparison.OrdinalIgnoreCase) || name.Equals("VectorXR", StringComparison.OrdinalIgnoreCase);
    delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    static Dictionary<int, IntPtr> TrayWindows() {
        var result = new Dictionary<int, IntPtr>();
        EnumWindows((window, _) => {
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (name.ToString() == "OFXRBridgeTrayWindow") {
                GetWindowThreadProcessId(window, out var pid);
                using var process = FindProcess((int)pid);
                if (process != null && ProcessInspection.Read(() => process.ProcessName)?.Equals("OFXRBridgeTray", StringComparison.OrdinalIgnoreCase) == true) result[(int)pid] = window;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    static bool Tig3rSettingsEditorOpen() {
        var open = false;
        EnumWindows((window, _) => {
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (name.ToString() == "OFXRBridgeSettingsV449") {
                GetWindowThreadProcessId(window, out var pid);
                using var process = FindProcess((int)pid);
                if (process != null && ProcessInspection.Read(() => process.ProcessName)?.Equals("OFXRBridgeTray", StringComparison.OrdinalIgnoreCase) == true) { open = true; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return open;
    }
    public async Task StopTray() {
        if (startingTray != null && !startingTray.HasExited && !TrayWindows().ContainsKey(startingTray.Id))
            throw new IOException("The provider is still starting or showing a startup error. Close it manually before recovering; WSG did not terminate it.");
        foreach (var pair in TrayWindows()) using (var p = FindProcess(pair.Key)) {
            if (p == null) continue;
            PostMessage(pair.Value, 0x0010, IntPtr.Zero, IntPtr.Zero);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try { await p.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { throw new IOException("OFXR did not close cleanly. Close its tray manually, then use Recover. WSG did not terminate it."); }
        }
        for (var attempt = 0; attempt < 20; attempt++) {
            if (!Layers().Any(r => r.Ofxr && r.Enabled)) return;
            await Task.Delay(100);
        }
        throw new IOException("An OFXR registration remains after the tray closed. Use its original tray to disarm it before retrying.");
    }
    public async Task StartTray(string executable) {
        SafeFiles.NoLinks(executable);
        startingTray?.Dispose();
        startingTray = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! }) ?? throw new IOException("OFXR did not start.");
        for (var attempt = 0; attempt < 60; attempt++) {
            if (startingTray.HasExited) throw new IOException("OFXR exited during startup. Check its dependencies and provider logs.");
            if (TrayWindows().ContainsKey(startingTray.Id)) { startingTray.Dispose(); startingTray = null; return; }
            await Task.Delay(200);
        }
        throw new IOException("OFXR did not expose its tray window. Close any provider startup error and use Recover.");
    }
    public async Task SetVector(IReadOnlyList<RegistryChange> changes) {
        if (changes.Count == 0) return;
        var machine = changes.Where(c => c.Before.Hive == "Machine").ToList();
        if (machine.Count > 0) {
            var helper = Path.Combine(AppContext.BaseDirectory, "WSG OpenXR FG Switcher.LayerHelper.exe");
            var info = ProcessLaunch.ForFile(helper, true);
            info.Verb = "runas"; info.WindowStyle = ProcessWindowStyle.Hidden;
            info.ArgumentList.Add("--vector-flags"); info.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Write(machine))));
            using var process = Process.Start(info) ?? throw new IOException("Unable to launch layer helper.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException("VectorXR registration change failed (code " + process.ExitCode + "). Refresh or recover; no unrelated layers were changed.");
        }
        OpenXrRegistry.SetVectorBatch(changes.Where(c => c.Before.Hive == "User").ToArray());
    }
    public async Task Verify(Installation? installation, bool keepVector, CatalogRelease? release = null) {
        var ofxr = installation != null && ProviderInfo.IsOfxr(installation.Provider);
        var vector = installation?.Provider == ProviderId.VectorXR || keepVector;
        for (var attempt = 0; attempt < 30; attempt++) {
            var layers = Layers(); var xr = layers.Where(r => r.Ofxr && r.Enabled && r.Key == OpenXrRegistry.OpenXr).ToArray();
            var vk = layers.Where(r => r.Ofxr && r.Enabled && r.Key == OpenXrRegistry.Vulkan).ToArray();
            var vectors = layers.Where(r => r.Vector && r.Enabled).ToArray();
            var ready = ofxr ? RunningTray()?.Equals(installation!.Executable, StringComparison.OrdinalIgnoreCase) == true
                && OfxrActivation.Matches(installation!, layers, release) : xr.Length == 0 && vk.Length == 0 && RunningTray() == null;
            ready &= vector ? vectors.Count(r => r.View == 64) == 1 : vectors.Length == 0;
            if (ready) return;
            await Task.Delay(200);
        }
        throw new IOException("Provider activation could not be verified. The previous setup will be restored.");
    }
}
