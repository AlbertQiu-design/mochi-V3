using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Mochi.Core;

namespace MochiIsland;

/// <summary>
/// The Windows counterpart of the macOS kitten panel: a borderless layered window
/// with per-pixel alpha (isOpaque = false, clear background) that draws the PNG
/// straight onto the desktop. Transparent pixels let clicks through.
/// </summary>
internal sealed class PetOverlay : IDisposable
{
    private const string ClassName = "MochiIslandPetOverlay";
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopMost = 0x00000008;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmCaptureChanged = 0x0215;
    private const int HtClient = 1;
    private const int HtTransparent = -1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int SwShowNoActivate = 4;
    private const int UlwAlpha = 2;

    private static readonly WndProc Proc = OnMessage;
    private static readonly Dictionary<IntPtr, PetOverlay> Instances = new();
    private static bool registered;

    private readonly Action click;
    private readonly Action<int, int> moved;
    private IntPtr hwnd;
    private Bitmap? art;
    private int x;
    private int y;
    private bool down;
    private bool dragging;
    private int downX;
    private int downY;
    private string look = "a";

    public PetOverlay(Action click, Action<int, int> moved)
    {
        this.click = click;
        this.moved = moved;
        EnsureClass();
        hwnd = CreateWindowEx(
            WsExLayered | WsExToolWindow | WsExTopMost,
            ClassName,
            "",
            WsPopup,
            0, 0, 1, 1,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (hwnd != IntPtr.Zero) Instances[hwnd] = this;
    }

    public bool Shown { get; private set; }

    public void Show(int left, int top, int width, int height, string petLook)
    {
        if (hwnd == IntPtr.Zero || width < 1 || height < 1) return;
        x = left;
        y = top;
        var size = Math.Max(1, Math.Min(width, height));
        if (art == null || art.Width != size || art.Height != size || look != petLook)
        {
            art?.Dispose();
            art = Render(petLook, size);
            look = petLook;
        }
        Apply(size);
        SetWindowPos(hwnd, HwndTopMost, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        ShowWindow(hwnd, SwShowNoActivate);
        Shown = true;
    }

    /// <summary>Another topmost window can end up above us; re-assert every so often.</summary>
    public void EnsureTopMost()
    {
        if (hwnd == IntPtr.Zero || !Shown) return;
        SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
    }

    public void Hide()
    {
        if (hwnd != IntPtr.Zero) ShowWindow(hwnd, 0);
        Shown = false;
    }

    public void Dispose()
    {
        if (hwnd == IntPtr.Zero) return;
        Instances.Remove(hwnd);
        DestroyWindow(hwnd);
        hwnd = IntPtr.Zero;
        art?.Dispose();
        art = null;
    }

    /// <summary>Screen position of the badge centre, used for the visibility self-check.</summary>
    public static (int X, int Y) BadgeCentre(int left, int top, double dipSide, double raster)
    {
        var side = dipSide * raster;
        var badge = Math.Max(10, dipSide * 0.28) * raster;
        var margin = dipSide * 0.04 * raster;
        var start = side - margin - badge;
        return ((int)Math.Round(left + start + badge / 2), (int)Math.Round(top + start + badge / 2));
    }

    private static void EnsureClass()
    {
        if (registered) return;
        registered = true;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Proc,
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName
        };
        RegisterClassEx(ref wc);
    }

    private static Bitmap Render(string look, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        var path = KittenArt.ArtworkPath(look);
        if (path != null)
        {
            using var source = Image.FromFile(path);
            var fit = Math.Min((double)size / source.Width, (double)size / source.Height);
            var drawWidth = Math.Max(1, (int)Math.Round(source.Width * fit));
            var drawHeight = Math.Max(1, (int)Math.Round(source.Height * fit));
            graphics.DrawImage(source, (size - drawWidth) / 2, (size - drawHeight) / 2, drawWidth, drawHeight);
        }
        var badge = Math.Max(10.0, size * 0.28);
        var margin = size * 0.04;
        var bx = (float)(size - margin - badge);
        var by = (float)(size - margin - badge);
        var colour = KittenArt.Badge(look);
        using var fill = new SolidBrush(Color.FromArgb(255, colour.R, colour.G, colour.B));
        graphics.FillEllipse(fill, bx, by, (float)badge, (float)badge);
        using var stroke = new Pen(Color.FromArgb(217, 255, 255, 255), (float)Math.Max(0.8, badge * 0.06));
        graphics.DrawEllipse(stroke, bx + 0.5f, by + 0.5f, (float)badge - 1, (float)badge - 1);
        using var font = new Font("Microsoft YaHei UI", (float)(badge * 0.62), FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(IslandPrefs.PetLetter(look), font, Brushes.White, new RectangleF(bx, by, (float)badge, (float)badge), format);
        return bitmap;
    }

    private void Apply(int size)
    {
        if (art == null) return;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER { biSize = 40, biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32, biCompression = 0 };
        var info = new BITMAPINFO { bmiHeader = header };
        var dib = CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
        var stride = size * 4;
        var data = art.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (var row = 0; row < size; row++)
            {
                CopyMemory(bits + row * stride, data.Scan0 + row * data.Stride, (UIntPtr)(uint)stride);
            }
        }
        finally
        {
            art.UnlockBits(data);
        }
        var previous = SelectObject(memory, dib);
        var destination = new POINT { X = x, Y = y };
        var source = new POINT { X = 0, Y = 0 };
        var area = new SIZE { CX = size, CY = size };
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        UpdateLayeredWindow(hwnd, screen, ref destination, ref area, memory, ref source, 0, ref blend, UlwAlpha);
        SelectObject(memory, previous);
        DeleteObject(dib);
        DeleteDC(memory);
        ReleaseDC(IntPtr.Zero, screen);
    }

    private static IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (!Instances.TryGetValue(hwnd, out var overlay)) return DefWindowProc(hwnd, message, wParam, lParam);
        switch (message)
        {
            case WmNcHitTest:
            {
                GetCursorPos(out var point);
                var localX = point.X - overlay.x;
                var localY = point.Y - overlay.y;
                if (overlay.art == null || localX < 0 || localY < 0 || localX >= overlay.art.Width || localY >= overlay.art.Height) return new IntPtr(HtTransparent);
                return new IntPtr(overlay.art.GetPixel(localX, localY).A > 24 ? HtClient : HtTransparent);
            }
            case WmLButtonDown:
            {
                GetCursorPos(out var point);
                overlay.down = true;
                overlay.dragging = false;
                overlay.downX = point.X;
                overlay.downY = point.Y;
                SetCapture(hwnd);
                return IntPtr.Zero;
            }
            case WmMouseMove:
            {
                if (!overlay.down) break;
                GetCursorPos(out var point);
                var dx = point.X - overlay.downX;
                var dy = point.Y - overlay.downY;
                if (!overlay.dragging && dx * dx + dy * dy >= 16) overlay.dragging = true;
                if (!overlay.dragging) break;
                overlay.x = point.X - (overlay.art?.Width ?? 0) / 2;
                overlay.y = point.Y - (overlay.art?.Height ?? 0) / 2;
                SetWindowPos(hwnd, HwndTopMost, overlay.x, overlay.y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                return IntPtr.Zero;
            }
            case WmLButtonUp:
            {
                if (!overlay.down) break;
                overlay.down = false;
                ReleaseCapture();
                if (overlay.dragging)
                {
                    overlay.dragging = false;
                    var size = overlay.art?.Width ?? 0;
                    overlay.moved(overlay.x + size / 2, overlay.y + size / 2);
                }
                else
                {
                    overlay.click();
                }
                return IntPtr.Zero;
            }
            case WmCaptureChanged:
                overlay.down = false;
                overlay.dragging = false;
                break;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static readonly IntPtr HwndTopMost = new(-1);

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public WndProc? lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int CX; public int CY; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public int bmiColors; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
        public byte Padding;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr screen, ref POINT destination, ref SIZE size, IntPtr memory, ref POINT source, int colourKey, ref BLENDFUNCTION blend, int flags);
}
