using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TrayKiller.Core;

namespace TrayKiller.UI
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    static class Res
    {
        public static Brush B(string key) => (Brush)Application.Current.Resources[key];
    }

    public sealed class ActivityVM
    {
        public ActivityVM(ActivityEntry e)
        {
            Entry = e;
            var t = e.Time.ToLocalTime();
            TimeText = t.Date == DateTime.Today ? t.ToString("HH:mm:ss") : t.ToString("M/d HH:mm");
            DayText = Loc.Day(t);
            switch (e.Kind)
            {
                case ActivityKind.Kill: Glyph = ""; Brush = Res.B("B.Accent"); Soft = Res.B("B.AccentSoft"); Group = "kill"; break;
                case ActivityKind.Block: Glyph = ""; Brush = Res.B("B.Accent2"); Soft = Res.B("B.AmberSoft"); Group = "block"; break;
                case ActivityKind.Hide: Glyph = ""; Brush = Res.B("B.Purple"); Soft = Res.B("B.PurpleSoft"); Group = "block"; break;
                case ActivityKind.Restore: Glyph = ""; Brush = Res.B("B.Blue"); Soft = Res.B("B.BlueSoft"); Group = "block"; break;
                case ActivityKind.Update: Glyph = ""; Brush = Res.B("B.Purple"); Soft = Res.B("B.PurpleSoft"); Group = "system"; break;
                case ActivityKind.Warn: Glyph = ""; Brush = Res.B("B.Amber"); Soft = Res.B("B.AmberSoft"); Group = "system"; break;
                case ActivityKind.Start: Glyph = ""; Brush = Res.B("B.Green"); Soft = Res.B("B.GreenSoft"); Group = "system"; break;
                default: Glyph = ""; Brush = Res.B("B.Muted"); Soft = Res.B("B.MutedSoft"); Group = "system"; break;
            }
        }

        public ActivityEntry Entry { get; }
        public string Title => Entry.Title;
        public string Detail => Entry.Detail;
        public string TimeText { get; }
        public string DayText { get; }
        public string Glyph { get; }
        public Brush Brush { get; }
        public Brush Soft { get; }
        public string Group { get; }
    }

    public sealed class StatVM : Observable
    {
        string value, caption;
        public string Glyph { get; set; }
        public string Label { get; set; }
        public Brush Brush { get; set; }
        public Brush Soft { get; set; }
        public string Value { get => value; set => Set(ref this.value, value); }
        public string Caption { get => caption; set => Set(ref caption, value); }
    }

    public sealed class DayBarVM
    {
        public string Label { get; set; }
        public int Count { get; set; }
        public double BarHeight { get; set; }
        public bool IsToday { get; set; }
        public Brush Fill => IsToday ? Res.B("B.AccentGrad") : Res.B("B.Surface3");
    }

    public sealed class RuleVM : Observable
    {
        readonly Engine engine;
        public TargetRule Rule { get; }

        public RuleVM(Engine engine, TargetRule rule)
        {
            this.engine = engine;
            Rule = rule;
            Icon = IconCache.ForExe(rule.Path);
        }

        public string Name => Rule.Name;
        public string Pattern => Rule.Pattern;
        public string PathText => string.IsNullOrEmpty(Rule.Path) ? Loc.T("rule.anyPath") : Rule.Path;
        public ImageSource Icon { get; }
        public bool HasIcon => Icon != null;
        public string KillCountText => Loc.F("rule.kills", Rule.KillCount.ToString("N0"));
        public string LastKilledText => Rule.LastKilled.HasValue ? Loc.F("rule.lastKill", Loc.Ago(Rule.LastKilled.Value)) : Loc.T("rule.noKillYet");
        public int BlockedCount => engine.Blocked.ForRule(Rule.Id).Count;
        public string BlockedText => Loc.F("rule.blockedItems", BlockedCount);

        public bool Enabled { get => Rule.Enabled; set { Rule.Enabled = value; Changed(true); } }
        public bool KillProcess { get => Rule.KillProcess; set { Rule.KillProcess = value; Changed(true); } }
        public bool BlockAutostart { get => Rule.BlockAutostart; set { Rule.BlockAutostart = value; Changed(true); } }
        public bool HideTrayIcon { get => Rule.HideTrayIcon; set { Rule.HideTrayIcon = value; Changed(true); } }
        public bool DisableServices { get => Rule.DisableServices; set { Rule.DisableServices = value; Changed(true); } }

        void Changed(bool rescan)
        {
            engine.RuleChanged(Rule, rescan);
            RaiseAll();
        }

        public void Refresh() => RaiseAll();
    }

    public sealed class BlockedVM
    {
        public BlockedVM(BlockedItem item, string ruleName)
        {
            Item = item;
            RuleName = ruleName;
            switch (item.Kind)
            {
                case BlockKind.Registry: Glyph = ""; KindText = Loc.T("kind.registry"); break;
                case BlockKind.StartupFile: Glyph = ""; KindText = Loc.T("kind.startupFolder"); break;
                case BlockKind.Task: Glyph = ""; KindText = Loc.T("kind.task"); break;
                case BlockKind.Service: Glyph = ""; KindText = Loc.T("kind.service"); break;
                default: Glyph = ""; KindText = Loc.T("kind.trayIcon"); break;
            }
        }

        public BlockedItem Item { get; }
        public string Glyph { get; }
        public string KindText { get; }
        public string RuleName { get; }
        public string Title => Item.Kind == BlockKind.TrayIcon ? System.IO.Path.GetFileName(Item.Detail ?? Item.Name) : Item.Name;
        public string Detail => Item.Kind == BlockKind.TrayIcon ? Item.Detail : Item.Location + "  ·  " + Item.Detail;
        public string TimeText => Loc.Ago(Item.BlockedAt.ToLocalTime());
    }

    /// <summary>Everything the main window shows, kept in sync with the engine.</summary>
    public sealed class MainViewModel : Observable
    {
        readonly Engine engine;
        readonly Dispatcher ui;
        readonly DispatcherTimer tick;
        bool refreshQueued;

        public MainViewModel(Engine engine)
        {
            this.engine = engine;
            ui = Dispatcher.CurrentDispatcher;
            StatCards = new ObservableCollection<StatVM>
            {
                new StatVM { Glyph = "", Label = Loc.T("stat.today"), Brush = Res.B("B.Accent"), Soft = Res.B("B.AccentSoft") },
                new StatVM { Glyph = "", Label = Loc.T("stat.total"), Brush = Res.B("B.Purple"), Soft = Res.B("B.PurpleSoft") },
                new StatVM { Glyph = "", Label = Loc.T("stat.blocked"), Brush = Res.B("B.Accent2"), Soft = Res.B("B.AmberSoft") },
                new StatVM { Glyph = "", Label = Loc.T("stat.next"), Brush = Res.B("B.Green"), Soft = Res.B("B.GreenSoft") },
            };
            foreach (var e in engine.Activity.Snapshot()) Activity.Add(new ActivityVM(e));
            engine.ActivityAdded += e => ui.BeginInvoke(new Action(() => OnActivity(e)));
            engine.StateChanged += QueueRefresh;
            tick = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(1) };
            tick.Tick += (s, e) => Tick();
            RebuildRules();
            Refresh();
        }

        public ObservableCollection<StatVM> StatCards { get; }
        public ObservableCollection<DayBarVM> WeekBars { get; } = new ObservableCollection<DayBarVM>();
        public ObservableCollection<ActivityVM> Activity { get; } = new ObservableCollection<ActivityVM>();
        public ObservableCollection<ActivityVM> RecentActivity { get; } = new ObservableCollection<ActivityVM>();
        public ObservableCollection<RuleVM> Rules { get; } = new ObservableCollection<RuleVM>();
        public ObservableCollection<BlockedVM> BlockedItems { get; } = new ObservableCollection<BlockedVM>();

        public Engine Engine => engine;
        AppSettings S => engine.Settings;

        bool visible;

        public void SetVisible(bool isVisible)
        {
            visible = isVisible;
            if (visible) { Refresh(); tick.Start(); }
            else tick.Stop();
        }

        // ── status ──────────────────────────────────────────────────────────────────
        public bool IsPaused => S.Paused;
        public bool IsElevated => AppInfo.IsElevated;
        public bool IsLimited => !AppInfo.IsElevated;
        /// <summary>Only nag about admin rights when it actually got in the way.</summary>
        public bool ShowElevateBanner => !AppInfo.IsElevated && engine.NeedsElevation;
        public bool HasRules => Rules.Count > 0;
        public bool NoRules => Rules.Count == 0;
        public string VersionText => "v" + AppInfo.VersionText;
        public string RuleCountText => Rules.Count.ToString();
        public string WeekTotalText => Loc.F("dash.weekTotal", WeekBars.Sum(b => b.Count));
        public string BlockedCountText => BlockedItems.Count.ToString();

        public string StatusLabel => S.Paused ? Loc.T("status.paused") : Loc.T("status.protecting");
        public Brush StatusBrush => S.Paused ? Res.B("B.Amber") : Res.B("B.Green");
        public Brush StatusSoft => S.Paused ? Res.B("B.AmberSoft") : Res.B("B.GreenSoft");

        public string HeroTitle
        {
            get
            {
                if (S.Paused) return Loc.T("hero.paused");
                int n = engine.ActiveRuleCount;
                return n == 0 ? Loc.T("hero.noRules") : Loc.F("hero.watching", n);
            }
        }

        public string HeroSubtitle
        {
            get
            {
                var parts = new List<string>();
                parts.Add(S.RealtimeWatch ? Loc.T("hero.realtimeOn") : Loc.T("hero.realtimeOff"));
                parts.Add(Loc.F("hero.every", Loc.Interval(S.SweepSeconds)));
                parts.Add(AppInfo.IsElevated ? Loc.T("hero.admin") : Loc.T("hero.limited"));
                return string.Join("  ·  ", parts);
            }
        }

        public string PauseText => S.Paused ? Loc.T("btn.resume") : Loc.T("btn.pause");
        public string PauseGlyph => S.Paused ? "" : "";
        public string Greeting => Loc.Greeting();

        // ── settings proxies ────────────────────────────────────────────────────────
        public bool RealtimeWatch { get => S.RealtimeWatch; set { S.RealtimeWatch = value; SettingsChanged(); } }
        public int SweepSeconds { get => S.SweepSeconds; set { S.SweepSeconds = value; SettingsChanged(); } }
        public bool CleanTrayIcons { get => S.CleanTrayIcons; set { S.CleanTrayIcons = value; SettingsChanged(); } }
        public bool NotifyOnKill { get => S.NotifyOnKill; set { S.NotifyOnKill = value; SettingsChanged(); } }
        public bool AutoUpdate { get => S.AutoUpdate; set { S.AutoUpdate = value; SettingsChanged(); } }
        public string Language
        {
            get => S.Language;
            set { S.Language = value; engine.SaveSettings(); Raise(); Raise(nameof(LanguageNote)); }
        }
        public string LanguageNote => Loc.T("settings.languageNote");

        bool startWithWindows = true;
        public bool StartWithWindows
        {
            get => startWithWindows;
            set
            {
                string error = null;
                // Test profiles (--profile x) never touch the real startup registration.
                bool ok = !AppPaths.IsDefaultProfile
                    || StartupManager.Apply(AppInfo.ExePath, value, out error);
                if (ok)
                {
                    S.StartWithWindows = value;
                    engine.SaveSettings();
                    startWithWindows = value;
                    engine.AddActivity(ActivityKind.Info, value ? Loc.T("act.startupOn") : Loc.T("act.startupOff"), null);
                }
                else
                {
                    engine.AddActivity(ActivityKind.Warn, Loc.T("act.startupFailed"), error);
                }
                Raise();
                Raise(nameof(StartupNote));
            }
        }

        public void LoadStartupState() => ui.BeginInvoke(new Action(() =>
        {
            startWithWindows = AppPaths.IsDefaultProfile ? StartupManager.IsEnabled(S.StartWithWindows) : S.StartWithWindows;
            Raise(nameof(StartWithWindows));
            Raise(nameof(StartupNote));
        }), DispatcherPriority.Background);

        public string StartupNote => AppInfo.IsElevated || !AppPaths.IsDefaultProfile || StartupManager.TaskExists()
            ? Loc.T("settings.startup.task") : Loc.T("settings.startup.run");

        string updateStatus;
        bool updateBusy;
        public string UpdateStatus { get => updateStatus ?? Loc.T("update.idle"); set => Set(ref updateStatus, value); }
        public bool UpdateBusy { get => updateBusy; set { Set(ref updateBusy, value); Raise(nameof(UpdateIdle)); } }
        public bool UpdateIdle => !updateBusy;

        void SettingsChanged()
        {
            engine.SaveSettings();
            engine.ApplySettings();
            RaiseAll();
        }

        // ── refresh ─────────────────────────────────────────────────────────────────
        void QueueRefresh()
        {
            if (refreshQueued || !visible) return;
            refreshQueued = true;
            ui.BeginInvoke(new Action(() => { refreshQueued = false; Refresh(); }), DispatcherPriority.Background);
        }

        void OnActivity(ActivityEntry e)
        {
            Activity.Insert(0, new ActivityVM(e));
            while (Activity.Count > 500) Activity.RemoveAt(Activity.Count - 1);
            ActivityChanged?.Invoke();
            QueueRefresh();
        }

        public event Action ActivityChanged;

        public void ReloadActivity()
        {
            Activity.Clear();
            foreach (var e in engine.Activity.Snapshot()) Activity.Add(new ActivityVM(e));
            ActivityChanged?.Invoke();
            Refresh();
        }

        public void RebuildRules()
        {
            Rules.Clear();
            foreach (var r in engine.Rules) Rules.Add(new RuleVM(engine, r));
        }

        public void Refresh()
        {
            // rules: rebuild only when the set changed
            var ids = engine.Rules.Select(r => r.Id).ToList();
            if (!ids.SequenceEqual(Rules.Select(r => r.Rule.Id))) RebuildRules();
            else foreach (var r in Rules) r.Refresh();

            RecentActivity.Clear();
            foreach (var a in Activity.Take(5)) RecentActivity.Add(a);

            var names = engine.Rules.ToDictionary(r => r.Id, r => r.Name);
            BlockedItems.Clear();
            foreach (var b in engine.Blocked.Snapshot().OrderByDescending(b => b.BlockedAt))
            {
                string name;
                names.TryGetValue(b.RuleId ?? "", out name);
                BlockedItems.Add(new BlockedVM(b, name));
            }

            WeekBars.Clear();
            var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i - 6)).ToList();
            var counts = days.Select(d => engine.Stats.KillsOn(d)).ToList();
            int max = Math.Max(1, counts.Max());
            for (int i = 0; i < 7; i++)
                WeekBars.Add(new DayBarVM
                {
                    Label = Loc.ShortDay(days[i]),
                    Count = counts[i],
                    BarHeight = counts[i] == 0 ? 4 : 8 + 112.0 * counts[i] / max,
                    IsToday = i == 6
                });

            StatCards[0].Value = engine.Stats.KillsOn(DateTime.Today).ToString("N0");
            StatCards[0].Caption = Loc.T("stat.today.caption");
            StatCards[1].Value = engine.Stats.TotalKills.ToString("N0");
            StatCards[1].Caption = Loc.T("stat.total.caption");
            StatCards[2].Value = engine.Blocked.Count.ToString("N0");
            StatCards[2].Caption = Loc.T("stat.blocked.caption");
            Tick();
            RaiseAll();
        }

        void Tick()
        {
            var card = StatCards[3];
            if (S.Paused) { card.Value = "—"; card.Caption = Loc.T("stat.next.paused"); return; }
            var left = Math.Max(0, (int)Math.Ceiling((engine.NextSweep - DateTime.Now).TotalSeconds));
            card.Value = Loc.Seconds(left);
            card.Caption = engine.WatcherRunning ? Loc.T("stat.next.realtime") : Loc.T("stat.next.sweepOnly");
        }
    }
}
