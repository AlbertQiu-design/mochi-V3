namespace Mochi.Core;

public readonly record struct YupPoint(double X, double Y);

public readonly record struct YupSize(double Width, double Height);

public readonly record struct YupRect(double X, double Y, double Width, double Height)
{
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;

    public bool Intersects(YupRect other)
    {
        return MinX < other.MaxX && MaxX > other.MinX && MinY < other.MaxY && MaxY > other.MinY;
    }
}

public readonly record struct CapsulePlace(YupRect Frame, bool Scrolls, bool Upward);

public readonly record struct AgentChrome(double AgentsTop, double Tail);

public readonly record struct CapsuleStack(double Kitten, double Divider, double Agents, double Plus, double Gear);

public static class ShellScale
{
    public static double Factor(double value)
    {
        var number = value == 0 ? 1 : value;
        return Math.Min(2, Math.Max(0.5, number));
    }
}

public static class ShellGeometry
{
    public static double Clamp01(double value) => Math.Min(1, Math.Max(0, value));

    public static (double X, double Y) Ratios(YupPoint anchor, YupRect screen)
    {
        var x = screen.Width == 0 ? 0.5 : (anchor.X - screen.MinX) / screen.Width;
        var y = screen.Height == 0 ? 0.5 : (anchor.Y - screen.MinY) / screen.Height;
        return (Clamp01(x), Clamp01(y));
    }

    public static YupPoint Anchor(double x, double y, YupRect screen)
    {
        return new YupPoint(
            screen.MinX + Clamp01(x) * screen.Width,
            screen.MinY + Clamp01(y) * screen.Height);
    }

    public static YupPoint DefaultAnchor(YupRect screen, YupRect visible, double scale = 1)
    {
        var kitten = AppConfig.KittenSize * ShellScale.Factor(scale);
        var raw = new YupPoint(screen.MaxX - AppConfig.ScreenInset - kitten / 2, screen.MidY);
        return ClampCenter(raw, kitten, visible);
    }

    public static YupPoint LeftAnchor(YupRect screen, YupRect visible, double scale = 1)
    {
        var kitten = AppConfig.KittenSize * ShellScale.Factor(scale);
        var raw = new YupPoint(screen.MinX + AppConfig.ScreenInset + kitten / 2, screen.MidY);
        return ClampCenter(raw, kitten, visible);
    }

    public static YupPoint ClampCenter(YupPoint center, double size, YupRect visible)
    {
        var half = size / 2;
        var x = Math.Min(Math.Max(center.X, visible.MinX + half), visible.MaxX - half);
        var y = Math.Min(Math.Max(center.Y, visible.MinY + half), visible.MaxY - half);
        return new YupPoint(x, y);
    }

    public static double ContentHeight(int agentCount, double scale = 1)
    {
        var s = ShellScale.Factor(scale);
        var count = Math.Max(0, agentCount);
        var agents = count == 0 ? 0 : count * AppConfig.AgentRow + (count - 1) * AppConfig.RowGap;
        var beforePlus = count == 0 ? 0 : AppConfig.RowGap;
        return (AppConfig.CapsulePadY
            + AppConfig.AgentRow
            + AppConfig.DividerGap
            + AppConfig.DividerHeight
            + AppConfig.DividerGap
            + agents
            + beforePlus
            + AppConfig.ControlRow
            + AppConfig.RowGap
            + AppConfig.ControlRow
            + AppConfig.CapsulePadY) * s;
    }

    public static CapsuleStack Stack(int agentCount, double scale = 1)
    {
        var s = ShellScale.Factor(scale);
        var divider = (AppConfig.CapsulePadY + AppConfig.AgentRow + AppConfig.DividerGap) * s;
        var agents = divider + (AppConfig.DividerHeight + AppConfig.DividerGap) * s;
        var count = Math.Max(0, agentCount);
        var block = count == 0
            ? 0
            : (count * AppConfig.AgentRow + Math.Max(0, count - 1) * AppConfig.RowGap) * s;
        var plus = agents + block + (count == 0 ? 0 : AppConfig.RowGap * s);
        var gear = plus + (AppConfig.ControlRow + AppConfig.RowGap) * s;
        return new CapsuleStack(0, divider, agents, plus, gear);
    }

    public static CapsulePlace PlaceCapsule(
        YupPoint anchorCenter,
        bool expanded,
        int agentCount,
        YupRect visible,
        double scale = 1)
    {
        var s = ShellScale.Factor(scale);
        var kitten = AppConfig.KittenSize * s;
        var center = ClampCenter(anchorCenter, kitten, visible);
        var top = center.Y + kitten / 2;
        var bottom = center.Y - kitten / 2;
        if (!expanded)
        {
            return new CapsulePlace(
                new YupRect(center.X - kitten / 2, bottom, kitten, kitten),
                false,
                false);
        }
        var natural = ContentHeight(agentCount, s);
        var width = AppConfig.CapsuleWidth * s;
        var x = center.X - width / 2;
        var roomDown = Math.Max(kitten, top - visible.MinY);
        var roomUp = Math.Max(kitten, visible.MaxY - bottom);
        if (natural <= roomDown + 0.5)
        {
            return new CapsulePlace(new YupRect(x, top - natural, width, natural), false, false);
        }
        if (natural <= roomUp + 0.5)
        {
            return new CapsulePlace(new YupRect(x, bottom, width, natural), false, true);
        }
        if (roomDown >= roomUp)
        {
            var height = Math.Min(natural, roomDown);
            return new CapsulePlace(new YupRect(x, top - height, width, height), true, false);
        }
        var fitted = Math.Min(natural, Math.Max(kitten, visible.MaxY - bottom));
        return new CapsulePlace(new YupRect(x, bottom, width, fitted), natural > fitted + 0.5, true);
    }

    public static AgentChrome AgentChrome(int agentCount, double scale, bool upward)
    {
        var s = ShellScale.Factor(scale);
        var count = Math.Max(0, agentCount);
        var before = count == 0 ? 0 : AppConfig.RowGap;
        if (!upward)
        {
            var tops = Stack(count, s);
            var tail = (before + AppConfig.ControlRow + AppConfig.RowGap + AppConfig.ControlRow + AppConfig.CapsulePadY) * s;
            return new AgentChrome(tops.Agents, tail);
        }
        var head = (AppConfig.CapsulePadY + AppConfig.ControlRow + AppConfig.RowGap + AppConfig.ControlRow + before) * s;
        var downTail = (AppConfig.DividerGap + AppConfig.DividerHeight + AppConfig.DividerGap + AppConfig.AgentRow + AppConfig.CapsulePadY) * s;
        return new AgentChrome(head, downTail);
    }

    /// Converts a y-up rect into top-left coordinates inside the same work area.
    public static YupRect ToTopLeft(YupRect frame, double workAreaHeight)
    {
        return new YupRect(frame.X, workAreaHeight - frame.MaxY, frame.Width, frame.Height);
    }
}

public readonly record struct BubblePlacement(YupRect Frame, double BeakY, bool PointsRight);

public static class BubblePlacer
{
    public static BubblePlacement Place(YupRect anchor, YupRect capsule, YupSize size, YupRect screen)
    {
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        var onLeftHalf = capsule.MidX < screen.MidX;
        var pointsRight = !onLeftHalf;
        var x = pointsRight
            ? capsule.MinX - AppConfig.BeakGap - width
            : capsule.MaxX + AppConfig.BeakGap;
        var beakRoom = AppConfig.BubbleRadius + 8;
        var y = anchor.MidY + beakRoom - height;
        y = Math.Min(Math.Max(y, screen.MinY), Math.Max(screen.MinY, screen.MaxY - height));
        x = Math.Min(Math.Max(x, screen.MinX), Math.Max(screen.MinX, screen.MaxX - width));
        var frame = new YupRect(x, y, width, height);
        if (frame.Intersects(capsule))
        {
            var nudged = pointsRight
                ? capsule.MinX - AppConfig.BeakGap - width
                : capsule.MaxX + AppConfig.BeakGap;
            frame = new YupRect(nudged, frame.Y, width, height);
        }
        var fromBottom = anchor.MidY - frame.MinY;
        var beakFromTop = height - fromBottom;
        var lower = AppConfig.BubbleRadius + 8;
        var upper = height - AppConfig.BubbleRadius - 8;
        beakFromTop = upper >= lower ? Math.Min(Math.Max(beakFromTop, lower), upper) : height / 2;
        return new BubblePlacement(frame, beakFromTop, pointsRight);
    }
}

public static class BubbleAge
{
    public static string Relative(double updated, double now)
    {
        if (updated <= 1) return "";
        var seconds = Math.Max(0, now - updated);
        if (seconds < 60) return "now";
        var minutes = (int)(seconds / 60);
        if (minutes < 60) return minutes + "m";
        var hours = minutes / 60;
        if (hours < 48) return hours + "h";
        return (hours / 24) + "d";
    }
}

public static class CapsuleOutline
{
    public static bool Contains(YupPoint point, double width, double height)
    {
        var w = Math.Max(width, 1);
        var h = Math.Max(height, 1);
        if (point.X < 0 || point.Y < 0 || point.X > w || point.Y > h) return false;
        var radius = Math.Min(w, h) / 2;
        if (point.Y >= radius && point.Y <= h - radius) return true;
        var centerY = point.Y < radius ? radius : h - radius;
        var dx = point.X - w / 2;
        var dy = point.Y - centerY;
        return dx * dx + dy * dy <= radius * radius + 0.25;
    }
}

public readonly record struct AgentIconCandidate(
    string Id,
    string Source,
    string Title,
    string Status,
    bool Read,
    double Updated,
    bool HasPendingOptions = false);

public readonly record struct AgentIconRow(
    string Id,
    string Source,
    string Title,
    string Status,
    bool Read,
    bool ShowsDot,
    IReadOnlyList<string> MemberIDs);

public static class AgentIconList
{
    public static readonly string[] Order = ["cursor", "codex", "claude", "gemini", "copilot", "opencode", "grok-bot", "gpt"];

    public static List<AgentIconRow> Rows(
        IReadOnlyList<AgentIconCandidate> items,
        IReadOnlySet<string>? holding = null,
        double? now = null)
    {
        holding ??= new HashSet<string>();
        var visible = new List<AgentIconCandidate>();
        foreach (var item in items)
        {
            var source = AgentLaunch.Normalize(item.Source);
            if (source == "mochi") continue;
            if (!ShouldShow(item with { Source = source }, holding, now)) continue;
            visible.Add(item with { Source = source });
        }
        visible.Sort((lhs, rhs) =>
        {
            var left = Array.IndexOf(Order, lhs.Source);
            var right = Array.IndexOf(Order, rhs.Source);
            if (left < 0) left = 99;
            if (right < 0) right = 99;
            if (left != right) return left.CompareTo(right);
            if (lhs.Updated != rhs.Updated) return rhs.Updated.CompareTo(lhs.Updated);
            return string.Compare(rhs.Id, lhs.Id, StringComparison.Ordinal);
        });
        return visible.Select(item =>
        {
            var needsAttention = item.Status == "wait" || item.HasPendingOptions
                || item.Status == "done" || item.Status == "fail";
            var dot = !item.Read && needsAttention;
            return new AgentIconRow(item.Id, item.Source, item.Title, item.Status, item.Read, dot, [item.Id]);
        }).ToList();
    }

    public static bool IsWorking(AgentIconCandidate item, double? now)
    {
        if (item.Status != "running") return false;
        if (now is not double clock) return true;
        return item.Updated > 0 && clock - item.Updated <= AppConfig.ActiveTaskWindow;
    }

    public static bool IsExpired(AgentIconCandidate item, double? now)
    {
        if (now is not double clock || item.Updated <= 0) return false;
        return clock - item.Updated > AppConfig.IconMaxAge;
    }

    public static bool ShouldShow(AgentIconCandidate item, IReadOnlySet<string>? holding = null, double? now = null)
    {
        holding ??= new HashSet<string>();
        if (IsExpired(item, now)) return false;
        if (holding.Contains(item.Id)) return true;
        if (item.HasPendingOptions && item.Status != "running") return true;
        return item.Status switch
        {
            "running" => IsWorking(item, now),
            "wait" => true,
            "done" or "fail" => !item.Read,
            _ => false
        };
    }

    public static bool PermissionStaysOpen(string taskStatus, bool isFace)
    {
        return taskStatus == "wait" && isFace;
    }
}

public struct AlertSoundGate
{
    public double? Last;

    public bool ShouldPlay(double now)
    {
        if (Last is double last && now - last < 1) return false;
        Last = now;
        return true;
    }
}
