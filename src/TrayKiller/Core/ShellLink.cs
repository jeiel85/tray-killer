using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace TrayKiller.Core
{
    /// <summary>Minimal IShellLink wrapper: resolve and create .lnk files without WSH.</summary>
    public static class ShellLink
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class CShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, int flags);
            void GetIDList(out IntPtr idl);
            void SetIDList(IntPtr idl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string rel, int reserved);
            void Resolve(IntPtr hwnd, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        /// <summary>Returns (target, arguments) of a shortcut, or null when it can't be read.</summary>
        public static Tuple<string, string> Read(string lnkPath)
        {
            object obj = null;
            try
            {
                obj = new CShellLink();
                ((IPersistFile)obj).Load(lnkPath, 0);
                var link = (IShellLinkW)obj;
                var target = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4 /* SLGP_RAWPATH */);
                var args = new StringBuilder(2048);
                link.GetArguments(args, args.Capacity);
                return Tuple.Create(Environment.ExpandEnvironmentVariables(target.ToString()), args.ToString());
            }
            catch (Exception ex)
            {
                Log.Warn("Cannot read shortcut " + lnkPath + ": " + ex.Message);
                return null;
            }
            finally
            {
                if (obj != null) Marshal.FinalReleaseComObject(obj);
            }
        }

        public static void Create(string lnkPath, string target, string args, string workDir, string description)
        {
            object obj = new CShellLink();
            try
            {
                var link = (IShellLinkW)obj;
                link.SetPath(target);
                link.SetArguments(args ?? "");
                link.SetWorkingDirectory(workDir ?? Path.GetDirectoryName(target));
                link.SetDescription(description ?? "");
                link.SetIconLocation(target, 0);
                Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));
                ((IPersistFile)obj).Save(lnkPath, true);
            }
            finally { Marshal.FinalReleaseComObject(obj); }
        }
    }
}
