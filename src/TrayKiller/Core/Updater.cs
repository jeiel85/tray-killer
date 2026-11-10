using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace TrayKiller.Core
{
    public sealed class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string ExeUrl;
        public string ShaUrl;
        public string PageUrl;
        public string Notes;
    }

    /// <summary>Self-update from GitHub Releases: download, verify SHA-256, swap the exe, restart.</summary>
    public static class Updater
    {
        static HttpClient http;

        static HttpClient Http
        {
            get
            {
                if (http != null) return http;
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var c = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                c.DefaultRequestHeaders.UserAgent.ParseAdd("TrayKiller/" + AppInfo.VersionText);
                c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                return http = c;
            }
        }

        public static Version Current => new Version(AppInfo.Version.Major, AppInfo.Version.Minor, Math.Max(0, AppInfo.Version.Build));

        public static Version ParseTag(string tag)
        {
            Version v;
            var s = (tag ?? "").Trim().TrimStart('v', 'V');
            if (!Version.TryParse(s, out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        /// <summary>Returns the latest release when it is newer than this build, otherwise null.</summary>
        public static async Task<UpdateInfo> CheckAsync()
        {
            var json = await Http.GetStringAsync("https://api.github.com/repos/" + AppInfo.Owner + "/" + AppInfo.Repo + "/releases/latest").ConfigureAwait(false);
            var o = (Dictionary<string, object>)Json.Parse(json);
            var tag = o["tag_name"] as string;
            var version = ParseTag(tag);
            if (version == null || version <= Current) return null;

            var info = new UpdateInfo
            {
                Version = version, Tag = tag,
                PageUrl = o.ContainsKey("html_url") ? o["html_url"] as string : AppInfo.ReleasesUrl,
                Notes = o.ContainsKey("body") ? o["body"] as string : null
            };
            if (o["assets"] is object[] assets)
            {
                foreach (var a in assets.OfType<Dictionary<string, object>>())
                {
                    var name = a["name"] as string;
                    var url = a["browser_download_url"] as string;
                    if (string.Equals(name, AppInfo.ExeName, StringComparison.OrdinalIgnoreCase)) info.ExeUrl = url;
                    else if (string.Equals(name, AppInfo.ExeName + ".sha256", StringComparison.OrdinalIgnoreCase)) info.ShaUrl = url;
                }
            }
            return info.ExeUrl == null ? null : info;
        }

        public static async Task<string> DownloadAsync(UpdateInfo u)
        {
            Directory.CreateDirectory(AppPaths.UpdateDir);
            var bytes = await Http.GetByteArrayAsync(u.ExeUrl).ConfigureAwait(false);
            if (bytes.Length < 32 * 1024 || bytes[0] != 'M' || bytes[1] != 'Z')
                throw new InvalidDataException("Downloaded file is not a Windows executable.");
            if (u.ShaUrl == null) throw new InvalidDataException("Release has no checksum file.");

            var expected = (await Http.GetStringAsync(u.ShaUrl).ConfigureAwait(false))
                .Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            string actual;
            using (var sha = SHA256.Create())
                actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checksum mismatch.");

            var file = Path.Combine(AppPaths.UpdateDir, "TrayKiller-" + u.Version + ".exe");
            File.WriteAllBytes(file, bytes);
            return file;
        }

        /// <summary>Swaps the running exe for the new one (a running exe can be renamed, not overwritten)
        /// and starts the new version; the caller must exit right after.</summary>
        public static void ApplyAndRestart(string newExe, string extraArgs = "")
        {
            var current = AppInfo.ExePath;
            var old = current + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            File.Move(current, old);
            try { File.Copy(newExe, current, true); }
            catch
            {
                File.Move(old, current);
                throw;
            }
            Process.Start(new ProcessStartInfo(current, "--updated --background" + extraArgs)
            {
                UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(current)
            });
        }

        public static void CleanupOld()
        {
            try { var old = AppInfo.ExePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
            try { if (Directory.Exists(AppPaths.UpdateDir)) Directory.Delete(AppPaths.UpdateDir, true); } catch { }
        }
    }
}
