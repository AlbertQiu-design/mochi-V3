using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Mochi.Core;
using Windows.Graphics;
using Windows.UI;

namespace MochiIsland
{
    public sealed partial class ShellWindow : Window
    {
        private readonly DesktopState state = new();
        private BubbleWindow? bubble;
        private AlertSoundGate alerts = new();
        private readonly List<ChromeRow> rows = [];
        private FileSystemWatcher? watcher;
        private DispatcherQueueTimer? spring;
        private DispatcherQueueTimer? watchTimer;
        private DispatcherQueueTimer? life;
        private DispatcherQueueTimer? dots;
        private IntPtr hwnd;
        private bool pointerDown;
        private bool dragging;
        private int dragX;
        private int dragY;
        private int centerX;
        private int centerY;
        private double animFrom;
        private double animTo;
        private double animHeight;
        private bool animating;
        private long animStart;
        private string chromeKey = "";
        private bool upward;
        private int dotPhase;
        private double selectionY;
        private double selectionTarget;
        private bool selectionMoving;

        public ShellWindow()
        {
            InitializeComponent();
            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            AppWindow.SetPresenter(presenter);
            SystemBackdrop = null;
            Stage.PointerPressed += Pressed;
            Stage.PointerMoved += Moved;
            Stage.PointerReleased += Released;
            Stage.PointerCaptureLost += (_, _) => pointerDown = false;
            Stage.Loaded += (_, _) =>
            {
                if (hwnd != IntPtr.Zero) LayoutWindow(animating ? animHeight : null);
            };
            Closed += (_, _) => Shutdown();
            Activated += (_, _) => Ensure();
        }

        private void Ensure()
        {
            if (hwnd != IntPtr.Zero) return;
            hwnd = NativeWindow.Prepare(this);
            state.Load();
            spring = DispatcherQueue.CreateTimer();
            spring.Interval = TimeSpan.FromMilliseconds(16);
            spring.Tick += (_, _) => StepSpring();
            watchTimer = DispatcherQueue.CreateTimer();
            watchTimer.Interval = TimeSpan.FromMilliseconds(TaskWatchGate.CoalesceMilliseconds);
            watchTimer.Tick += (_, _) => Reload(playCues: true);
            life = DispatcherQueue.CreateTimer();
            life.Interval = TimeSpan.FromSeconds(PetRules.DecayInterval);
            life.IsRepeating = true;
            life.Tick += (_, _) => Reload(playCues: true);
            life.Start();
            dots = DispatcherQueue.CreateTimer();
            dots.Interval = TimeSpan.FromMilliseconds(320);
            dots.IsRepeating = true;
            dots.Tick += (_, _) => { dotPhase = (dotPhase + 1) % 4; SyncBubble(); };
            dots.Start();
            WatchTasks();
            LayoutWindow(null);
        }

        private void Pressed(object sender, PointerRoutedEventArgs e)
        {
            pointerDown = true;
            dragging = false;
            (dragX, dragY) = NativeWindow.Cursor();
            var bounds = AppWindow.Position;
            var size = AppWindow.Size;
            centerX = bounds.X + size.Width / 2;
            centerY = bounds.Y + size.Height / 2;
            Stage.CapturePointer(e.Pointer);
        }

        private void Moved(object sender, PointerRoutedEventArgs e)
        {
            if (!pointerDown) return;
            var (x, y) = NativeWindow.Cursor();
            var dx = x - dragX;
            var dy = y - dragY;
            var scale = Scale();
            if (!dragging && dx * dx + dy * dy >= 9 * scale * scale)
            {
                dragging = true;
                if (state.Expanded || animating)
                {
                    state.Expanded = false;
                    state.CloseBubble();
                    animating = false;
                    chromeKey = "";
                    LayoutWindow(null);
                }
            }
            if (!dragging) return;
            centerX += dx;
            centerY += dy;
            dragX = x;
            dragY = y;
            var kitten = KittenPixels();
            AppWindow.MoveAndResize(new RectInt32(centerX - kitten / 2, centerY - kitten / 2, kitten, kitten));
            NativeWindow.SetStadium(hwnd, kitten, kitten);
        }

        private void Released(object sender, PointerRoutedEventArgs e)
        {
            Stage.ReleasePointerCapture(e.Pointer);
            var wasDrag = dragging;
            pointerDown = false;
            dragging = false;
            if (wasDrag)
            {
                SaveCenter();
                LayoutWindow(null);
                return;
            }
            var point = e.GetCurrentPoint(Stage).Position;
            var hit = rows.LastOrDefault(row => point.Y >= row.Top && point.Y < row.Top + row.Height);
            if (!state.Expanded || hit == null)
            {
                var opening = !state.Expanded;
                state.Toggle();
                StartSpring(opening);
                return;
            }
            if (hit.Kind == "kitten") state.Play(DesktopState.Now());
            else if (hit.Kind == "agent" && hit.Id != null) state.Select(hit.Id);
            else if (hit.Kind == "plus") state.ShowPicker();
            else if (hit.Kind == "gear") OpenSettings();
            LayoutWindow(null);
        }

        private void StartSpring(bool opening)
        {
            var scale = ShellScale.Factor(state.Prefs.PetScale);
            animFrom = animating ? animHeight : (opening ? AppConfig.KittenSize * scale : TargetHeight());
            animTo = opening ? TargetHeight() : AppConfig.KittenSize * scale;
            animHeight = animFrom;
            animating = true;
            animStart = Environment.TickCount64;
            spring?.Start();
            LayoutWindow(animHeight);
        }

        private void StepSpring()
        {
            var t = Math.Clamp((Environment.TickCount64 - animStart) / 320.0, 0, 1);
            var eased = t * t * (3 - 2 * t);
            animHeight = animFrom + (animTo - animFrom) * eased;
            LayoutWindow(animHeight);
            if (t >= 1)
            {
                animating = false;
                spring?.Stop();
                LayoutWindow(null);
            }
        }

        private void LayoutWindow(double? animatedHeight)
        {
            if (hwnd == IntPtr.Zero) return;
            var work = WorkArea();
            var scale = Scale();
            var screen = new YupRect(work.X / scale, 0, work.Width / scale, work.Height / scale);
            var anchor = state.Prefs.KittenPlaced
                ? ShellGeometry.Anchor(state.Prefs.KittenX, state.Prefs.KittenY, screen)
                : state.Prefs.Dock == "left"
                    ? ShellGeometry.LeftAnchor(screen, screen, state.Prefs.PetScale)
                    : ShellGeometry.DefaultAnchor(screen, screen, state.Prefs.PetScale);
            var placed = ShellGeometry.PlaceCapsule(anchor, state.Expanded, state.Rows.Count, screen, state.Prefs.PetScale);
            upward = placed.Upward;
            var frame = placed.Frame;
            if (animatedHeight is double height)
            {
                frame = upward
                    ? new YupRect(frame.X, frame.Y, frame.Width, height)
                    : new YupRect(frame.X, frame.MaxY - height, frame.Width, height);
            }
            var topLeft = ShellGeometry.ToTopLeft(frame, screen.Height);
            var px = (int)Math.Round(topLeft.X * scale);
            var py = (int)Math.Round(work.Y + topLeft.Y * scale);
            var pw = Math.Max(1, (int)Math.Round(frame.Width * scale));
            var ph = Math.Max(1, (int)Math.Round(frame.Height * scale));
            if (!dragging) AppWindow.MoveAndResize(new RectInt32(px, py, pw, ph));
            NativeWindow.SetStadium(hwnd, pw, ph);
            var dipHeight = frame.Height;
            NativeWindow.SetHitTest(hwnd, (x, y) => CapsuleOutline.Contains(new YupPoint(x / scale, y / scale), frame.Width, dipHeight));
            SystemBackdrop = state.Expanded ? new DesktopAcrylicBackdrop() : null;
            Rebuild(frame.Width, dipHeight);
            PlaceBubble(screen, frame, scale, work);
        }

        private void Rebuild(double width, double height)
        {
            var key = string.Join("|", new[]
            {
                state.Expanded ? "1" : "0",
                upward ? "u" : "d",
                state.Prefs.Look,
                state.Prefs.PetScale.ToString("0.###"),
                state.SelectedId ?? "",
                string.Join(",", state.Rows.Select(row => row.Id + row.Status + (row.ShowsDot ? "1" : "0")))
            });
            if (key == chromeKey && !animating) return;
            var fresh = key != chromeKey;
            chromeKey = key;
            if (!fresh) return;
            Stage.Children.Clear();
            rows.Clear();
            var s = ShellScale.Factor(state.Prefs.PetScale);
            var rowHeight = AppConfig.AgentRow * s;
            var pad = AppConfig.CapsulePadY * s;
            if (!state.Expanded)
            {
                AddMark(0, height, width, "kitten", null, IslandPrefs.PetLetter(state.Prefs.Look), null);
                return;
            }
            if (!upward)
            {
                var y = pad;
                AddMark(y, rowHeight, width, "kitten", null, IslandPrefs.PetLetter(state.Prefs.Look), null);
                y += rowHeight + AppConfig.DividerGap * s;
                AddDivider(y, width, s);
                y += (AppConfig.DividerHeight + AppConfig.DividerGap) * s;
                foreach (var icon in state.Rows)
                {
                    AddMark(y, rowHeight, width, "agent", icon.Id, SlotFace.DisplayName(icon.Source), icon);
                    y += rowHeight + AppConfig.RowGap * s;
                }
                if (state.Rows.Count > 0) y -= AppConfig.RowGap * s;
                y += state.Rows.Count == 0 ? 0 : AppConfig.RowGap * s;
                AddMark(y, rowHeight, width, "plus", null, "+", null);
                y += rowHeight + AppConfig.RowGap * s;
                AddMark(y, rowHeight, width, "gear", null, "⚙", null);
                return;
            }
            var cursor = height - pad - rowHeight;
            AddMark(cursor, rowHeight, width, "kitten", null, IslandPrefs.PetLetter(state.Prefs.Look), null);
            cursor -= (AppConfig.DividerGap + AppConfig.DividerHeight) * s;
            AddDivider(cursor, width, s);
            cursor -= AppConfig.DividerGap * s + rowHeight;
            foreach (var icon in state.Rows)
            {
                AddMark(cursor, rowHeight, width, "agent", icon.Id, SlotFace.DisplayName(icon.Source), icon);
                cursor -= rowHeight + AppConfig.RowGap * s;
            }
            if (state.Rows.Count > 0) cursor += AppConfig.RowGap * s;
            cursor -= state.Rows.Count == 0 ? 0 : AppConfig.RowGap * s;
            AddMark(cursor, rowHeight, width, "plus", null, "+", null);
            cursor -= rowHeight + AppConfig.RowGap * s;
            AddMark(cursor, rowHeight, width, "gear", null, "⚙", null);
        }

        private void AddMark(double top, double height, double width, string kind, string? id, string title, AgentIconRow? icon)
        {
            var host = new Grid { Width = width, Height = height };
            Canvas.SetLeft(host, 0);
            Canvas.SetTop(host, top);
            var selected = kind == "agent" && id == state.SelectedId;
            if (selected)
            {
                var fill = new Border
                {
                    Width = AppConfig.AgentIcon * ShellScale.Factor(state.Prefs.PetScale) + 8,
                    Height = AppConfig.AgentIcon * ShellScale.Factor(state.Prefs.PetScale) + 8,
                    CornerRadius = new CornerRadius(AppConfig.SelectedRadius),
                    Background = selectionMoving
                        ? new SolidColorBrush(Color.FromArgb(96, 255, 255, 255))
                        : new SolidColorBrush(Color.FromArgb(42, 28, 28, 32)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                host.Children.Add(fill);
                if (Math.Abs(selectionTarget - top) > 0.5)
                {
                    selectionTarget = top;
                    selectionMoving = true;
                    var timer = DispatcherQueue.CreateTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(320);
                    timer.Tick += (_, _) =>
                    {
                        selectionMoving = false;
                        timer.Stop();
                        chromeKey = "";
                        LayoutWindow(animating ? animHeight : null);
                    };
                    timer.Start();
                }
            }
            var face = Face(kind, icon, title);
            face.HorizontalAlignment = HorizontalAlignment.Center;
            face.VerticalAlignment = VerticalAlignment.Center;
            host.Children.Add(face);
            if (icon?.ShowsDot == true)
            {
                var dot = new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(255, 220, 68, 55)),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 10, 0)
                };
                host.Children.Add(dot);
            }
            Stage.Children.Add(host);
            rows.Add(new ChromeRow(kind, id, top, height));
        }

        private UIElement Face(string kind, AgentIconRow? icon, string title)
        {
            if (icon != null)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Web", "pets", SlotFace.AgentMark(icon.Source) + ".png");
                if (File.Exists(path))
                {
                    var image = new Image
                    {
                        Source = new BitmapImage(new Uri(path)),
                        Width = AppConfig.AgentIcon,
                        Height = AppConfig.AgentIcon
                    };
                    return image;
                }
            }
            return new TextBlock
            {
                Text = kind == "kitten" ? title : title.Length > 0 ? title[..1] : "",
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = kind == "kitten" ? 22 : 16,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 36, 31, 28)),
                TextAlignment = TextAlignment.Center
            };
        }

        private void AddDivider(double top, double width, double scale)
        {
            var line = new Border
            {
                Width = AppConfig.DividerWidth * scale,
                Height = Math.Max(1, AppConfig.DividerHeight * scale),
                Background = new SolidColorBrush(Color.FromArgb(80, 44, 41, 38))
            };
            Canvas.SetLeft(line, (width - line.Width) / 2);
            Canvas.SetTop(line, top);
            Stage.Children.Add(line);
        }

        private BubbleWindow Bubble()
        {
            return bubble ??= new BubbleWindow();
        }

        private void PlaceBubble(YupRect screen, YupRect capsule, double scale, Windows.Graphics.RectInt32 work)
        {
            if (!state.Expanded || (state.SelectedId == null && !state.PickerOpen))
            {
                bubble?.Hide();
                return;
            }
            ChromeRow? anchor = state.PickerOpen
                ? rows.FirstOrDefault(row => row.Kind == "plus")
                : rows.FirstOrDefault(row => row.Id == state.SelectedId);
            anchor ??= rows.FirstOrDefault();
            if (anchor == null)
            {
                bubble?.Hide();
                return;
            }
            var icon = new YupRect(capsule.X, capsule.MaxY - anchor.Top - anchor.Height, capsule.Width, anchor.Height);
            var cardHeight = state.PickerOpen ? 72 : state.CardExpanded ? 280 : 168;
            var placement = BubblePlacer.Place(icon, capsule, new YupSize(220, cardHeight), screen);
            var beak = AppConfig.BeakWidth;
            var windowX = placement.PointsRight ? placement.Frame.X : placement.Frame.X - beak;
            var bodyX = placement.PointsRight ? 0 : beak;
            var frame = new YupRect(windowX, placement.Frame.Y, placement.Frame.Width + beak, placement.Frame.Height);
            var topLeft = ShellGeometry.ToTopLeft(frame, screen.Height);
            var task = state.SelectedTask;
            var answer = task?.Answer ?? "";
            if (task?.Status == "running" && answer.Length == 0) answer = new string('·', Math.Max(1, dotPhase));
            Bubble().Show(new BubbleModel(
                state.PickerOpen,
                state.Prefs.PickerSources(),
                task,
                answer,
                state.CardExpanded,
                placement.PointsRight,
                bodyX,
                Math.Max(0, placement.BeakY - AppConfig.BeakHeight / 2),
                Source =>
                {
                    AgentOpener.Open(Source);
                    state.CloseBubble();
                    LayoutWindow(null);
                },
                option =>
                {
                    state.Choose(option);
                    LayoutWindow(null);
                },
                () =>
                {
                    state.CardExpanded = !state.CardExpanded;
                    LayoutWindow(null);
                }), new RectInt32(
                    (int)Math.Round(topLeft.X * scale),
                    (int)Math.Round(work.Y + topLeft.Y * scale),
                    Math.Max(1, (int)Math.Round(frame.Width * scale)),
                    Math.Max(1, (int)Math.Round(frame.Height * scale))), scale);
        }

        private void SyncBubble()
        {
            if (state.SelectedTask?.Status == "running") LayoutWindow(animating ? animHeight : null);
        }

        private SettingsWindow? settings;

        private void OpenSettings()
        {
            if (settings == null)
            {
                settings = new SettingsWindow(state, () => LayoutWindow(animating ? animHeight : null));
                settings.Closed += (_, _) => settings = null;
            }
            settings.Activate();
        }

        private void Reload(bool playCues)
        {
            state.Refresh(DesktopState.Now());
            if (playCues)
            {
                foreach (var cue in state.Cues)
                {
                    var gate = alerts;
                    var play = gate.ShouldPlay(DesktopState.Now());
                    alerts = gate;
                    if (!play) break;
                    var name = cue switch
                    {
                        TaskCue.Submit => state.Prefs.SoundSubmit,
                        TaskCue.Complete => state.Prefs.SoundDone,
                        _ => state.Prefs.SoundStuck
                    };
                    TonePlayer.Play(name, true);
                }
            }
            chromeKey = "";
            LayoutWindow(animating ? animHeight : null);
        }

        private void WatchTasks()
        {
            var path = AppConfig.TasksFile;
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var gate = new TaskWatchGate();
            gate.Arm(TaskWatchGate.Stamp(path));
            watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            void Note()
            {
                if (!gate.Note(TaskWatchGate.Stamp(path))) return;
                DispatcherQueue.TryEnqueue(() =>
                {
                    watchTimer?.Stop();
                    watchTimer?.Start();
                });
            }
            watcher.Changed += (_, _) => Note();
            watcher.Created += (_, _) => Note();
            watcher.Renamed += (_, _) => Note();
            watcher.EnableRaisingEvents = true;
        }

        private void SaveCenter()
        {
            var work = WorkArea();
            var scale = Scale();
            var screen = new YupRect(work.X / scale, 0, work.Width / scale, work.Height / scale);
            var yUp = screen.Height - (centerY - work.Y) / scale;
            var ratios = ShellGeometry.Ratios(new YupPoint(centerX / scale, yUp), screen);
            state.SaveAnchor(ratios.X, ratios.Y);
        }

        private double TargetHeight()
        {
            return ShellGeometry.ContentHeight(state.Rows.Count, state.Prefs.PetScale);
        }

        private int KittenPixels()
        {
            return Math.Max(1, (int)Math.Round(AppConfig.KittenSize * ShellScale.Factor(state.Prefs.PetScale) * Scale()));
        }

        private double Scale()
        {
            return Stage.XamlRoot?.RasterizationScale ?? 1;
        }

        private RectInt32 WorkArea()
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            return area?.WorkArea ?? new RectInt32(0, 0, 1280, 800);
        }

        private void Shutdown()
        {
            watcher?.Dispose();
            NativeWindow.Release(hwnd);
            bubble?.Close();
        }

        private sealed record ChromeRow(string Kind, string? Id, double Top, double Height);
    }
}
