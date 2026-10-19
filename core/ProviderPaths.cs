namespace Wsg.OpenXR;

public static class ProviderPaths
{
    public static string ValidateRoot(string path) {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Any(c => char.IsControl(c) || c == '"'))
            throw new InvalidDataException("Choose an absolute local folder, such as D:\\OpenXR Mods.");
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var drive = Path.GetPathRoot(full);
        if (drive == null || drive.Length != 3 || drive[1] != ':' || full.Length <= drive.Length)
            throw new InvalidDataException("Choose a folder on a local drive, not a drive root or network share.");
        return SafeFiles.Child(drive, full[drive.Length..].Replace('\\', '/'));
    }
    public static string Root(Storage storage, AppState state) => ValidateRoot(state.ProviderRoot ?? Path.Combine(storage.Root, "providers"));
    public static string Destination(Storage storage, AppState state, CatalogRelease release, Installation? installedVector = null) {
        if (release.Provider == ProviderId.VectorXR) {
            if (installedVector != null) return installedVector.Directory;
            return state.ProviderRoot == null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VectorXR") : SafeFiles.Child(Root(storage, state), "VectorXR");
        }
        return SafeFiles.Child(SafeFiles.Child(Root(storage, state), release.Provider.ToString()), release.Version);
    }
    public static void CheckWritable(string root) {
        root = ValidateRoot(root); Directory.CreateDirectory(root);
        var probe = SafeFiles.Child(root, ".wsg-write-test-" + Guid.NewGuid().ToString("N"));
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        stream.WriteByte(0);
    }
    public static string NsisDirectoryArgument(string destination) => "/D=" + ValidateRoot(destination);
}
