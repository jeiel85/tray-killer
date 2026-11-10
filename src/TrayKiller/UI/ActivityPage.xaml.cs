using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TrayKiller.Core;

namespace TrayKiller.UI
{
    public partial class ActivityPage : UserControl
    {
        readonly MainWindow host;
        readonly ListCollectionView view;
        string filter = "all";

        public ActivityPage(MainWindow host)
        {
            this.host = host;
            InitializeComponent();
            view = new ListCollectionView(host.VM.Activity);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActivityVM.DayText)));
            view.Filter = o => filter == "all" || ((ActivityVM)o).Group == filter;
            List.ItemsSource = view;
            host.VM.ActivityChanged += UpdateEmpty;
            UpdateEmpty();
        }

        void UpdateEmpty() => Empty.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        void Filter_Checked(object sender, RoutedEventArgs e)
        {
            filter = (string)((FrameworkElement)sender).Tag;
            if (view == null) return;
            view.Refresh();
            UpdateEmpty();
        }

        void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (!Dialog.Confirm(host, Loc.T("dlg.clear.title"), Loc.T("dlg.clear.body"), Loc.T("btn.clearLog"), Loc.T("btn.cancel"), true)) return;
            host.VM.Engine.ClearActivity();
            host.VM.ReloadActivity();
        }
    }
}
