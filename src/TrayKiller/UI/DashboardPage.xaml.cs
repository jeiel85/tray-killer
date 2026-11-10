using System.Windows;
using System.Windows.Controls;

namespace TrayKiller.UI
{
    public partial class DashboardPage : UserControl
    {
        readonly MainWindow host;

        public DashboardPage(MainWindow host)
        {
            this.host = host;
            InitializeComponent();
        }

        void Pause_Click(object sender, RoutedEventArgs e) => host.VM.Engine.SetPaused(!host.VM.Engine.Settings.Paused);

        void Sweep_Click(object sender, RoutedEventArgs e)
        {
            host.VM.Engine.QueueSweep(true);
            host.VM.Engine.AddActivity(Core.ActivityKind.Info, Core.Loc.T("act.manualSweep"), null);
        }

        void Add_Click(object sender, RoutedEventArgs e) => host.OpenAddTarget();

        void ViewAll_Click(object sender, RoutedEventArgs e) => host.Navigate("activity");
    }
}
