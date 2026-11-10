using System;
using System.Management;
using System.Threading;

namespace TrayKiller.Core
{
    /// <summary>
    /// Real-time process start notifications. Elevated: kernel-backed Win32_ProcessStartTrace
    /// (instant, cheap). Not elevated: falls back to a 1-second WMI instance-creation poll.
    /// </summary>
    public sealed class ProcessWatcher : IDisposable
    {
        ManagementEventWatcher watcher;
        readonly object gate = new object();
        bool wanted, disposed;
        int failures;

        public event Action<int, string> ProcessStarted;
        public bool IsRunning { get; private set; }
        public bool IsTraceMode { get; private set; }

        public void Start(bool elevated)
        {
            lock (gate)
            {
                wanted = true;
                if (IsRunning || disposed) return;
                if (elevated && TryStart(new WqlEventQuery("SELECT ProcessID, ProcessName FROM Win32_ProcessStartTrace"), true)) return;
                TryStart(new WqlEventQuery("__InstanceCreationEvent", TimeSpan.FromSeconds(1), "TargetInstance ISA 'Win32_Process'"), false);
            }
        }

        bool TryStart(WqlEventQuery query, bool trace)
        {
            var w = new ManagementEventWatcher(new ManagementScope(@"\\.\root\cimv2"), query);
            try
            {
                w.EventArrived += OnEvent;
                w.Stopped += OnStopped;
                w.Start();
                watcher = w;
                IsRunning = true;
                IsTraceMode = trace;
                failures = 0;
                Log.Info("Process watcher started (" + (trace ? "trace" : "poll") + ")");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Process watcher (" + (trace ? "trace" : "poll") + ") failed: " + ex.Message);
                try { w.Dispose(); } catch { }
                return false;
            }
        }

        void OnEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                int pid;
                string name;
                var ev = e.NewEvent;
                if (ev.ClassPath.ClassName == "Win32_ProcessStartTrace")
                {
                    pid = Convert.ToInt32(ev["ProcessID"]);
                    name = ev["ProcessName"] as string;
                }
                else
                {
                    var target = (ManagementBaseObject)ev["TargetInstance"];
                    pid = Convert.ToInt32(target["ProcessId"]);
                    name = target["Name"] as string;
                }
                ProcessStarted?.Invoke(pid, name);
            }
            catch (Exception ex) { Log.Error("watcher event", ex); }
        }

        void OnStopped(object sender, StoppedEventArgs e)
        {
            lock (gate)
            {
                IsRunning = false;
                if (!wanted || disposed) return;
            }
            // WMI hiccup (service restart, etc.) — come back with a gentle backoff.
            int delay = Math.Min(60000, 2000 * (++failures));
            Log.Warn("Process watcher stopped (" + e.Status + "), restarting in " + delay + "ms");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(delay);
                lock (gate)
                {
                    if (!wanted || disposed || IsRunning) return;
                    CleanupWatcher();
                }
                Start(AppInfo.IsElevated);
            });
        }

        public void Stop()
        {
            lock (gate)
            {
                wanted = false;
                CleanupWatcher();
            }
        }

        void CleanupWatcher()
        {
            var w = watcher;
            watcher = null;
            IsRunning = false;
            if (w == null) return;
            try
            {
                w.EventArrived -= OnEvent;
                w.Stopped -= OnStopped;
                w.Stop();
            }
            catch { }
            try { w.Dispose(); } catch { }
        }

        public void Dispose()
        {
            lock (gate) disposed = true;
            Stop();
        }
    }
}
