using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TrayKiller.Core;

namespace TrayKiller.UI
{
    public partial class AddTargetWindow : Window
    {
        readonly Engine engine;
        string mode = "tray";
        List<AppCandidate> trayApps, runningApps;

        public AddTargetWindow(Engine engine)
        {
            this.engine = engine;
            InitializeComponent();
            SourceInitialized += (s, e) => WindowEffects.Apply(this);
            Loaded += async (s, e) => await LoadAsync();
        }

        async Task LoadAsync()
        {
            ListStatus.Text = Loc.T("add.loading");
            ListStatus.Visibility = Visibility.Visible;
            try
            {
                if (mode == "tray" && trayApps == null) trayApps = await Task.Run(() => AppCatalog.TrayApps());
                if (mode == "running" && runningApps == null) runningApps = await Task.Run(() => AppCatalog.RunningApps());
            }
            catch (Exception ex)
            {
                Log.Error("load candidates", ex);
            }
            ApplyFilter();
        }

        void ApplyFilter()
        {
            var source = mode == "tray" ? trayApps : runningApps;
            if (source == null) return;
            var q = (Search.Text ?? "").Trim();
            var items = q.Length == 0
                ? source
                : source.Where(c => (c.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                                 || (c.Path ?? c.FileName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            List.ItemsSource = items;
            ListStatus.Text = Loc.T(source.Count == 0 ? "add.emptySource" : "add.noMatch");
            ListStatus.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateAddEnabled();
        }

        async void Tab_Checked(object sender, RoutedEventArgs e)
        {
            mode = (string)((FrameworkElement)sender).Tag;
            if (ListMode == null) return;
            ErrorText.Text = "";
            bool manual = mode == "manual";
            ListMode.Visibility = manual ? Visibility.Collapsed : Visibility.Visible;
            ManualMode.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
            if (manual) { ManualPattern.Focus(); UpdateAddEnabled(); }
            else
            {
                List.ItemsSource = null;
                await LoadAsync();
            }
        }

        void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
        void Manual_TextChanged(object sender, TextChangedEventArgs e) => UpdateAddEnabled();
        void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAddEnabled();

        void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (List.SelectedItem != null) Add_Click(sender, e);
        }

        void UpdateAddEnabled()
        {
            if (AddButton == null) return;
            AddButton.IsEnabled = mode == "manual" ? !string.IsNullOrWhiteSpace(ManualPattern.Text) : List.SelectedItem != null;
        }

        void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = Loc.T("add.browseFilter") + " (*.exe)|*.exe", Title = Loc.T("add.browseTitle") };
            if (dlg.ShowDialog(this) != true) return;
            ManualPattern.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(ManualName.Text))
                ManualName.Text = AppCatalog.Describe(dlg.FileName, System.IO.Path.GetFileName(dlg.FileName));
        }

        void Add_Click(object sender, RoutedEventArgs e)
        {
            TargetRule rule;
            if (mode == "manual")
            {
                var raw = ManualPattern.Text.Trim().Trim('"');
                rule = new TargetRule
                {
                    Name = ManualName.Text.Trim(),
                    Pattern = raw,
                    Path = raw.Contains("\\") && System.IO.File.Exists(raw) ? raw : null
                };
            }
            else
            {
                var c = List.SelectedItem as AppCandidate;
                if (c == null) return;
                rule = new TargetRule { Name = c.Name, Pattern = c.FileName, Path = c.Path };
            }
            rule.BlockAutostart = OptAutostart.IsChecked == true;
            rule.HideTrayIcon = OptHide.IsChecked == true;
            rule.DisableServices = OptServices.IsChecked == true;

            var error = engine.AddRule(rule);
            if (error != null)
            {
                ErrorText.Text = error;
                return;
            }
            if (IsModal()) DialogResult = true;
            else Close();
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (IsModal()) DialogResult = false;
            else Close();
        }

        bool IsModal() => Owner != null && !Owner.IsEnabled;

        /// <summary>Pre-selects a tab and search text (used for scripted screenshots).</summary>
        public void Preset(string tab, string search)
        {
            if (tab == "running") TabRunning.IsChecked = true;
            else if (tab == "manual") TabManual.IsChecked = true;
            else TabTray.IsChecked = true;
            if (!string.IsNullOrEmpty(search)) Search.Text = search;
        }
    }
}
