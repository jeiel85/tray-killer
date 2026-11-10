using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TrayKiller.Core;

namespace TrayKiller.UI
{
    public partial class MainWindow : Window
    {
        readonly MainViewModel vm;
        readonly DashboardPage dashboard;
        TargetsPage targets;
        ActivityPage activity;
        SettingsPage settings;

        public MainWindow(MainViewModel vm)
        {
            this.vm = vm;
            InitializeComponent();
            DataContext = vm;
            dashboard = new DashboardPage(this);
            PageHost.Content = dashboard;
            SourceInitialized += (s, e) => WindowEffects.Apply(this);
            StateChanged += (s, e) =>
            {
                Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
                MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
            };
            IsVisibleChanged += (s, e) => vm.SetVisible(IsVisible);
        }

        public MainViewModel VM => vm;

        public void Navigate(string page)
        {
            switch ((page ?? "").ToLowerInvariant())
            {
                case "targets": NavTargets.IsChecked = true; break;
                case "activity": NavActivity.IsChecked = true; break;
                case "settings": NavSettings.IsChecked = true; break;
                default: NavDashboard.IsChecked = true; break;
            }
        }

        void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PageHost == null) return;
            switch ((string)((FrameworkElement)sender).Tag)
            {
                case "targets": PageHost.Content = targets ?? (targets = new TargetsPage(this)); break;
                case "activity": PageHost.Content = activity ?? (activity = new ActivityPage(this)); break;
                case "settings": PageHost.Content = settings ?? (settings = new SettingsPage(this)); break;
                default: PageHost.Content = dashboard; break;
            }
        }

        public void OpenAddTarget()
        {
            var dlg = new AddTargetWindow(vm.Engine) { Owner = this };
            if (dlg.ShowDialog() == true) Navigate("targets");
        }

        void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            // Closing the window only hides it; Tray Killer keeps guarding from the tray.
            if (!((App)Application.Current).IsExiting)
            {
                e.Cancel = true;
                Hide();
                ((App)Application.Current).OnMainHidden();
            }
            base.OnClosing(e);
        }

        /// <summary>Renders the window content to a PNG (used for docs/screenshots).</summary>
        public void Snapshot(string path, double scale)
        {
            Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var size = new Size(Root.ActualWidth, Root.ActualHeight);
            var rtb = new RenderTargetBitmap((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(Root);
            WindowEffects.SavePng(rtb, path);
        }
    }

    static class WindowEffects
    {
        public static void Apply(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                int dark = 1;
                Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));          // DWMWA_USE_IMMERSIVE_DARK_MODE
                int round = 2;
                Native.DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));         // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND
                int border = 0x00342A22;                                                // #222A34 as COLORREF (BGR)
                Native.DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));        // DWMWA_BORDER_COLOR
            }
            catch { /* older Windows: cosmetic only */ }
        }

        public static void SavePng(BitmapSource bmp, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);
        }
    }
}
