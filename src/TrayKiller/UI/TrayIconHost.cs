using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrayKiller.Core;
using Forms = System.Windows.Forms;

namespace TrayKiller.UI
{
    /// <summary>The notification-area icon plus its (WPF-styled) context menu.</summary>
    public sealed class TrayIconHost : IDisposable
    {
        readonly App app;
        readonly Engine engine;
        readonly Forms.NotifyIcon icon;
        readonly System.Drawing.Icon normalIcon, pausedIcon;
        readonly ContextMenu menu;
        TextBlock headerStatus;
        MenuItem pauseItem, updateItem;

        public TrayIconHost(App app, Engine engine)
        {
            this.app = app;
            this.engine = engine;
            normalIcon = LoadIcon("TrayKiller.ico");
            pausedIcon = LoadIcon("TrayKiller-paused.ico");
            icon = new Forms.NotifyIcon { Icon = normalIcon, Text = AppInfo.Name, Visible = true };
            icon.MouseUp += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) app.ToggleMain();
                else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
            };
            icon.BalloonTipClicked += (s, e) => app.ShowMain();
            menu = BuildMenu();
            engine.StateChanged += () => app.Dispatcher.BeginInvoke(new Action(Refresh));
            Refresh();
        }

        static System.Drawing.Icon LoadIcon(string name)
        {
            var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + name));
            using (sri.Stream) return new System.Drawing.Icon(sri.Stream, Forms.SystemInformation.SmallIconSize);
        }

        ContextMenu BuildMenu()
        {
            var m = new ContextMenu { Style = (Style)Application.Current.FindResource("TrayMenu") };
            var itemStyle = (Style)Application.Current.FindResource("TrayMenuItem");
            var sepStyle = (Style)Application.Current.FindResource("TrayMenuSep");

            // header
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 10) };
            header.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo-64.png")),
                Width = 30, Height = 30
            });
            var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = AppInfo.Name, FontWeight = FontWeights.Bold, FontSize = 14 });
            headerStatus = new TextBlock { FontSize = 12, Foreground = (Brush)Application.Current.FindResource("B.Muted"), Margin = new Thickness(0, 2, 0, 0) };
            texts.Children.Add(headerStatus);
            header.Children.Add(texts);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            m.Items.Add(new MenuItem
            {
                Header = header, Focusable = false, IsHitTestVisible = false,
                Template = new ControlTemplate(typeof(MenuItem)) { VisualTree = presenter }
            });
            m.Items.Add(new Separator { Style = sepStyle });

            MenuItem Item(string glyph, string text, Action click)
            {
                var mi = new MenuItem { Header = text, Tag = glyph, Style = itemStyle };
                mi.Click += (s, e) => click();
                m.Items.Add(mi);
                return mi;
            }

            Item("", Loc.T("tray.open"), () => app.ShowMain());
            Item("", Loc.T("tray.add"), () => { app.ShowMain(); app.MainWin.OpenAddTarget(); });
            Item("", Loc.T("btn.sweep"), () =>
            {
                engine.QueueSweep(true);
                engine.AddActivity(ActivityKind.Info, Loc.T("act.manualSweep"), null);
            });
            pauseItem = Item("", Loc.T("btn.pause"), () => engine.SetPaused(!engine.Settings.Paused));
            m.Items.Add(new Separator { Style = sepStyle });
            updateItem = Item("", Loc.T("tray.update"), async () => await app.Updates.CheckNow(true));
            Item("", Loc.T("tray.exit"), () => app.ExitApp());
            return m;
        }

        public void Refresh()
        {
            bool paused = engine.Settings.Paused;
            icon.Icon = paused ? pausedIcon : normalIcon;
            var status = paused
                ? Loc.T("status.paused")
                : Loc.F("tray.status", engine.ActiveRuleCount, engine.Stats.KillsOn(DateTime.Today));
            var tip = AppInfo.Name + " — " + status;
            icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            headerStatus.Text = status;
            pauseItem.Header = paused ? Loc.T("btn.resume") : Loc.T("btn.pause");
            pauseItem.Tag = paused ? "" : "";
            var upd = app.Updates?.Available;
            updateItem.Header = upd != null ? Loc.F("tray.updateTo", upd.Version) : Loc.T("tray.update");
        }

        public void ShowMenu(Point? at = null)
        {
            Refresh();
            Point p;
            if (at.HasValue) p = at.Value;
            else
            {
                Native.POINT cursor;
                Native.GetCursorPos(out cursor);
                double scale;
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0;
                p = new Point(cursor.X / scale, cursor.Y / scale);
            }
            menu.Placement = PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = p.X;
            menu.VerticalOffset = p.Y;
            menu.IsOpen = true;
            // Without this the menu would not close when clicking elsewhere.
            var src = PresentationSource.FromVisual(menu) as HwndSource;
            if (src != null) Native.SetForegroundWindow(src.Handle);
        }

        public ContextMenu Menu => menu;

        public void Balloon(string title, string text)
        {
            try { icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.None); } catch { }
        }

        /// <summary>Screen rectangle of our icon (physical pixels), if Explorer can tell.</summary>
        internal Native.RECT? IconRect()
        {
            try
            {
                var t = typeof(Forms.NotifyIcon);
                var window = (Forms.NativeWindow)t.GetField("window", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(icon);
                var id = (int)t.GetField("id", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(icon);
                var nid = new Native.NOTIFYICONIDENTIFIER
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.NOTIFYICONIDENTIFIER)),
                    hWnd = window.Handle, uID = id
                };
                Native.RECT r;
                return Native.Shell_NotifyIconGetRect(ref nid, out r) == 0 ? r : (Native.RECT?)null;
            }
            catch { return null; }
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
        }
    }
}
