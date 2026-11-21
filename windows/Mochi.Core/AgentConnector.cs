namespace Mochi.Core;

public interface IAgentConnector
{
    string Id { get; }
    string DisplayName { get; }
    bool NeedsPrivacyAck { get; }
    HookInstallResult Status(string binaryPath);
    HookInstallResult Connect(string binaryPath, bool privacyAccepted);
    HookInstallResult Disconnect(string binaryPath);
    bool Retarget(string binaryPath);
}

public static class AgentConnectorRegistry
{
    public static IReadOnlyList<IAgentConnector> Primary { get; } =
    [
        new CursorConnector(),
        new CodexConnector(),
        new GrokConnector()
    ];

    public static IAgentConnector? Connector(string id)
    {
        var key = AgentLaunch.Normalize(id);
        return Primary.FirstOrDefault(connector => AgentLaunch.Normalize(connector.Id) == key);
    }

    public static void RetargetAll(string binaryPath)
    {
        foreach (var connector in Primary) connector.Retarget(binaryPath);
    }
}

public sealed class CursorConnector : IAgentConnector
{
    public string Id => "cursor";
    public string DisplayName => "Cursor";
    public bool NeedsPrivacyAck => true;

    public HookInstallResult Status(string binaryPath) => HookInstaller.Status(HooksPath, binaryPath, TasksPath);
    public HookInstallResult Connect(string binaryPath, bool privacyAccepted) => HookInstaller.Install(HooksPath, binaryPath, TasksPath, privacyAccepted);
    public HookInstallResult Disconnect(string binaryPath) => HookInstaller.Remove(HooksPath, binaryPath, TasksPath);
    public bool Retarget(string binaryPath) => HookInstaller.RetargetCursor(HooksPath, binaryPath);

    private static string HooksPath => Path.Combine(AppConfig.CursorDirectory, "hooks.json");
    private static string TasksPath => AppConfig.TasksFile;
}

public sealed class CodexConnector : IAgentConnector
{
    public string Id => "codex";
    public string DisplayName => "Codex";
    public bool NeedsPrivacyAck => false;

    public HookInstallResult Status(string binaryPath) => HookInstaller.CodexStatus(ConfigPath, HooksPath, binaryPath);
    public HookInstallResult Connect(string binaryPath, bool privacyAccepted) => HookInstaller.InstallCodex(ConfigPath, HooksPath, binaryPath, privacyAccepted);
    public HookInstallResult Disconnect(string binaryPath) => HookInstaller.RemoveNested(HooksPath, binaryPath);

    public bool Retarget(string binaryPath)
    {
        var pointed = HookInstaller.RetargetNested(HooksPath, binaryPath, "codex");
        var trusted = HookInstaller.TrustCodex(ConfigPath, HooksPath, binaryPath);
        return pointed && trusted;
    }

    private static string ConfigPath => Path.Combine(AppConfig.HomeDirectory, ".codex", "config.toml");
    private static string HooksPath => Path.Combine(AppConfig.HomeDirectory, ".codex", "hooks.json");
}

public sealed class GrokConnector : IAgentConnector
{
    public string Id => "grok-bot";
    public string DisplayName => "Grok Bot";
    public bool NeedsPrivacyAck => false;

    public HookInstallResult Status(string binaryPath) => Gate(PassiveHooks.GrokStatus(DirectoryPath, binaryPath), binaryPath);
    public HookInstallResult Connect(string binaryPath, bool privacyAccepted) => Gate(PassiveHooks.InstallGrok(DirectoryPath, binaryPath, privacyAccepted), binaryPath);

    public HookInstallResult Disconnect(string binaryPath)
    {
        return PassiveHooks.Remove(FilePath, binaryPath);
    }

    public bool Retarget(string binaryPath) => HookInstaller.RetargetNested(FilePath, binaryPath, "grok");

    private static HookInstallResult Gate(HookInstallResult result, string binaryPath)
    {
        if (!result.Connected) return result;
        if (GrokCLI.IsInstalled()) return result;
        return new HookInstallResult(false, binaryPath, error: "Grok Bot is not installed");
    }

    private static string DirectoryPath => PassiveHooks.GrokDirectory(AppConfig.HomeDirectory);
    private static string FilePath => Path.Combine(DirectoryPath, "mochi-island.json");
}

public static class GrokCLI
{
    public static bool IsInstalled(IReadOnlyDictionary<string, string>? environment = null)
    {
        string path;
        if (environment != null)
        {
            path = environment.TryGetValue("PATH", out var value) ? value : "";
        }
        else
        {
            path = Environment.GetEnvironmentVariable("PATH") ?? "";
        }
        foreach (var directory in path.Split([';', ':'], StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in new[] { "grok", "grok.exe" })
            {
                if (FileBits.IsExecutable(Path.Combine(directory, name))) return true;
            }
        }
        return false;
    }
}
