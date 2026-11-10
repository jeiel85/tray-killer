using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TrayKiller.Core;
using TrayKiller.UI;

namespace TrayKiller
{
    public partial class App : Application
    {
        public Engine Engine { get; private set; }
        public TrayIconHost Tray { get; private set; }
        public MainWindow MainWin { get; private set; }
        public MainViewModel ViewModel { get; private set; }
        public AutoUpdater Updates { get; private set; }
        public bool IsExiting { get; private set; }

        Args args;
        AppSettings settings;

        string ProfileArg => AppPaths.IsDefaultProfile ? "" : " --profile " + AppPaths.Profile;

        /// <summary>Switches a restarted copy should inherit (after an update).</summary>
        public string RelaunchArgs => (args.NoElevate ? " --no-elevate" : "") + ProfileArg;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            args = Args.Parse(e.Args);
            try { AppPaths.Init(args.Profile); }
            catch (Exception ex)
            {
                // Elevated but the admin-only data folder can't be secured: refuse to run rather than trust user-writable data.
                Log.Error("Data folder setup failed", ex);
                MessageBox.Show("Tray Killer could not secure its data folder and will exit.\n\n" + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
            HookCrashHandlers();
            Log.Info("Tray Killer v" + AppInfo.VersionText + " starting (elevated=" + AppInfo.IsElevated + ", exe=" + AppInfo.ExePath + ", args=" + string.Join(" ", e.Args) + ")");
            Log.Prune(14);
            Installer.ImportLegacyData();
            settings = Json.Load<AppSettings>(AppPaths.SettingsFile);
            Loc.Init(settings.Language);

            if (args.Uninstall)
            {
                RunUninstall();
                return;
            }

            if (!InstanceChannel.TryAcquire(args.Updated || args.Wait ? 20000 : 0))
            {
                InstanceChannel.Send(args.Command ?? (args.Background ? "noop" : "show"));
                Shutdown();
                return;
            }

            bool managed = AppPaths.IsDefaultProfile && !args.NoElevate;
            if (managed && !AppInfo.IsElevated)
            {
                if (StartupManager.TaskExists())
                {
                    // The logon task runs us elevated without a UAC prompt.
                    InstanceChannel.Release();
                    if (!args.Background) InstanceChannel.QueueCommand(args.Command ?? "show");
                    if (StartupManager.RunTask()) { Shutdown(); return; }
                    InstanceChannel.TryAcquire(3000);
                }
                else if (!settings.SetupDone || !AppInfo.IsInstalledCopy)
                {
                    // First run: one UAC prompt to install and register the elevated logon task.
                    InstanceChannel.Release();
                    if (Elevation.Relaunch("--install --wait")) { Shutdown(); return; }
                    if (!InstanceChannel.TryAcquire(3000)) { Shutdown(); return; }
                }
            }

            if (managed && AppInfo.IsElevated && (args.Install || !AppInfo.IsInstalledCopy || !settings.SetupDone))
            {
                try
                {
                    bool moving = !AppInfo.IsInstalledCopy;
                    var installed = Installer.Install();
                    settings.SetupDone = true;
                    if (moving) settings.SelfPromoted = false; // new exe path = new tray icon entry
                    Json.Save(AppPaths.SettingsFile, settings);
                    string error;
                    if (!StartupManager.Apply(installed, settings.StartWithWindows, out error)) Log.Warn("Startup registration failed: " + error);
                    if (moving)
                    {
                        // Hand over to the protected copy (keeps "background"/"updated" so an auto-update stays quiet).
                        var next = (args.Background ? "--background" : "--welcome") + " --wait" + (args.Updated ? " --updated" : "");
                        InstanceChannel.Release();
                        Process.Start(new ProcessStartInfo(installed, next) { UseShellExecute = false, WorkingDirectory = AppPaths.InstallDir });
                        Shutdown();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Install failed — running in place", ex);
                    // Never leave an elevated logon task pointing at an exe a normal user could replace.
                    if (!AppInfo.IsInstalledCopy) StartupManager.Remove();
                }
            }

            Updater.CleanupOld();

            Engine = new Engine(settings);
            ViewModel = new MainViewModel(Engine);
            Updates = new AutoUpdater(this, Engine);
            Tray = new TrayIconHost(this, Engine);
            Engine.Killed += OnKilled;
            // A failure in one subsystem must not leave the app half-started (no window, no command channel).
            try { Engine.Start(); } catch (Exception ex) { Log.Error("engine start", ex); }
            Updates.Start();
            SessionEnding += (s, ev) => ExitApp();
            InstanceChannel.Listen(cmd => Dispatcher.BeginInvoke(new Action(() => HandleCommand(cmd))));

            var pending = InstanceChannel.ConsumePending();
            if (!args.Background || args.Welcome) ShowMain();
            if (pending != null) HandleCommand(pending);
            if (args.Command != null) HandleCommand(args.Command);
            if (args.Updated)
            {
                Engine.AddActivity(ActivityKind.Update, Loc.F("act.updated", AppInfo.VersionText), null);
                Tray.Balloon(Loc.T("balloon.updated.title"), Loc.F("balloon.updated.body", AppInfo.VersionText));
            }

            Later(5000, PromoteSelf);
            // The logon task starts us at "above normal" priority to win the race; settle down afterwards.
            Later(60000, () => { try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal; } catch { } });
            if (managed && AppInfo.IsElevated && AppInfo.IsInstalledCopy)
            {
                // Keep the logon task pointing at this protected copy, and remove the v1.0.x user-writable install.
                if (!args.Install) Task.Run(() => { string err; StartupManager.Apply(AppInfo.ExePath, settings.StartWithWindows, out err); });
                Later(15000, Installer.CleanupLegacyInstall);
            }
        }

        void HookCrashHandlers()
        {
            DispatcherUnhandledException += (s, e) =>
            {
                Log.Error("UI exception", e.Exception);
                e.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Fatal exception", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Error("Task exception", e.Exception);
                e.SetObserved();
            };
        }

        void Later(int ms, Action action)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                try { action(); } catch (Exception ex) { Log.Error("deferred action", ex); }
            };
            t.Start();
        }

        // ── Window ────────────────────────────────────────────────────────────────────
        public void ShowMain()
        {
            if (MainWin == null) MainWin = new MainWindow(ViewModel);
            if (!MainWin.IsVisible) MainWin.Show();
            if (MainWin.WindowState == WindowState.Minimized) MainWin.WindowState = WindowState.Normal;
            MainWin.Activate();
            MainWin.Topmost = true;
            MainWin.Topmost = false;
            MainWin.Focus();
        }

        public void ToggleMain() => ShowMain();

        public void OnMainHidden()
        {
            if (Engine.Settings.TrayHintShown) return;
            Engine.Settings.TrayHintShown = true;
            Engine.SaveSettings();
            Tray.Balloon(Loc.T("balloon.hidden.title"), Loc.T("balloon.hidden.body"));
        }

        void OnKilled(TargetRule rule, string exe)
        {
            if (!Engine.Settings.NotifyOnKill) return;
            Dispatcher.BeginInvoke(new Action(() => Tray.Balloon(Loc.F("balloon.killed.title", rule.Name), exe)));
        }

        // ── Commands from other instances / command line ────────────────────────────────
        void HandleCommand(string cmd)
        {
            try
            {
                var parts = cmd.Split(new[] { '|' }, 2);
                var verb = parts[0].Trim().ToLowerInvariant();
                var arg = parts.Length > 1 ? parts[1] : null;
                switch (verb)
                {
                    case "noop": break;
                    case "show": ShowMain(); break;
                    case "hide": MainWin?.Hide(); break;
                    case "menu": Tray.ShowMenu(); break;
                    case "sweep": Engine.QueueSweep(true); break;
                    case "pause": Engine.SetPaused(true); break;
                    case "resume": Engine.SetPaused(false); break;
                    case "exit": ExitApp(); break;
                    case "add": ShowMain(); MainWin.OpenAddTarget(); break;
                    case "snap": Snap(arg); break;
                    case "update": _ = Updates.CheckNow(true); break;
                    default:
                        if (verb.StartsWith("page:"))
                        {
                            ShowMain();
                            MainWin.Navigate(verb.Substring(5));
                        }
                        break;
                }
            }
            catch (Exception ex) { Log.Error("command " + cmd, ex); }
        }

        /// <summary>Renders a piece of the live UI to PNG: "main|file.png|2", "menu|file.png|2", "add|file.png|2".</summary>
        void Snap(string arg)
        {
            var p = (arg ?? "main").Split('|');
            var target = p[0].Trim().ToLowerInvariant();
            // Commands can come from any of the user's processes: only a bare file name is accepted and the
            // image always lands in our own data folder (never a caller-chosen path, which an elevated
            // instance would otherwise write anywhere).
            var name = p.Length > 1 ? Path.GetFileName(p[1]) : "";
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = "snap-" + target + ".png";
            Directory.CreateDirectory(AppPaths.SnapDir);
            var path = Path.Combine(AppPaths.SnapDir, name);
            var scale = p.Length > 2 ? double.Parse(p[2], CultureInfo.InvariantCulture) : 2.0;
            switch (target)
            {
                case "menu":
                    Tray.ShowMenu(new Point(400, 200));
                    Later(900, () =>
                    {
                        RenderToPng(Tray.Menu, path, scale);
                        Tray.Menu.IsOpen = false;
                    });
                    break;
                case "add":
                    ShowMain();
                    var dlg = new AddTargetWindow(Engine) { Owner = MainWin };
                    dlg.Show();
                    dlg.Preset(p.Length > 3 ? p[3] : "tray", p.Length > 4 ? p[4] : null);
                    Later(4000, () =>
                    {
                        if (p.Length > 5 && int.TryParse(p[5], out var sel)) ((System.Windows.Controls.ListBox)dlg.FindName("List")).SelectedIndex = sel;
                        dlg.UpdateLayout();
                        Later(300, () =>
                        {
                            RenderToPng((FrameworkElement)dlg.Content, path, scale);
                            dlg.Close();
                        });
                    });
                    break;
                default:
                    ShowMain();
                    Later(900, () => MainWin.Snapshot(path, scale));
                    break;
            }
        }

        static void RenderToPng(FrameworkElement el, string path, double scale)
        {
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth * scale), (int)Math.Ceiling(el.ActualHeight * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(el);
            UI.WindowEffects.SavePng(rtb, path);
        }

        // ── Elevation / uninstall / exit ─────────────────────────────────────────────
        public void RestartElevated()
        {
            if (Elevation.Relaunch("--install --wait --cmd show" + ProfileArg)) ExitApp();
        }

        public void BeginUninstall()
        {
            if (!Dialog.Confirm(MainWin, Loc.T("dlg.uninstall.title"), Loc.T("dlg.uninstall.body"), Loc.T("btn.uninstall"), Loc.T("btn.cancel"), true)) return;
            var a = "--uninstall --quiet" + ProfileArg;
            bool started;
            try
            {
                Process.Start(new ProcessStartInfo(AppInfo.ExePath, a) { UseShellExecute = false });
                started = true;
            }
            catch (Exception ex)
            {
                Log.Error("start uninstaller", ex);
                started = false;
            }
            if (started) ExitApp();
        }

        void RunUninstall()
        {
            try
            {
                // Program Files, the machine-wide Apps entry and the protected data need admin rights to remove.
                if (!AppInfo.IsElevated && !args.NoElevate && AppPaths.IsDefaultProfile)
                {
                    if (Elevation.Relaunch("--uninstall" + (args.Quiet ? " --quiet" : "") + ProfileArg)) { Shutdown(); return; }
                }
                if (!args.Quiet && !Dialog.Confirm(null, Loc.T("dlg.uninstall.title"), Loc.T("dlg.uninstall.body"), Loc.T("btn.uninstall"), Loc.T("btn.cancel"), true))
                {
                    Shutdown();
                    return;
                }
                InstanceChannel.Send("exit");
                InstanceChannel.TryAcquire(15000);
                int restored = Engine.RestoreEverything(new BlockedStore());
                if (AppPaths.IsDefaultProfile)
                {
                    StartupManager.Remove();
                    Installer.RemoveRegistration();
                    Installer.CleanupLegacyInstall();
                }
                InstanceChannel.Release();
                Log.Info("Uninstalled; restored " + restored + " item(s)");
                Dialog.Info(null, Loc.T("dlg.uninstalled.title"), Loc.F("dlg.uninstalled.body", restored));
                Installer.ScheduleFolderRemoval();
            }
            catch (Exception ex) { Log.Error("uninstall", ex); }
            Shutdown();
        }

        public void ExitApp(bool forUpdate = false)
        {
            if (IsExiting) return;
            IsExiting = true;
            Log.Info("Exiting" + (forUpdate ? " for update" : ""));
            try { MainWin?.Close(); } catch { }
            try { Tray?.Dispose(); } catch { }
            try { Engine?.Dispose(); } catch { }
            InstanceChannel.Release();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (!IsExiting)
            {
                IsExiting = true;
                try { Tray?.Dispose(); } catch { }
                try { Engine?.Dispose(); } catch { }
                InstanceChannel.Release();
            }
            base.OnExit(e);
        }

        // ── Keep our own icon visible on Windows 11 (only if the user never decided) ───
        void PromoteSelf()
        {
            if (Engine.Settings.SelfPromoted) return;
            using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", false))
            {
                if (root == null) return;
                foreach (var id in root.GetSubKeyNames())
                {
                    try
                    {
                        using (var k = root.OpenSubKey(id, true))
                        {
                            var path = Native.ResolveShellPath(k?.GetValue("ExecutablePath") as string);
                            if (path == null || !string.Equals(Path.GetFullPath(path), AppInfo.ExePath, StringComparison.OrdinalIgnoreCase)) continue;
                            if (k.GetValue("IsPromoted") == null) k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                            Engine.Settings.SelfPromoted = true;
                            Engine.SaveSettings();
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
