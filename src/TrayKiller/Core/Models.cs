using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace TrayKiller.Core
{
    /// <summary>An app the user wants kept out of the tray.</summary>
    public class TargetRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        /// <summary>Executable file name or wildcard pattern, e.g. "agent.exe" or "agent*.exe".</summary>
        public string Pattern { get; set; }
        /// <summary>Full path seen when the rule was created (used for the icon only).</summary>
        public string Path { get; set; }
        public bool Enabled { get; set; } = true;
        public bool KillProcess { get; set; } = true;
        public bool BlockAutostart { get; set; } = true;
        public bool HideTrayIcon { get; set; } = true;
        public bool DisableServices { get; set; }
        public long KillCount { get; set; }
        public DateTime? LastKilled { get; set; }
        public DateTime Added { get; set; } = DateTime.Now;
        /// <summary>Blocked-item keys the user explicitly restored; never blocked again for this rule.</summary>
        public List<string> AllowKeys { get; set; } = new List<string>();
    }

    public class AppSettings
    {
        public int SweepSeconds { get; set; } = 10;
        public bool RealtimeWatch { get; set; } = true;
        public bool CleanTrayIcons { get; set; } = true;
        public bool NotifyOnKill { get; set; }
        public bool StartWithWindows { get; set; } = true;
        public bool AutoUpdate { get; set; } = true;
        public bool Paused { get; set; }
        public string Language { get; set; } = "auto";
        public bool SetupDone { get; set; }
        public bool TrayHintShown { get; set; }
        public bool SelfPromoted { get; set; }
        public DateTime? LastUpdateCheck { get; set; }
        public List<TargetRule> Rules { get; set; } = new List<TargetRule>();
    }

    public class Stats
    {
        public long TotalKills { get; set; }
        public long TotalBlocked { get; set; }
        public Dictionary<string, int> Daily { get; set; } = new Dictionary<string, int>();

        public static string DayKey(DateTime t) => t.ToString("yyyy-MM-dd");

        public int KillsOn(DateTime day)
        {
            int n;
            return Daily != null && Daily.TryGetValue(DayKey(day), out n) ? n : 0;
        }
    }

    public static class ActivityKind
    {
        public const string Kill = "kill";
        public const string Block = "block";
        public const string Hide = "hide";
        public const string Restore = "restore";
        public const string Update = "update";
        public const string Info = "info";
        public const string Warn = "warn";
        public const string Start = "start";
    }

    public class ActivityEntry
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string Kind { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public string RuleId { get; set; }
    }

    public static class BlockKind
    {
        public const string Registry = "Registry";
        public const string StartupFile = "StartupFile";
        public const string Task = "Task";
        public const string Service = "Service";
        public const string TrayIcon = "TrayIcon";
    }

    /// <summary>Something Tray Killer disabled, with enough data to put it back.</summary>
    public class BlockedItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string RuleId { get; set; }
        public string Kind { get; set; }
        /// <summary>Human readable location, e.g. "HKCU\…\Run".</summary>
        public string Location { get; set; }
        /// <summary>Value / file / task / service name.</summary>
        public string Name { get; set; }
        public string Detail { get; set; }
        public DateTime BlockedAt { get; set; } = DateTime.Now;

        // Registry
        public string Hive { get; set; }
        public string View { get; set; }
        public string KeyPath { get; set; }
        public string ValueData { get; set; }
        public int ValueKind { get; set; }
        // Startup folder
        public string FilePath { get; set; }
        public string BackupPath { get; set; }
        // Task / service / tray icon
        public string TaskPath { get; set; }
        public string ServiceName { get; set; }
        public int OldValue { get; set; } = -1;

        [ScriptIgnore] public string Key => Kind + "|" + Location + "|" + Name;

        /// <summary>Short, human friendly "where" text.</summary>
        [ScriptIgnore] public string Where
        {
            get
            {
                switch (Kind)
                {
                    case BlockKind.TrayIcon: return Loc.T("where.trayIcon") + " · " + System.IO.Path.GetFileName(Detail ?? "");
                    case BlockKind.StartupFile: return Loc.T("kind.startupFolder") + " · " + Name;
                    case BlockKind.Task: return Loc.T("kind.task") + " · " + Name;
                    case BlockKind.Service: return Loc.T("kind.service") + " · " + Name;
                    default: return Location + " · " + Name;
                }
            }
        }
    }
}
