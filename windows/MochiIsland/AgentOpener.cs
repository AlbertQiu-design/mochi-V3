using System.Diagnostics;
using Mochi.Core;

namespace MochiIsland;

internal static class AgentOpener
{
    public static void Open(string source)
    {
        var plan = AgentLaunch.Plan(source);
        if (!string.IsNullOrEmpty(plan.AppPath))
        {
            if (IsClaudeCli(plan.AppPath))
            {
                OpenInTerminal(plan.AppPath);
                return;
            }
            Process.Start(new ProcessStartInfo(plan.AppPath) { UseShellExecute = true });
            return;
        }
        if (!string.IsNullOrEmpty(plan.Url))
        {
            Process.Start(new ProcessStartInfo(plan.Url) { UseShellExecute = true });
        }
    }

    /// <summary>Claude Code ships as a console program; double-clicking it would flash a console.</summary>
    private static bool IsClaudeCli(string path)
    {
        var name = Path.GetFileName(path);
        var cli = Path.Combine(".local", "bin");
        return path.Contains(cli, StringComparison.OrdinalIgnoreCase)
            && (name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("claude", StringComparison.OrdinalIgnoreCase));
    }

    private static void OpenInTerminal(string path)
    {
        try
        {
            var start = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
            start.ArgumentList.Add("-d");
            start.ArgumentList.Add(AppConfig.HomeDirectory);
            start.ArgumentList.Add(path);
            // Do not hand Claude Code the marker of whatever session launched Mochi;
            // it would disable transcript saving ("inherited CLAUDE_CODE_CHILD_SESSION").
            foreach (var key in start.Environment.Keys.ToList())
            {
                if (key.StartsWith("CLAUDE_CODE", StringComparison.OrdinalIgnoreCase) || key == "CLAUDECODE")
                {
                    start.Environment.Remove(key);
                }
            }
            Process.Start(start);
        }
        catch
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }
}
