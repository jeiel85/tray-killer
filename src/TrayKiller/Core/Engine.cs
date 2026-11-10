using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace TrayKiller.Core
{
    /// <summary>
    /// The heart of Tray Killer: real-time watcher + periodic sweeps that kill target processes,
    /// clean their tray icons and keep their auto-start entries switched off.
    /// </summary>
    public sealed class Engine : IDisposable
    {
        public AppSettings Settings { get; }
        public Stats Stats { get; }
        public ActivityStore Activity { get; } = new ActivityStore();
        public BlockedStore Blocked { get; } = new BlockedStore();

        readonly AutostartBlocker blocker;
        readonly ProcessWatcher watcher = new ProcessWatcher();
        readonly object ruleGate = new object();
        readonly object statsGate = new object();
        readonly object sweepGate = new object();
        readonly Dictionary<string, DateTime> problems = new Dictionary<string, DateTime>();
        volatile List<CompiledRule> compiled = new List<CompiledRule>();
        Timer sweepTimer, deepTimer, saveTimer;
        bool disposed;

        public DateTime? LastSweep { get; private set; }
        public DateTime NextSweep { get; private set; } = DateTime.Now;
        public bool WatcherRunning => watcher.IsRunning;
        public bool WatcherTraceMode => watcher.IsTraceMode;

        /// <summary>Rules, stats, pause state or blocked items changed.</summary>
        public event Action StateChanged;
        public event Action<ActivityEntry> ActivityAdded;
        /// <summary>(rule, exe name) after a successful kill.</summary>
        public event Action<TargetRule, string> Killed;

        public Engine(AppSettings settings)
        {
            Settings = settings;
            Stats = Json.Load<Stats>(AppPaths.StatsFile);
            blocker = new AutostartBlocker(Blocked);
            blocker.Blocked += OnBlocked;
            blocker.Problem += (key, msg) => Problem(key, Loc.T("act.problem"), msg);
            watcher.ProcessStarted += OnProcessStarted;
            Recompile();
        }

        public void Start()
        {
            ApplySettings();
            deepTimer = new Timer(_ => SafeSweep(true), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            AddActivity(ActivityKind.Start, Loc.T("act.started"),
                AppInfo.IsElevated ? Loc.T("act.started.admin") : Loc.T("act.started.limited"));
            QueueSweep(true);
        }

        // ── Settings & rules ──────────────────────────────────────────────────────────────
        public List<TargetRule> Rules
        {
            get { lock (ruleGate) return Settings.Rules.ToList(); }
        }

        public int ActiveRuleCount => compiled.Count;

        void Recompile()
        {
            lock (ruleGate) compiled = Matcher.Compile(Settings.Rules);
        }

        public void ApplySettings()
        {
            var interval = TimeSpan.FromSeconds(Math.Max(3, Math.Min(3600, Settings.SweepSeconds)));
            sweepTimer?.Dispose();
            sweepTimer = new Timer(_ => SafeSweep(false), null, interval, interval);
            NextSweep = DateTime.Now + interval;
            ApplyWatcher();
            RaiseChanged();
        }

        void ApplyWatcher()
        {
            // The watcher is an optimisation on top of sweeps — it must never take the app down.
            try
            {
                if (Settings.RealtimeWatch && !Settings.Paused && compiled.Count > 0) watcher.Start(AppInfo.IsElevated);
                else watcher.Stop();
            }
            catch (Exception ex) { Log.Error("process watcher", ex); }
        }

        /// <summary>Adds a rule. Returns an error message, or null on success.</summary>
        public string AddRule(TargetRule rule)
        {
            rule.Pattern = Matcher.Normalize(rule.Pattern);
            if (rule.Pattern.Length == 0) return Loc.T("err.emptyPattern");
            var hit = Matcher.ProtectedHit(rule.Pattern);
            if (hit != null) return Loc.F("err.protected", hit);
            if (string.IsNullOrWhiteSpace(rule.Name)) rule.Name = Path.GetFileNameWithoutExtension(rule.Pattern.Replace("*", ""));
            lock (ruleGate)
            {
                if (Settings.Rules.Any(r => string.Equals(r.Pattern, rule.Pattern, StringComparison.OrdinalIgnoreCase)))
                    return Loc.T("err.duplicate");
                Settings.Rules.Add(rule);
            }
            Recompile();
            SaveSettings();
            AddActivity(ActivityKind.Info, Loc.F("act.ruleAdded", rule.Name), rule.Pattern, rule.Id);
            ApplyWatcher();
            QueueSweep(true);
            RaiseChanged();
            return null;
        }

        public void RemoveRule(TargetRule rule, bool restore)
        {
            if (restore)
            {
                foreach (var item in Blocked.ForRule(rule.Id))
                {
                    string err;
                    if (blocker.Restore(item, out err))
                        AddActivity(ActivityKind.Restore, Loc.F("act.restored", item.Name), item.Where, rule.Id);
                    else
                        AddActivity(ActivityKind.Warn, Loc.F("act.restoreFailed", item.Name), err, rule.Id);
                }
            }
            lock (ruleGate) Settings.Rules.RemoveAll(r => r.Id == rule.Id);
            Recompile();
            SaveSettings();
            Blocked.Save();
            AddActivity(ActivityKind.Info, Loc.F("act.ruleRemoved", rule.Name), rule.Pattern, rule.Id);
            ApplyWatcher();
            RaiseChanged();
        }

        /// <summary>Call after editing a rule's switches.</summary>
        public void RuleChanged(TargetRule rule, bool rescan)
        {
            Recompile();
            SaveSettings();
            ApplyWatcher();
            if (rescan) QueueSweep(true);
            RaiseChanged();
        }

        public bool RestoreItem(BlockedItem item, out string error)
        {
            var ok = blocker.Restore(item, out error);
            if (ok)
            {
                lock (ruleGate)
                {
                    var rule = Settings.Rules.FirstOrDefault(r => r.Id == item.RuleId);
                    if (rule != null && !rule.AllowKeys.Contains(item.Key)) rule.AllowKeys.Add(item.Key);
                }
                Recompile();
                SaveSettings();
                Blocked.Save();
                AddActivity(ActivityKind.Restore, Loc.F("act.restored", item.Kind == BlockKind.TrayIcon ? System.IO.Path.GetFileName(item.Detail ?? item.Name) : item.Name), item.Where, item.RuleId);
            }
            RaiseChanged();
            return ok;
        }

        /// <summary>Puts back everything Tray Killer ever disabled (used by uninstall).</summary>
        public static int RestoreEverything(BlockedStore store)
        {
            var blocker = new AutostartBlocker(store);
            int n = 0;
            foreach (var item in store.Snapshot())
            {
                string err;
                if (blocker.Restore(item, out err)) n++;
            }
            store.Save();
            return n;
        }

        public void SetPaused(bool paused)
        {
            Settings.Paused = paused;
            SaveSettings();
            ApplyWatcher();
            AddActivity(ActivityKind.Info, paused ? Loc.T("act.paused") : Loc.T("act.resumed"), null);
            if (!paused) QueueSweep(false);
            RaiseChanged();
        }

        // ── Sweeping ──────────────────────────────────────────────────────────────────────
        public void QueueSweep(bool deep) => ThreadPool.QueueUserWorkItem(_ => SafeSweep(deep));

        /// <summary>Runs one rule right now (from the UI), even while paused.</summary>
        public void RunRuleNow(TargetRule rule) => ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var one = Matcher.Compile(new[] { rule });
                if (one.Count == 0) return;
                KillMatching(one);
                blocker.EnforceFast(one);
                blocker.EnforceDeep(one);
            }
            catch (Exception ex) { Log.Error("run rule", ex); }
            finally { RaiseChanged(); }
        });

        void SafeSweep(bool deep)
        {
            if (disposed) return;
            try { Sweep(deep); }
            catch (Exception ex) { Log.Error("sweep", ex); }
        }

        void Sweep(bool deep)
        {
            if (!Monitor.TryEnter(sweepGate)) return;
            try
            {
                var rules = compiled;
                if (!Settings.Paused && rules.Count > 0)
                {
                    KillMatching(rules);
                    blocker.EnforceFast(rules);
                    if (deep) blocker.EnforceDeep(rules);
                }
            }
            finally
            {
                LastSweep = DateTime.Now;
                NextSweep = DateTime.Now.AddSeconds(Math.Max(3, Settings.SweepSeconds));
                Monitor.Exit(sweepGate);
                RaiseChanged();
            }
        }

        void KillMatching(IList<CompiledRule> rules)
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.Id == AppInfo.Pid || p.Id <= 4) continue;
                    string name;
                    try { name = p.ProcessName + ".exe"; } catch { continue; }
                    var rule = Matcher.Find(rules, name, r => r.Rule.KillProcess);
                    if (rule != null) KillOne(p.Id, name, rule, false);
                }
            }
        }

        void OnProcessStarted(int pid, string rawName)
        {
            try
            {
                if (Settings.Paused || pid == AppInfo.Pid) return;
                var rules = compiled;
                if (rules.Count == 0) return;
                // Trace events can truncate long image names, so prefer the real path.
                var path = ProcessTools.GetImagePath(pid);
                var name = path != null ? Path.GetFileName(path) : rawName;
                var rule = Matcher.Find(rules, name, r => r.Rule.KillProcess);
                if (rule == null) return;
                KillOne(pid, name, rule, true);
                // Installers and launchers often re-register auto-start right when they spawn the app.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(2000);
                    try { blocker.EnforceFast(compiled); } catch (Exception ex) { Log.Error("post-kill enforce", ex); }
                });
            }
            catch (Exception ex) { Log.Error("process started handler", ex); }
        }

        void KillOne(int pid, string name, CompiledRule rule, bool realtime)
        {
            int icons;
            string error;
            var result = ProcessTools.Kill(pid, Settings.CleanTrayIcons, out icons, out error);
            if (result == KillResult.Killed)
            {
                lock (statsGate)
                {
                    Stats.TotalKills++;
                    var key = Stats.DayKey(DateTime.Now);
                    int n;
                    Stats.Daily.TryGetValue(key, out n);
                    Stats.Daily[key] = n + 1;
                    rule.Rule.KillCount++;
                    rule.Rule.LastKilled = DateTime.Now;
                }
                var detail = name + " · PID " + pid + " · " + (realtime ? Loc.T("act.via.realtime") : Loc.T("act.via.sweep"));
                if (icons > 0) detail += " · " + Loc.T("act.trayCleaned");
                AddActivity(ActivityKind.Kill, Loc.F("act.killed", rule.Rule.Name), detail, rule.Rule.Id);
                Log.Info("Killed " + name + " (" + pid + ") icons=" + icons + " realtime=" + realtime);
                Killed?.Invoke(rule.Rule, name);
                ScheduleSave();
            }
            else if (result == KillResult.Failed)
            {
                Problem("kill:" + name, Loc.F("act.killFailed", rule.Rule.Name),
                    AppInfo.IsElevated ? error : Loc.T("act.needAdminKill"));
            }
        }

        void OnBlocked(BlockedItem item, bool isNew)
        {
            lock (statsGate) Stats.TotalBlocked++;
            var rule = Rules.FirstOrDefault(r => r.Id == item.RuleId);
            var title = isNew
                ? Loc.F("act.blocked." + item.Kind, rule?.Name ?? item.Name)
                : Loc.F("act.reblocked", rule?.Name ?? item.Name);
            AddActivity(item.Kind == BlockKind.TrayIcon ? ActivityKind.Hide : ActivityKind.Block, title, item.Where, item.RuleId);
            Blocked.Save();
            ScheduleSave();
            RaiseChanged();
        }

        /// <summary>True once something could not be handled because we lack admin rights.</summary>
        public bool NeedsElevation { get; private set; }

        /// <summary>Reports a problem at most once every 30 minutes per key.</summary>
        void Problem(string key, string title, string detail)
        {
            if (!AppInfo.IsElevated && !NeedsElevation)
            {
                NeedsElevation = true;
                RaiseChanged();
            }
            lock (problems)
            {
                DateTime last;
                if (problems.TryGetValue(key, out last) && last > DateTime.Now.AddMinutes(-30)) return;
                problems[key] = DateTime.Now;
            }
            AddActivity(ActivityKind.Warn, title, detail);
            Log.Warn(title + " — " + detail);
        }

        // ── Activity & persistence ────────────────────────────────────────────────────────
        public void AddActivity(string kind, string title, string detail, string ruleId = null)
        {
            var e = new ActivityEntry { Kind = kind, Title = title, Detail = detail, RuleId = ruleId };
            Activity.Add(e);
            ActivityAdded?.Invoke(e);
            ScheduleSave();
        }

        public void ClearActivity()
        {
            Activity.Clear();
            ScheduleSave();
            RaiseChanged();
        }

        void RaiseChanged()
        {
            try { StateChanged?.Invoke(); } catch (Exception ex) { Log.Error("StateChanged handler", ex); }
        }

        void ScheduleSave()
        {
            if (disposed) return;
            if (saveTimer == null) saveTimer = new Timer(_ => SaveAll(), null, Timeout.Infinite, Timeout.Infinite);
            saveTimer.Change(2000, Timeout.Infinite);
        }

        public void SaveSettings()
        {
            try { lock (ruleGate) Json.Save(AppPaths.SettingsFile, Settings); }
            catch (Exception ex) { Log.Error("save settings", ex); }
        }

        public void SaveAll()
        {
            SaveSettings();
            try
            {
                lock (statsGate)
                {
                    var cutoff = Stats.DayKey(DateTime.Now.AddDays(-90));
                    foreach (var k in Stats.Daily.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList()) Stats.Daily.Remove(k);
                    Json.Save(AppPaths.StatsFile, Stats);
                }
            }
            catch (Exception ex) { Log.Error("save stats", ex); }
            try { Activity.Save(); } catch (Exception ex) { Log.Error("save activity", ex); }
            try { Blocked.Save(); } catch (Exception ex) { Log.Error("save blocked", ex); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            watcher.Dispose();
            sweepTimer?.Dispose();
            deepTimer?.Dispose();
            saveTimer?.Dispose();
            SaveAll();
        }
    }
}
