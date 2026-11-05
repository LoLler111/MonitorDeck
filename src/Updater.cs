using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MonitorDeck
{
    class ReleaseInfo
    {
        public Version Version;
        public string Tag, Notes, ExeUrl, ShaUrl, PageUrl;
        public string VersionText { get { return Updater.Format(Version); } }
    }

    // Aggiornamenti dalle release GitHub: controlla l'ultima versione, scarica MonitorDeck.exe,
    // verifica l'impronta SHA-256 e sostituisce l'exe in uso (rinominandolo in .old).
    static class Updater
    {
        const string ExeAsset = "MonitorDeck.exe";
        const string ShaAsset = "MonitorDeck.exe.sha256";

        public static Version Current { get { return Parse(AppInfo.Version); } }
        public static string ReleasesUrl { get { return AppInfo.RepoUrl + "/releases"; } }
        public static string ChangelogUrl { get { return AppInfo.RepoUrl + "/blob/main/CHANGELOG.md"; } }

        public static string Format(Version v)
        {
            if (v == null) return "?";
            return v.Build >= 0 ? v.Major + "." + v.Minor + "." + v.Build : v.Major + "." + v.Minor;
        }

        public static Version Parse(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            string t = tag.Trim().TrimStart('v', 'V');
            int dash = t.IndexOfAny(new char[] { '-', '+' });
            if (dash >= 0) t = t.Substring(0, dash);
            Version v;
            if (!Version.TryParse(t, out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            WebClient wc = new WebClient();
            wc.Encoding = Encoding.UTF8;
            wc.Headers[HttpRequestHeader.UserAgent] = "MonitorDeck-Updater/" + AppInfo.Version;
            return wc;
        }

        // null = nessuna release pubblicata
        public static ReleaseInfo FetchLatest()
        {
            string json;
            try
            {
                using (WebClient wc = Client())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = wc.DownloadString("https://api.github.com/repos/" + AppInfo.RepoOwner + "/" + AppInfo.RepoName + "/releases/latest");
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse r = ex.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.NotFound) return null;
                throw;
            }

            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) throw new Exception("Risposta di GitHub non valida");
            ReleaseInfo info = new ReleaseInfo();
            info.Tag = Str(d, "tag_name");
            info.Version = Parse(info.Tag);
            info.Notes = Str(d, "body");
            info.PageUrl = Str(d, "html_url") ?? ReleasesUrl;
            object[] assets = d.ContainsKey("assets") ? d["assets"] as object[] : null;
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Str(a, "name");
                    if (string.Equals(name, ExeAsset, StringComparison.OrdinalIgnoreCase)) info.ExeUrl = Str(a, "browser_download_url");
                    else if (string.Equals(name, ShaAsset, StringComparison.OrdinalIgnoreCase)) info.ShaUrl = Str(a, "browser_download_url");
                }
            }
            if (info.Version == null) throw new Exception("Versione della release non riconosciuta: " + info.Tag);
            return info;
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object o;
            return d.TryGetValue(k, out o) ? o as string : null;
        }

        public static string Download(ReleaseInfo r)
        {
            if (string.IsNullOrEmpty(r.ExeUrl)) throw new Exception("La release non contiene " + ExeAsset + ".");
            string exe = Application.ExecutablePath;
            string tmp = exe + ".download";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (WebClient wc = Client()) wc.DownloadFile(r.ExeUrl, tmp);

            byte[] data = File.ReadAllBytes(tmp);
            if (data.Length < 10000 || data[0] != 'M' || data[1] != 'Z')
            {
                File.Delete(tmp);
                throw new Exception("Il file scaricato non è un eseguibile valido.");
            }
            if (!string.IsNullOrEmpty(r.ShaUrl))
            {
                string expected;
                using (WebClient wc = Client()) expected = wc.DownloadString(r.ShaUrl);
                expected = expected.Trim().Split(new char[] { ' ', '\t', '*', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
                string actual;
                using (SHA256 sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
                if (actual != expected)
                {
                    File.Delete(tmp);
                    throw new Exception("L'impronta SHA-256 del file scaricato non corrisponde.");
                }
            }
            return tmp;
        }

        // Sostituisce l'exe in uso e avvia la nuova versione, che aspetta la chiusura di questa.
        public static void ReplaceAndRestart(string downloaded)
        {
            string exe = Application.ExecutablePath;
            string old = exe + ".old";
            try { if (File.Exists(old)) File.Delete(old); }
            catch { old = exe + "." + DateTime.Now.Ticks + ".old"; }
            File.Move(exe, old);
            try { File.Move(downloaded, exe); }
            catch { File.Move(old, exe); throw; }
            ProcessStartInfo psi = new ProcessStartInfo(exe, "--updated");
            psi.WorkingDirectory = Path.GetDirectoryName(exe);
            psi.UseShellExecute = false;
            Process.Start(psi);
        }

        // All'avvio: elimina l'exe della versione precedente (puo' essere ancora bloccato per qualche istante).
        public static void CleanupOld()
        {
            Thread t = new Thread(delegate()
            {
                try
                {
                    string exe = Application.ExecutablePath;
                    string dir = Path.GetDirectoryName(exe);
                    string name = Path.GetFileName(exe);
                    for (int i = 0; i < 20; i++)
                    {
                        bool left = false;
                        foreach (string f in Directory.GetFiles(dir, name + "*.old"))
                        {
                            try { File.Delete(f); } catch { left = true; }
                        }
                        if (!left) break;
                        Thread.Sleep(500);
                    }
                    string dl = exe + ".download";
                    if (File.Exists(dl)) File.Delete(dl);
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        // Le note di rilascio sono in Markdown: le rendo leggibili come testo semplice.
        public static string NotesToText(string md)
        {
            if (string.IsNullOrEmpty(md)) return "Nessuna nota di rilascio.";
            StringBuilder sb = new StringBuilder();
            foreach (string raw in md.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                string t = line.TrimStart();
                t = Regex.Replace(t, @"\[([^\]]+)\]\([^)]+\)", "$1");
                t = t.Replace("**", "").Replace("`", "");
                if (t.StartsWith("#"))
                {
                    t = t.TrimStart('#').Trim();
                    if (sb.Length > 0) sb.Append("\r\n");
                    sb.Append(t.ToUpperInvariant()).Append("\r\n");
                }
                else if (t.StartsWith("- ") || t.StartsWith("* "))
                {
                    int indent = line.Length - line.TrimStart().Length;
                    sb.Append(new string(' ', 2 + indent)).Append("•  ").Append(t.Substring(2).Trim()).Append("\r\n");
                }
                else sb.Append(t).Append("\r\n");
            }
            return sb.ToString().Trim();
        }
    }
}
