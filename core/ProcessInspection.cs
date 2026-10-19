using System.ComponentModel;

namespace Wsg.OpenXR;

public static class ProcessInspection
{
    // Keep OS property reads separate from application blockers. Process exit messages
    // vary by property and Windows language; never identify this race by message text.
    public static T? Read<T>(Func<T> read) where T : class {
        try { return read(); }
        catch (InvalidOperationException) { return null; }
        catch (Win32Exception) { return null; }
        catch (NotSupportedException) { return null; }
    }

    public static string? IdleBlockReason(int id, Func<string> name, Func<string[]> modules, ISet<string> games) {
        var processName = Read(name);
        if (processName == null) return null;
        if (games.Contains(processName)) return "Close " + processName + " before changing the setup.";
        if (WindowsHost.IsVectorEditor(processName)) return "Close the VectorXR settings app before applying a setup. Its layer remains available.";
        if (id == Environment.ProcessId || processName.Equals("OFXRBridgeTray", StringComparison.OrdinalIgnoreCase)) return null;
        var moduleNames = Read(modules);
        if (moduleNames?.Any(module => module.Contains("XRFrameBridge", StringComparison.OrdinalIgnoreCase)
            || module.Equals("XR_APILAYER_DIENERTECH_VECTORXR.dll", StringComparison.OrdinalIgnoreCase)) == true)
            return "Close " + processName + ": an OpenXR provider is loaded in it.";
        return null;
    }
}
