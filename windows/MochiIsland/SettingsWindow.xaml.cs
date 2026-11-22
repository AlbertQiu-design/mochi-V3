using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mochi.Core;
using Windows.Graphics;
using Windows.UI;

namespace MochiIsland
{
    public sealed partial class SettingsWindow : Window
    {
        private readonly DesktopState state;
        private readonly Action changed;
        private readonly Border highlight = new();
        private int tab;
        private string status = "";

        public SettingsWindow(DesktopState state, Action changed)
        {
            this.state = state;
            this.changed = changed;
            InitializeComponent();
            var presenter = OverlappedPresenter.Create();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            AppWindow.SetPresenter(presenter);
            SystemBackdrop = new MicaBackdrop();
            CloseButton.Click += (_, _) => Close();
            AppWindow.Resize(new SizeInt32(420, 560));
            NativeWindow.EnsureTopMost(this);
            BuildTabs();
            ShowPage();
            Closed += (_, _) => changed();
        }

        private void BuildTabs()
        {
            TabBar.Children.Clear();
            highlight.Background = new SolidColorBrush(Color.FromArgb(28, 36, 31, 28));
            highlight.CornerRadius = new CornerRadius(AppConfig.SettingsSelectRadius);
            highlight.Width = 70;
            highlight.Height = 28;
            highlight.HorizontalAlignment = HorizontalAlignment.Left;
            highlight.VerticalAlignment = VerticalAlignment.Center;
            TabBar.Children.Add(highlight);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            var names = new[]
            {
                T("General", "通用"),
                T("Pet", "宠物"),
                T("Sound", "声音"),
                T("Agents", "智能体")
            };
            for (var index = 0; index < names.Length; index++)
            {
                var captured = index;
                var button = new Button
                {
                    Content = names[index],
                    Width = 70,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                    BorderThickness = new Thickness(0)
                };
                button.Click += (_, _) =>
                {
                    tab = captured;
                    highlight.Margin = new Thickness(captured * 71, 0, 0, 0);
                    highlight.Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
                    var timer = DispatcherQueue.CreateTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(280);
                    timer.Tick += (_, _) =>
                    {
                        highlight.Background = new SolidColorBrush(Color.FromArgb(36, 36, 31, 28));
                        timer.Stop();
                    };
                    timer.Start();
                    ShowPage();
                };
                bar.Children.Add(button);
            }
            TabBar.Children.Add(bar);
        }

        private string T(string en, string zh) => L.T(state.Prefs, en, zh);

        private void ShowPage()
        {
            Pages.Children.Clear();
            Pages.Children.Add(tab switch
            {
                0 => General(),
                1 => Pet(),
                2 => Sound(),
                _ => Agents()
            });
        }

        private UIElement General()
        {
            var panel = new StackPanel { Spacing = 12 };
            var reset = new Button { Content = T("Reset kitten position", "重置小猫位置") };
            reset.Click += (_, _) =>
            {
                state.ResetPlace();
                changed();
            };
            var login = new ToggleSwitch { Header = T("Open at login", "开机自动启动"), IsOn = LoginStartup.IsEnabled() };
            login.Toggled += (_, _) => LoginStartup.Set(login.IsOn);
            var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString() ?? "0.1.0";
            panel.Children.Add(reset);
            panel.Children.Add(login);
            panel.Children.Add(new TextBlock { Text = T("Language", "语言"), Margin = new Thickness(0, 8, 0, 0) });
            var language = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var (id, label) in new[] { ("system", T("System", "跟随系统")), ("zh", "中文"), ("en", "English") })
            {
                var picked = id;
                var button = new Button { Content = label, Width = 96 };
                if (state.Prefs.Language == id) button.Background = new SolidColorBrush(Color.FromArgb(28, 36, 31, 28));
                button.Click += (_, _) =>
                {
                    state.SetLanguage(picked);
                    BuildTabs();
                    ShowPage();
                    changed();
                };
                language.Children.Add(button);
            }
            panel.Children.Add(language);
            panel.Children.Add(new TextBlock { Text = "Mochi Island " + version, Opacity = 0.7 });
            return panel;
        }

        private UIElement Pet()
        {
            var panel = new StackPanel { Spacing = 12 };
            var preview = new ContentControl
            {
                Content = PetPreview(state.Prefs.Look),
                HorizontalAlignment = HorizontalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            preview.Tapped += (_, _) => state.Play(DesktopState.Now());
            var size = new Slider { Minimum = 0.5, Maximum = 2, Value = ShellScale.Factor(state.Prefs.PetScale), StepFrequency = 0.05 };
            size.ValueChanged += (_, args) =>
            {
                state.SetScale(args.NewValue);
                changed();
            };
            var letters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var look in IslandPrefs.PetLooks)
            {
                var button = new Button { Content = look.ToUpperInvariant(), Width = 36 };
                var picked = look;
                button.Click += (_, _) =>
                {
                    state.SetLook(picked);
                    preview.Content = PetPreview(picked);
                    changed();
                };
                letters.Children.Add(button);
            }
            panel.Children.Add(preview);
            panel.Children.Add(new TextBlock { Text = T("Size", "大小") });
            panel.Children.Add(size);
            panel.Children.Add(letters);
            panel.Children.Add(new TextBlock { Text = T("A-F are six coat colours. The preview and the desktop kitten change together.", "A-F 是六种毛色，预览图和桌面上的小猫会一起变化。"), TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
            return panel;
        }

        private UIElement Sound()
        {
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = T("A muted computer stays quiet.", "电脑静音时不会发出声音。"), Opacity = 0.75 });
            AddSounds(panel, T("Click", "点击"), IslandPrefs.SoundIDs, state.Prefs.Sound, id => state.SetSound("click", id), false);
            AddSounds(panel, T("Submit", "提交"), IslandPrefs.SubmitSounds, state.Prefs.SoundSubmit, id => state.SetSound("submit", id), true);
            AddSounds(panel, T("Done", "完成"), IslandPrefs.DoneSounds, state.Prefs.SoundDone, id => state.SetSound("done", id), true);
            AddSounds(panel, T("Stuck", "卡住"), IslandPrefs.StuckSounds, state.Prefs.SoundStuck, id => state.SetSound("stuck", id), true);
            return new ScrollViewer { Content = panel };
        }

        private static FrameworkElement PetPreview(string look)
        {
            if (KittenArt.Artwork(look) != null) return KittenArt.Face(look, 96);
            return new TextBlock
            {
                Text = IslandPrefs.PetLetter(look),
                FontSize = 42,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        private static void AddSounds(Panel panel, string title, IReadOnlyList<string> ids, string selected, Action<string> pick, bool sustained)
        {
            panel.Children.Add(new TextBlock { Text = title, Margin = new Thickness(0, 8, 0, 0) });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var id in ids)
            {
                var button = new Button { Content = id == selected ? id + " ·" : id };
                var picked = id;
                button.Click += (_, _) =>
                {
                    pick(picked);
                    TonePlayer.Play(picked, sustained);
                };
                row.Children.Add(button);
            }
            panel.Children.Add(row);
        }

        private UIElement Agents()
        {
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = T(DesktopState.PrivacyLine, "Mochi 只会把钩子写进这台电脑上该 Agent 自己的配置里。对话留在这台电脑上。Shell 和 MCP 照常运行。需要选择时仍会显示在小猫旁边。"),
                TextWrapping = TextWrapping.Wrap
            });
            if (!File.Exists(DesktopState.HookPath))
            {
                panel.Children.Add(new TextBlock { Text = T("MochiHookCLI.exe is not beside the app yet.", "MochiHookCLI.exe 还不在程序旁边。"), TextWrapping = TextWrapping.Wrap });
            }
            foreach (var connector in AgentConnectorRegistry.Primary)
            {
                var current = connector;
                var row = new StackPanel { Spacing = 4 };
                var connected = false;
                try { connected = current.Status(DesktopState.HookPath).Connected; } catch { }
                row.Children.Add(new TextBlock { Text = current.DisplayName + (connected ? T(" · Connected", " · 已连接") : T(" · Not connected", " · 未连接")) });
                var button = new Button { Content = connected ? T("Disconnect", "断开") : T("Connect", "连接") };
                button.Click += async (_, _) =>
                {
                    if (connected)
                    {
                        var removed = state.Disconnect(current);
                        status = removed.Error ?? T("Disconnected", "已断开");
                        ShowPage();
                        return;
                    }
                    var probe = state.Connect(current, false);
                    if (probe.NeedsPrivacy)
                    {
                        var dialog = new ContentDialog
                        {
                            Title = T("Connect " + current.DisplayName + "?", "连接 " + current.DisplayName + "？"),
                            Content = new TextBlock
                            {
                                Text = T(DesktopState.PrivacyLine, "Mochi 只会把钩子写进这台电脑上该 Agent 自己的配置里。对话留在这台电脑上。Shell 和 MCP 照常运行。需要选择时仍会显示在小猫旁边。"),
                                TextWrapping = TextWrapping.Wrap
                            },
                            PrimaryButtonText = T("Connect", "连接"),
                            CloseButtonText = T("Cancel", "取消"),
                            XamlRoot = Root.XamlRoot
                        };
                        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                        probe = state.Connect(current, true);
                    }
                    status = probe.Error ?? (probe.Connected ? T("Connected", "已连接") : T("Not connected", "未连接"));
                    ShowPage();
                };
                row.Children.Add(button);
                panel.Children.Add(row);
            }
            if (status.Length > 0) panel.Children.Add(new TextBlock { Text = status, Opacity = 0.75 });
            var scan = new ToggleSwitch
            {
                Header = T("Notice recent local agent files", "留意本地 Agent 的近期文件"),
                IsOn = state.Prefs.ScanTranscripts
            };
            scan.Toggled += (_, _) => state.SetScan(scan.IsOn);
            panel.Children.Add(scan);
            panel.Children.Add(new TextBlock { Text = T("Off by default. It records names and times on this computer, not chat text.", "默认关闭。只在这台电脑上记录文件名和时间，不记录聊天内容。"), TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
            panel.Children.Add(new TextBlock { Text = T("Plus picker", "加号菜单") });
            foreach (var source in IslandPrefs.PickerOrder)
            {
                var toggle = new ToggleSwitch { Header = SlotFace.DisplayName(source), IsOn = state.Prefs.PickerSources().Contains(source) };
                var picked = source;
                toggle.Toggled += (_, _) => state.SetPicker(picked, toggle.IsOn);
                panel.Children.Add(toggle);
            }
            return new ScrollViewer { Content = panel };
        }
    }
}
