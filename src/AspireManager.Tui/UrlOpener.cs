using System.Diagnostics;
using AspireManager.Core;

namespace AspireManager.Tui;

internal static class UrlOpener
{
    /// <summary>
    /// Hands the URL to the desktop's default handler. Refuses anything that is not http(s): the string
    /// comes from the AppHost and goes to the operating system's shell, so the scheme is checked here and
    /// not merely when the list is built.
    /// </summary>
    public static string Open(string url)
    {
        if (!ShellModel.IsOpenable(url))
        {
            return $"refused to open {url}";
        }

        try
        {
            // UseShellExecute picks open/xdg-open/the shell association, so this works on every platform.
            using Process? started = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return $"opened {url}";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"could not open {url}";
        }
    }
}
