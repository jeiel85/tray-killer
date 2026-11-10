using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace TrayKiller.Core
{
    public static class AppInfo
    {
        public const string Name = "Tray Killer";
        public const string ExeName = "TrayKiller.exe";
        public const string Owner = "jeiel85";
        public const string Repo = "tray-killer";
        public const string TaskName = "TrayKiller";

        public static string RepoUrl => "https://github.com/" + Owner + "/" + Repo;
        public static string ReleasesUrl => RepoUrl + "/releases/latest";
        public static string Homepage => "https://" + Owner + ".github.io/" + Repo + "/";

        public static Version Version { get; } = typeof(AppInfo).Assembly.GetName().Version;
        public static string VersionText => Version.Major + "." + Version.Minor + "." + Math.Max(0, Version.Build);

        public static string ExePath { get; } = Process.GetCurrentProcess().MainModule.FileName;
        public static int Pid { get; } = Process.GetCurrentProcess().Id;

        public static bool IsElevated { get; } =
            new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        public static int SessionId { get; } = Process.GetCurrentProcess().SessionId;

        public static bool IsInstalledCopy =>
            string.Equals(Path.GetFullPath(ExePath), Path.GetFullPath(AppPaths.InstalledExe), StringComparison.OrdinalIgnoreCase);
    }

    public static class AppPaths
    {
        public static string Profile { get; private set; } = "";
        public static bool IsDefaultProfile => Profile.Length == 0;
        public static string InstanceSuffix => IsDefaultProfile ? "default" : Profile;

        static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        /// <summary>Admin-only install folder. The elevated logon task runs this exe, so a normal user must not be able to replace it.</summary>
        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TrayKiller");
        public static string InstalledExe => Path.Combine(InstallDir, AppInfo.ExeName);
        /// <summary>Where v1.0.x installed itself (user-writable); migrated away from on the first elevated start.</summary>
        public static string LegacyInstallDir => Path.Combine(LocalAppData, "Programs", "TrayKiller");
        public static string ProtectedRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TrayKiller");

        /// <summary>Per-user, user-writable folder: command channel, limited-mode data, test profiles.</summary>
        public static string LocalDir { get; private set; }
        /// <summary>Settings, stats, backups. For the elevated default profile this is the admin-only protected store.</summary>
        public static string DataDir { get; private set; }
        public static bool IsProtectedStore { get; private set; }

        public static string SettingsFile => Path.Combine(DataDir, "settings.json");
        public static string StatsFile => Path.Combine(DataDir, "stats.json");
        public static string ActivityFile => Path.Combine(DataDir, "activity.json");
        public static string BlockedFile => Path.Combine(DataDir, "blocked.json");
        public static string LogDir => Path.Combine(DataDir, "logs");
        public static string BackupDir => Path.Combine(DataDir, "backup");
        public static string UpdateDir => Path.Combine(DataDir, "update");
        public static string SnapDir => Path.Combine(DataDir, "snaps");
        public static string CommandFile => Path.Combine(LocalDir, "command.txt");

        public static void Init(string profile)
        {
            Profile = new string((profile ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray());
            var root = Path.Combine(LocalAppData, "TrayKiller");
            LocalDir = IsDefaultProfile ? root : Path.Combine(root, "profiles", Profile);
            Directory.CreateDirectory(LocalDir);
            DataDir = LocalDir;
            if (IsDefaultProfile && AppInfo.IsElevated)
            {
                // Fail closed: an elevated instance never falls back to user-writable data.
                DataDir = SecureStore.Prepare();
                IsProtectedStore = true;
            }
        }
    }

    /// <summary>Command line switches.</summary>
    public sealed class Args
    {
        public bool Background, Install, Uninstall, Updated, NoElevate, Welcome, Quiet, Wait;
        public string Profile, Command;

        public static Args Parse(string[] argv)
        {
            var a = new Args();
            for (int i = 0; i < argv.Length; i++)
            {
                switch (argv[i].ToLowerInvariant())
                {
                    case "--background": a.Background = true; break;
                    case "--install": a.Install = true; break;
                    case "--uninstall": a.Uninstall = true; break;
                    case "--updated": a.Updated = true; break;
                    case "--no-elevate": a.NoElevate = true; break;
                    case "--welcome": a.Welcome = true; break;
                    case "--quiet": a.Quiet = true; break;
                    case "--wait": a.Wait = true; break;
                    case "--profile": if (i + 1 < argv.Length) a.Profile = argv[++i]; break;
                    case "--cmd": if (i + 1 < argv.Length) a.Command = argv[++i]; break;
                    case "--sweep": a.Command = "sweep"; break;
                }
            }
            return a;
        }
    }
}
