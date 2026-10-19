internal static class UpdaterProgram
{
    private static int Main(string[] args)
    {
        try { return AppUpdate.Helper(args); }
        catch (Exception ex)
        {
            // Recovery data stays beside the app; never discard it on failure.
            var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSG OpenXR FG Switcher");
            try { Directory.CreateDirectory(logs); File.AppendAllText(Path.Combine(logs, "updater.log"), $"{DateTimeOffset.Now:O} {ex}\n"); }
            catch { }
            return 1;
        }
    }
}

