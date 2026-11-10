using System.Windows;
using System.Windows.Controls;
using TrayKiller.Core;

namespace TrayKiller.UI
{
    public partial class TargetsPage : UserControl
    {
        readonly MainWindow host;

        public TargetsPage(MainWindow host)
        {
            this.host = host;
            InitializeComponent();
        }

        void Add_Click(object sender, RoutedEventArgs e) => host.OpenAddTarget();

        void Elevate_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).RestartElevated();

        void RunNow_Click(object sender, RoutedEventArgs e)
        {
            var rule = (RuleVM)((FrameworkElement)sender).Tag;
            host.VM.Engine.AddActivity(ActivityKind.Info, Loc.F("act.runNow", rule.Name), rule.Pattern, rule.Rule.Id);
            host.VM.Engine.RunRuleNow(rule.Rule);
        }

        void Delete_Click(object sender, RoutedEventArgs e)
        {
            var rule = (RuleVM)((FrameworkElement)sender).Tag;
            var count = host.VM.Engine.Blocked.ForRule(rule.Rule.Id).Count;
            var msg = count > 0 ? Loc.F("dlg.delete.bodyRestore", rule.Name, count) : Loc.F("dlg.delete.body", rule.Name);
            if (!Dialog.Confirm(host, Loc.T("dlg.delete.title"), msg, Loc.T("btn.delete"), Loc.T("btn.cancel"), true)) return;
            host.VM.Engine.RemoveRule(rule.Rule, true);
            host.VM.Refresh();
        }

        void Restore_Click(object sender, RoutedEventArgs e)
        {
            var item = (BlockedVM)((FrameworkElement)sender).Tag;
            if (!Dialog.Confirm(host, Loc.T("dlg.restore.title"), Loc.F("dlg.restore.body", item.Title), Loc.T("btn.restore"), Loc.T("btn.cancel"), false)) return;
            string error;
            if (!host.VM.Engine.RestoreItem(item.Item, out error))
                Dialog.Info(host, Loc.T("dlg.restoreFailed.title"), error);
            host.VM.Refresh();
        }
    }
}
