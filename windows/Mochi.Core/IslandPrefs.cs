using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mochi.Core;

public sealed class IslandPrefs
{
    public static readonly string[] SoundIDs = ["boop", "chirp", "mew", "pop", "twinkle"];
    public static readonly string[] SubmitSounds = ["glass", "ripple", "ping"];
    public static readonly string[] DoneSounds = ["chime", "bloom", "spark"];
    public static readonly string[] StuckSounds = ["drop", "thud", "hush"];
    public static readonly string[] Docks = ["left", "right"];
    public static readonly string[] PickerOrder = ["cursor", "codex", "claude", "gemini", "grok-bot", "gpt"];
    public static readonly string[] PetLooks = ["a", "b", "c", "d", "e", "f"];

    public double Dx { get; set; }
    public double Dy { get; set; }
    public double BubbleDx { get; set; }
    public double BubbleDy { get; set; }
    public string Fill { get; set; } = "#ffffff";
    public string Text { get; set; } = "#241f1c";
    public string Stroke { get; set; } = "#2c2926";
    public string CardFill { get; set; } = "#fffdf8";
    public string CardStroke { get; set; } = "#2c2926";
    public string Sound { get; set; } = "boop";
    public string SoundSubmit { get; set; } = "glass";
    public string SoundDone { get; set; } = "chime";
    public string SoundStuck { get; set; } = "drop";
    public string Look { get; set; } = "a";
    public double PetScale { get; set; } = 1;
    public double BubbleScale { get; set; } = 1;
    public bool CursorPrivacyAccepted { get; set; }
    public bool ScanTranscripts { get; set; }
    public string Dock { get; set; } = "right";
    public double KittenX { get; set; } = 1;
    public double KittenY { get; set; } = 0.5;
    public bool KittenPlaced { get; set; }
    public bool PickerCursor { get; set; } = true;
    public bool PickerCodex { get; set; } = true;
    public bool PickerClaude { get; set; } = true;
    public bool PickerGemini { get; set; } = true;
    public bool PickerGrok { get; set; } = true;
    public bool PickerGPT { get; set; } = true;

    public IslandPrefs()
    {
        Normalize();
    }

    public static string NormalizeLook(string raw)
    {
        var lower = raw.Trim().ToLowerInvariant();
        if (PetLooks.Contains(lower)) return lower;
        if (lower == "mochi") return "a";
        return "a";
    }

    public static string PetLetter(string look) => NormalizeLook(look).ToUpperInvariant();

    public List<string> PickerSources()
    {
        var sources = new List<string>();
        if (PickerCursor) sources.Add("cursor");
        if (PickerCodex) sources.Add("codex");
        if (PickerClaude) sources.Add("claude");
        if (PickerGemini) sources.Add("gemini");
        if (PickerGrok) sources.Add("grok-bot");
        if (PickerGPT) sources.Add("gpt");
        return sources;
    }

    public Dictionary<string, object?> Dictionary()
    {
        return new Dictionary<string, object?>
        {
            ["dx"] = Dx,
            ["dy"] = Dy,
            ["bubble_dx"] = BubbleDx,
            ["bubble_dy"] = BubbleDy,
            ["fill"] = Fill,
            ["text"] = Text,
            ["stroke"] = Stroke,
            ["card_fill"] = CardFill,
            ["card_stroke"] = CardStroke,
            ["sound"] = Sound,
            ["sound_submit"] = SoundSubmit,
            ["sound_done"] = SoundDone,
            ["sound_stuck"] = SoundStuck,
            ["look"] = Look,
            ["pet_scale"] = PetScale,
            ["bubble_scale"] = BubbleScale,
            ["dock"] = Dock,
            ["kitten_x"] = KittenX,
            ["kitten_y"] = KittenY,
            ["kitten_placed"] = KittenPlaced,
            ["picker_cursor"] = PickerCursor,
            ["picker_codex"] = PickerCodex,
            ["picker_claude"] = PickerClaude,
            ["picker_gemini"] = PickerGemini,
            ["picker_grok"] = PickerGrok,
            ["picker_gpt"] = PickerGPT
        };
    }

    public static IslandPrefs Parse(IReadOnlyDictionary<string, object?> json, IslandPrefs? basis = null)
    {
        var next = basis?.Copy() ?? new IslandPrefs();
        next.Dx = Bags.Number(Get(json, "dx"), next.Dx);
        next.Dy = Bags.Number(Get(json, "dy"), next.Dy);
        next.BubbleDx = Bags.Number(Get(json, "bubble_dx"), next.BubbleDx);
        next.BubbleDy = Bags.Number(Get(json, "bubble_dy"), next.BubbleDy);
        next.Fill = Hex(Get(json, "fill")) ?? next.Fill;
        next.Text = Hex(Get(json, "text")) ?? next.Text;
        next.Stroke = Hex(Get(json, "stroke")) ?? next.Stroke;
        next.CardFill = Hex(Get(json, "card_fill")) ?? next.CardFill;
        next.CardStroke = Hex(Get(json, "card_stroke")) ?? next.CardStroke;
        if (Get(json, "sound") is string sound && SoundIDs.Contains(sound)) next.Sound = sound;
        if (Get(json, "sound_submit") is string submit && SubmitSounds.Contains(submit)) next.SoundSubmit = submit;
        if (Get(json, "sound_done") is string done && DoneSounds.Contains(done)) next.SoundDone = done;
        if (Get(json, "sound_stuck") is string stuck && StuckSounds.Contains(stuck)) next.SoundStuck = stuck;
        if (Get(json, "look") is string look && Regex.IsMatch(look, "^[a-z0-9_-]{1,32}$"))
        {
            next.Look = NormalizeLook(look);
        }
        next.PetScale = Scale(Get(json, "pet_scale"), next.PetScale);
        next.BubbleScale = Scale(Get(json, "bubble_scale"), next.BubbleScale);
        if (json.ContainsKey("cursor_privacy_accepted"))
        {
            next.CursorPrivacyAccepted = Bags.Flag(Get(json, "cursor_privacy_accepted"), next.CursorPrivacyAccepted);
        }
        if (json.ContainsKey("scan_transcripts"))
        {
            next.ScanTranscripts = Bags.Flag(Get(json, "scan_transcripts"), next.ScanTranscripts);
        }
        if (Get(json, "dock") is string dock && Docks.Contains(dock)) next.Dock = dock;
        if (json.ContainsKey("kitten_x")) next.KittenX = Unit(Bags.Number(Get(json, "kitten_x"), next.KittenX));
        if (json.ContainsKey("kitten_y")) next.KittenY = Unit(Bags.Number(Get(json, "kitten_y"), next.KittenY));
        if (json.ContainsKey("kitten_placed")) next.KittenPlaced = Bags.Flag(Get(json, "kitten_placed"), next.KittenPlaced);
        if (next.KittenPlaced) next.Dock = next.KittenX < 0.5 ? "left" : "right";
        if (json.ContainsKey("picker_cursor")) next.PickerCursor = Bags.Flag(Get(json, "picker_cursor"), next.PickerCursor);
        if (json.ContainsKey("picker_codex")) next.PickerCodex = Bags.Flag(Get(json, "picker_codex"), next.PickerCodex);
        if (json.ContainsKey("picker_claude")) next.PickerClaude = Bags.Flag(Get(json, "picker_claude"), next.PickerClaude);
        if (json.ContainsKey("picker_gemini")) next.PickerGemini = Bags.Flag(Get(json, "picker_gemini"), next.PickerGemini);
        if (json.ContainsKey("picker_grok")) next.PickerGrok = Bags.Flag(Get(json, "picker_grok"), next.PickerGrok);
        if (json.ContainsKey("picker_gpt")) next.PickerGPT = Bags.Flag(Get(json, "picker_gpt"), next.PickerGPT);
        return next;
    }

    public string Encode()
    {
        return JsonSerializer.Serialize(ToFile(), new JsonSerializerOptions { WriteIndented = false });
    }

    public static IslandPrefs Decode(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var prefs = new IslandPrefs
        {
            Dx = Double(root, "dx", 0),
            Dy = Double(root, "dy", 0),
            BubbleDx = Double(root, "bubbleDx", 0),
            BubbleDy = Double(root, "bubbleDy", 0),
            Fill = String(root, "fill", "#ffffff"),
            Text = String(root, "text", "#241f1c"),
            Stroke = String(root, "stroke", "#2c2926"),
            CardFill = String(root, "cardFill", "#fffdf8"),
            CardStroke = String(root, "cardStroke", "#2c2926"),
            Sound = String(root, "sound", "boop"),
            SoundSubmit = String(root, "sound_submit", "glass"),
            SoundDone = String(root, "sound_done", "chime"),
            SoundStuck = String(root, "sound_stuck", "drop"),
            Look = String(root, "look", "a"),
            PetScale = Double(root, "petScale", 1),
            BubbleScale = Double(root, "bubbleScale", 1),
            CursorPrivacyAccepted = Bool(root, "cursorPrivacyAccepted", false),
            ScanTranscripts = Bool(root, "scanTranscripts", false),
            Dock = String(root, "dock", "right"),
            KittenX = Double(root, "kitten_x", 1),
            KittenY = Double(root, "kitten_y", 0.5),
            KittenPlaced = Bool(root, "kitten_placed", false),
            PickerCursor = Bool(root, "picker_cursor", true),
            PickerCodex = Bool(root, "picker_codex", true),
            PickerClaude = Bool(root, "picker_claude", true),
            PickerGemini = Bool(root, "picker_gemini", true),
            PickerGrok = Bool(root, "picker_grok", true),
            PickerGPT = Bool(root, "picker_gpt", true)
        };
        prefs.Normalize();
        return prefs;
    }

    public IslandPrefs Copy()
    {
        return new IslandPrefs
        {
            Dx = Dx,
            Dy = Dy,
            BubbleDx = BubbleDx,
            BubbleDy = BubbleDy,
            Fill = Fill,
            Text = Text,
            Stroke = Stroke,
            CardFill = CardFill,
            CardStroke = CardStroke,
            Sound = Sound,
            SoundSubmit = SoundSubmit,
            SoundDone = SoundDone,
            SoundStuck = SoundStuck,
            Look = Look,
            PetScale = PetScale,
            BubbleScale = BubbleScale,
            CursorPrivacyAccepted = CursorPrivacyAccepted,
            ScanTranscripts = ScanTranscripts,
            Dock = Dock,
            KittenX = KittenX,
            KittenY = KittenY,
            KittenPlaced = KittenPlaced,
            PickerCursor = PickerCursor,
            PickerCodex = PickerCodex,
            PickerClaude = PickerClaude,
            PickerGemini = PickerGemini,
            PickerGrok = PickerGrok,
            PickerGPT = PickerGPT
        };
    }

    private void Normalize()
    {
        Sound = SoundIDs.Contains(Sound) ? Sound : "boop";
        SoundSubmit = SubmitSounds.Contains(SoundSubmit) ? SoundSubmit : "glass";
        SoundDone = DoneSounds.Contains(SoundDone) ? SoundDone : "chime";
        SoundStuck = StuckSounds.Contains(SoundStuck) ? SoundStuck : "drop";
        Look = NormalizeLook(Look);
        Dock = Docks.Contains(Dock) ? Dock : "right";
        KittenX = Unit(KittenX);
        KittenY = Unit(KittenY);
        if (KittenPlaced) Dock = KittenX < 0.5 ? "left" : "right";
    }

    private PrefsFile ToFile()
    {
        return new PrefsFile
        {
            Dx = Dx,
            Dy = Dy,
            BubbleDx = BubbleDx,
            BubbleDy = BubbleDy,
            Fill = Fill,
            Text = Text,
            Stroke = Stroke,
            CardFill = CardFill,
            CardStroke = CardStroke,
            Sound = Sound,
            SoundSubmit = SoundSubmit,
            SoundDone = SoundDone,
            SoundStuck = SoundStuck,
            Look = Look,
            PetScale = PetScale,
            BubbleScale = BubbleScale,
            CursorPrivacyAccepted = CursorPrivacyAccepted,
            ScanTranscripts = ScanTranscripts,
            Dock = Dock,
            KittenX = KittenX,
            KittenY = KittenY,
            KittenPlaced = KittenPlaced,
            PickerCursor = PickerCursor,
            PickerCodex = PickerCodex,
            PickerClaude = PickerClaude,
            PickerGemini = PickerGemini,
            PickerGrok = PickerGrok,
            PickerGPT = PickerGPT
        };
    }

    private static object? Get(IReadOnlyDictionary<string, object?> json, string key)
    {
        return json.TryGetValue(key, out var value) ? value : null;
    }

    private static double Unit(double value) => Math.Min(1, Math.Max(0, value));

    private static double Scale(object? value, double fallback)
    {
        var number = Bags.Number(value, fallback);
        if (number == 0) return 1;
        return Math.Min(2, Math.Max(0.5, number));
    }

    private static string? Hex(object? value)
    {
        if (value is not string raw) return null;
        var trimmed = raw.Trim().ToLowerInvariant();
        return Regex.IsMatch(trimmed, "^#[0-9a-f]{6}$") ? trimmed : null;
    }

    private static double Double(JsonElement root, string name, double fallback)
    {
        return root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : fallback;
    }

    private static string String(JsonElement root, string name, string fallback)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static bool Bool(JsonElement root, string name, bool fallback)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }

    private sealed class PrefsFile
    {
        [JsonPropertyName("dx")] public double Dx { get; set; }
        [JsonPropertyName("dy")] public double Dy { get; set; }
        [JsonPropertyName("bubbleDx")] public double BubbleDx { get; set; }
        [JsonPropertyName("bubbleDy")] public double BubbleDy { get; set; }
        [JsonPropertyName("fill")] public string Fill { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("stroke")] public string Stroke { get; set; } = "";
        [JsonPropertyName("cardFill")] public string CardFill { get; set; } = "";
        [JsonPropertyName("cardStroke")] public string CardStroke { get; set; } = "";
        [JsonPropertyName("sound")] public string Sound { get; set; } = "";
        [JsonPropertyName("sound_submit")] public string SoundSubmit { get; set; } = "";
        [JsonPropertyName("sound_done")] public string SoundDone { get; set; } = "";
        [JsonPropertyName("sound_stuck")] public string SoundStuck { get; set; } = "";
        [JsonPropertyName("look")] public string Look { get; set; } = "";
        [JsonPropertyName("petScale")] public double PetScale { get; set; }
        [JsonPropertyName("bubbleScale")] public double BubbleScale { get; set; }
        [JsonPropertyName("cursorPrivacyAccepted")] public bool CursorPrivacyAccepted { get; set; }
        [JsonPropertyName("scanTranscripts")] public bool ScanTranscripts { get; set; }
        [JsonPropertyName("dock")] public string Dock { get; set; } = "";
        [JsonPropertyName("kitten_x")] public double KittenX { get; set; }
        [JsonPropertyName("kitten_y")] public double KittenY { get; set; }
        [JsonPropertyName("kitten_placed")] public bool KittenPlaced { get; set; }
        [JsonPropertyName("picker_cursor")] public bool PickerCursor { get; set; }
        [JsonPropertyName("picker_codex")] public bool PickerCodex { get; set; }
        [JsonPropertyName("picker_claude")] public bool PickerClaude { get; set; }
        [JsonPropertyName("picker_gemini")] public bool PickerGemini { get; set; }
        [JsonPropertyName("picker_grok")] public bool PickerGrok { get; set; }
        [JsonPropertyName("picker_gpt")] public bool PickerGPT { get; set; }
    }
}

public sealed class PrefsStore
{
    public string FilePath { get; }

    public PrefsStore(string directory)
    {
        FilePath = Path.Combine(directory, "prefs.json");
    }

    public static PrefsStore Live() => new(AppConfig.SupportDirectory);

    public IslandPrefs Load(IEnumerable<string>? migrating = null)
    {
        var existing = Read(FilePath);
        if (existing != null) return existing;
        if (migrating != null)
        {
            foreach (var legacy in migrating)
            {
                var prefs = Read(legacy);
                if (prefs == null) continue;
                Save(prefs);
                return prefs;
            }
        }
        var fresh = new IslandPrefs();
        Save(fresh);
        return fresh;
    }

    public void Save(IslandPrefs prefs)
    {
        AtomicFile.WriteText(prefs.Encode(), FilePath);
    }

    private static IslandPrefs? Read(string path)
    {
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path);
        try
        {
            return IslandPrefs.Decode(text);
        }
        catch (JsonException)
        {
            var json = JsonFile.Read(path);
            return json.Count == 0 && !text.Contains('{') ? null : IslandPrefs.Parse(json);
        }
    }
}
