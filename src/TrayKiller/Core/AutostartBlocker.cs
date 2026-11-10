using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.ServiceProcess;
using Microsoft.Win32;

namespace TrayKiller.Core
{
    /// <summary>
    /// Finds every place a target app can auto-start from and switches it off — keeping a backup
    /// of each change so it can be put back exactly as it was.
    /// </summary>
    public sealed class AutostartBlocker
    {
        sealed class RunKey
        {
            public RegistryHive Hive;
            public RegistryView View;
            public string Path;
            public string Display;
        }

        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
        const string PolicyRunPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run";
        const string NotifyIconSettings = @"Control Panel\NotifyIconSettings";

        static readonly RunKey[] RunKeys =
        {
            new RunKey { Hive = RegistryHive.CurrentUser, View = RegistryView.Default, Path = RunPath, Display = @"HKCU\…\Run" },
            new RunKey { Hive = RegistryHive.CurrentUser, View = RegistryView.Default, Path = RunOncePath, Display = @"HKCU\…\RunOnce" },
            new RunKey { Hive = RegistryHive.CurrentUser, View = RegistryView.Default, Path = PolicyRunPath, Display = @"HKCU\…\Policies\Explorer\Run" },
            new RunKey { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, Path = RunPath, Display = @"HKLM\…\Run" },
            new RunKey { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, Path = RunOncePath, Display = @"HKLM\…\RunOnce" },
            new RunKey { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry32, Path = RunPath, Display = @"HKLM\…\WOW6432Node\…\Run" },
            new RunKey { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry32, Path = RunOncePath, Display = @"HKLM\…\WOW6432Node\…\RunOnce" },
            new RunKey { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, Path = PolicyRunPath, Display = @"HKLM\…\Policies\Explorer\Run" },
        };

        readonly BlockedStore store;

        /// <summary>(item, isNew) — isNew is false when an app re-created something we had already blocked.</summary>
        public event Action<BlockedItem, bool> Blocked;
        /// <summary>(key, message) — e.g. a location that needs administrator rights.</summary>
        public event Action<string, string> Problem;

        public AutostartBlocker(BlockedStore store)
        {
            this.store = store;
        }

        /// <summary>Cheap checks, run on every sweep: Run keys, Startup folders, tray-icon visibility.</summary>
        public void EnforceFast(IList<CompiledRule> rules)
        {
            if (rules.Any(r => r.Rule.BlockAutostart))
            {
                EnforceRunKeys(rules);
                EnforceStartupFolders(rules);
            }
            if (rules.Any(r => r.Rule.HideTrayIcon)) EnforceTrayIcons(rules);
        }

        /// <summary>Expensive checks, run every few minutes: scheduled tasks and services.</summary>
        public void EnforceDeep(IList<CompiledRule> rules)
        {
            if (rules.Any(r => r.Rule.BlockAutostart)) EnforceTasks(rules);
            if (rules.Any(r => r.Rule.DisableServices)) EnforceServices(rules);
        }

        CompiledRule MatchCommand(IList<CompiledRule> rules, string command, Func<CompiledRule, bool> filter)
        {
            var hit = Matcher.Find(rules, Matcher.FileNameFromCommand(command), filter);
            if (hit != null) return hit;
            foreach (var name in Matcher.FileNamesInArgs(command))
            {
                hit = Matcher.Find(rules, name, filter);
                if (hit != null) return hit;
            }
            return null;
        }

        bool Record(CompiledRule rule, BlockedItem item)
        {
            if (rule.Allow.Contains(item.Key)) return false;
            item.RuleId = rule.Rule.Id;
            bool isNew = store.Upsert(item);
            Blocked?.Invoke(item, isNew);
            return true;
        }

        // ── Registry Run keys ──────────────────────────────────────────────────────────────
        void EnforceRunKeys(IList<CompiledRule> rules)
        {
            foreach (var def in RunKeys)
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(def.Hive, def.View))
                    using (var key = baseKey.OpenSubKey(def.Path, false))
                    {
                        if (key == null) continue;
                        foreach (var name in key.GetValueNames())
                        {
                            if (def.Hive == RegistryHive.CurrentUser && def.Path == RunPath && name == StartupManager.RunValueName) continue;
                            var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                            if (data == null) continue;
                            var rule = MatchCommand(rules, data, r => r.Rule.BlockAutostart);
                            if (rule == null) continue;
                            var item = new BlockedItem
                            {
                                Kind = BlockKind.Registry, Location = def.Display, Name = name, Detail = data,
                                Hive = def.Hive.ToString(), View = def.View.ToString(), KeyPath = def.Path,
                                ValueData = data, ValueKind = (int)key.GetValueKind(name)
                            };
                            if (rule.Allow.Contains(item.Key)) continue;
                            try
                            {
                                using (var writable = baseKey.OpenSubKey(def.Path, true)) writable?.DeleteValue(name, false);
                                Record(rule, item);
                            }
                            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is SecurityException)
                            {
                                Problem?.Invoke("reg:" + item.Key, Loc.F("problem.needAdmin", def.Display + "\\" + name));
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Error("Run key scan " + def.Display, ex); }
            }
        }

        // ── Startup folders ────────────────────────────────────────────────────────────────
        void EnforceStartupFolders(IList<CompiledRule> rules)
        {
            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };
            foreach (var dir in folders)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                string[] files;
                try { files = Directory.GetFiles(dir); } catch { continue; }
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".ini") continue;
                    CompiledRule rule;
                    string target = file;
                    if (ext == ".lnk")
                    {
                        var link = ShellLink.Read(file);
                        if (link == null) continue;
                        target = link.Item1;
                        rule = MatchCommand(rules, "\"" + link.Item1 + "\" " + link.Item2, r => r.Rule.BlockAutostart);
                    }
                    else rule = Matcher.Find(rules, Path.GetFileName(file), r => r.Rule.BlockAutostart);
                    if (rule == null) continue;

                    var item = new BlockedItem
                    {
                        Kind = BlockKind.StartupFile, Location = dir, Name = Path.GetFileName(file), Detail = target, FilePath = file
                    };
                    if (rule.Allow.Contains(item.Key)) continue;
                    try
                    {
                        var backupDir = Path.Combine(AppPaths.BackupDir, "startup");
                        Directory.CreateDirectory(backupDir);
                        item.BackupPath = Path.Combine(backupDir, item.Id + "_" + Path.GetFileName(file));
                        File.Move(file, item.BackupPath);
                        Record(rule, item);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                    {
                        Problem?.Invoke("file:" + file, Loc.F("problem.needAdmin", file));
                    }
                }
            }
        }

        // ── Win11 tray icon visibility ─────────────────────────────────────────────────────
        void EnforceTrayIcons(IList<CompiledRule> rules)
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(NotifyIconSettings, false))
                {
                    if (root == null) return;
                    foreach (var id in root.GetSubKeyNames())
                    {
                        using (var k = root.OpenSubKey(id, true))
                        {
                            var exe = k?.GetValue("ExecutablePath") as string;
                            var rule = Matcher.Find(rules, Matcher.SafeFileName(exe), r => r.Rule.HideTrayIcon);
                            if (rule == null) continue;
                            var current = k.GetValue("IsPromoted") as int?;
                            if (current == 0) continue;
                            var item = new BlockedItem
                            {
                                Kind = BlockKind.TrayIcon, Location = NotifyIconSettings, Name = id,
                                Detail = Native.ResolveShellPath(exe), OldValue = current ?? -1
                            };
                            if (rule.Allow.Contains(item.Key)) continue;
                            k.SetValue("IsPromoted", 0, RegistryValueKind.DWord);
                            Record(rule, item);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Error("Tray icon settings scan", ex); }
        }

        // ── Scheduled tasks ────────────────────────────────────────────────────────────────
        void EnforceTasks(IList<CompiledRule> rules)
        {
            dynamic service = null;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                service.Connect();
                WalkTaskFolder(service.GetFolder("\\"), rules);
            }
            catch (Exception ex) { Log.Error("Task scan", ex); }
            finally
            {
                if (service != null) Marshal.FinalReleaseComObject(service);
            }
        }

        void WalkTaskFolder(dynamic folder, IList<CompiledRule> rules)
        {
            foreach (dynamic task in folder.GetTasks(1 /* TASK_ENUM_HIDDEN */))
            {
                try { CheckTask(task, rules); }
                catch (Exception ex) { Log.Warn("Task check failed: " + ex.Message); }
            }
            foreach (dynamic sub in folder.GetFolders(0))
                WalkTaskFolder(sub, rules);
        }

        void CheckTask(dynamic task, IList<CompiledRule> rules)
        {
            string path = task.Path;
            if (string.Equals(path, "\\" + AppInfo.TaskName, StringComparison.OrdinalIgnoreCase)) return;
            if (!(bool)task.Enabled) return;
            foreach (dynamic action in task.Definition.Actions)
            {
                if ((int)action.Type != 0) continue; // TASK_ACTION_EXEC
                string exe = action.Path;
                string args = action.Arguments;
                var rule = MatchCommand(rules, "\"" + (exe ?? "").Trim('"') + "\" " + args, r => r.Rule.BlockAutostart);
                if (rule == null) continue;
                var item = new BlockedItem
                {
                    Kind = BlockKind.Task, Location = "Task Scheduler", Name = path, TaskPath = path,
                    Detail = (exe + " " + args).Trim()
                };
                if (rule.Allow.Contains(item.Key)) return;
                try
                {
                    task.Enabled = false;
                    Record(rule, item);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is COMException)
                {
                    Problem?.Invoke("task:" + path, Loc.F("problem.needAdmin", path));
                }
                return;
            }
        }

        // ── Services ───────────────────────────────────────────────────────────────────────
        void EnforceServices(IList<CompiledRule> rules)
        {
            try
            {
                using (var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false))
                {
                    if (root == null) return;
                    foreach (var name in root.GetSubKeyNames())
                    {
                        using (var k = root.OpenSubKey(name, false))
                        {
                            if (k == null) continue;
                            var type = k.GetValue("Type") as int?;
                            if (type == null || (type.Value & 0x30) == 0) continue; // Win32 services only
                            var image = k.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                            var rule = Matcher.Find(rules, Matcher.FileNameFromCommand(image), r => r.Rule.DisableServices);
                            if (rule == null) continue;
                            int start = (k.GetValue("Start") as int?) ?? 3;
                            if (start == 4) continue; // already disabled
                            var item = new BlockedItem
                            {
                                Kind = BlockKind.Service, Location = "Services", Name = name, ServiceName = name,
                                Detail = (k.GetValue("DisplayName") as string ?? name) + " · " + image, OldValue = start
                            };
                            if (rule.Allow.Contains(item.Key)) continue;
                            string error;
                            if (!SetServiceStart(name, 4, out error))
                            {
                                Problem?.Invoke("svc:" + name, Loc.F("problem.needAdmin", name));
                                continue;
                            }
                            try
                            {
                                using (var sc = new ServiceController(name))
                                    if (sc.Status != ServiceControllerStatus.Stopped && sc.CanStop) sc.Stop();
                            }
                            catch (Exception ex) { Log.Warn("Stop service " + name + ": " + ex.Message); }
                            Record(rule, item);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Error("Service scan", ex); }
        }

        static bool SetServiceStart(string name, int start, out string error)
        {
            error = null;
            var scm = Native.OpenSCManager(null, null, 0x0001 /* SC_MANAGER_CONNECT */);
            if (scm == IntPtr.Zero) { error = "OpenSCManager " + Marshal.GetLastWin32Error(); return false; }
            try
            {
                var svc = Native.OpenService(scm, name, 0x0002 /* SERVICE_CHANGE_CONFIG */);
                if (svc == IntPtr.Zero) { error = "OpenService " + Marshal.GetLastWin32Error(); return false; }
                try
                {
                    if (Native.ChangeServiceConfig(svc, Native.SERVICE_NO_CHANGE, (uint)start, Native.SERVICE_NO_CHANGE,
                            null, null, IntPtr.Zero, null, null, null, null)) return true;
                    error = "ChangeServiceConfig " + Marshal.GetLastWin32Error();
                    return false;
                }
                finally { Native.CloseServiceHandle(svc); }
            }
            finally { Native.CloseServiceHandle(scm); }
        }

        // ── Restore ────────────────────────────────────────────────────────────────────────
        public bool Restore(BlockedItem it, out string error)
        {
            error = null;
            try
            {
                switch (it.Kind)
                {
                    case BlockKind.Registry:
                        var hive = (RegistryHive)Enum.Parse(typeof(RegistryHive), it.Hive);
                        var view = (RegistryView)Enum.Parse(typeof(RegistryView), it.View);
                        using (var b = RegistryKey.OpenBaseKey(hive, view))
                        using (var k = b.CreateSubKey(it.KeyPath))
                            k.SetValue(it.Name, it.ValueData, (RegistryValueKind)it.ValueKind);
                        break;

                    case BlockKind.StartupFile:
                        if (File.Exists(it.BackupPath))
                        {
                            if (File.Exists(it.FilePath)) File.Delete(it.BackupPath);
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(it.FilePath));
                                File.Move(it.BackupPath, it.FilePath);
                            }
                        }
                        break;

                    case BlockKind.Task:
                        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                        try
                        {
                            service.Connect();
                            dynamic task = service.GetFolder("\\").GetTask(it.TaskPath);
                            task.Enabled = true;
                        }
                        finally { Marshal.FinalReleaseComObject(service); }
                        break;

                    case BlockKind.Service:
                        string err;
                        if (!SetServiceStart(it.ServiceName, it.OldValue < 0 ? 3 : it.OldValue, out err))
                            throw new InvalidOperationException(err);
                        break;

                    case BlockKind.TrayIcon:
                        using (var k = Registry.CurrentUser.OpenSubKey(NotifyIconSettings + "\\" + it.Name, true))
                        {
                            if (k != null)
                            {
                                if (it.OldValue < 0) k.DeleteValue("IsPromoted", false);
                                else k.SetValue("IsPromoted", it.OldValue, RegistryValueKind.DWord);
                            }
                        }
                        break;
                }
                store.Remove(it);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("Restore " + it.Key, ex);
                return false;
            }
        }
    }
}
