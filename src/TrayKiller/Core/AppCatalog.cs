using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace TrayKiller.Core
{
    /// <summary>An app the user can pick as a target.</summary>
    public sealed class AppCandidate
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string Path { get; set; }
        public ImageSource Icon { get; set; }
        public bool Running { get; set; }
        public string Sub => FileName + (string.IsNullOrEmpty(Path) ? "" : "  ·  " + System.IO.Path.GetDirectoryName(Path));
    }

    public static class AppCatalog
    {
        static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";

        static bool Skip(string path, string file)
        {
            if (string.IsNullOrEmpty(file) || Matcher.IsProtected(file)) return true;
            if (!string.IsNullOrEmpty(path) && path.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(file, AppInfo.ExeName, StringComparison.OrdinalIgnoreCase);
        }

        static HashSet<string> RunningNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
                using (p) { try { set.Add(p.ProcessName + ".exe"); } catch { } }
            return set;
        }

        /// <summary>Every app Windows 11 has ever seen in the notification area (HKCU\Control Panel\NotifyIconSettings).</summary>
        public static List<AppCandidate> TrayApps()
        {
            var running = RunningNames();
            var byPath = new Dictionary<string, AppCandidate>(StringComparer.OrdinalIgnoreCase);
            using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", false))
            {
                if (root == null) return new List<AppCandidate>();
                foreach (var id in root.GetSubKeyNames())
                {
                    try
                    {
                        using (var k = root.OpenSubKey(id, false))
                        {
                            var raw = k?.GetValue("ExecutablePath") as string;
                            if (raw == null) continue;
                            var path = Native.ResolveShellPath(raw);
                            var file = Matcher.SafeFileName(path);
                            if (Skip(path, file)) continue;
                            var tip = (k.GetValue("InitialTooltip") as string ?? "").Split('\n')[0].Trim();
                            AppCandidate existing;
                            if (byPath.TryGetValue(path, out existing) && !string.IsNullOrEmpty(existing.Name) && tip.Length == 0) continue;
                            var icon = FromPng(k.GetValue("IconSnapshot") as byte[]) ?? IconCache.ForExe(path);
                            byPath[path] = new AppCandidate
                            {
                                Name = tip.Length > 0 ? tip : Describe(path, file),
                                FileName = file,
                                Path = path,
                                Icon = icon,
                                Running = running.Contains(file)
                            };
                        }
                    }
                    catch (Exception ex) { Log.Warn("Tray entry " + id + ": " + ex.Message); }
                }
            }
            // Same exe in several versioned folders (app-1.2, app-1.3 …) → keep one.
            return byPath.Values
                .GroupBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(c => c.Running).ThenByDescending(c => File.Exists(c.Path)).First())
                .OrderByDescending(c => c.Running).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Apps running in this session (outside the Windows folder).</summary>
        public static List<AppCandidate> RunningApps()
        {
            var session = Process.GetCurrentProcess().SessionId;
            var list = new Dictionary<string, AppCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.SessionId != session || p.Id == AppInfo.Pid) continue;
                        var path = ProcessTools.GetImagePath(p.Id);
                        var file = Matcher.SafeFileName(path) ?? p.ProcessName + ".exe";
                        if (path == null || Skip(path, file) || list.ContainsKey(path)) continue;
                        list[path] = new AppCandidate
                        {
                            Name = Describe(path, file), FileName = file, Path = path, Icon = IconCache.ForExe(path), Running = true
                        };
                    }
                    catch { }
                }
            }
            return list.Values.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static string Describe(string path, string file)
        {
            try
            {
                if (File.Exists(path))
                {
                    var v = FileVersionInfo.GetVersionInfo(path);
                    var d = (v.FileDescription ?? "").Trim();
                    if (d.Length > 1) return d;
                    if (!string.IsNullOrWhiteSpace(v.ProductName)) return v.ProductName.Trim();
                }
            }
            catch { }
            return System.IO.Path.GetFileNameWithoutExtension(file);
        }

        static ImageSource FromPng(byte[] data)
        {
            if (data == null || data.Length < 8) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(data);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }
    }

    public static class IconCache
    {
        static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource ForExe(string path, int size = 48)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (Cache)
            {
                ImageSource cached;
                if (Cache.TryGetValue(path, out cached)) return cached;
            }
            ImageSource result = null;
            try
            {
                if (File.Exists(path))
                {
                    IntPtr large, small;
                    if (Native.SHDefExtractIcon(path, 0, 0, out large, out small, (uint)((16 << 16) | size)) == 0)
                    {
                        try
                        {
                            if (large != IntPtr.Zero)
                            {
                                var src = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                                src.Freeze();
                                result = src;
                            }
                        }
                        finally
                        {
                            if (large != IntPtr.Zero) Native.DestroyIcon(large);
                            if (small != IntPtr.Zero) Native.DestroyIcon(small);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn("Icon " + path + ": " + ex.Message); }
            lock (Cache) Cache[path] = result;
            return result;
        }
    }
}
