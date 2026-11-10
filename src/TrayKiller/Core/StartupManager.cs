using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace TrayKiller.Core
{
    /// <summary>
    /// Makes Tray Killer start before every other tray app: an elevated Task Scheduler logon task
    /// fires the moment the user signs in, while ordinary startup apps wait for Explorer's startup delay.
    /// The task also lets a normal launch hand over to the elevated copy without a UAC prompt, so it stays
    /// registered even when "start with Windows" is off (only its logon trigger is disabled then).
    /// Without admin rights it falls back to a HKCU Run entry.
    /// </summary>
    public static class StartupManager
    {
        public const string RunValueName = "TrayKiller";
        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static int Schtasks(string args, out string output)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default
            };
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return p.HasExited ? p.ExitCode : -1;
            }
        }

        public static bool TaskExists()
        {
            try { string _; return Schtasks("/Query /TN \"" + AppInfo.TaskName + "\"", out _) == 0; }
            catch { return false; }
        }

        public static bool RunTask()
        {
            try { string _; return Schtasks("/Run /TN \"" + AppInfo.TaskName + "\"", out _) == 0; }
            catch { return false; }
        }

        public static bool RunValueExists()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunPath, false))
                return k?.GetValue(RunValueName) != null;
        }

        /// <param name="settingOn">The app's own "start with Windows" setting (the task's trigger mirrors it).</param>
        public static bool IsEnabled(bool settingOn) => (settingOn && TaskExists()) || RunValueExists();

        /// <summary>Elevated: (re)registers the task pointing at <paramref name="exe"/>. Otherwise: HKCU Run fallback.</summary>
        public static bool Apply(string exe, bool startWithWindows, out string error)
        {
            error = null;
            try
            {
                if (AppInfo.IsElevated)
                {
                    RegisterTask(exe, startWithWindows);
                    RemoveRunValue();
                }
                else if (TaskExists())
                {
                    if (!startWithWindows) { error = Loc.T("problem.needAdminStartup"); return false; }
                }
                else if (startWithWindows)
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunPath))
                        k.SetValue(RunValueName, "\"" + exe + "\" --background");
                }
                else RemoveRunValue();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("Apply startup", ex);
                return false;
            }
        }

        /// <summary>Removes every trace (uninstall).</summary>
        public static void Remove()
        {
            try { RemoveRunValue(); } catch { }
            if (TaskExists())
            {
                string output;
                if (Schtasks("/Delete /TN \"" + AppInfo.TaskName + "\" /F", out output) != 0) Log.Warn("Task delete: " + output.Trim());
            }
        }

        static void RemoveRunValue()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunPath, true))
                k?.DeleteValue(RunValueName, false);
        }

        static string X(string s) => SecurityElement.Escape(s);

        static void RegisterTask(string exe, bool logonEnabled)
        {
            // The definition file must not be writable by a normal user between writing and registering it,
            // otherwise the task (which runs with highest privileges) could be swapped. Keep it in the protected store.
            if (!AppPaths.IsProtectedStore) throw new InvalidOperationException("Refusing to register the elevated task outside the protected data folder.");
            if (!string.Equals(Path.GetFullPath(exe), Path.GetFullPath(AppPaths.InstalledExe), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The elevated task may only run the copy in " + AppPaths.InstallDir);

            var id = WindowsIdentity.GetCurrent();
            var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>Tray Killer</Author>
    <Description>Starts Tray Killer at logon, before other tray apps, so unwanted tray apps never get a foothold.</Description>
    <URI>\{AppInfo.TaskName}</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>{(logonEnabled ? "true" : "false")}</Enabled>
      <UserId>{X(id.Name)}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{X(id.User.Value)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
    <RestartOnFailure>
      <Interval>PT1M</Interval>
      <Count>5</Count>
    </RestartOnFailure>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{X(exe)}""</Command>
      <Arguments>--background</Arguments>
      <WorkingDirectory>{X(Path.GetDirectoryName(exe))}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
            var file = Path.Combine(AppPaths.DataDir, "task-" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(file, xml, Encoding.Unicode);
            try
            {
                string output;
                int code = Schtasks("/Create /TN \"" + AppInfo.TaskName + "\" /XML \"" + file + "\" /F", out output);
                if (code != 0) throw new InvalidOperationException("schtasks /Create failed (" + code + "): " + output.Trim());
                Log.Info("Logon task registered for " + exe + " (logon trigger " + (logonEnabled ? "on" : "off") + ")");
            }
            finally { try { File.Delete(file); } catch { } }
        }
    }

    public static class Elevation
    {
        /// <summary>Starts this exe elevated with the given arguments. Returns false if the user declined UAC.</summary>
        public static bool Relaunch(string args)
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppInfo.ExePath, args)
                {
                    UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(AppInfo.ExePath)
                });
                return true;
            }
            catch (Win32Exception ex)
            {
                Log.Info("Elevation declined: " + ex.Message);
                return false;
            }
        }
    }

    /// <summary>Machine-wide install into %ProgramFiles% (admin-only), Start menu entry for all users, Apps-list entry.</summary>
    public static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TrayKiller";
        static readonly string[] DataFiles = { "settings.json", "stats.json", "activity.json", "blocked.json" };

        public static string StartMenuShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Tray Killer.lnk");
        static string LegacyShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Tray Killer.lnk");

        /// <summary>Copies this exe to %ProgramFiles%\TrayKiller and registers it with Windows (elevated only).</summary>
        public static string Install()
        {
            if (!AppInfo.IsElevated) throw new InvalidOperationException("Install requires administrator rights.");
            Directory.CreateDirectory(AppPaths.InstallDir);
            var target = AppPaths.InstalledExe;
            if (!AppInfo.IsInstalledCopy)
            {
                if (File.Exists(target))
                {
                    try { File.Delete(target + ".old"); } catch { }
                    File.Move(target, target + ".old");
                }
                File.Copy(AppInfo.ExePath, target, true);
            }
            try { ShellLink.Create(StartMenuShortcut, target, "", AppPaths.InstallDir, "Tray Killer"); }
            catch (Exception ex) { Log.Warn("Start menu shortcut: " + ex.Message); }
            WriteUninstallEntry(target);
            Log.Info("Installed to " + target);
            return target;
        }

        public static void WriteUninstallEntry(string exe)
        {
            try
            {
                using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", "Tray Killer");
                    k.SetValue("DisplayVersion", AppInfo.VersionText);
                    k.SetValue("Publisher", AppInfo.Owner);
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("InstallLocation", Path.GetDirectoryName(exe));
                    k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                    k.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --quiet");
                    k.SetValue("URLInfoAbout", AppInfo.RepoUrl);
                    k.SetValue("HelpLink", AppInfo.RepoUrl + "/issues");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { Log.Warn("Uninstall entry: " + ex.Message); }
        }

        public static void RemoveRegistration()
        {
            try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { if (File.Exists(StartMenuShortcut)) File.Delete(StartMenuShortcut); } catch { }
            RemoveLegacyRegistration();
        }

        static void RemoveLegacyRegistration()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { if (File.Exists(LegacyShortcut) && !SafeFs.IsReparse(LegacyShortcut)) File.Delete(LegacyShortcut); } catch { }
        }

        /// <summary>
        /// One-time move of v1.0.x data (user-writable %LOCALAPPDATA%\TrayKiller) into the protected store.
        /// Links are skipped so nothing outside the user's own files can be pulled in.
        /// </summary>
        public static void ImportLegacyData()
        {
            if (!AppPaths.IsProtectedStore) return;
            var src = AppPaths.LocalDir;
            var dst = AppPaths.DataDir;
            if (File.Exists(Path.Combine(dst, "settings.json")) || !File.Exists(Path.Combine(src, "settings.json"))) return;
            try
            {
                foreach (var name in DataFiles) SafeFs.CopyRegular(Path.Combine(src, name), Path.Combine(dst, name));
                var srcBackup = Path.Combine(src, "backup", "startup");
                var dstBackup = Path.Combine(AppPaths.BackupDir, "startup");
                if (Directory.Exists(srcBackup) && !SafeFs.IsReparse(srcBackup))
                    foreach (var f in Directory.GetFiles(srcBackup)) SafeFs.CopyRegular(f, Path.Combine(dstBackup, Path.GetFileName(f)));

                var blocked = Json.Load<System.Collections.Generic.List<BlockedItem>>(AppPaths.BlockedFile);
                foreach (var b in blocked.Where(b => b.BackupPath != null && b.BackupPath.StartsWith(srcBackup, StringComparison.OrdinalIgnoreCase)))
                    b.BackupPath = Path.Combine(dstBackup, Path.GetFileName(b.BackupPath));
                Json.Save(AppPaths.BlockedFile, blocked);

                foreach (var name in DataFiles) { try { File.Delete(Path.Combine(src, name)); } catch { } }
                SafeFs.DeleteTree(Path.Combine(src, "backup"));
                Log.Info("Moved settings from " + src + " into the protected store " + dst);
            }
            catch (Exception ex) { Log.Error("Legacy data import", ex); }
        }

        /// <summary>Removes the v1.0.x user-writable install (exe, shortcut, per-user Apps entry).</summary>
        public static void CleanupLegacyInstall()
        {
            var dir = AppPaths.LegacyInstallDir;
            if (Directory.Exists(dir)) SafeFs.DeleteTree(dir);
            RemoveLegacyRegistration();
        }

        /// <summary>Deletes the install folder and all data a few seconds after this process exits.</summary>
        public static void ScheduleFolderRemoval()
        {
            // User-writable folders are cleaned here without following links...
            SafeFs.DeleteTree(Path.Combine(AppPaths.LocalDir, "logs"));
            SafeFs.DeleteTree(Path.Combine(AppPaths.LocalDir, "snaps"));
            foreach (var name in DataFiles.Concat(new[] { "command.txt" }))
                try { File.Delete(Path.Combine(AppPaths.LocalDir, name)); } catch { }
            // ...admin-only folders (no user-made links possible) once this exe has exited.
            var cmd = "/c ping 127.0.0.1 -n 4 > nul";
            if (AppPaths.IsProtectedStore) cmd += " & rmdir /s /q \"" + AppPaths.DataDir + "\"";
            if (AppPaths.IsDefaultProfile && AppInfo.IsElevated) cmd += " & rmdir /s /q \"" + AppPaths.InstallDir + "\"";
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd)
            {
                CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Environment.SystemDirectory
            });
        }
    }
}
