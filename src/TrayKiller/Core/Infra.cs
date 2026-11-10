using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TrayKiller.Core
{
    public static class Log
    {
        static readonly object Gate = new object();

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg, Exception ex = null) => Write("ERROR", ex == null ? msg : msg + " :: " + ex);

        static void Write(string level, string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.LogDir);
                    var file = Path.Combine(AppPaths.LogDir, "traykiller-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level + "] " + msg + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* logging must never throw */ }
        }

        public static void Prune(int keepDays)
        {
            try
            {
                foreach (var f in Directory.GetFiles(AppPaths.LogDir, "traykiller-*.log")
                             .Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-keepDays)))
                    File.Delete(f);
            }
            catch { }
        }
    }

    public static class Json
    {
        static JavaScriptSerializer New() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };

        public static string Serialize(object o) => Indent(New().Serialize(o));
        public static T Deserialize<T>(string s) => New().Deserialize<T>(s);
        public static object Parse(string s) => New().DeserializeObject(s);

        public static T Load<T>(string path) where T : class, new()
        {
            try
            {
                if (File.Exists(path))
                {
                    var t = Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
                    if (t != null) return t;
                }
            }
            catch (Exception ex)
            {
                Log.Error("JSON load failed: " + path, ex);
                try { File.Copy(path, path + ".corrupt", true); } catch { }
            }
            return new T();
        }

        public static void Save(string path, object o)
        {
            var text = Serialize(o);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
        }

        static string Indent(string json)
        {
            var sb = new StringBuilder(json.Length + json.Length / 4);
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) sb.Append(json[++i]);
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        depth++; NewLine(sb, depth); break;
                    case '}':
                    case ']': depth--; NewLine(sb, depth); sb.Append(c); break;
                    case ',': sb.Append(c); NewLine(sb, depth); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        static void NewLine(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            sb.Append(' ', Math.Max(0, depth) * 2);
        }
    }
}
