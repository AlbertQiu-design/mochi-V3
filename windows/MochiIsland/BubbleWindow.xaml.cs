using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Mochi.Core;
using Windows.Graphics;
using Windows.UI;

namespace MochiIsland
{
    public sealed record BubbleModel(
        bool Picker,
        IReadOnlyList<string> Sources,
        CursorTask? Task,
        string Answer,
        bool Expanded,
        bool PointsRight,
        double BodyX,
        double BeakTop,
        Action<string> Open,
        Action<string> Choose,
        Action ToggleExpand);

    public sealed partial class BubbleWindow : Window
    {
        private IntPtr hwnd;
        private bool shown;
        private string contentKey = "";

        public BubbleWindow()
        {
            InitializeComponent();
            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            AppWindow.SetPresenter(presenter);
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }

        public void Show(BubbleModel model, RectInt32 pixels, double scale)
        {
            if (hwnd == IntPtr.Zero) hwnd = NativeWindow.Prepare(this);
            AppWindow.MoveAndResize(pixels);
            var bodyX = (int)Math.Round(model.BodyX * scale);
            var bodyWidth = Math.Max(1, pixels.Width - (int)Math.Round(AppConfig.BeakWidth * scale));
            var beakX = model.PointsRight ? bodyWidth : 0;
            var beakY = (int)Math.Round(model.BeakTop * scale);
            var beakW = Math.Max(1, (int)Math.Round(AppConfig.BeakWidth * scale));
            var beakH = Math.Max(1, (int)Math.Round(AppConfig.BeakHeight * scale));
            Canvas.SetLeft(Body, model.BodyX);
            Canvas.SetTop(Body, 0);
            Body.Width = pixels.Width / scale - AppConfig.BeakWidth;
            Body.Height = pixels.Height / scale;
            BeakHost.Children.Clear();
            var tip = new Border
            {
                Width = AppConfig.BeakWidth,
                Height = AppConfig.BeakHeight,
                Background = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
                CornerRadius = new CornerRadius(3)
            };
            Canvas.SetLeft(tip, model.PointsRight ? Body.Width : 0);
            Canvas.SetTop(tip, model.BeakTop);
            BeakHost.Children.Add(tip);
            NativeWindow.SetBubble(hwnd, bodyX, 0, bodyWidth, pixels.Height, (int)Math.Round(AppConfig.BubbleRadius * scale), beakX, beakY, beakW, beakH);
            var key = model.Picker
                ? "picker:" + string.Join(",", model.Sources)
                : (model.Task?.Id ?? "") + "|" + (model.Task?.Status ?? "") + "|" + model.Answer + "|" + model.Expanded;
            if (key != contentKey)
            {
                contentKey = key;
                Fill(model);
            }
            if (!shown) Activate();
            shown = true;
        }

        public void Hide()
        {
            if (!shown) return;
            shown = false;
            contentKey = "";
            AppWindow.Hide();
        }

        private void Fill(BubbleModel model)
        {
            Card.Children.Clear();
            Scroller.VerticalScrollBarVisibility = model.Expanded
                ? Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto
                : Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled;
            if (model.Picker)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (var source in model.Sources)
                {
                    var button = new Button { Width = 36, Height = 36, Padding = new Thickness(4), Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
                    var path = Path.Combine(AppContext.BaseDirectory, "Web", "pets", SlotFace.AgentMark(source) + ".png");
                    button.Content = File.Exists(path)
                        ? new Image { Source = new BitmapImage(new Uri(path)), Width = 28, Height = 28 }
                        : new TextBlock { Text = SlotFace.DisplayName(source)[..1], FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center };
                    var picked = source;
                    button.Click += (_, _) => model.Open(picked);
                    row.Children.Add(button);
                }
                Card.Children.Add(row);
                return;
            }
            var task = model.Task;
            if (task == null) return;
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = SlotFace.DisplayName(task.Source), FontSize = 13 };
            var status = new TextBlock
            {
                Text = PermissionCopy.StatusLine(task.Status) + "  " + BubbleAge.Relative(task.Updated, DesktopState.Now()),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(status, 1);
            head.Children.Add(name);
            head.Children.Add(status);
            Card.Children.Add(head);
            Card.Children.Add(Line("Q：", PermissionCopy.CardBlurb(task.Summary, task.Detail, task.Kind, task.Status, task.Options.Select(option => option["id"]).ToList(), task.Title), model.Expanded));
            Card.Children.Add(Line("A：", model.Answer, model.Expanded));
            var expand = new Button { Content = "展开", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            expand.Click += (_, _) => model.ToggleExpand();
            Card.Children.Add(expand);
            if (task.Status == "wait" && task.Options.Count > 0)
            {
                var question = new TextBlock
                {
                    Text = PermissionCopy.ChoiceQuestion(task.Kind, task.Detail, task.ChoicePrompt, task.Status, task.Options.Select(option => option["id"]).ToList()),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 4)
                };
                Card.Children.Add(question);
                foreach (var option in task.Options)
                {
                    var button = new Button { Content = option["label"], HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2, 0, 0) };
                    var id = option["id"];
                    button.Click += (_, _) => model.Choose(id);
                    Card.Children.Add(button);
                }
            }
            var open = new Button { Content = SlotFace.DisplayName(task.Source), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            open.Click += (_, _) => model.Open(task.Source);
            Card.Children.Add(open);
        }

        private static StackPanel Line(string label, string text, bool expanded)
        {
            var block = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = expanded ? 0 : 2,
                FontSize = 13
            };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            panel.Children.Add(new TextBlock { Text = label, FontSize = 13 });
            panel.Children.Add(block);
            return panel;
        }
    }
}
