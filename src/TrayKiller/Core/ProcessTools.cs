using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TrayKiller.Core
{
    public enum KillResult { Killed, Gone, Failed }

    public static class ProcessTools
    {
        const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public static string GetImagePath(int pid)
        {
            var h = Native.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally { Native.CloseHandle(h); }
        }

        /// <summary>
        /// Removes the process' tray icons (so no "ghost" stays behind), then terminates it.
        /// </summary>
        public static KillResult Kill(int pid, bool cleanTray, out int iconsRemoved, out string error)
        {
            iconsRemoved = 0;
            error = null;
            List<IntPtr> windows = null;
            if (cleanTray)
            {
                windows = TrayCleaner.WindowsOf(pid);
                iconsRemoved += TrayCleaner.RemoveIcons(windows);
            }
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch (ArgumentException) { return KillResult.Gone; }
            catch (InvalidOperationException) { return KillResult.Gone; }
            catch (Win32Exception ex)
            {
                // ERROR_ACCESS_DENIED on an already-exiting process is also reported here.
                try { using (var p = Process.GetProcessById(pid)) if (p.HasExited) return KillResult.Gone; }
                catch { return KillResult.Gone; }
                error = ex.Message;
                return KillResult.Failed;
            }
            if (cleanTray)
            {
                iconsRemoved += TrayCleaner.RemoveIcons(windows);
                TrayCleaner.RefreshLegacyArea();
            }
            return KillResult.Killed;
        }
    }

    /// <summary>
    /// Removes notification-area icons that belong to another process. A process that is killed
    /// never gets the chance to call NIM_DELETE, which is what leaves "ghost" icons behind —
    /// so Tray Killer deletes them on the process' behalf before and after terminating it.
    /// </summary>
    public static class TrayCleaner
    {
        static readonly int[] ExtraIds = { 1000, 1001, 1002, 1024, 2000, 4096, 9999, 12345, 0x7FFF, 0xFFFF };
        static readonly HashSet<string> SkipClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "IME", "MSCTFIME UI", "Default IME" };

        public static List<IntPtr> WindowsOf(int pid)
        {
            var list = new List<IntPtr>();
            Native.EnumWindows((h, l) =>
            {
                int p;
                Native.GetWindowThreadProcessId(h, out p);
                if (p == pid && !SkipClasses.Contains(Native.ClassOf(h))) list.Add(h);
                return true;
            }, IntPtr.Zero);
            IntPtr child = IntPtr.Zero;
            while ((child = Native.FindWindowEx(Native.HWND_MESSAGE, child, null, null)) != IntPtr.Zero)
            {
                int p;
                Native.GetWindowThreadProcessId(child, out p);
                if (p == pid) list.Add(child);
            }
            return list;
        }

        public static int RemoveIcons(IEnumerable<IntPtr> windows)
        {
            if (windows == null) return 0;
            int removed = 0;
            var data = new Native.NOTIFYICONDATA { cbSize = Marshal.SizeOf(typeof(Native.NOTIFYICONDATA)) };
            foreach (var h in windows)
            {
                data.hWnd = h;
                foreach (var id in Enumerable.Range(0, 257).Concat(ExtraIds))
                {
                    data.uID = id;
                    if (Native.Shell_NotifyIcon(Native.NIM_DELETE, ref data)) removed++;
                }
            }
            return removed;
        }

        /// <summary>Classic trick for the legacy (Windows 10) notification area: hovering over
        /// every icon makes Explorer drop the ones whose owner window no longer exists.</summary>
        public static void RefreshLegacyArea()
        {
            try
            {
                foreach (var toolbar in LegacyToolbars())
                {
                    Native.RECT r;
                    if (!Native.GetClientRect(toolbar, out r)) continue;
                    for (int x = 0; x < r.Right; x += 4)
                        for (int y = 0; y < r.Bottom; y += 4)
                            Native.SendMessage(toolbar, Native.WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)((y << 16) | (x & 0xFFFF)));
                }
            }
            catch { }
        }

        static IEnumerable<IntPtr> LegacyToolbars()
        {
            var tray = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            var notify = Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            var pager = Native.FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
            if (pager != IntPtr.Zero)
            {
                var tb = Native.FindWindowEx(pager, IntPtr.Zero, "ToolbarWindow32", null);
                if (tb != IntPtr.Zero) yield return tb;
            }
            var overflow = Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "NotifyIconOverflowWindow", null);
            if (overflow != IntPtr.Zero)
            {
                var tb = Native.FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null);
                if (tb != IntPtr.Zero) yield return tb;
            }
        }
    }
}
