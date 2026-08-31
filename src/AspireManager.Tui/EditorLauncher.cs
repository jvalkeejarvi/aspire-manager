using System.Diagnostics;
using AspireManager.Core;

namespace AspireManager.Tui;

internal static class EditorLauncher
{
    /// <summary>
    /// Starts the configured editor and returns immediately — the process is never awaited, so an editor
    /// that blocks until its buffer closes would have to be told not to (emacsclient's <c>-n</c>, VS Code
    /// without <c>--wait</c>). Returns the message to show.
    /// </summary>
    public static string Open(EditorSettings? editor, string file, int? line)
    {
        (string? template, int resolvedLine) = EditorCommandLine.Choose(editor, line);

        if (EditorCommandLine.Build(template, file, resolvedLine) is not { } command)
        {
            return "no editor configured; add \"editor\" to ~/.aspire-manager.json";
        }

        ProcessStartInfo info = new(command.Command) { UseShellExecute = false };
        foreach (string argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using Process? started = Process.Start(info);
            return line is { } at ? $"opened {Path.GetFileName(file)}:{at}" : $"opened {Path.GetFileName(file)}";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"could not run '{command.Command}': {e.Message}";
        }
    }
}
