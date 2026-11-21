using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mochi.Core;

public static class JsonFile
{
    public static Dictionary<string, object?> Read(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, object?>();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object?>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, object?>();
            return AsObject(doc.RootElement);
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    public static void Write(object? value, string path)
    {
        var json = Serialize(value);
        AtomicFile.Write(new UTF8Encoding(false).GetBytes(json), path);
    }

    public static Dictionary<string, object?> AsObject(JsonElement element)
    {
        var bag = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            bag[property.Name] = FromElement(property.Value);
        }
        return bag;
    }

    public static object? FromElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return AsObject(element);
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(FromElement).ToList();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                var raw = element.GetRawText();
                if (raw.IndexOfAny(['.', 'e', 'E']) < 0 && element.TryGetInt32(out var whole))
                {
                    return whole;
                }
                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }

    public static string Serialize(object? value)
    {
        var builder = new StringBuilder();
        WriteValue(builder, value, 0);
        return builder.ToString();
    }

    private static void WriteValue(StringBuilder builder, object? value, int depth)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                return;
            case string text:
                builder.Append(Quote(text));
                return;
            case bool flag:
                builder.Append(flag ? "true" : "false");
                return;
            case int number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case long number:
                builder.Append(number.ToString(CultureInfo.InvariantCulture));
                return;
            case double number:
                builder.Append(FormatDouble(number));
                return;
            case float number:
                builder.Append(FormatDouble(number));
                return;
            case Dictionary<string, string> strings:
                WriteObject(builder, strings.ToDictionary(pair => pair.Key, pair => (object?)pair.Value), depth);
                return;
            case IDictionary<string, object?> bag:
                WriteObject(builder, bag, depth);
                return;
            case IEnumerable<object?> list:
                WriteArray(builder, list, depth);
                return;
            default:
                builder.Append(Quote(value.ToString() ?? ""));
                return;
        }
    }

    private static void WriteObject(StringBuilder builder, IDictionary<string, object?> bag, int depth)
    {
        builder.Append('{');
        var keys = bag.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
        if (keys.Count == 0)
        {
            builder.Append('}');
            return;
        }
        builder.Append('\n');
        for (var index = 0; index < keys.Count; index++)
        {
            var key = keys[index];
            Indent(builder, depth + 1);
            builder.Append(Quote(key));
            builder.Append(" : ");
            WriteValue(builder, bag[key], depth + 1);
            if (index + 1 < keys.Count) builder.Append(',');
            builder.Append('\n');
        }
        Indent(builder, depth);
        builder.Append('}');
    }

    private static void WriteArray(StringBuilder builder, IEnumerable<object?> list, int depth)
    {
        var items = list.ToList();
        builder.Append('[');
        if (items.Count == 0)
        {
            builder.Append(']');
            return;
        }
        builder.Append('\n');
        for (var index = 0; index < items.Count; index++)
        {
            Indent(builder, depth + 1);
            WriteValue(builder, items[index], depth + 1);
            if (index + 1 < items.Count) builder.Append(',');
            builder.Append('\n');
        }
        Indent(builder, depth);
        builder.Append(']');
    }

    private static void Indent(StringBuilder builder, int depth)
    {
        builder.Append(' ', depth * 2);
    }

    public static string Quote(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        foreach (var scalar in text)
        {
            switch (scalar)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (scalar < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)scalar).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(scalar);
                    }
                    break;
            }
        }
        builder.Append('"');
        return builder.ToString();
    }

    private static string FormatDouble(double number)
    {
        if (double.IsFinite(number) && Math.Abs(number - Math.Round(number)) < 1e-9
            && number is >= int.MinValue and <= int.MaxValue)
        {
            return ((long)Math.Round(number)).ToString(CultureInfo.InvariantCulture);
        }
        return number.ToString("G17", CultureInfo.InvariantCulture);
    }
}

public static class Bags
{
    public static string Text(object? value)
    {
        if (value is not string text) return "";
        var trimmed = text.Trim();
        return trimmed == "nil" ? "" : trimmed;
    }

    public static int Whole(object? value)
    {
        return value switch
        {
            int number => number,
            long number => (int)number,
            double number => (int)number,
            float number => (int)number,
            _ => 0
        };
    }

    public static double Number(object? value, double fallback)
    {
        return value switch
        {
            int number => number,
            long number => number,
            double number => number,
            float number => number,
            _ => fallback
        };
    }

    public static bool Flag(object? value, bool fallback)
    {
        return value switch
        {
            bool flag => flag,
            int number => number != 0,
            long number => number != 0,
            double number => number != 0,
            _ => fallback
        };
    }

    public static Dictionary<string, object?> Object(object? value)
    {
        return value switch
        {
            Dictionary<string, object?> bag => bag,
            Dictionary<string, string> strings => strings.ToDictionary(pair => pair.Key, pair => (object?)pair.Value),
            _ => new Dictionary<string, object?>()
        };
    }

    public static List<object?> Array(object? value)
    {
        return value switch
        {
            List<object?> list => list,
            object?[] items => items.ToList(),
            IEnumerable<object?> items => items.ToList(),
            _ => new List<object?>()
        };
    }

    public static List<string> Strings(object? value)
    {
        return value switch
        {
            List<string> typed => typed.ToList(),
            IEnumerable<object?> items => items.Select(Text).Where(item => item.Length > 0 || item == "").Where(_ => true).OfType<string>().ToList(),
            _ => new List<string>()
        };
    }

    public static List<Dictionary<string, string>> StringOptions(object? value)
    {
        var rows = new List<Dictionary<string, string>>();
        foreach (var item in Array(value))
        {
            var bag = Object(item);
            if (bag.Count == 0 && item is not Dictionary<string, object?> and not Dictionary<string, string>) continue;
            var label = Text(bag.GetValueOrDefault("label"));
            if (label.Length == 0) continue;
            rows.Add(new Dictionary<string, string>
            {
                ["id"] = Text(bag.GetValueOrDefault("id")),
                ["label"] = label
            });
        }
        return rows;
    }
}
