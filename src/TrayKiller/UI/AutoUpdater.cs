using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;
using TrayKiller.Core;

namespace TrayKiller.UI
{
    /// <summary>Checks GitHub Releases every few hours and installs new versions by itself.</summary>
    public sealed class AutoUpdater
    {
        static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);
        readonly App app;
        readonly Engine engine;
        readonly DispatcherTimer timer;
        bool busy;

        public UpdateInfo Available { get; private set; }

        /// <summary>Only the installed copy of the default profile replaces itself.</summary>
        /// <summary>Only the elevated, installed copy replaces itself (the install folder is admin-only).</summary>
        public static bool CanSelfUpdate => AppPaths.IsDefaultProfile && AppInfo.IsInstalledCopy && AppInfo.IsElevated;

        public AutoUpdater(App app, Engine engine)
        {
            this.app = app;
            this.engine = engine;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            timer.Tick += async (s, e) =>
            {
                timer.Interval = TimeSpan.FromMinutes(30);
                var last = engine.Settings.LastUpdateCheck?.ToLocalTime();
                if (engine.Settings.AutoUpdate && (last == null || DateTime.Now - last.Value > CheckEvery))
                    await CheckNow(false);
            };
        }

        public void Start()
        {
            if (AppPaths.IsDefaultProfile) timer.Start();
            SetStatus(null);
        }

        void SetStatus(string text)
        {
            if (app.ViewModel == null) return;
            if (text == null)
            {
                var last = engine.Settings.LastUpdateCheck?.ToLocalTime();
                text = last == null ? Loc.T("update.idle") : Loc.F("update.upToDate", Loc.Ago(last.Value));
                if (Available != null) text = Loc.F("update.available", Available.Version);
            }
            app.ViewModel.UpdateStatus = text;
        }

        public async Task CheckNow(bool userInitiated)
        {
            if (busy) return;
            busy = true;
            if (app.ViewModel != null) app.ViewModel.UpdateBusy = true;
            SetStatus(Loc.T("update.checking"));
            try
            {
                var info = await Task.Run(() => Updater.CheckAsync());
                engine.Settings.LastUpdateCheck = DateTime.Now;
                engine.SaveSettings();
                Available = info;
                app.Tray?.Refresh();
                if (info == null)
                {
                    SetStatus(null);
                    return;
                }
                Log.Info("Update available: " + info.Version);
                bool windowOpen = app.MainWin != null && app.MainWin.IsVisible;
                if (CanSelfUpdate && (userInitiated || (engine.Settings.AutoUpdate && !windowOpen)))
                {
                    await Install(info);
                }
                else
                {
                    SetStatus(Loc.F("update.available", info.Version));
                    if (userInitiated && !CanSelfUpdate)
                    {
                        if (Dialog.Confirm(app.MainWin, Loc.T("dlg.update.title"), Loc.F("dlg.update.portable", info.Version), Loc.T("btn.download"), Loc.T("btn.later"), false))
                            ShellOpen.Unelevated(info.PageUrl ?? AppInfo.ReleasesUrl);
                    }
                    else if (!userInitiated)
                        app.Tray?.Balloon(Loc.T("balloon.update.title"), Loc.F("update.available", info.Version));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Update check failed: " + ex.Message);
                SetStatus(Loc.F("update.failed", ex.GetBaseException().Message));
            }
            finally
            {
                busy = false;
                if (app.ViewModel != null) app.ViewModel.UpdateBusy = false;
            }
        }

        async Task Install(UpdateInfo info)
        {
            SetStatus(Loc.F("update.downloading", info.Version));
            var file = await Task.Run(() => Updater.DownloadAsync(info));
            engine.AddActivity(ActivityKind.Update, Loc.F("act.updating", info.Version), "v" + AppInfo.VersionText + " → v" + info.Version);
            engine.SaveAll();
            Updater.ApplyAndRestart(file, app.RelaunchArgs);
            app.ExitApp(forUpdate: true);
        }
    }
}
