using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Reflection;

namespace FurrycordInstaller
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
        }
    }

    public class LauncherForm : Form
    {
        private WebView2 _webView;
        private FurrycordBackend _backend;

        public LauncherForm()
        {
            this.Text = "Furrycord Installer";
            this.Size = new Size(740, 620); // Enlarged to prevent text clipping
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(11, 11, 24); // matching HTML root
            var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FurrycordInstaller.icon.ico");
            if (iconStream != null) this.Icon = new Icon(iconStream);

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.Transparent
            };
            this.Controls.Add(_webView);

            // Drag window from HTML
            _webView.NavigationCompleted += (s, e) => {
                _webView.CoreWebView2.WebMessageReceived += (sender, args) => {
                    if (args.TryGetWebMessageAsString() == "drag") {
                        ReleaseCapture();
                        SendMessage(this.Handle, 0xA1, 0x2, 0);
                    }
                };
            };

            InitializeWebView();
        }

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private async void InitializeWebView()
        {
            var userDataFolder = Path.Combine(Path.GetTempPath(), "FurrycordInstaller_WebView2");
            CoreWebView2Environment env;
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await _webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex) when (
                ex is System.Runtime.InteropServices.COMException ||
                ex.HResult == unchecked((int)0x80070002) ||
                ex.Message.Contains("WebView2") ||
                ex.Message.Contains("0x80070002")
            )
            {
                // WebView2 Runtime not installed on this machine
                MessageBox.Show(
                    "Microsoft Edge WebView2 Runtime is required to run the Furrycord Installer but was not found on your system.\n\n" +
                    "Please download and install it from:\nhttps://aka.ms/webview2\n\n" +
                    "After installing, restart the Furrycord Installer.",
                    "WebView2 Runtime Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                Application.Exit();
                return;
            }

            _backend = new FurrycordBackend(this, _webView);
            _webView.CoreWebView2.AddHostObjectToScript("backend", _backend);

            // Wrap COM proxy into exactly what index.html expects, and add drag support
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                window.furrycord = {
                    detectDiscord: async () => JSON.parse(await chrome.webview.hostObjects.backend.DetectDiscord()),
                    isInjected: async (path) => await chrome.webview.hostObjects.backend.IsInjected(path),
                    hasThirdPartyMod: async (path) => await chrome.webview.hostObjects.backend.HasThirdPartyMod(path),
                    inject: async (path) => JSON.parse(await chrome.webview.hostObjects.backend.Inject(path)),
                    startInject: (path) => JSON.parse(chrome.webview.hostObjects.sync.backend.StartInject(path)),
                    startRepair: (path) => JSON.parse(chrome.webview.hostObjects.sync.backend.StartRepair(path)),
                    startUninject: (path) => JSON.parse(chrome.webview.hostObjects.sync.backend.StartUninject(path)),
                    getInjectStatus: () => JSON.parse(chrome.webview.hostObjects.sync.backend.GetJobStatus()),
                    uninject: async (path) => JSON.parse(await chrome.webview.hostObjects.backend.Uninject(path)),
                    minimizeApp: () => chrome.webview.hostObjects.backend.MinimizeApp(),
                    closeApp: () => chrome.webview.hostObjects.backend.CloseApp(),
                    openUrl: (url) => chrome.webview.hostObjects.backend.OpenUrl(url)
                };

                // Add drag support to titlebar
                document.addEventListener('DOMContentLoaded', () => {
                    const titlebar = document.querySelector('.titlebar');
                    if (titlebar) {
                        titlebar.addEventListener('mousedown', (e) => {
                            if(e.target.tagName !== 'BUTTON' && !e.target.classList.contains('titlebar-title')) {
                                window.chrome.webview.postMessage('drag');
                            }
                        });
                    }
                });
            ");

            // Disable context menu and dev tools
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            // Load HTML from embedded resource or file
            string html;
            var htmlResourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FurrycordInstaller.index.html");
            if (htmlResourceStream != null)
            {
                using (var reader = new StreamReader(htmlResourceStream))
                {
                    html = reader.ReadToEnd();
                }
            }
            else
            {
                // Fallback to file if embedding fails
                var exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                var htmlPath = Path.Combine(exeDir, "index.html");
                html = File.ReadAllText(htmlPath);
            }

            // Convert Icon to Base64 PNG for HTML
            try {
                using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FurrycordInstaller.icon.ico"))
                {
                    var icon = new Icon(iconStream);
                    using (var bmp = icon.ToBitmap())
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        var base64 = Convert.ToBase64String(ms.ToArray());
                        html = html.Replace("{{ICON_BASE64}}", "data:image/png;base64," + base64);
                    }
                }
            } catch { }

            _webView.NavigateToString(html);
        }
    }

    [ComVisible(true)]
    public class FurrycordBackend
    {
        private LauncherForm _form;
        private WebView2 _webView;
        private HttpClient _http;
        private string _distDir;
        private string _exeDir;

        const string GITHUB_REPO = "ggfurrycord/furrycord";
        const string DIST_ZIP = "furrycord-dist.zip";

        public FurrycordBackend(LauncherForm form, WebView2 webView)
        {
            _form = form;
            _webView = webView;
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(30); // Prevent infinite hang on GitHub API
            _exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            _distDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Furrycord", "dist");
        }

        public void MinimizeApp() { _form.Invoke(new Action(() => _form.WindowState = FormWindowState.Minimized)); }
        public void CloseApp() { _form.Invoke(new Action(() => Application.Exit())); }
        public void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public void SetStatus(string type, string text)
        {
            _form.Invoke(new Action(() => {
                var safeText = text.Replace("'", "\\'").Replace("\n", " ");
                _webView.CoreWebView2.ExecuteScriptAsync($"if(typeof setStatus === 'function') setStatus('{type}', '{safeText}');");
            }));
        }

        public void SetProgress(double percent, string text, double mbDownloaded = -1, double mbTotal = -1)
        {
            AppendLog(text);
            _form.Invoke(new Action(() => {
                var safeText = text.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", " ");
                var percentStr = percent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                if (mbDownloaded >= 0 && mbTotal >= 0)
                {
                    var mbDlStr    = mbDownloaded.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    var mbTotalStr = mbTotal.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                    _webView.CoreWebView2.ExecuteScriptAsync($"if(typeof setLoading === 'function') setLoading(true, '{safeText}', {percentStr}, {mbDlStr}, {mbTotalStr});");
                }
                else
                {
                    _webView.CoreWebView2.ExecuteScriptAsync($"if(typeof setLoading === 'function') setLoading(true, '{safeText}', {percentStr});");
                }
            }));
        }

        public async Task<bool> IsInjected(string path)
        {
            return await Task.Run(() => {
                var appDir = System.IO.Path.Combine(path, "app");
                var pkgPath = System.IO.Path.Combine(appDir, "package.json");
                return Directory.Exists(appDir) && File.Exists(pkgPath) && File.ReadAllText(pkgPath).Contains("\"furrycord\"");
            });
        }

        /// <summary>
        /// Returns true if a third-party mod (Vencord, Equicord, OpenAsar) is detected
        /// but Furrycord is NOT yet injected.
        /// </summary>
        public async Task<bool> HasThirdPartyMod(string path)
        {
            return await Task.Run(() => {
                var appDir = System.IO.Path.Combine(path, "app");
                var pkgPath = System.IO.Path.Combine(appDir, "package.json");
                if (!Directory.Exists(appDir) || !File.Exists(pkgPath)) return false;
                var content = File.ReadAllText(pkgPath);
                // Already Furrycord → not a third-party mod
                if (content.Contains("\"furrycord\"")) return false;
                return content.Contains("vencord", StringComparison.OrdinalIgnoreCase)
                    || content.Contains("equicord", StringComparison.OrdinalIgnoreCase)
                    || content.Contains("openasar", StringComparison.OrdinalIgnoreCase);
            });
        }

        public void AppendLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _form.Invoke(new Action(() => {
                var safeText = text.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", " ");
                _webView.CoreWebView2.ExecuteScriptAsync($"if(typeof appendLog==='function')appendLog('{safeText}');");
            }));
        }

        /// <summary>Resolve dist folder containing patcher.js (bundled next to installer or from a prior build).</summary>
        private string FindLocalDistDir()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] candidates = {
                Path.Combine(_exeDir, "dist", "desktop"),
                Path.Combine(_exeDir, "dist"),
                Path.Combine(localAppData, "Furrycord", "dist", "desktop"),
                Path.Combine(localAppData, "Furrycord", "dist"),
            };

            foreach (var dir in candidates)
            {
                if (File.Exists(Path.Combine(dir, "patcher.js")))
                    return dir;
            }
            return null;
        }

        public async Task<string> DetectDiscord()
        {
            return await Task.Run(() => {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string[] channels = { "Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment" };
                string[] names = { "Discord", "Discord PTB", "Discord Canary", "Discord Dev" };

                var list = new List<object>();
                for (int i = 0; i < channels.Length; i++)
                {
                    var c = channels[i];
                    var dPath = Path.Combine(localAppData, c);
                    if (!Directory.Exists(dPath)) continue;

                    string latestDir = null;
                    Version latestVer = new Version(0, 0);

                    foreach (var dir in Directory.GetDirectories(dPath, "app-*"))
                    {
                        var verStr = Path.GetFileName(dir).Replace("app-", "");
                        if (!Version.TryParse(verStr, out var ver)) continue;
                        if (ver > latestVer)
                        {
                            latestVer = ver;
                            latestDir = dir;
                        }
                    }

                    if (latestDir == null) continue;

                    var resources = Path.Combine(latestDir, "resources");
                    if (!Directory.Exists(resources)) continue;

                    list.Add(new {
                        name = names[i],
                        path = resources,
                        asarPath = Path.Combine(resources, "app.asar"),
                        version = latestVer.ToString()
                    });
                }
                return JsonSerializer.Serialize(list);
            });
        }

        // Shared async job state (inject / repair / uninject)
        private string _jobState = "idle"; // idle | running | done | error
        private string _jobError = "";
        private string _jobAction = "";

        private string StartJob(string action, string resourcesPath, Func<Task> work)
        {
            if (_jobState == "running")
                return JsonSerializer.Serialize(new { started = false, error = "Already running" });

            _jobState = "running";
            _jobError = "";
            _jobAction = action;

            Task.Run(async () =>
            {
                try
                {
                    await work();
                    _jobState = "done";
                }
                catch (Exception ex)
                {
                    _jobError = ex.Message;
                    _jobState = "error";
                }
            });

            return JsonSerializer.Serialize(new { started = true });
        }

        public string StartInject(string resourcesPath)
        {
            return StartJob("inject", resourcesPath, async () =>
            {
                await EnsureDistAsync();
                await Task.Run(() => {
                    DoInject(resourcesPath);
                    EnsureBrandingAssets();
                    CreateDesktopShortcut(resourcesPath);
                });
            });
        }

        public string StartRepair(string resourcesPath)
        {
            return StartJob("repair", resourcesPath, async () =>
            {
                await EnsureDistAsync();
                await Task.Run(() => {
                    DoRepair(resourcesPath);
                    EnsureBrandingAssets();
                    CreateDesktopShortcut(resourcesPath);
                });
            });
        }

        public string StartUninject(string resourcesPath)
        {
            return StartJob("uninject", resourcesPath, async () =>
            {
                await Task.Run(() => DoUninject(resourcesPath));
            });
        }

        public string GetJobStatus()
        {
            return JsonSerializer.Serialize(new
            {
                state = _jobState,
                error = _jobError,
                action = _jobAction
            });
        }

        // Legacy aliases
        public string GetInjectStatus() => GetJobStatus();

        // Keep old Inject for compat but it just delegates
        public async Task<string> Inject(string resourcesPath)
        {
            try {
                await EnsureDistAsync();
                await Task.Run(() => DoInject(resourcesPath));
                return JsonSerializer.Serialize(new { success = true });
            } catch (Exception ex) {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        public async Task<string> Uninject(string resourcesPath)
        {
            try {
                await Task.Run(() => DoUninject(resourcesPath));
                return JsonSerializer.Serialize(new { success = true });
            } catch (Exception ex) {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        private async Task EnsureDistAsync()
        {
            var local = FindLocalDistDir();
            if (local != null)
            {
                _distDir = local;
                SetProgress(10, "Using bundled Furrycord files.");
                return;
            }

            // Already extracted to AppData from a previous install
            if (File.Exists(Path.Combine(_distDir, "patcher.js")))
            {
                SetProgress(10, "Using existing Furrycord installation files.");
                return;
            }

            SetProgress(2, "No local files found — checking GitHub for updates...");
            Directory.CreateDirectory(_distDir);

            var apiUrl = $"https://api.github.com/repos/{GITHUB_REPO}/releases/latest";
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("User-Agent", "Furrycord-Installer/2.0");
            _http.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            string json;
            try
            {
                json = await _http.GetStringAsync(apiUrl);
            }
            catch (TaskCanceledException)
            {
                throw new Exception(
                    "Download timed out. Place a 'dist' folder next to this installer (run pnpm run buildStandalone first), then try again."
                );
            }
            catch (HttpRequestException ex)
            {
                var hint = ex.Message.Contains("404")
                    ? "No GitHub release published yet. Build locally: run 'pnpm run buildStandalone' then 'build-installer.ps1' — the dist folder is bundled automatically."
                    : "Check your internet connection.";
                throw new Exception($"Could not download Furrycord: {ex.Message}. {hint}");
            }

            var zipUrl = ExtractJsonValue(json, "browser_download_url", DIST_ZIP);
            
            if (string.IsNullOrEmpty(zipUrl))
                throw new Exception($"'{DIST_ZIP}' not found in the GitHub release. The release may not be published yet.");

            SetProgress(5, "Starting download...");
            var tmpZip = Path.Combine(Path.GetTempPath(), "furrycord-dist.zip");
            
            using (var response = await _http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? (long)(348.0 * 1024 * 1024); // Fallback to 348MB if null
                
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fs = new FileStream(tmpZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    double lastReportedPercent = 0;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fs.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        
                        double percent = (double)totalRead / totalBytes * 100.0;
                        if (percent - lastReportedPercent >= 0.5 || percent >= 100.0)
                        {
                            lastReportedPercent = percent;
                            // Map 0-100% of download to 5%-75% of overall progress
                            double overallPercent = 5.0 + (percent * 0.70);
                            double totalMB = (double)totalBytes / (1024.0 * 1024.0);
                            double readMB  = (double)totalRead  / (1024.0 * 1024.0);
                            SetProgress(overallPercent, "Downloading Furrycord...", readMB, totalMB);
                        }
                    }
                }
            }

            SetProgress(75, "Preparing extraction...");
            await Task.Run(() => 
            {
                if (Directory.Exists(_distDir)) Directory.Delete(_distDir, true);
                Directory.CreateDirectory(_distDir);

                // Normalize _distDir with a guaranteed trailing separator for safe StartsWith checks
                var normalizedDistDir = _distDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                                + Path.DirectorySeparatorChar;

                using (var archive = ZipFile.OpenRead(tmpZip))
                {
                    int totalEntries = archive.Entries.Count;
                    int extractedEntries = 0;
                    double lastReportedPercent = 0;

                    foreach (var entry in archive.Entries)
                    {
                        // Normalize the zip entry path: replace forward slashes, strip leading slashes
                        var entryPath = entry.FullName
                            .Replace('/', Path.DirectorySeparatorChar)
                            .TrimStart(Path.DirectorySeparatorChar);

                        // Reject any entry that tries to traverse upward (e.g. ../../evil)
                        if (entryPath.Contains(".."))
                            continue;

                        var fullPath = Path.GetFullPath(Path.Combine(_distDir, entryPath));

                        // Security check: ensure resolved path stays inside _distDir
                        if (!fullPath.StartsWith(normalizedDistDir, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                        {
                            Directory.CreateDirectory(fullPath);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                            entry.ExtractToFile(fullPath, true);
                        }

                        extractedEntries++;
                        double percent = (double)extractedEntries / totalEntries * 100.0;
                        if (percent - lastReportedPercent >= 1.0 || percent >= 100.0)
                        {
                            lastReportedPercent = percent;
                            // Map 0-100% of extraction to 75%-90% of overall progress
                            double overallPercent = 75.0 + (percent * 0.15);
                            SetProgress(overallPercent, $"Extracting files ({extractedEntries}/{totalEntries})...");
                        }
                    }
                }
                
                try { File.Delete(tmpZip); } catch { }
            });
        }

        private void DoInject(string resPath)
        {
            var appDir = Path.Combine(resPath, "app");
            var backup = Path.Combine(resPath, "_app.asar");
            var appAsar = Path.Combine(resPath, "app.asar");

            SetProgress(90, "Closing Discord...");
            KillDiscord(resPath);

            SetProgress(91, "Removing previous mod injection (Vencord / Equicord / OpenAsar)...");
            // ── NETTOYAGE COMPLET DE TOUTE INJECTION PRÉCÉDENTE ──────────────────────
            // 1. Supprimer le dossier app/ quel que soit le mod qui l'a créé
            if (Directory.Exists(appDir))
            {
                // Déjà Furrycord → on réinjecte proprement sans bail-out partiel
                try { Directory.Delete(appDir, true); } catch { }
            }

            // 2. Supprimer tout app.asar faux (< 2 MB) créé par Vencord/OpenAsar/Equicord
            //    Le vrai app.asar Discord fait entre 40 MB et 80 MB.
            if (File.Exists(appAsar) && new FileInfo(appAsar).Length < 2_000_000)
            {
                File.Delete(appAsar);
            }

            // 3. Chercher un backup fait par un mod tiers (Vencord utilise _app.asar,
            //    Equicord aussi, OpenAsar utilise original_app.asar)
            string[] thirdPartyBackups = { "_app.asar", "original_app.asar", "app.asar.bak" };
            foreach (var bkName in thirdPartyBackups)
            {
                var bkPath = Path.Combine(resPath, bkName);
                // C'est un vrai backup si sa taille est > 2 MB
                if (File.Exists(bkPath) && new FileInfo(bkPath).Length > 2_000_000)
                {
                    // Si app.asar est absent ou corrompu, restaurer ce backup en tant que app.asar
                    if (!File.Exists(appAsar) || new FileInfo(appAsar).Length < 2_000_000)
                    {
                        if (File.Exists(appAsar)) File.Delete(appAsar);
                        // Copier (pas déplacer) pour ne pas perdre le backup si une erreur survient
                        File.Copy(bkPath, appAsar);
                    }
                    break;
                }
            }

            // 4. Nettoyer les patches dans discord_desktop_core que Vencord/Equicord injectent
            //    dans splashScreen.js et app_bootstrap (cause les settings parasites)
            CleanModulePatches(resPath);

            SetProgress(92, "Configuring Furrycord loader...");

            // Vérification finale : on doit avoir un vrai app.asar ou un backup avant de continuer
            if (!File.Exists(appAsar) && !File.Exists(backup))
            {
                throw new Exception(
                    "Critical error: no valid app.asar found. " +
                    "Please reinstall Discord from discord.com/download and try again."
                );
            }

            // Créer le backup Furrycord si app.asar existe et qu'on n'a pas encore de backup
            if (File.Exists(appAsar))
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(appAsar, backup);
            }

            SetProgress(94, "Creating app directory...");
            Directory.CreateDirectory(appDir);
            WriteLoader(appDir);
            CopyAssetsToDiscord(resPath);
            SetProgress(99, "Starting Discord...");
            StartDiscord(resPath);
            SetProgress(100, "Done!");
        }

        private void DoRepair(string resPath)
        {
            SetProgress(5, "Repairing Furrycord installation...");
            KillDiscord(resPath);
            CleanModulePatches(resPath);

            var appDir = Path.Combine(resPath, "app");
            if (Directory.Exists(appDir))
            {
                try { Directory.Delete(appDir, true); } catch { }
            }

            DoInject(resPath);
        }

        private void EnsureBrandingAssets()
        {
            try
            {
                var brandDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Furrycord");
                Directory.CreateDirectory(brandDir);
                var iconDest = Path.Combine(brandDir, "furrycord.ico");

                using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FurrycordInstaller.icon.ico"))
                {
                    if (iconStream != null)
                    {
                        using var fs = new FileStream(iconDest, FileMode.Create, FileAccess.Write);
                        iconStream.CopyTo(fs);
                    }
                }

                var iconFromDist = Path.Combine(_distDir, "furrycord.ico");
                if (File.Exists(iconFromDist))
                    File.Copy(iconFromDist, iconDest, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Furrycord] Branding warning: {ex.Message}");
            }
        }

        private void CreateDesktopShortcut(string resPath)
        {
            try
            {
                var appBase = Path.GetDirectoryName(resPath);
                var procName = resPath.Contains("DiscordPTB") ? "DiscordPTB.exe" :
                               resPath.Contains("DiscordCanary") ? "DiscordCanary.exe" :
                               resPath.Contains("DiscordDevelopment") ? "DiscordDevelopment.exe" : "Discord.exe";
                var channelLabel = resPath.Contains("DiscordPTB") ? "PTB" :
                                   resPath.Contains("DiscordCanary") ? "Canary" :
                                   resPath.Contains("DiscordDevelopment") ? "Dev" : "Stable";
                var discordExe = Path.Combine(appBase, procName);
                if (!File.Exists(discordExe)) return;

                var iconPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Furrycord", "furrycord.ico");
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var shortcutPath = Path.Combine(desktop, $"Furrycord ({channelLabel}).lnk");

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = discordExe;
                shortcut.WorkingDirectory = appBase;
                shortcut.Description = $"Discord with Furrycord ({channelLabel})";
                if (File.Exists(iconPath))
                    shortcut.IconLocation = iconPath + ",0";
                shortcut.Save();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Furrycord] Shortcut warning: {ex.Message}");
            }
        }

        /// <summary>
        /// Nettoie les patches laissés par Vencord/Equicord dans les modules natifs Discord.
        /// Ces patches (dans discord_desktop_core, etc.) font que les settings Discord affichent
        /// encore l'interface Vencord/Equicord même après suppression de leur dossier app/.
        /// </summary>
        private void CleanModulePatches(string resPath)
        {
            try
            {
                // Chemins où Vencord/Equicord injectent leurs hooks
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appBase = Path.GetDirectoryName(resPath);

                // Chercher le dossier modules (dans app-X.X.XXXX/modules/)
                string[] modulesSearchPaths = {
                    Path.Combine(appBase, "modules"),
                    Path.Combine(resPath, "modules")
                };

                foreach (var modulesDir in modulesSearchPaths)
                {
                    if (!Directory.Exists(modulesDir)) continue;

                    // Chercher discord_desktop_core-*/discord_desktop_core/
                    foreach (var coreParent in Directory.GetDirectories(modulesDir, "discord_desktop_core*"))
                    {
                        var corePath = Path.Combine(coreParent, "discord_desktop_core");
                        if (!Directory.Exists(corePath)) continue;

                        // Fichiers patchés par Vencord/Equicord dans discord_desktop_core
                        string[] patchedFiles = {
                            Path.Combine(corePath, "index.js"),
                            Path.Combine(corePath, "app", "app_bootstrap", "splashScreen.js"),
                            Path.Combine(corePath, "app", "app_bootstrap", "index.js"),
                        };

                        foreach (var pf in patchedFiles)
                        {
                            if (!File.Exists(pf)) continue;
                            var content = File.ReadAllText(pf);

                            // Détecter la présence d'une injection Vencord/Equicord dans ce fichier
                            bool isPatched = content.Contains("vencord", StringComparison.OrdinalIgnoreCase)
                                         || content.Contains("equicord", StringComparison.OrdinalIgnoreCase)
                                         || content.Contains("require(\"vencord")
                                         || content.Contains("require('vencord")
                                         || content.Contains("VencordNative")
                                         || content.Contains("equilotl");

                            if (!isPatched) continue;

                            // Chercher un backup .orig ou .bak laissé par le mod
                            string[] backupExts = { ".orig", ".bak", ".vanilla" };
                            bool restored = false;
                            foreach (var ext in backupExts)
                            {
                                var bk = pf + ext;
                                if (File.Exists(bk))
                                {
                                    File.Copy(bk, pf, true);
                                    File.Delete(bk);
                                    restored = true;
                                    break;
                                }
                            }

                            if (!restored)
                            {
                                // Pas de backup → supprimer le fichier patché.
                                // Discord le recrée au prochain démarrage depuis app.asar.
                                try { File.Delete(pf); } catch { }
                            }
                        }

                        // Supprimer le dossier app/ à l'intérieur de discord_desktop_core si injecté
                        var innerAppDir = Path.Combine(corePath, "app");
                        if (Directory.Exists(innerAppDir))
                        {
                            var innerPkg = Path.Combine(innerAppDir, "package.json");
                            if (File.Exists(innerPkg))
                            {
                                var pkgContent = File.ReadAllText(innerPkg);
                                bool isModInjection = pkgContent.Contains("vencord", StringComparison.OrdinalIgnoreCase)
                                                   || pkgContent.Contains("equicord", StringComparison.OrdinalIgnoreCase)
                                                   || pkgContent.Contains("openasar", StringComparison.OrdinalIgnoreCase);
                                if (isModInjection)
                                {
                                    try { Directory.Delete(innerAppDir, true); } catch { }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Non-fatal : on log et on continue
                Console.WriteLine($"[Furrycord] CleanModulePatches warning: {ex.Message}");
            }
        }

        private void DoUninject(string resPath)
        {
            var appDir = Path.Combine(resPath, "app");
            var backup = Path.Combine(resPath, "_app.asar");
            var appAsar = Path.Combine(resPath, "app.asar");

            SetProgress(10, "Closing Discord...");
            KillDiscord(resPath);

            SetProgress(30, "Removing injected folder...");
            // 1. Remove the injected 'app' folder
            if (Directory.Exists(appDir))
            {
                var pkg = Path.Combine(appDir, "package.json");
                if (File.Exists(pkg) && File.ReadAllText(pkg).Contains("\"furrycord\""))
                {
                    Directory.Delete(appDir, true);
                }
            }

            SetProgress(50, "Restoring original files...");
            // Nettoyage des faux app.asar avant de restaurer
            if (File.Exists(appAsar) && new FileInfo(appAsar).Length < 1000000) {
                File.Delete(appAsar);
            }

            // 2. Restore app.asar backup
            if (File.Exists(backup))
            {
                if (!File.Exists(appAsar)) {
                    File.Move(backup, appAsar);
                } else {
                    // S'il y a un vrai app.asar (taille > 1Mo), Discord s'est mis à jour, l'ancien backup est obsolète
                    File.Delete(backup); 
                }
            }

            SetProgress(70, "Cleaning up assets...");
            // 3. Clean up Furrycord-specific assets (folders we added)
            // We DON'T delete ffmpeg.dll because it's a native Discord file!
            var appBase = Path.GetDirectoryName(resPath);
            
            // Revert build_info.json patch
            var buildInfoPath = Path.Combine(resPath, "build_info.json");
            if (File.Exists(buildInfoPath)) {
                try {
                    var json = File.ReadAllText(buildInfoPath);
                    if (json.Contains("\"localModulesRoot\"")) {
                        // Simple regex to remove the line
                        json = Regex.Replace(json, @",\s*""localModulesRoot""\s*:\s*""modules""\s*", "");
                        File.WriteAllText(buildInfoPath, json);
                    }
                } catch { }
            }

            string[] filesToClean = { "node.exe", "yt-dlp.exe", "ffmpeg.exe" }; // safe to delete as Discord doesn't have these
            foreach (var f in filesToClean) { 
                var p = Path.Combine(appBase, f); 
                if (File.Exists(p)) try { File.Delete(p); } catch { } 
            }

            string[] dirsToClean = { "mac", "multi-instance-icons", "ghost-server" };
            foreach (var dir in dirsToClean) { 
                var p = Path.Combine(appBase, dir); 
                if (Directory.Exists(p)) try { Directory.Delete(p, true); } catch { } 
            }

            SetProgress(95, "Restarting Discord...");
            // Relancer Discord proprement après la désinstallation
            StartDiscord(resPath);
            SetProgress(100, "Done!");
        }

        private void WriteLoader(string appDir)
        {
            // CRITICAL: use a relative path from appDir to patcher.js
            // An absolute path breaks when the user's appdata path differs (e.g. Canary after update)
            // appDir = {resources}\app\
            // _distDir = %LOCALAPPDATA%\Furrycord\dist\
            // We must use the absolute path BUT with forward slashes and proper escaping for require()
            // The path is correct at install time; it only breaks if the user moves AppData.
            // Real fix: use path.join(__dirname, ...) relative navigation from patcher.js location.
            //
            // Strategy: write index.js so it resolves patcher.js relative to _distDir stored in a sibling file,
            // OR simply use the absolute path correctly escaped. The Canary bug is NOT the path —
            // it's that the dist folder may not exist yet when Canary loads. We add an existence check.
            var patcher = Path.Combine(_distDir, "patcher.js").Replace("\\", "/");
            File.WriteAllText(Path.Combine(appDir, "package.json"), "{\"name\":\"furrycord\",\"main\":\"index.js\"}");
            File.WriteAllText(Path.Combine(appDir, "index.js"),
                $"// Furrycord Injector\n" +
                $"\"use strict\";\n" +
                $"const fs = require('fs');\n" +
                $"const path = require('path');\n" +
                $"// Primary: injected dist path\n" +
                $"const primary = {JsonEscape(patcher)};\n" +
                $"// Fallback: dist/ folder next to the exe (portable mode)\n" +
                $"const exeDir = path.dirname(process.execPath);\n" +
                $"const fallback = path.join(exeDir, 'resources', 'dist', 'patcher.js');\n" +
                $"const fallback2 = path.join(exeDir, 'dist', 'patcher.js');\n" +
                $"const patcherPath = fs.existsSync(primary) ? primary : fs.existsSync(fallback) ? fallback : fallback2;\n" +
                $"if (!fs.existsSync(patcherPath)) throw new Error('[Furrycord] patcher.js not found. Expected at: ' + primary);\n" +
                $"require(patcherPath);\n"
            );
        }

        private string JsonEscape(string s)
        {
            // Produce a JSON string literal: "value" with proper escaping
            return System.Text.Json.JsonSerializer.Serialize(s);
        }

        private void CopyAssetsToDiscord(string resPath)
        {
            SetProgress(95, "Copying binaries...");
            var appBase = Path.GetDirectoryName(resPath);

            string[] filesToCopy = { "ffmpeg.exe", "ffmpeg.dll", "node.exe", "yt-dlp.exe" };
            foreach (var f in filesToCopy) {
                var src = Path.Combine(_distDir, f);
                if (File.Exists(src)) File.Copy(src, Path.Combine(appBase, f), true);
            }

            SetProgress(96, "Copying directories...");
            string[] dirsToCopy = { "mac", "multi-instance-icons", "modules", "ghost-server" };
            foreach (var dir in dirsToCopy) {
                var src = Path.Combine(_distDir, dir);
                if (Directory.Exists(src)) CopyDirectory(src, Path.Combine(appBase, dir));
            }

            SetProgress(98, "Patching build info...");
            var buildInfoPath = Path.Combine(resPath, "build_info.json");
            if (File.Exists(buildInfoPath)) {
                try {
                    var json = File.ReadAllText(buildInfoPath);
                    if (!json.Contains("\"localModulesRoot\"")) {
                        var idx = json.LastIndexOf('}');
                        if (idx != -1) {
                            json = json.Insert(idx, ",\n  \"localModulesRoot\": \"modules\"\n");
                            File.WriteAllText(buildInfoPath, json);
                        }
                    }
                } catch { }
            }
        }

        private void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
            foreach (var directory in Directory.GetDirectories(sourceDir))
                CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }

        private void KillDiscord(string resPath)
        {
            SetStatus("loading", "Closing Discord...");
            var procName = resPath.Contains("DiscordPTB") ? "DiscordPTB" :
                           resPath.Contains("DiscordCanary") ? "DiscordCanary" :
                           resPath.Contains("DiscordDevelopment") ? "DiscordDevelopment" : "Discord";
            foreach (var process in Process.GetProcessesByName(procName))
            {
                try { process.Kill(); process.WaitForExit(3000); } catch { }
            }
            System.Threading.Thread.Sleep(1000);
        }

        private void StartDiscord(string resPath)
        {
            try {
                var exe = Path.Combine(Path.GetDirectoryName(resPath), "..", "Update.exe");
                var procName = resPath.Contains("DiscordPTB") ? "DiscordPTB.exe" :
                               resPath.Contains("DiscordCanary") ? "DiscordCanary.exe" :
                               resPath.Contains("DiscordDevelopment") ? "DiscordDevelopment.exe" : "Discord.exe";
                               
                if (File.Exists(exe)) Process.Start(exe, $"--processStart {procName}");
            } catch { }
        }

        private string ExtractJsonValue(string json, string key, string matchPattern = null)
        {
            var matches = Regex.Matches(json, $"\"{key}\"\\s*:\\s*\"([^\"]+)\"");
            foreach (Match m in matches)
            {
                var val = m.Groups[1].Value;
                if (matchPattern == null || val.EndsWith(matchPattern)) return val;
            }
            return null;
        }
    }
}
