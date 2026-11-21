using Mochi.HookCLI;

namespace Mochi.Core.Tests;

public class HookCliTests
{
    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "mochi-hook-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void ShellPermissionAllowsAndContinues()
    {
        var tasks = Path.Combine(TempDir(), "tasks.json");
        var stdout = new StringWriter();
        HookSession.Run(
            new StringReader("{\"hook_event_name\":\"beforeShellExecution\",\"conversation_id\":\"abc\",\"command\":\"echo hi\"}"),
            stdout,
            [],
            new Dictionary<string, string>(),
            tasks);
        var text = stdout.ToString();
        Assert.Contains("allow", text);
        var record = Bags.Object(Bags.Object(JsonFile.Read(tasks)["tasks"])["abc"]);
        Assert.Equal("running", record["status"]);
        Assert.DoesNotContain("echo hi", File.ReadAllText(tasks));
    }

    [Fact]
    public void BrokenInputStillPrintsAnObject()
    {
        var tasks = Path.Combine(TempDir(), "tasks.json");
        var stdout = new StringWriter();
        var error = Record.Exception(() => HookSession.Run(
            new StringReader("not-json"),
            stdout,
            [],
            new Dictionary<string, string>(),
            tasks));
        Assert.Null(error);
        Assert.Contains("{", stdout.ToString());
    }

    [Fact]
    public void FailureStillReturnsControl()
    {
        var directory = TempDir();
        var blocker = Path.Combine(directory, "blocked");
        File.WriteAllText(blocker, "x");
        var stdout = new StringWriter();
        var error = Record.Exception(() => HookSession.Run(
            new StringReader("{\"hook_event_name\":\"stop\",\"conversation_id\":\"abc\"}"),
            stdout,
            ["--source", "codex"],
            new Dictionary<string, string>(),
            Path.Combine(blocker, "tasks.json")));
        Assert.Null(error);
        Assert.Contains("{}", stdout.ToString());
    }
}
