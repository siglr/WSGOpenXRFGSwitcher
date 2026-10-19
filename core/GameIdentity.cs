using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Wsg.OpenXR;

public static partial class GameDiscovery
{
    [StructLayout(LayoutKind.Sequential)]
    struct FileInformation {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle OpenMetadata(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    public static string Identity(string path) {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (ArgumentException) { return "path:" + path; }
        try {
            // Game paths can legitimately be Xbox/WindowsApps aliases or hard links.
            // Read identity only; never resolve them for writes or delete either alias.
            // Store-managed games can deny reading executable contents even though
            // Windows permits querying their identity. Zero access requests metadata only.
            using var file = OpenMetadata(full, 0, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (!file.IsInvalid && GetFileInformationByHandle(file, out var info)) return $"file:{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return "path:" + full;
    }

    public static int Normalize(AppState state) {
        var removed = 0;
        foreach (var group in state.Games.GroupBy(g => Identity(g.Executable), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToArray()) {
            var keep = group.OrderByDescending(g => g.Id == state.ActiveGameId)
                .ThenByDescending(g => g.Id == state.SelectedGameId).First();
            var preferred = PreferredPath(new[] { keep.Executable }.Concat(group.Select(g => g.Executable)));
            foreach (var duplicate in group.Where(g => !ReferenceEquals(g, keep))) {
                if (!state.DuplicateGamePresets.Any(g => g.Id == duplicate.Id)) state.DuplicateGamePresets.Add(duplicate);
                foreach (var setting in duplicate.Settings) keep.Settings.TryAdd(setting.Key, setting.Value);
                foreach (var version in duplicate.Versions) keep.Versions.TryAdd(version.Key, version.Value);
                if (state.SelectedGameId == duplicate.Id) state.SelectedGameId = keep.Id;
                if (state.Startup.GameId == duplicate.Id) state.Startup.GameId = keep.Id;
                state.Games.Remove(duplicate); removed++;
            }
            if (!keep.Executable.Equals(preferred, StringComparison.OrdinalIgnoreCase)) {
                // Keep the active preset ID/settings, but show the user-facing install path.
                if (!state.DuplicateGamePresets.Any(g => g.Id == keep.Id)) state.DuplicateGamePresets.Add(Json.Read<GamePreset>(Json.Write(keep)));
                keep.Executable = preferred;
            }
        }
        return removed;
    }

    public static string PreferredPath(IEnumerable<string> aliases) => aliases
        .OrderBy(path => path.Contains(@"\XboxGames\", StringComparison.OrdinalIgnoreCase) ? 0
            : path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ? 2 : 1).First();

    public static Dictionary<string, string> Labels(IEnumerable<GamePreset> games) => games.GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
        .SelectMany(group => group.Select(g => (g.Id, Label: group.Count() == 1 ? g.Name : g.Name + " — " + g.Executable)))
        .ToDictionary(g => g.Id, g => g.Label);
}

public static class SetupSelection
{
    public static bool MatchesApplied(AppState state, GamePreset? selected, bool verified) {
        var saved = state.AppliedSetup;
        if (!verified || selected == null || saved == null || state.ActiveProvider != saved.Provider
            || saved.Id != selected.Id || saved.Provider != selected.Provider || saved.KeepVectorFeatures != selected.KeepVectorFeatures) return false;
        return selected.Provider == ProviderId.Off || (saved.Versions.GetValueOrDefault(selected.Provider) == selected.Versions.GetValueOrDefault(selected.Provider)
            && saved.Settings.GetValueOrDefault(selected.Provider, EngineSettings.Defaults(selected.Provider)) == selected.Settings.GetValueOrDefault(selected.Provider, EngineSettings.Defaults(selected.Provider)));
    }
}
