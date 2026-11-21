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
    private const long WsExAppWindow = 0x00040000;
    private const int RgnOr = 2;

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
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        if (!Hits.ContainsKey(hwnd))
        {
            Hits[hwnd] = new HitState();
            SetWindowSubclass(hwnd, Proc, 1, 0);
        }
        return hwnd;
    }

    public static void SetHitTest(IntPtr hwnd, Func<int, int, bool> contains)
    {
        if (Hits.TryGetValue(hwnd, out var state)) state.Contains = contains;
    }

    public static void SetStadium(IntPtr hwnd, int width, int height)
    {
        var diameter = Math.Max(1, Math.Min(width, height));
        var region = CreateRoundRectRgn(0, 0, Math.Max(1, width), Math.Max(1, height), diameter, diameter);
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

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr left, IntPtr right, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
