using System.Security.Cryptography;
using System.Text;

namespace Wsg.OpenXR;

public sealed partial class Storage
{
    public string Root { get; }
    public string StatePath => Path.Combine(Root, "settings.json");
    public string JournalPath => Path.Combine(Root, "recovery.json");
    public Storage(string? root = null) { Root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG OpenXR FG Switcher")); Directory.CreateDirectory(Root); SafeFiles.NoLinks(Root); }
    public AppState Load() {
        if (!File.Exists(StatePath)) return new();
        var state = Json.Read<AppState>(File.ReadAllText(StatePath));
        if (state.Schema != 1) throw new InvalidDataException("Unsupported WSG settings format. Your settings have been preserved.");
        return state;
    }
    public void Save(AppState state) => SafeFiles.AtomicWrite(StatePath, Json.Write(state));
    public string PresetPath(string gameId, ProviderId provider, string version = "") {
        if (!Guid.TryParseExact(gameId, "N", out _)) throw new InvalidDataException("Invalid preset identifier.");
        var suffix = version.Length == 0 ? "" : "-" + SafeFiles.Hash(Encoding.UTF8.GetBytes(version))[..16];
        return Path.Combine(Root, "presets", gameId, provider + suffix + ".ini");
    }
    public void Log(string message) { try { File.AppendAllText(Path.Combine(Root, "switcher.log"), $"{DateTimeOffset.Now:O} {message}\n"); } catch (IOException) { } }
    public void AddNote(ComparisonNote note) {
        var path = Path.Combine(Root, "notes.json");
        var notes = File.Exists(path) ? Json.Read<List<ComparisonNote>>(File.ReadAllText(path)) : [];
        notes.Add(note); SafeFiles.AtomicWrite(path, Json.Write(notes));
    }
    public void ExportNotes(string path) {
        var source = Path.Combine(Root, "notes.json");
        var notes = File.Exists(source) ? Json.Read<List<ComparisonNote>>(File.ReadAllText(source)) : [];
        static string Cell(string? text) {
            text ??= "";
            if (text.TrimStart().StartsWithAny('=', '+', '-', '@', '\t', '\r')) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        var output = new StringBuilder("Time,Game,Executable,Provider,Version,Settings,VectorFeatures,Runtime,RenderFPS,DisplayFPS,Notes,SetupSource\r\n");
        foreach (var n in notes) output.AppendLine(string.Join(',', new[] { n.Time.ToString("O"), n.Game, n.Executable, ProviderInfo.NameOf(n.Provider), n.Version, Json.Write(n.Settings), n.KeepVectorFeatures.ToString(), n.Runtime, n.RenderFps, n.DisplayFps, n.Notes, n.SetupSource }.Select(Cell)));
        File.WriteAllText(path, output.ToString(), new UTF8Encoding(true));
    }
    public List<ComparisonNote> ReadNotes() {
        var path = Path.Combine(Root, "notes.json");
        return File.Exists(path) ? Json.Read<List<ComparisonNote>>(File.ReadAllText(path)) : [];
    }
}
public static class SafeFiles
{
    public static bool StartsWithAny(this string value, params char[] chars) => value.Length > 0 && chars.Contains(value[0]);
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string? HashFile(string path) { NoLinks(path); return File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null; }
    public static void NoLinks(string path) {
        for (string? part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files and directories are not supported: " + part);
    }
    public static string Child(string root, string relative) {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || Path.IsPathRooted(relative)) throw new InvalidDataException("Invalid relative filename.");
        foreach (var part in relative.Split('/')) {
            if (part is "" or "." or ".." || part.TrimEnd(' ', '.') != part || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || System.Text.RegularExpressions.Regex.IsMatch(part.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidDataException("Unsafe filename: " + relative);
        }
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path leaves its package.");
        NoLinks(path); return path;
    }
    public static void AtomicWrite(string path, string content) => AtomicWrite(path, Encoding.UTF8.GetBytes(content));
    public static void AtomicWrite(string path, byte[] content) {
        NoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { stream.Write(content); stream.Flush(true); }
            File.Move(temp, path, true);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
