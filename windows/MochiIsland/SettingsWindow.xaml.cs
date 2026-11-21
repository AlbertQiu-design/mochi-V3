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
            BuildTabs();
            ShowPage();
            Closed += (_, _) => changed();
        }

        private void BuildTabs()
        {
            highlight.Background = new SolidColorBrush(Color.FromArgb(28, 36, 31, 28));
            highlight.CornerRadius = new CornerRadius(AppConfig.SettingsSelectRadius);
            highlight.Width = 90;
            highlight.Height = 28;
            highlight.HorizontalAlignment = HorizontalAlignment.Left;
            highlight.VerticalAlignment = VerticalAlignment.Center;
            TabBar.Children.Add(highlight);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var names = new[] { "General", "Pet", "Sound", "Agents" };
            for (var index = 0; index < names.Length; index++)
            {
                var captured = index;
                var button = new Button
                {
                    Content = names[index],
                    Width = 90,
                    Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                    BorderThickness = new Thickness(0)
                };
                button.Click += (_, _) =>
                {
                    tab = captured;
                    highlight.Margin = new Thickness(captured * 94, 0, 0, 0);
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
            var reset = new Button { Content = "Reset kitten position" };
            reset.Click += (_, _) =>
            {
                state.ResetPlace();
                changed();
            };
            var login = new ToggleSwitch { Header = "Open at login", IsOn = LoginStartup.IsEnabled() };
            login.Toggled += (_, _) => LoginStartup.Set(login.IsOn);
            var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString() ?? "0.1.0";
            panel.Children.Add(reset);
            panel.Children.Add(login);
            panel.Children.Add(new TextBlock { Text = "Mochi Island " + version, Opacity = 0.7 });
            return panel;
        }

        private UIElement Pet()
        {
            var panel = new StackPanel { Spacing = 12 };
            var preview = new TextBlock
            {
                Text = IslandPrefs.PetLetter(state.Prefs.Look),
                FontSize = 42,
                HorizontalAlignment = HorizontalAlignment.Center
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
                    preview.Text = IslandPrefs.PetLetter(picked);
                    changed();
                };
                letters.Children.Add(button);
            }
            panel.Children.Add(preview);
            panel.Children.Add(new TextBlock { Text = "Size" });
            panel.Children.Add(size);
            panel.Children.Add(letters);
            panel.Children.Add(new TextBlock { Text = "Letters stand in for more pets. The Size preview and desktop pet change together.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
            return panel;
        }

        private UIElement Sound()
        {
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "A muted computer stays quiet.", Opacity = 0.75 });
            AddSounds(panel, "Click", IslandPrefs.SoundIDs, state.Prefs.Sound, id => state.SetSound("click", id));
            AddSounds(panel, "Submit", IslandPrefs.SubmitSounds, state.Prefs.SoundSubmit, id => state.SetSound("submit", id));
            AddSounds(panel, "Done", IslandPrefs.DoneSounds, state.Prefs.SoundDone, id => state.SetSound("done", id));
            AddSounds(panel, "Stuck", IslandPrefs.StuckSounds, state.Prefs.SoundStuck, id => state.SetSound("stuck", id));
            return new ScrollViewer { Content = panel };
        }

        private static void AddSounds(Panel panel, string title, IReadOnlyList<string> ids, string selected, Action<string> pick)
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
                    TonePlayer.Play(picked, title != "Click");
                };
                row.Children.Add(button);
            }
            panel.Children.Add(row);
        }

        private UIElement Agents()
        {
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = DesktopState.PrivacyLine, TextWrapping = TextWrapping.Wrap });
            if (!File.Exists(DesktopState.HookPath))
            {
                panel.Children.Add(new TextBlock { Text = "MochiHookCLI.exe is not beside the app yet.", TextWrapping = TextWrapping.Wrap });
            }
            foreach (var connector in AgentConnectorRegistry.Primary)
            {
                var current = connector;
                var row = new StackPanel { Spacing = 4 };
                var connected = false;
                try { connected = current.Status(DesktopState.HookPath).Connected; } catch { }
                row.Children.Add(new TextBlock { Text = current.DisplayName + (connected ? " · Connected" : " · Not connected") });
                var button = new Button { Content = connected ? "Disconnect" : "Connect" };
                button.Click += async (_, _) =>
                {
                    if (connected)
                    {
                        var removed = state.Disconnect(current);
                        status = removed.Error ?? "Disconnected";
                        ShowPage();
                        return;
                    }
                    var probe = state.Connect(current, false);
                    if (probe.NeedsPrivacy)
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "Connect Cursor?",
                            Content = new TextBlock { Text = DesktopState.PrivacyLine, TextWrapping = TextWrapping.Wrap },
                            PrimaryButtonText = "Connect",
                            CloseButtonText = "Cancel",
                            XamlRoot = Root.XamlRoot
                        };
                        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                        probe = state.Connect(current, true);
                    }
                    status = probe.Error ?? (probe.Connected ? "Connected" : "Not connected");
                    ShowPage();
                };
                row.Children.Add(button);
                panel.Children.Add(row);
            }
            if (status.Length > 0) panel.Children.Add(new TextBlock { Text = status, Opacity = 0.75 });
            var scan = new ToggleSwitch
            {
                Header = "Notice recent local agent files",
                IsOn = state.Prefs.ScanTranscripts
            };
            scan.Toggled += (_, _) => state.SetScan(scan.IsOn);
            panel.Children.Add(scan);
            panel.Children.Add(new TextBlock { Text = "Off by default. It records names and times on this computer, not chat text.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
            panel.Children.Add(new TextBlock { Text = "Plus picker" });
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
