namespace GlassBeat;
internal static class DiagnosticLog
{
    public static void Write(string text)
    {
        if (!Environment.GetCommandLineArgs().Contains("--diagnostics")) return;
        try
        {
            Directory.CreateDirectory(PreferencesStore.Folder);
            var path = Path.Combine(PreferencesStore.Folder, "diagnostics.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {text}\n");
        }
        catch { }
    }
}
