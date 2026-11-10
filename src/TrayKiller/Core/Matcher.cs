using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TrayKiller.Core
{
    public sealed class CompiledRule
    {
        public TargetRule Rule;
        public Regex Regex;
        public HashSet<string> Allow;

        public bool IsMatch(string fileName) => !string.IsNullOrEmpty(fileName) && Regex.IsMatch(fileName);
    }

    public static class Matcher
    {
        /// <summary>Processes Tray Killer refuses to touch, no matter what the user types.</summary>
        static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "traykiller.exe", "system", "registry", "idle", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe",
            "services.exe", "lsass.exe", "lsaiso.exe", "svchost.exe", "dwm.exe", "explorer.exe", "fontdrvhost.exe",
            "sihost.exe", "taskhostw.exe", "ctfmon.exe", "conhost.exe", "dllhost.exe", "runtimebroker.exe",
            "searchhost.exe", "searchindexer.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe",
            "textinputhost.exe", "applicationframehost.exe", "audiodg.exe", "spoolsv.exe", "msmpeng.exe",
            "securityhealthservice.exe", "securityhealthsystray.exe", "taskmgr.exe", "logonui.exe", "userinit.exe",
            "wudfhost.exe", "memory compression", "smartscreen.exe", "consent.exe", "mmc.exe", "cmd.exe",
            "powershell.exe", "pwsh.exe", "rundll32.exe", "regsvr32.exe", "wmiprvse.exe", "msiexec.exe",
            "windowsterminal.exe", "openconsole.exe"
        };

        public static bool IsProtected(string fileName) => !string.IsNullOrEmpty(fileName) && Protected.Contains(fileName);

        /// <summary>Normalizes user input ("C:\x\Agent.exe", "agent", "agent*.exe") into a file-name pattern.</summary>
        public static string Normalize(string input)
        {
            var p = (input ?? "").Trim().Trim('"').Trim();
            if (p.Length == 0) return "";
            int slash = p.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) p = p.Substring(slash + 1);
            if (!p.EndsWith("*") && Path.GetExtension(p).Length == 0) p += ".exe";
            return p.ToLowerInvariant();
        }

        public static Regex ToRegex(string pattern) =>
            new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Returns the protected process a pattern would hit, or null when the pattern is safe.</summary>
        public static string ProtectedHit(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return null;
            if (pattern.Trim('*', '?', '.').Length == 0) return "*";
            var rx = ToRegex(pattern);
            return Protected.FirstOrDefault(rx.IsMatch);
        }

        public static List<CompiledRule> Compile(IEnumerable<TargetRule> rules) =>
            rules.Where(r => r.Enabled && !string.IsNullOrEmpty(r.Pattern))
                 .Select(r => new CompiledRule
                 {
                     Rule = r,
                     Regex = ToRegex(r.Pattern),
                     Allow = new HashSet<string>(r.AllowKeys ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
                 })
                 .ToList();

        public static CompiledRule Find(IList<CompiledRule> rules, string fileName, Func<CompiledRule, bool> filter = null)
        {
            if (string.IsNullOrEmpty(fileName) || IsProtected(fileName)) return null;
            foreach (var r in rules)
                if ((filter == null || filter(r)) && r.IsMatch(fileName)) return r;
            return null;
        }

        public static string SafeFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int i = path.LastIndexOfAny(new[] { '\\', '/' });
            return (i >= 0 ? path.Substring(i + 1) : path).Trim().Trim('"');
        }

        /// <summary>Extracts the executable file name from a command line such as
        /// <c>"C:\Program Files\X\x.exe" --tray</c> or <c>C:\X\x.exe /min</c>.</summary>
        public static string FileNameFromCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            var cmd = Environment.ExpandEnvironmentVariables(command.Trim());
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                return SafeFileName(end > 0 ? cmd.Substring(1, end - 1) : cmd.Substring(1));
            }
            int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0) return SafeFileName(cmd.Substring(0, exe + 4));
            int space = cmd.IndexOf(' ');
            return SafeFileName(space > 0 ? cmd.Substring(0, space) : cmd);
        }

        /// <summary>All file-name-looking tokens in an argument string (for tasks launched through wrappers).</summary>
        public static IEnumerable<string> FileNamesInArgs(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) yield break;
            foreach (Match m in Regex.Matches(args, "\"([^\"]+)\"|(\\S+)"))
            {
                var token = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) yield return SafeFileName(token);
            }
        }
    }
}
