using System.Text.Json;
using Mochi.Core;

namespace Mochi.HookCLI;

public static class HookSession
{
    public static void Run(TextReader input, TextWriter output, string[] args, System.Collections.IDictionary environment, string tasksPath)
    {
        try
        {
            var payload = ReadPayload(input);
            var outcome = HookEvents.Interpret(
                Flag(args, "--source"),
                payload,
                EnvironmentMap(environment),
                Flag(args, "--event"));
            if (outcome != null)
            {
                WithLock(tasksPath, () =>
                {
                    HookEvents.Commit(outcome, tasksPath);
                    if (outcome.WaitsForPermission)
                    {
                        HookEvents.Acknowledge(outcome.Id, outcome.RequestID, tasksPath);
                    }
                });
                if (outcome.WaitsForPermission)
                {
                    WriteObject(output, HookEvents.PermissionStdout(outcome.PermissionStyle, "allow"));
                    return;
                }
            }
            WriteObject(output, new Dictionary<string, object?>());
        }
        catch
        {
            try
            {
                output.Write("{}\n");
                output.Flush();
            }
            catch
            {
                // A hook that fails must still let the agent continue.
            }
        }
    }

    private static void WithLock(string tasksPath, Action body)
    {
        FileStream? gate = null;
        try
        {
            var directory = Path.GetDirectoryName(tasksPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            gate = new FileStream(tasksPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch
        {
            gate = null;
        }
        try
        {
            body();
        }
        finally
        {
            gate?.Dispose();
        }
    }

    private static Dictionary<string, object?> ReadPayload(TextReader input)
    {
        var text = input.ReadToEnd();
        if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object?>();
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, object?>();
            return JsonFile.AsObject(document.RootElement);
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private static Dictionary<string, string> EnvironmentMap(System.Collections.IDictionary environment)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in environment.Keys)
        {
            if (key == null) continue;
            var name = key.ToString() ?? "";
            if (name.Length == 0) continue;
            map[name] = environment[key]?.ToString() ?? "";
        }
        return map;
    }

    private static string? Flag(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == name) return args[index + 1];
        }
        return null;
    }

    private static void WriteObject(TextWriter output, Dictionary<string, object?> value)
    {
        output.Write(JsonFile.Serialize(value));
        output.Write('\n');
        output.Flush();
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var environment = Environment.GetEnvironmentVariables();
            HookSession.Run(Console.In, Console.Out, args, environment, AppConfig.TasksFile);
        }
        catch
        {
            try { Console.Out.Write("{}\n"); } catch { }
        }
        return 0;
    }
}
