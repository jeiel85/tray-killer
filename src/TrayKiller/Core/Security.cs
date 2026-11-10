using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TrayKiller.Core
{
    /// <summary>
    /// Admin-only data folder for the elevated instance: %ProgramData%\TrayKiller\&lt;user SID&gt;.
    /// Settings and restore data drive privileged actions (registry writes, file moves, service changes),
    /// so they must not live anywhere a non-admin process could edit them.
    /// </summary>
    public static class SecureStore
    {
        static readonly SecurityIdentifier SystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        static readonly SecurityIdentifier AdminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        public static string Prepare()
        {
            var root = AppPaths.ProtectedRoot;
            var user = WindowsIdentity.GetCurrent().User;

            // Anyone can create folders in ProgramData, so a pre-existing folder (or junction) is not ours to trust.
            if (Directory.Exists(root) && !IsTrusted(root))
            {
                var aside = root + ".untrusted-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                Directory.Move(root, aside);
                Log.Warn("Moved untrusted " + root + " aside to " + aside);
            }

            var security = Build(user);
            if (!Directory.Exists(root)) Directory.CreateDirectory(root, security);
            if (!IsTrusted(root)) throw new UnauthorizedAccessException("Could not secure " + root);
            Directory.SetAccessControl(root, security);

            var dir = Path.Combine(root, user.Value);
            if (Directory.Exists(dir) && SafeFs.IsReparse(dir)) throw new UnauthorizedAccessException("Unexpected link at " + dir);
            Directory.CreateDirectory(dir);
            return dir;
        }

        static DirectorySecurity Build(SecurityIdentifier user)
        {
            var s = new DirectorySecurity();
            s.SetOwner(AdminsSid);
            s.SetAccessRuleProtection(true, false);
            const InheritanceFlags all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            s.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
            s.AddAccessRule(new FileSystemAccessRule(AdminsSid, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
            // The user may read (logs, "open data folder") but never write.
            s.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
            return s;
        }

        static bool IsTrusted(string dir)
        {
            if (SafeFs.IsReparse(dir)) return false;
            var owner = Directory.GetAccessControl(dir, AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            return owner != null && (owner.Equals(AdminsSid) || owner.Equals(SystemSid));
        }
    }

    /// <summary>File helpers that never follow junctions/symlinks — used whenever an elevated
    /// process touches a folder a normal user can write to.</summary>
    public static class SafeFs
    {
        public static bool IsReparse(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch { return false; }
        }

        /// <summary>True when no folder from <paramref name="root"/> down to <paramref name="path"/> is a link.</summary>
        public static bool NoLinksBetween(string root, string path)
        {
            var full = Path.GetFullPath(path).TrimEnd('\\');
            var top = Path.GetFullPath(root).TrimEnd('\\');
            if (!full.StartsWith(top + "\\", StringComparison.OrdinalIgnoreCase) && !string.Equals(full, top, StringComparison.OrdinalIgnoreCase))
                return false;
            for (var p = full; p != null && p.Length >= top.Length; p = Path.GetDirectoryName(p))
                if (Directory.Exists(p) && IsReparse(p)) return false;
            return true;
        }

        /// <summary>Copies a regular file (not a link) — reading through a planted link could leak admin-only files.</summary>
        public static bool CopyRegular(string source, string dest)
        {
            if (!File.Exists(source) || IsReparse(source)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(source, dest, true);
            return true;
        }

        /// <summary>Deletes a folder tree; links are removed as links, their targets are never entered.</summary>
        public static void DeleteTree(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                if (IsReparse(dir)) { Directory.Delete(dir, false); return; }
                foreach (var f in Directory.GetFiles(dir))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
                }
                foreach (var d in Directory.GetDirectories(dir)) DeleteTree(d);
                Directory.Delete(dir, false);
            }
            catch (Exception ex) { Log.Warn("Delete " + dir + ": " + ex.Message); }
        }
    }

    public static class ShellOpen
    {
        /// <summary>Opens a URL or folder as the normal (non-elevated) user, so e.g. the browser never runs as admin.</summary>
        public static void Unelevated(string target)
        {
            try
            {
                if (AppInfo.IsElevated)
                    Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + target + "\"")
                    { UseShellExecute = false });
                else
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Warn("Open " + target + ": " + ex.Message); }
        }
    }
}
