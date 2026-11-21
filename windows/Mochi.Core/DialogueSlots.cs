using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mochi.Core;

public sealed class DialogueSlot
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "cursor";
    public string Title { get; set; } = "New chat";
    public string Skin { get; set; } = "cursor";
    public double Created { get; set; }
    public bool Read { get; set; }
    public string Status { get; set; } = "";

    public DialogueSlot()
    {
    }

    public DialogueSlot(string id, string source, string title, string skin, double created, bool read = false, string status = "")
    {
        Id = id;
        Source = source;
        Title = title;
        Skin = skin;
        Created = created;
        Read = read;
        Status = status;
    }
}

public sealed class DialogueSlotStore
{
    public static readonly string[] Skins = ["cursor", "grok-bot", "mochi", "codex", "claude", "gpt"];

    public string Selected { get; set; } = "";
    public List<DialogueSlot> Slots { get; set; } = [];

    public DialogueSlotStore()
    {
    }

    public DialogueSlotStore(string selected, List<DialogueSlot>? slots = null)
    {
        Selected = selected;
        Slots = slots ?? [];
    }

    public static string Face(string source)
    {
        return AgentLaunch.Normalize(source) switch
        {
            "grok-bot" => "grok-bot",
            "mochi" => "mochi",
            "codex" => "codex",
            "claude" => "claude",
            "copilot" => "copilot",
            "opencode" => "opencode",
            "gemini" => "gemini",
            "gpt" => "gpt",
            _ => "cursor"
        };
    }

    public static string StableSkin(string id) => Face(id.StartsWith("seed-", StringComparison.Ordinal) ? id[5..] : "cursor");

    public DialogueSlotStore Normalized()
    {
        var next = new DialogueSlotStore(Selected, Slots.Select(Copy).ToList());
        foreach (var slot in next.Slots)
        {
            slot.Source = AgentLaunch.Normalize(slot.Source);
            slot.Skin = Face(slot.Source);
            if (string.IsNullOrWhiteSpace(slot.Title)) slot.Title = "New chat";
        }
        if (!next.Slots.Any(slot => slot.Id == next.Selected))
        {
            next.Selected = next.Slots.FirstOrDefault()?.Id ?? "";
        }
        return next;
    }

    public DialogueSlotStore DroppingIdleSeeds(IReadOnlySet<string> activeTaskIDs)
    {
        var next = Normalized();
        next.Slots.RemoveAll(slot =>
        {
            var seeded = slot.Id.StartsWith("seed-", StringComparison.Ordinal);
            var kitten = AgentLaunch.Normalize(slot.Source) == "mochi";
            if (!seeded && !kitten) return false;
            return !activeTaskIDs.Contains(slot.Id);
        });
        if (!next.Slots.Any(slot => slot.Id == next.Selected))
        {
            next.Selected = next.Slots.FirstOrDefault()?.Id ?? "";
        }
        return next;
    }

    public DialogueSlotStore RemovingReadDone(IReadOnlyList<CursorTask> tasks)
    {
        var live = new Dictionary<string, string>();
        foreach (var task in tasks) live[task.Id] = task.Status;
        var next = Normalized();
        next.Slots.RemoveAll(slot =>
        {
            if (!slot.Read) return false;
            if (live.ContainsKey(slot.Id)) return false;
            return slot.Status is "done" or "fail";
        });
        if (!next.Slots.Any(slot => slot.Id == next.Selected))
        {
            next.Selected = next.Slots.FirstOrDefault()?.Id ?? "";
        }
        return next;
    }

    public DialogueSlotStore Merged(IReadOnlyList<CursorTask> tasks)
    {
        var next = Normalized();
        var indexByID = new Dictionary<string, int>();
        for (var index = 0; index < next.Slots.Count; index++) indexByID[next.Slots[index].Id] = index;
        foreach (var task in tasks)
        {
            var title = task.Title.Trim();
            if (indexByID.TryGetValue(task.Id, out var index))
            {
                next.Slots[index].Source = AgentLaunch.Normalize(task.Source);
                next.Slots[index].Skin = Face(next.Slots[index].Source);
                next.Slots[index].Status = task.Status;
                if (title.Length > 0) next.Slots[index].Title = title;
                continue;
            }
            var source = AgentLaunch.Normalize(task.Source);
            var slot = new DialogueSlot(
                task.Id,
                source,
                title.Length == 0 ? "New chat" : title,
                Face(source),
                task.Updated > 0 ? task.Updated : 0,
                false,
                task.Status);
            indexByID[task.Id] = next.Slots.Count;
            next.Slots.Add(slot);
        }
        if (!next.Slots.Any(slot => slot.Id == next.Selected))
        {
            next.Selected = next.Slots.FirstOrDefault()?.Id ?? "";
        }
        return next;
    }

    public (DialogueSlotStore Store, DialogueSlot Slot) Created(string source, double now)
    {
        var next = Normalized();
        var normalized = AgentLaunch.Normalize(source);
        var slot = new DialogueSlot(
            "slot-" + Guid.NewGuid().ToString().ToLowerInvariant(),
            normalized,
            "New chat",
            Face(normalized),
            now);
        next.Slots.Add(slot);
        next.Selected = slot.Id;
        return (next, slot);
    }

    public DialogueSlotStore Selecting(string id)
    {
        var next = Normalized();
        if (next.Slots.Any(slot => slot.Id == id)) next.Selected = id;
        return next;
    }

    public DialogueSlotStore MarkingRead(string id)
    {
        var next = Normalized();
        var index = next.Slots.FindIndex(slot => slot.Id == id);
        if (index >= 0) next.Slots[index].Read = true;
        return next;
    }

    public DialogueSlotStore SelectingNewWaits(IReadOnlyList<CursorTask> tasks, ref HashSet<string> known, ref bool primed)
    {
        var next = Merged(tasks);
        var waits = new List<CursorTask>();
        foreach (var task in tasks)
        {
            if (task.Status == "wait") waits.Add(task);
        }
        if (primed)
        {
            CursorTask? newest = null;
            foreach (var task in waits)
            {
                if (known.Contains(task.Id)) continue;
                if (newest == null || task.Updated >= newest.Updated) newest = task;
            }
            if (newest != null) next.Selected = newest.Id;
        }
        known = [];
        foreach (var task in waits) known.Add(task.Id);
        primed = true;
        return next;
    }

    private static DialogueSlot Copy(DialogueSlot slot)
    {
        return new DialogueSlot(slot.Id, slot.Source, slot.Title, slot.Skin, slot.Created, slot.Read, slot.Status);
    }
}

public sealed class DialogueSlotDisk
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; }

    public DialogueSlotDisk(string directory)
    {
        FilePath = Path.Combine(directory, "dialogue-slots.json");
    }

    public static DialogueSlotDisk Live() => new(AppConfig.SupportDirectory);

    public DialogueSlotStore Load(double now = 0, IReadOnlySet<string>? activeTaskIDs = null)
    {
        _ = now;
        var existing = Read();
        if (existing == null) return new DialogueSlotStore();
        var migrated = existing.DroppingIdleSeeds(activeTaskIDs ?? new HashSet<string>());
        if (!Same(migrated, existing.Normalized())) Save(migrated);
        return migrated;
    }

    public void Save(DialogueSlotStore store)
    {
        var normalized = store.Normalized();
        var payload = new SlotFile
        {
            Selected = normalized.Selected,
            Slots = normalized.Slots.Select(slot => new SlotRow
            {
                Id = slot.Id,
                Source = slot.Source,
                Title = slot.Title,
                Skin = slot.Skin,
                Created = slot.Created,
                Read = slot.Read,
                Status = slot.Status
            }).ToList()
        };
        AtomicFile.WriteText(JsonSerializer.Serialize(payload, JsonOptions), FilePath);
    }

    private DialogueSlotStore? Read()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            var file = JsonSerializer.Deserialize<SlotFile>(File.ReadAllText(FilePath));
            if (file == null) return null;
            return new DialogueSlotStore(
                file.Selected ?? "",
                (file.Slots ?? []).Select(slot => new DialogueSlot(
                    slot.Id ?? "",
                    slot.Source ?? "cursor",
                    slot.Title ?? "New chat",
                    slot.Skin ?? "cursor",
                    slot.Created,
                    slot.Read,
                    slot.Status ?? "")).ToList()).Normalized();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Same(DialogueSlotStore left, DialogueSlotStore right)
    {
        if (left.Selected != right.Selected || left.Slots.Count != right.Slots.Count) return false;
        for (var index = 0; index < left.Slots.Count; index++)
        {
            var a = left.Slots[index];
            var b = right.Slots[index];
            if (a.Id != b.Id || a.Source != b.Source || a.Title != b.Title || a.Skin != b.Skin
                || a.Created != b.Created || a.Read != b.Read || a.Status != b.Status)
            {
                return false;
            }
        }
        return true;
    }

    private sealed class SlotFile
    {
        [JsonPropertyName("selected")] public string? Selected { get; set; }
        [JsonPropertyName("slots")] public List<SlotRow>? Slots { get; set; }
    }

    private sealed class SlotRow
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("skin")] public string? Skin { get; set; }
        [JsonPropertyName("created")] public double Created { get; set; }
        [JsonPropertyName("read")] public bool Read { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
    }
}
