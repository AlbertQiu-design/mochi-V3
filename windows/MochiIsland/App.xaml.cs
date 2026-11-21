using Microsoft.UI.Xaml;

namespace MochiIsland
{
    public partial class App : Application
    {
        private ShellWindow? shell;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            shell = new ShellWindow();
            shell.Activate();
        }
    }
}
