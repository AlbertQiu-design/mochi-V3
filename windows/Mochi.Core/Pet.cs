using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mochi.Core;

public static class PetRules
{
    public const int FeedHunger = -32;
    public const int FeedEnergy = 8;
    public const int FeedHappiness = 10;
    public const int PlayHunger = 10;
    public const int PlayEnergy = -24;
    public const int PlayHappiness = 16;
    public const int DecayHunger = 7;
    public const int DecayEnergy = -4;
    public const int DecayHappiness = -8;
    public const int AsleepHunger = 95;
    public const int AsleepEnergy = 0;
    public const double DecayInterval = 6;
    public const int MoodHungry = 48;
    public const int MoodSleepy = 36;
    public const int MoodAngry = 30;
    public const int AngryPlayHunger = 52;
    public const double MoodActionHold = 8;
    public const string DefaultName = "Mochi";
    public const int DefaultHunger = 28;
    public const int DefaultEnergy = 72;
    public const int DefaultHappiness = 68;
    public const int StatMin = 0;
    public const int StatMax = 100;
    public const string FeedMessage = "Yum! That hit the spot.";
}

public sealed class PetState : IEquatable<PetState>
{
    public string Name { get; set; } = PetRules.DefaultName;
    public int Hunger { get; set; } = PetRules.DefaultHunger;
    public int Energy { get; set; } = PetRules.DefaultEnergy;
    public int Happiness { get; set; } = PetRules.DefaultHappiness;
    public bool Asleep { get; set; }
    public double LastDecayTs { get; set; }
    public string LastAction { get; set; } = "";
    public double LastActionTs { get; set; }
    public List<string> FedTaskIDs { get; set; } = [];

    public PetState()
    {
        LastDecayTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }

    public PetState(
        string? name = null,
        int? hunger = null,
        int? energy = null,
        int? happiness = null,
        bool asleep = false,
        double? lastDecayTs = null,
        string lastAction = "",
        double lastActionTs = 0,
        List<string>? fedTaskIDs = null)
    {
        Name = name ?? PetRules.DefaultName;
        Hunger = hunger ?? PetRules.DefaultHunger;
        Energy = energy ?? PetRules.DefaultEnergy;
        Happiness = happiness ?? PetRules.DefaultHappiness;
        Asleep = asleep;
        LastDecayTs = lastDecayTs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        LastAction = lastAction;
        LastActionTs = lastActionTs;
        FedTaskIDs = fedTaskIDs ?? [];
    }

    public void Clamp()
    {
        Hunger = Math.Min(PetRules.StatMax, Math.Max(PetRules.StatMin, Hunger));
        Energy = Math.Min(PetRules.StatMax, Math.Max(PetRules.StatMin, Energy));
        Happiness = Math.Min(PetRules.StatMax, Math.Max(PetRules.StatMin, Happiness));
        if (string.IsNullOrEmpty(Name)) Name = PetRules.DefaultName;
        if (FedTaskIDs.Count > AppConfig.FedTaskLimit)
        {
            FedTaskIDs = FedTaskIDs.Skip(FedTaskIDs.Count - AppConfig.FedTaskLimit).ToList();
        }
    }

    public bool Equals(PetState? other)
    {
        if (other is null) return false;
        return Name == other.Name
            && Hunger == other.Hunger
            && Energy == other.Energy
            && Happiness == other.Happiness
            && Asleep == other.Asleep
            && LastDecayTs == other.LastDecayTs
            && LastAction == other.LastAction
            && LastActionTs == other.LastActionTs
            && FedTaskIDs.SequenceEqual(other.FedTaskIDs);
    }

    public override bool Equals(object? obj) => Equals(obj as PetState);

    public override int GetHashCode() => HashCode.Combine(Name, Hunger, Energy, Happiness, Asleep, LastAction);
}

public static class PetEngine
{
    public static void Tick(PetState state, double now)
    {
        if (state.Asleep) return;
        while (!state.Asleep && now - state.LastDecayTs >= PetRules.DecayInterval)
        {
            Decay(state);
            state.LastDecayTs += PetRules.DecayInterval;
        }
    }

    public static void Play(PetState state, double now)
    {
        Tick(state, now);
        if (state.Asleep) return;
        Apply(state, PetRules.PlayHunger, PetRules.PlayEnergy, PetRules.PlayHappiness);
        state.LastAction = "play";
        state.LastActionTs = now;
    }

    public static bool FeedFromAI(PetState state, string taskID, double now)
    {
        var id = taskID.Trim();
        if (id.Length == 0 || state.FedTaskIDs.Contains(id)) return false;
        Tick(state, now);
        var wasAsleep = state.Asleep;
        if (wasAsleep) state.Asleep = false;
        Apply(state, PetRules.FeedHunger, PetRules.FeedEnergy, PetRules.FeedHappiness);
        if (wasAsleep) state.Asleep = false;
        state.LastAction = "feed";
        state.LastActionTs = now;
        state.FedTaskIDs.Add(id);
        state.Clamp();
        return true;
    }

    public static string Mood(PetState state, double now)
    {
        if (state.Asleep) return "sleepy";
        var held = now - state.LastActionTs < PetRules.MoodActionHold;
        if (state.LastAction == "play" && held && state.Hunger >= PetRules.AngryPlayHunger) return "angry";
        if (state.Energy <= PetRules.MoodSleepy) return "sleepy";
        if (state.Hunger >= PetRules.MoodHungry) return "hungry";
        if (state.LastAction == "play" && held) return "playful";
        if (state.Happiness <= PetRules.MoodAngry) return "angry";
        return "happy";
    }

    public static string Message(PetState state, double now)
    {
        if (state.LastAction == "feed" && now - state.LastActionTs < PetRules.MoodActionHold)
        {
            return PetRules.FeedMessage;
        }
        return Mood(state, now) switch
        {
            "playful" => "Zoom zoom! Catch me!",
            "sleepy" => "Zzz... five more minutes.",
            "hungry" => "Feed me now, human.",
            "angry" => "Don't poke me when I'm hungry!",
            _ => "Let's take care of Mochi!"
        };
    }

    public static string HoverLine(PetState state, double now)
    {
        if (state.LastAction == "feed" && now - state.LastActionTs < PetRules.MoodActionHold) return "吃到啦。";
        if (state.Asleep) return "在睡觉。";
        return Mood(state, now) switch
        {
            "playful" => "想玩一会儿。",
            "sleepy" => "有点困了。",
            "hungry" => "肚子有点空。",
            "angry" => "想自己待一会儿。",
            "asleep" => "在睡觉。",
            _ => "心情正好。"
        };
    }

    public static Dictionary<string, object?> Snapshot(PetState state, double now)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = state.Name,
            ["hunger"] = state.Hunger,
            ["energy"] = state.Energy,
            ["happiness"] = state.Happiness,
            ["is_alive"] = true,
            ["asleep"] = state.Asleep,
            ["mood"] = Mood(state, now),
            ["message"] = Message(state, now),
            ["hover"] = HoverLine(state, now)
        };
    }

    private static void Decay(PetState state)
    {
        if (state.Asleep) return;
        Apply(state, PetRules.DecayHunger, PetRules.DecayEnergy, PetRules.DecayHappiness);
    }

    private static void Apply(PetState state, int hunger, int energy, int happiness)
    {
        if (state.Asleep) return;
        state.Hunger += hunger;
        state.Energy += energy;
        state.Happiness += happiness;
        state.Clamp();
        if (state.Hunger >= PetRules.AsleepHunger || state.Energy <= PetRules.AsleepEnergy)
        {
            state.Asleep = true;
        }
    }
}

public sealed class PetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string DirectoryPath { get; }
    public string FilePath { get; }

    public PetStore(string directory)
    {
        DirectoryPath = directory;
        FilePath = Path.Combine(directory, "pet_state.json");
    }

    public static PetStore Live() => new(AppConfig.SupportDirectory);

    public PetState Load(IEnumerable<string>? migrating = null)
    {
        var existing = Read(FilePath);
        if (existing != null) return existing;
        if (migrating != null)
        {
            foreach (var legacy in migrating)
            {
                if (legacy == FilePath) continue;
                var state = Read(legacy);
                if (state == null) continue;
                Save(state);
                return state;
            }
        }
        var fresh = new PetState();
        Save(fresh);
        return fresh;
    }

    public void Save(PetState state)
    {
        state.Clamp();
        var payload = new PetFile
        {
            Name = state.Name,
            Hunger = state.Hunger,
            Energy = state.Energy,
            Happiness = state.Happiness,
            Asleep = state.Asleep,
            IsAlive = true,
            LastDecayTs = state.LastDecayTs,
            LastAction = state.LastAction,
            LastActionTs = state.LastActionTs,
            FedTaskIDs = state.FedTaskIDs
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        AtomicFile.WriteText(json, FilePath);
    }

    private static PetState? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var state = new PetState(
                name: root.TryGetProperty("name", out var name) ? name.GetString() : PetRules.DefaultName,
                hunger: root.TryGetProperty("hunger", out var hunger) ? hunger.GetInt32() : PetRules.DefaultHunger,
                energy: root.TryGetProperty("energy", out var energy) ? energy.GetInt32() : PetRules.DefaultEnergy,
                happiness: root.TryGetProperty("happiness", out var happiness) ? happiness.GetInt32() : PetRules.DefaultHappiness,
                lastDecayTs: root.TryGetProperty("last_decay_ts", out var decay) ? decay.GetDouble() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                lastAction: root.TryGetProperty("last_action", out var action) ? action.GetString() ?? "" : "",
                lastActionTs: root.TryGetProperty("last_action_ts", out var actionTs) ? actionTs.GetDouble() : 0);
            if (root.TryGetProperty("fed_task_ids", out var fed) && fed.ValueKind == JsonValueKind.Array)
            {
                state.FedTaskIDs = fed.EnumerateArray().Select(item => item.GetString() ?? "").ToList();
            }
            if (root.TryGetProperty("asleep", out var asleep))
            {
                state.Asleep = asleep.GetBoolean();
            }
            else if (root.TryGetProperty("is_alive", out var alive))
            {
                state.Asleep = !alive.GetBoolean();
            }
            state.Clamp();
            return state;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class PetFile
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("hunger")] public int Hunger { get; set; }
        [JsonPropertyName("energy")] public int Energy { get; set; }
        [JsonPropertyName("happiness")] public int Happiness { get; set; }
        [JsonPropertyName("asleep")] public bool Asleep { get; set; }
        [JsonPropertyName("is_alive")] public bool IsAlive { get; set; }
        [JsonPropertyName("last_decay_ts")] public double LastDecayTs { get; set; }
        [JsonPropertyName("last_action")] public string LastAction { get; set; } = "";
        [JsonPropertyName("last_action_ts")] public double LastActionTs { get; set; }
        [JsonPropertyName("fed_task_ids")] public List<string> FedTaskIDs { get; set; } = [];
    }
}
