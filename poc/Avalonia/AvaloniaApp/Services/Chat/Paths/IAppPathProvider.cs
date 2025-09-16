using System;
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
        var appSupport = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appSupport, "SidePrompter");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string GetChatsDirectory()
    {
        var dir = Path.Combine(GetAppDataDirectory(), "Chats");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
