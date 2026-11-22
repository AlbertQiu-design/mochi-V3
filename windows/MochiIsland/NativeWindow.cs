using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace MochiIsland;

internal static class NativeWindow
{
    private const int HtTransparent = -1;
    private const uint WmNcHitTest = 0x0084;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExTopMost = 0x00000008;
    private const long WsExAppWindow = 0x00040000;
    private const long WsExWindowEdge = 0x00000100;
    private const int RgnOr = 2;
    private static readonly IntPtr HwndTopMost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmncrpDisabled = 1;
    private const int DwmwcpDoNotRound = 1;
    private const uint DwmColorNone = 0xFFFFFFFE;

    private static readonly SubclassProc Proc = OnMessage;
    private static readonly Dictionary<IntPtr, HitState> Hits = new();

    public static IntPtr Prepare(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        style |= WsExToolWindow;
        style &= ~WsExAppWindow;
        style &= ~WsExWindowEdge;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));

        // Windows 11 draws a 1px system border around every window; on a shaped
        // window it follows the region and reads as a dashed outline. Turn it off.
        var none = unchecked((int)DwmColorNone);
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(int));
        var square = DwmwcpDoNotRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref square, sizeof(int));
        var noFrame = DwmncrpDisabled;
        DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, ref noFrame, sizeof(int));
        if (!Hits.ContainsKey(hwnd))
        {
            Hits[hwnd] = new HitState();
            SetWindowSubclass(hwnd, Proc, 1, 0);
        }
        return hwnd;
    }

    /// <summary>
    /// WS_EX_TOPMOST cannot be set through SetWindowLongPtr, and a call made before the
    /// window is first shown gets overwritten by WinUI. Re-assert it once the window is up.
    /// </summary>
    public static int IdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return (int)(unchecked((uint)Environment.TickCount - info.dwTime) / 1000);
    }

    public static void EnsureTopMost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
    }

    public static void EnsureTopMost(Window window) => EnsureTopMost(WindowNative.GetWindowHandle(window));

    /// <summary>WinUI can re-apply its own window border during activation, so re-assert it.</summary>
    public static void EnsureBorderless(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var none = unchecked((int)DwmColorNone);
        DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(int));
        var square = DwmwcpDoNotRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref square, sizeof(int));
        var noFrame = DwmncrpDisabled;
        DwmSetWindowAttribute(hwnd, DwmwaNcRenderingPolicy, ref noFrame, sizeof(int));
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        if ((style & WsExWindowEdge) != 0)
        {
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style & ~WsExWindowEdge));
        }
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        // WinUI re-applies its frame after showing; force DWM to recompute it.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0002 | 0x0001 | 0x0004 | 0x0010);
    }

    /// <summary>Move without touching size, z-order or layout — used while dragging.</summary>
    public static void MoveOnly(IntPtr hwnd, int x, int y)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010 | 0x0400);
    }

    public static void SetHitTest(IntPtr hwnd, Func<int, int, bool> contains)
    {
        if (Hits.TryGetValue(hwnd, out var state)) state.Contains = contains;
    }

    /// <summary>Drop the window region so DWM can round a plain rectangular window.</summary>
    public static void ClearRegion(IntPtr hwnd)
    {
        SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    public static void SetRoundCorners(IntPtr hwnd)
    {
        const int DwmwaWindowCornerPreference = 33;
        const int DwmwcpRound = 2;
        var round = DwmwcpRound;
        DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
    }

    /// <summary>
    /// Shape the window to polygon outlines of the kitten artwork (plus the badge
    /// disc) so the desktop shows through everywhere the art is transparent.
    /// DWM falls back to the bounding box for rectangle-list regions; polygons work.
    /// </summary>
    public static void SetRegionFromLoops(IntPtr hwnd, int width, int height, List<List<System.Drawing.Point>> loops)
    {
        IntPtr region = IntPtr.Zero;
        var made = 0;
        var only = loops.OrderByDescending(item => item.Count).FirstOrDefault();
        var used = only == null ? loops : new List<List<System.Drawing.Point>> { only };
        foreach (var loop in used)
        {
            if (loop.Count < 3) continue;
            var points = new POINT[loop.Count];
            for (var index = 0; index < loop.Count; index++)
            {
                points[index] = new POINT { X = loop[index].X, Y = loop[index].Y };
            }
            var polygon = CreatePolygonRgn(points, points.Length, 1);
            if (polygon == IntPtr.Zero) continue;
            made++;
            if (region == IntPtr.Zero)
            {
                region = polygon;
            }
            else
            {
                CombineRgn(region, region, polygon, RgnOr);
                DeleteObject(polygon);
            }
        }
        if (region == IntPtr.Zero) region = CreateRectRgn(0, 0, width, height);
        var ok = SetWindowRgn(hwnd, region, true);
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mochi-mask.log"), $"loops={loops.Count} made={made} set={ok}" + Environment.NewLine); } catch { }
    }

    /// <summary>A plain inset rectangle: clips the outermost pixels, where a
    /// compositor frame can leave a light strip.</summary>
    public static void SetInsetRect(IntPtr hwnd, int width, int height, int inset)
    {
        var region = CreateRectRgn(inset, inset, Math.Max(inset + 1, width - inset), Math.Max(inset + 1, height - inset));
        SetWindowRgn(hwnd, region, true);
    }

    public static void SetStadium(IntPtr hwnd, int width, int height)
    {
        var diameter = Math.Max(1, Math.Min(width, height));
        var region = CreateRoundRectRgn(0, 0, Math.Max(1, width), Math.Max(1, height), diameter, diameter);
        SetWindowRgn(hwnd, region, true);
    }

    /// <summary>
    /// The collapsed capsule plus the letter badge disc that sits just outside the
    /// kitten circle, so the badge is not clipped by the stadium region.
    /// </summary>
    public static void SetStadiumWithDisc(IntPtr hwnd, int width, int height, int discX, int discY, int discSize)
    {
        var diameter = Math.Max(1, Math.Min(width, height));
        var region = CreateRoundRectRgn(0, 0, Math.Max(1, width), Math.Max(1, height), diameter, diameter);
        var disc = CreateRoundRectRgn(discX, discY, discX + Math.Max(1, discSize), discY + Math.Max(1, discSize), Math.Max(1, discSize), Math.Max(1, discSize));
        CombineRgn(region, region, disc, RgnOr);
        DeleteObject(disc);
        SetWindowRgn(hwnd, region, true);
    }

    public static void SetBubble(IntPtr hwnd, int bodyX, int bodyY, int bodyWidth, int bodyHeight, int radius, int beakX, int beakY, int beakWidth, int beakHeight)
    {
        var body = CreateRoundRectRgn(bodyX, bodyY, bodyX + Math.Max(1, bodyWidth), bodyY + Math.Max(1, bodyHeight), Math.Max(1, radius * 2), Math.Max(1, radius * 2));
        var beak = CreateRoundRectRgn(beakX, beakY, beakX + Math.Max(1, beakWidth), beakY + Math.Max(1, beakHeight), 6, 6);
        CombineRgn(body, body, beak, RgnOr);
        DeleteObject(beak);
        SetWindowRgn(hwnd, body, true);
    }

    public static (int X, int Y) Cursor()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    public static void Release(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Hits.Remove(hwnd)) return;
        RemoveWindowSubclass(hwnd, Proc, 1);
    }

    private static IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (message == WmNcHitTest && Hits.TryGetValue(hwnd, out var state) && state.Contains != null)
        {
            var point = new Point
            {
                X = (short)(lParam.ToInt64() & 0xFFFF),
                Y = (short)((lParam.ToInt64() >> 16) & 0xFFFF)
            };
            if (ScreenToClient(hwnd, ref point) && !state.Contains(point.X, point.Y))
            {
                return new IntPtr(HtTransparent);
            }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private sealed class HitState
    {
        public Func<int, int, bool>? Contains;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, nuint id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePolygonRgn(POINT[] points, int count, int mode);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr left, IntPtr right, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
