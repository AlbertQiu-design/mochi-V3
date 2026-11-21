namespace Mochi.Core;

public static class AppConfig
{
    public const string BundleID = "com.mochiisland.win";
    public const string HookMarker = "mochi-island";
    public const int HookVersion = 1;
    public const int FedTaskLimit = 500;
    public const double DoneKeep = 14 * 24 * 60 * 60;
    public const double LiveKeep = 30 * 60;
    public const double ActiveTaskWindow = 180;
    public const double IconMaxAge = 24 * 60 * 60;

    public const string MaterialAPI = "glassEffect.regular";
    public const double BubbleRadius = 12;
    public const double BeakWidth = 10;
    public const double BeakHeight = 16;
    public const double BeakGap = 10;
    public const double BeakTipRadius = 2.8;
    public const double BubbleMinWidth = 160;
    public const double BubbleMaxWidth = 280;
    public const double BubbleBeakInset = 0;
    public const double BubblePadX = 12;
    public const double BubblePadY = 8;
    public const double BubbleMinHeight = 44;
    public const double KittenSize = 56;
    public const double CapsuleWidth = 56;
    public const double CapsuleRadius = 28;
    public const double CapsulePadY = 8;
    public const double CapsulePadX = 6;
    public const double KittenMark = 40;
    public const double DividerWidth = 32;
    public const double DividerHeight = 1;
    public const double DividerGap = 8;
    public const double AgentRow = 44;
    public const double AgentIcon = 28;
    public const double RowGap = 4;
    public const double ControlRow = 44;
    public const double SelectedRadius = 14;
    public const double UnreadDot = 8;
    public const double ScreenInset = 8;
    public const double DragThreshold = 3;
    public const double SettingsBarHeight = 32;
    public const double SettingsBarRadius = 16;
    public const double SettingsSelectRadius = 14;
    public const double SettingsTabMinWidth = 64;
    public const double SettingsWidth = 420;
    public const double GlassTintRed = 28;
    public const double GlassTintGreen = 28;
    public const double GlassTintBlue = 30;
    public const double SymbolSize = 20;

    public static string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string SupportDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MochiIsland");

    public static string CursorDirectory => Path.Combine(HomeDirectory, ".cursor");

    public static string TasksFile => Path.Combine(CursorDirectory, "mochi-island-tasks.json");

    public static string ReplyFile => Path.Combine(CursorDirectory, "mochi-island-reply.json");
}
