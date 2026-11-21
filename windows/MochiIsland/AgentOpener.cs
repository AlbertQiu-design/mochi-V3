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
            Process.Start(new ProcessStartInfo(plan.AppPath) { UseShellExecute = true });
            return;
        }
        if (!string.IsNullOrEmpty(plan.Url))
        {
            Process.Start(new ProcessStartInfo(plan.Url) { UseShellExecute = true });
        }
    }
}
