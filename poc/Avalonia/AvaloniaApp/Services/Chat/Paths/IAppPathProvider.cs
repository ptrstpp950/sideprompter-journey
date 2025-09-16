using System.IO;

namespace AvaloniaApp.Services.Chat;

public interface IAppPathProvider
{
    string GetAppDataDirectory();
    string GetChatsDirectory();
}

public class AppPathProvider : IAppPathProvider
{
    public string GetAppDataDirectory()
    {
        string baseDir;
#if WINDOWS
        baseDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(baseDir, "SidePrompter");
#elif MACOS || OSX || MACCATALYST
        baseDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(baseDir, "Application Support", "SidePrompter");
#else
        baseDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(baseDir, "SidePrompter");
#endif
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetChatsDirectory()
    {
        var dir = Path.Combine(GetAppDataDirectory(), "Chats");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
