namespace Mochi.Core;

public enum TaskCue
{
    Submit,
    Complete,
    Stuck
}

public readonly record struct TaskCueEvent(string Id, string Status, int SubmitGen, double Updated)
{
    public int SubmitGen { get; init; } = Math.Max(0, SubmitGen);
}

public sealed class TaskCueTracker
{
    public readonly record struct Memory(string Status, int SubmitGen);

    public Dictionary<string, Memory> Seen { get; } = new();
    public bool Primed { get; private set; }

    public List<TaskCue> Observe(IReadOnlyList<TaskCueEvent> tasks)
    {
        if (!Primed)
        {
            Primed = true;
            foreach (var task in tasks)
            {
                Seen[task.Id] = new Memory(task.Status, task.SubmitGen);
            }
            return [];
        }
        var cues = new List<TaskCue>();
        foreach (var task in tasks.OrderByDescending(item => item.Updated))
        {
            var cue = CueFor(task, Seen.TryGetValue(task.Id, out var previous) ? previous : null);
            if (cue is TaskCue found) cues.Add(found);
            Seen[task.Id] = new Memory(task.Status, task.SubmitGen);
        }
        return cues;
    }

    private static TaskCue? CueFor(TaskCueEvent task, Memory? previous)
    {
        var genIncreased = task.SubmitGen > (previous?.SubmitGen ?? 0);
        var statusChanged = previous is null || previous.Value.Status != task.Status;
        if (!statusChanged && !genIncreased) return null;
        if (statusChanged)
        {
            return task.Status switch
            {
                "done" => TaskCue.Complete,
                "fail" or "wait" => TaskCue.Stuck,
                "running" => genIncreased ? TaskCue.Submit : null,
                _ => null
            };
        }
        return genIncreased ? TaskCue.Submit : null;
    }
}

public enum WatchPublish
{
    Quiet,
    Fire,
    Retry
}

/// Decides when a tasks-file note is worth a refresh. The shell waits 16ms, then calls Publish.
public sealed class TaskWatchGate
{
    public const int CoalesceMilliseconds = 16;
    public string LastStamp { get; private set; } = "";

    public void Arm(string stamp) => LastStamp = stamp ?? "";

    public bool Note(string stamp) => stamp != LastStamp;

    public WatchPublish Publish(string stamp, bool retryIfMissing)
    {
        if (retryIfMissing && stamp == "missing" && LastStamp.Length > 0 && LastStamp != "missing")
        {
            return WatchPublish.Retry;
        }
        if (stamp == LastStamp) return WatchPublish.Quiet;
        LastStamp = stamp;
        return WatchPublish.Fire;
    }

    public static string Stamp(string path)
    {
        if (!File.Exists(path)) return "missing";
        var info = new FileInfo(path);
        return info.Length + "-" + info.LastWriteTimeUtc.Ticks;
    }
}
