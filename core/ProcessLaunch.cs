using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Wsg.OpenXR;

public static class ProcessLaunch
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);

    public static string PhysicalFile(string path) {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute file path is required.", nameof(path));
        SafeFiles.NoLinks(path);
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (length >= buffer.Capacity) throw new PathTooLongException("The resolved file path is too long.");
        var physical = buffer.ToString();
        if (physical.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) physical = @"\\" + physical[8..];
        else if (physical.StartsWith(@"\\?\", StringComparison.Ordinal)) physical = physical[4..];
        SafeFiles.NoLinks(physical);
        return physical;
    }

    public static ProcessStartInfo ForFile(string executable, bool shellExecute) {
        // A packaged launcher can redirect AppData without giving this process package identity.
        // The elevation broker cannot see that virtual path: pass the path of the file we opened.
        executable = PhysicalFile(executable);
        return new ProcessStartInfo(executable) {
            UseShellExecute = shellExecute,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
    }

    public static Process StartInstaller(string executable, Action notStarted, bool recovery = false, bool uninstall = false, string? installDirectory = null) {
        try {
            var info = ForFile(executable, true);
            // NSIS requires /D= to be the final unquoted argument, including paths with spaces.
            // There is no command shell: only the validated local directory is passed to NSIS.
            if (installDirectory != null) {
                if (uninstall) throw new InvalidOperationException("An uninstall cannot change its destination.");
                info.Arguments = ProviderPaths.NsisDirectoryArgument(installDirectory);
            }
            return Process.Start(info) ?? throw new IOException("Windows did not return an installer process. Use Recover operation before retrying.");
        } catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException) {
            // ShellExecute failed before returning a process (including cancelled UAC).
            // No installer ran, so the pre-launch snapshot can be discarded without rollback.
            notStarted();
            var operation = uninstall ? "uninstaller" : "installer";
            var retry = recovery ? "Use Recover operation to try again. Your recovery snapshot is preserved." : "Choose Download & install to retry.";
            throw new IOException(ex is Win32Exception { NativeErrorCode: 1223 }
                ? "The " + operation + " was cancelled before it started. " + retry
                : "Windows could not start the VectorXR " + operation + ". " + retry + "\n\n" + ex.Message, ex);
        }
    }
}
