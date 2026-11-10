using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace TrayKiller.Core
{
    /// <summary>
    /// Makes Tray Killer start before every other tray app: an elevated Task Scheduler logon task
    /// fires the moment the user signs in, while ordinary startup apps wait for Explorer's startup delay.
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

        public static bool IsEnabled() => TaskExists() || RunValueExists();

        public static bool Enable(string exe, out string error)
        {
            error = null;
            try
            {
                if (AppInfo.IsElevated)
                {
                    RegisterTask(exe);
                    RemoveRunValue();
                }
                else if (!TaskExists())
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunPath))
                        k.SetValue(RunValueName, "\"" + exe + "\" --background");
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("Enable startup", ex);
                return false;
            }
        }

        public static bool Disable(out string error)
        {
            error = null;
            bool ok = true;
            try { RemoveRunValue(); } catch (Exception ex) { error = ex.Message; ok = false; }
            if (TaskExists())
            {
                string output;
                if (Schtasks("/Delete /TN \"" + AppInfo.TaskName + "\" /F", out output) != 0)
                {
                    error = output.Trim();
                    ok = false;
                }
            }
            return ok;
        }

        static void RemoveRunValue()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunPath, true))
                k?.DeleteValue(RunValueName, false);
        }

        static string X(string s) => SecurityElement.Escape(s);

        static void RegisterTask(string exe)
        {
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
      <Enabled>true</Enabled>
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
            var file = Path.Combine(Path.GetTempPath(), "TrayKiller-task-" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(file, xml, Encoding.Unicode);
            try
            {
                string output;
                int code = Schtasks("/Create /TN \"" + AppInfo.TaskName + "\" /XML \"" + file + "\" /F", out output);
                if (code != 0) throw new InvalidOperationException("schtasks /Create failed (" + code + "): " + output.Trim());
                Log.Info("Logon task registered for " + exe);
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

    public static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TrayKiller";

        public static string StartMenuShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Tray Killer.lnk");

        /// <summary>Copies this exe to %LOCALAPPDATA%\Programs\TrayKiller and registers it with Windows.</summary>
        public static string Install()
        {
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
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
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
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { if (File.Exists(StartMenuShortcut)) File.Delete(StartMenuShortcut); } catch { }
        }

        /// <summary>Deletes the install folder and all data a few seconds after this process exits.</summary>
        public static void ScheduleFolderRemoval()
        {
            var cmd = "/c ping 127.0.0.1 -n 4 > nul & rmdir /s /q \"" + AppPaths.DataDir + "\"";
            if (AppPaths.IsDefaultProfile) cmd += " & rmdir /s /q \"" + AppPaths.InstallDir + "\"";
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd)
            {
                CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath()
            });
        }
    }
}
