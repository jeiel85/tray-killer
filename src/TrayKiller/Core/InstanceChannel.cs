using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace TrayKiller.Core
{
    /// <summary>
    /// Single-instance guard plus a tiny command channel (a named event + a command file), so a second
    /// launch — elevated or not — can ask the running instance to show itself, open the menu, sweep, etc.
    /// </summary>
    public static class InstanceChannel
    {
        static Mutex mutex;
        static EventWaitHandle signal;
        static Thread listener;

        static string MutexName => @"Local\TrayKiller." + AppPaths.InstanceSuffix + ".instance";
        static string SignalName => @"Local\TrayKiller." + AppPaths.InstanceSuffix + ".signal";
        /// <summary>Only this user's own processes (elevated or not) may open the instance objects.</summary>
        static SecurityIdentifier CurrentUser => WindowsIdentity.GetCurrent().User;

        public static bool TryAcquire(int waitMs)
        {
            var until = DateTime.UtcNow.AddMilliseconds(waitMs);
            while (true)
            {
                var sec = new MutexSecurity();
                sec.AddAccessRule(new MutexAccessRule(CurrentUser, MutexRights.FullControl, AccessControlType.Allow));
                bool created;
                Mutex m;
                try { m = new Mutex(true, MutexName, out created, sec); }
                catch (UnauthorizedAccessException) { m = null; created = false; }
                if (created)
                {
                    mutex = m;
                    return true;
                }
                m?.Dispose();
                if (DateTime.UtcNow >= until) return false;
                Thread.Sleep(250);
            }
        }

        public static void Release()
        {
            try { mutex?.ReleaseMutex(); } catch { }
            try { mutex?.Dispose(); } catch { }
            mutex = null;
        }

        public static void QueueCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return;
            try { File.WriteAllText(AppPaths.CommandFile, command); } catch { }
        }

        /// <summary>Hands a command to the running instance. Returns false if no instance is listening.</summary>
        public static bool Send(string command)
        {
            QueueCommand(command ?? "noop");
            try
            {
                using (var e = EventWaitHandle.OpenExisting(SignalName, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize))
                    e.Set();
                return true;
            }
            catch { return false; }
        }

        /// <summary>A command left by a launcher that started us (e.g. "show" before running the logon task).</summary>
        public static string ConsumePending()
        {
            try
            {
                var f = AppPaths.CommandFile;
                if (!File.Exists(f)) return null;
                var fresh = File.GetLastWriteTimeUtc(f) > DateTime.UtcNow.AddSeconds(-30);
                var cmd = File.ReadAllText(f).Trim();
                File.Delete(f);
                return fresh && cmd.Length > 0 ? cmd : null;
            }
            catch { return null; }
        }

        public static void Listen(Action<string> onCommand)
        {
            var sec = new EventWaitHandleSecurity();
            sec.AddAccessRule(new EventWaitHandleAccessRule(CurrentUser, EventWaitHandleRights.FullControl, AccessControlType.Allow));
            bool created;
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName, out created, sec);
            listener = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        signal.WaitOne();
                        var cmd = ConsumePending() ?? "show";
                        if (cmd != "noop") onCommand(cmd);
                    }
                    catch (ObjectDisposedException) { return; }
                    catch (Exception ex) { Log.Error("command listener", ex); }
                }
            }) { IsBackground = true, Name = "TrayKiller.Commands" };
            listener.Start();
        }
    }
}
