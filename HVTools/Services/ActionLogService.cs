using System.IO;

namespace HVTools.Services;

public sealed class ActionLogService
{
    private static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClusterIQ", "logs");

    private static string FilePath => Path.Combine(FolderPath, "actions.log");

    public void Write(string message)
    {
        Directory.CreateDirectory(FolderPath);
        File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
