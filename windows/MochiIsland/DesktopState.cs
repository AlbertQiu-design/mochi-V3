using Mochi.Core;

namespace MochiIsland;

public sealed class DesktopState
{
    public const string PrivacyLine = "Mochi will write its hook into ~/.cursor/hooks.json on this computer only. Chats stay on this computer. Shell and MCP continue on their own. A choice still shows beside the kitten.";

    private readonly PrefsStore prefsStore = PrefsStore.Live();
    private readonly PetStore petStore = PetStore.Live();
    private readonly TaskTransitionTracker transitions = new();
    private readonly TaskCueTracker cues = new();
    private readonly HashSet<string> read = [];
    private List<CursorTask> hints = [];
    private double hintScan;

    public IslandPrefs Prefs { get; private set; } = new();
    public PetState Pet { get; private set; } = new();
    public List<CursorTask> Tasks { get; private set; } = [];
    public List<AgentIconRow> Rows { get; private set; } = [];
    public List<TaskCue> Cues { get; private set; } = [];
    public HashSet<string> Holding { get; } = [];
    public string? SelectedId { get; private set; }
    public bool Expanded { get; set; }
    public bool PickerOpen { get; private set; }
    public bool CardExpanded { get; set; }

    public static string HookPath => Path.Combine(AppContext.BaseDirectory, "MochiHookCLI.exe");

    public void Load()
    {
        Directory.CreateDirectory(AppConfig.SupportDirectory);
        Prefs = prefsStore.Load();
        Pet = petStore.Load();
        foreach (var id in ReadIds()) read.Add(id);
        try { AgentConnectorRegistry.RetargetAll(HookPath); } catch { }
        Refresh(Now());
    }

    public void Refresh(double now)
    {
        PetEngine.Tick(Pet, now);
        try
        {
            // Grok Bot's desktop app does not run hook files; follow its local store.
            var grokHook = Path.Combine(AppConfig.HomeDirectory, ".grok", "hooks", "mochi-island.json");
            if (File.Exists(grokHook) && GrokBotSession.Available()) GrokBotSession.Sync(AppConfig.TasksFile, now);
        }
        catch
        {
            // The store can be mid-write; the next refresh picks it up.
        }
        var loaded = TaskReader.Load(AppConfig.TasksFile, now, true);
        if (Prefs.ScanTranscripts && TranscriptHints.Due(hintScan, now, true))
        {
            hintScan = now;
            hints = RecentNames(now);
        }
        if (!Prefs.ScanTranscripts) hints = [];
        Tasks = TranscriptHints.Merge(loaded, hints, Prefs.ScanTranscripts);
        var fed = false;
        foreach (var id in transitions.NewlyCompleted(Tasks))
        {
            fed |= PetEngine.FeedFromAI(Pet, id, now);
        }
        petStore.Save(Pet);
        _ = fed;
        Rows = AgentIconList.Rows(Tasks.Select(Candidate).ToList(), Holding, now);
        if (SelectedId != null && Rows.All(row => row.Id != SelectedId))
        {
            SelectedId = null;
            PickerOpen = false;
        }
        Cues = cues.Observe(Tasks.Select(task => new TaskCueEvent(task.Id, task.Status, task.SubmitGen, task.Updated)).ToList());
    }

    public CursorTask? SelectedTask => Tasks.FirstOrDefault(task => task.Id == SelectedId);

    public void Toggle()
    {
        Expanded = !Expanded;
        if (!Expanded) CloseBubble();
    }

    public void Play(double now)
    {
        PetEngine.Play(Pet, now);
        petStore.Save(Pet);
    }

    public void Select(string id)
    {
        if (SelectedId == id && !PickerOpen)
        {
            CloseBubble();
            return;
        }
        PickerOpen = false;
        CardExpanded = false;
        SelectedId = id;
        var task = SelectedTask;
        if (task?.Status == "running") Holding.Add(id);
    }

    public void ShowPicker()
    {
        if (PickerOpen)
        {
            CloseBubble();
            return;
        }
        SelectedId = null;
        CardExpanded = false;
        PickerOpen = true;
    }

    public void CloseBubble()
    {
        var task = SelectedTask;
        if (task != null && task.Status is "done" or "fail")
        {
            read.Add(task.Id);
            Holding.Remove(task.Id);
            WriteIds();
        }
        SelectedId = null;
        PickerOpen = false;
        CardExpanded = false;
    }

    public void Choose(string optionId)
    {
        var task = SelectedTask;
        if (task == null) return;
        if (task.Kind == "permission" || PermissionCopy.AnswersPermission(task.Kind, optionId))
        {
            ReplyWriter.AnswerPermission(task.Id, task.RequestID, optionId, "permission", AppConfig.ReplyFile, AppConfig.TasksFile);
        }
        else
        {
            ReplyWriter.WriteChoice(task.Id, task.RequestID, optionId, task.Kind, AppConfig.ReplyFile);
        }
        if (ReplyWriter.ClosesBubble(task.Kind, optionId)) CloseBubble();
        Refresh(Now());
    }

    public void SaveAnchor(double x, double y)
    {
        Prefs.KittenX = ShellGeometry.Clamp01(x);
        Prefs.KittenY = ShellGeometry.Clamp01(y);
        Prefs.KittenPlaced = true;
        Prefs.Dock = Prefs.KittenX < 0.5 ? "left" : "right";
        prefsStore.Save(Prefs);
    }

    public void ResetPlace()
    {
        Prefs.KittenPlaced = false;
        Prefs.KittenX = 1;
        Prefs.KittenY = 0.5;
        Prefs.Dock = "right";
        prefsStore.Save(Prefs);
    }

    public void SetScale(double scale)
    {
        Prefs.PetScale = scale;
        prefsStore.Save(Prefs);
    }

    public void SetLook(string look)
    {
        Prefs.Look = IslandPrefs.NormalizeLook(look);
        prefsStore.Save(Prefs);
    }

    public void SetLanguage(string language)
    {
        Prefs.Language = IslandPrefs.Languages.Contains(language) ? language : "system";
        prefsStore.Save(Prefs);
    }

    public void SetSound(string kind, string id)
    {
        if (kind == "submit") Prefs.SoundSubmit = id;
        else if (kind == "done") Prefs.SoundDone = id;
        else if (kind == "stuck") Prefs.SoundStuck = id;
        else Prefs.Sound = id;
        prefsStore.Save(Prefs);
    }

    public void SetScan(bool enabled)
    {
        Prefs.ScanTranscripts = enabled;
        if (!enabled) hints = [];
        prefsStore.Save(Prefs);
    }

    public void SetPicker(string source, bool enabled)
    {
        switch (AgentLaunch.Normalize(source))
        {
            case "cursor": Prefs.PickerCursor = enabled; break;
            case "codex": Prefs.PickerCodex = enabled; break;
            case "claude": Prefs.PickerClaude = enabled; break;
            case "gemini": Prefs.PickerGemini = enabled; break;
            case "grok-bot": Prefs.PickerGrok = enabled; break;
            case "gpt": Prefs.PickerGPT = enabled; break;
        }
        prefsStore.Save(Prefs);
    }

    public HookInstallResult Connect(IAgentConnector connector, bool privacyAccepted)
    {
        if (connector.NeedsPrivacyAck && !Prefs.CursorPrivacyAccepted && !privacyAccepted)
        {
            return new HookInstallResult(false, HookPath, needsPrivacy: true);
        }
        var result = connector.Connect(HookPath, true);
        if (result.Connected && connector.NeedsPrivacyAck)
        {
            Prefs.CursorPrivacyAccepted = true;
            prefsStore.Save(Prefs);
        }
        return result;
    }

    public HookInstallResult Disconnect(IAgentConnector connector)
    {
        return connector.Disconnect(HookPath);
    }

    public static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    private AgentIconCandidate Candidate(CursorTask task)
    {
        return new AgentIconCandidate(
            task.Id,
            task.Source,
            task.Title,
            task.Status,
            read.Contains(task.Id),
            task.Updated,
            task.Status == "wait" && task.Options.Count > 0);
    }

    private static List<CursorTask> RecentNames(double now)
    {
        var root = Path.Combine(AppConfig.CursorDirectory, "projects");
        var hints = new List<CursorTask>();
        if (!Directory.Exists(root)) return hints;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
            {
                if (hints.Count >= 20) break;
                if (!file.Contains($"{Path.DirectorySeparatorChar}agent-transcripts{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
                var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(file)).ToUnixTimeMilliseconds() / 1000.0;
                if (modified <= 0 || now - modified > AppConfig.LiveKeep) continue;
                hints.Add(new CursorTask(Path.GetFileNameWithoutExtension(file), "Cursor task", "running", updated: modified));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return hints;
    }

    private static string ReadPath => Path.Combine(AppConfig.SupportDirectory, "read-ids.json");

    private static List<string> ReadIds()
    {
        return Bags.Strings(JsonFile.Read(ReadPath).GetValueOrDefault("ids"));
    }

    private void WriteIds()
    {
        var ids = read.TakeLast(AppConfig.FedTaskLimit).Select(id => (object?)id).ToList();
        JsonFile.Write(new Dictionary<string, object?> { ["ids"] = ids }, ReadPath);
    }
}
