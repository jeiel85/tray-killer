using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TrayKiller.Core;

namespace TrayKiller.UI
{
    public partial class SettingsPage : UserControl
    {
        readonly MainWindow host;

        public SettingsPage(MainWindow host)
        {
            this.host = host;
            InitializeComponent();
            host.VM.LoadStartupState();
        }

        App App => (App)Application.Current;

        void Elevate_Click(object sender, RoutedEventArgs e) => App.RestartElevated();

        async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await App.Updates.CheckNow(true);

        void GitHub_Click(object sender, RoutedEventArgs e) => Open(AppInfo.RepoUrl);

        void Folder_Click(object sender, RoutedEventArgs e) => Open(AppPaths.DataDir);

        void Uninstall_Click(object sender, RoutedEventArgs e) => App.BeginUninstall();

        static void Open(string target) => ShellOpen.Unelevated(target);
    }
}
