using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.IO.Pipes;
using LocalCloud.Core;
using Microsoft.Win32;
namespace LocalCloud.Desktop;

internal static class Program
{
    [STAThread] static void Main(string[] args) { ApplicationConfiguration.Initialize(); if (Path.GetFileName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Equals("payload",StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(AppContext.BaseDirectory, "release.json"))) { MessageBox.Show("Это временная папка обновления. Запустите Update-LocalCloud.cmd, выберите установленную папку и затем откройте LocalCloud.exe именно оттуда.", "LocalCloud"); return; } using var mutex = new Mutex(true, "LocalCloud.Desktop", out var first); if (!first) { LocalControl.Send("desktop", AppContext.BaseDirectory, "open").GetAwaiter().GetResult(); return; } using var app = new TrayApp(args.Contains("--background")); if (!app.Cancelled) Application.Run(app); }
}
sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon tray = new(); Process? server; AppSettings settings; readonly string config = Environment.GetEnvironmentVariable("LOCALCLOUD_CONFIG") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCloud", "settings.json"); bool exiting; bool stopping; bool restarting; readonly SemaphoreSlim stopGate = new(1); MainWindow? window;
    readonly CancellationTokenSource controlCancellation = new(); readonly Control dispatcher = new();
    public bool Cancelled { get; private set; }
    public TrayApp(bool background)
    {
        var first = !File.Exists(config); settings = first ? new AppSettings(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "LocalCloud")) : JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(config))!;
        if (first && !Configure(true)) { Cancelled = true; return; }
        dispatcher.CreateControl(); _ = ListenForExit(controlCancellation.Token);
        tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; tray.Text = "LocalCloud 1.0.0"; tray.Visible = true;
        var menu = new ContextMenuStrip(); menu.Items.Add("LocalCloud").Enabled = false; menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Открыть LocalCloud", null, (_, _) => OpenWindow()); menu.Items.Add("Открыть в браузере", null, (_, _) => OpenBrowser()); menu.Items.Add("Открыть папку", null, (_, _) => Open(settings.StorageRoot)); menu.Items.Add("Адрес сервера", null, (_, _) => { var addresses = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address)).Select(a => $"http://{a.Address}:{settings.Port}"); MessageBox.Show(string.Join("\n", addresses), "Открыть на телефоне"); });
        menu.Items.Add("Активные загрузки", null, (_, _) => OpenWindow("/?uploads=1")); menu.Items.Add("Настройки", null, async (_, _) => { if (Configure(false)) { await StopAsync(); window?.Dispose(); window = null; Start(); OpenBrowserWhenReady(); } }); menu.Items.Add("Разрешить домашнюю сеть", null, (_, _) => { var script = Path.Combine(AppContext.BaseDirectory, "scripts", "firewall.ps1"); if (File.Exists(script)) { try { var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas" }; foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Port", settings.Port.ToString() }) start.ArgumentList.Add(arg); Process.Start(start); } catch (System.ComponentModel.Win32Exception) { MessageBox.Show("Разрешение не изменено. Можно разрешить LocalCloud в настройках брандмауэра Windows.", "LocalCloud"); } } }); menu.Items.Add("Открыть логи", null, (_, _) => Open(Path.Combine(settings.StorageRoot, ".localcloud", "logs"))); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Запустить сервер", null, (_, _) => Start()); menu.Items.Add("Перезапустить сервер", null, async (_, _) => await Restart()); menu.Items.Add("Остановить сервер", null, async (_, _) => await StopAsync()); menu.Items.Add("Выход", null, async (_, _) => await Shutdown()); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => OpenWindow();
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if (settings.LaunchAtStartup) key.SetValue("LocalCloud", $"\"{Application.ExecutablePath}\" --background"); }
        Start(); CheckServerReady(!background || first);
    }
    bool Configure(bool first)
    {
        // Read current settings written by the web client before opening the native settings dialog.
        if (File.Exists(config)) settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(config))!;
        using var dialog = new SetupForm(settings, first); if (dialog.ShowDialog() != DialogResult.OK) return false;
        var next = settings with { StorageRoot = dialog.StorageRoot, LaunchAtStartup = dialog.LaunchAtStartup, AllowLan = dialog.AllowLan }; Directory.CreateDirectory(next.StorageRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!); File.WriteAllText(config + ".tmp", JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true })); File.Move(config + ".tmp", config, true); settings = next;
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); if (next.LaunchAtStartup) key.SetValue("LocalCloud", $"\"{Application.ExecutablePath}\" --background"); else key.DeleteValue("LocalCloud", false); return true;
    }
    void Start()
    {
        if (exiting || stopping || server != null && !server.HasExited) return;
        if (File.Exists(config)) settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(config))!;
        var exe = Path.Combine(AppContext.BaseDirectory, "server", "LocalCloud.Server.exe");
        if (!File.Exists(exe)) { MessageBox.Show("Не найдена папка server рядом с LocalCloud.exe. Распакуйте весь архив, а не только EXE.", "LocalCloud"); return; }
        var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true }; start.Environment["LOCALCLOUD_CONFIG"] = config;
        try { server = Process.Start(start); server!.EnableRaisingEvents = true; server.Exited += (_, _) => { if (!exiting && !stopping) dispatcher.BeginInvoke(new Action(() => tray.ShowBalloonTip(3000, "LocalCloud", "Сервер остановлен. Логи доступны в меню приложения.", ToolTipIcon.Info))); }; } catch (Exception e) { MessageBox.Show("Не удалось запустить сервер: " + e.Message, "LocalCloud"); }
    }
    async Task StopAsync()
    {
        await stopGate.WaitAsync(); stopping = true;
        try
        {
            var process = server;
            if (process != null && !process.HasExited)
            {
                await LocalControl.Send("server", config, "stop");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            }
            process?.Dispose(); server = null;
        }
        catch (InvalidOperationException) { server = null; }
        finally { stopping = false; stopGate.Release(); }
    }
    async Task Restart()
    {
        if(exiting||stopping||restarting)return;restarting=true;try{await Task.Delay(200);window?.Dispose();window=null;await StopAsync();Start();OpenBrowserWhenReady();}finally{restarting=false;}
    }
    async Task Shutdown()
    {
        if (exiting) return;
        exiting = true; tray.Text = "LocalCloud — завершение…";
        if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Enabled = false;
        window?.Dispose(); window = null;
        await StopAsync(); tray.Visible = false; ExitThread();
    }
    async Task ListenForExit(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(LocalControl.Name("desktop", AppContext.BaseDirectory), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(5000);
                using var reader = new StreamReader(pipe, leaveOpen: true); using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var command = await reader.ReadLineAsync(timeout.Token);
                if (command == "restart") { await writer.WriteLineAsync("ok"); dispatcher.BeginInvoke(new Action(async () => await Restart())); }
                if (command == "open") { await writer.WriteLineAsync("ok"); dispatcher.BeginInvoke(() => OpenWindow()); }
                if (command == "exit")
                {
                    await writer.WriteLineAsync("ok");
                    dispatcher.BeginInvoke(new Action(async () => await Shutdown()));
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException) { }
        }
    }
    void OpenWindow(string? path = null) { if (window == null || window.IsDisposed) window = new MainWindow(settings.Port, () => exiting); window.OpenPage(path); }
    void AllowFirewall() { var script = Path.Combine(AppContext.BaseDirectory, "scripts", "firewall.ps1"); if (!File.Exists(script)) return; try { var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas" }; foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Port", settings.Port.ToString() }) start.ArgumentList.Add(arg); Process.Start(start); } catch (System.ComponentModel.Win32Exception) { tray.ShowBalloonTip(4000, "Доступ с телефона", "Разрешите домашнюю сеть через меню LocalCloud, когда будете готовы подтвердить запрос Windows.", ToolTipIcon.Info); } }
    void OpenBrowser() => Open($"http://localhost:{settings.Port}");
    void OpenBrowserWhenReady() => CheckServerReady(true);
    async void CheckServerReady(bool open) { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) }; for (var i = 0; i < 25; i++) { if(exiting||stopping)return; try { var response = await client.GetAsync($"http://localhost:{settings.Port}/api/status"); if (!response.IsSuccessStatusCode) { await Task.Delay(300); continue; } if (!exiting && !stopping) { if(File.Exists(config)) settings=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(config))!;if(open)OpenWindow();var marker=config+".firewall-requested-1.3";if(settings.AllowLan&&!File.Exists(marker)){AllowFirewall();File.WriteAllText(marker,"requested");} } return; } catch (HttpRequestException) { await Task.Delay(300); } catch (TaskCanceledException) { await Task.Delay(300); } } if(!exiting)tray.ShowBalloonTip(3000, "LocalCloud", "Сервер не ответил. Проверьте логи и доступность порта.", ToolTipIcon.Warning); }
    static void Open(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception e) { MessageBox.Show(e.Message, "LocalCloud"); } }
    protected override void Dispose(bool disposing) { if (disposing) { exiting = true; controlCancellation.Cancel(); if (server != null && !server.HasExited) { try { server.Kill(true); } catch (InvalidOperationException) { } } server?.Dispose(); window?.Dispose(); tray?.Dispose(); dispatcher.Dispose(); controlCancellation.Dispose(); } base.Dispose(disposing); }
}
sealed class SetupForm : Form
{
    static bool Russian => !File.Exists(LanguagePath) || File.ReadAllText(LanguagePath).Trim() != "en";
    static string LanguagePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCloud", "language.txt");
    static string T(string ru, string en) => Russian ? ru : en;
    readonly TextBox root; readonly CheckBox startup, lan;
    public string StorageRoot => Path.GetFullPath(root.Text.Trim()); public bool LaunchAtStartup => startup.Checked; public bool AllowLan => lan.Checked;
    public SetupForm(AppSettings settings, bool first)
    {
        Text = first ? T("Добро пожаловать в LocalCloud", "Welcome to LocalCloud") : T("Настройки LocalCloud", "LocalCloud settings"); ClientSize = new Size(580, 370); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(248, 249, 252); Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        var title = new Label { Text = first ? T("Ваше облако. У вас дома.", "Your cloud. At home.") : T("Настройки LocalCloud", "LocalCloud settings"), Font = new Font("Segoe UI", 22, FontStyle.Regular), Location = new Point(30, 28), Size = new Size(520, 47) };
        var description = new Label { Text = T("Оригиналы хранятся в обычных папках Windows.", "Originals are stored in regular Windows folders."), ForeColor = Color.FromArgb(110, 120, 140), Location = new Point(32, 86), Size = new Size(510, 25) };
        var label = new Label { Text = T("Папка хранения", "Library folder"), Location = new Point(32, 131), Size = new Size(360, 25) }; root = new TextBox { Text = settings.StorageRoot, Location = new Point(32, 162), Size = new Size(394, 30) };
        var browse = new Button { Text = T("Выбрать…", "Browse…"), Location = new Point(438, 161), Size = new Size(106, 32) }; browse.Click += (_, _) => { using var picker = new FolderBrowserDialog { InitialDirectory = Directory.Exists(root.Text) ? root.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Description = T("Папка хранения LocalCloud", "LocalCloud library folder"), UseDescriptionForTitle = true }; if (picker.ShowDialog() == DialogResult.OK) root.Text = picker.SelectedPath; };
        startup = new CheckBox { Text = T("Запускать вместе с Windows", "Start with Windows"), Checked = settings.LaunchAtStartup, Location = new Point(32, 215), Size = new Size(470, 26) }; lan = new CheckBox { Text = T("Разрешить доступ из домашней сети", "Allow access from your home network"), Checked = settings.AllowLan, Location = new Point(32, 246), Size = new Size(470, 26) };
        var start = new Button { Text = first ? T("Начать", "Start") : T("Сохранить и перезапустить", "Save and restart"), Location = new Point(300, 305), Size = new Size(244, 38), BackColor = Color.FromArgb(66, 100, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; start.FlatAppearance.BorderSize = 0; start.Click += (_, _) => { try { if (string.IsNullOrWhiteSpace(root.Text) || !Path.IsPathFullyQualified(root.Text)) throw new ArgumentException(T("Выберите полный путь к папке.", "Choose an absolute folder path.")); var full = StorageRoot; var appDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar); var libraryDir = full.TrimEnd(Path.DirectorySeparatorChar); if (libraryDir.Equals(appDir, StringComparison.OrdinalIgnoreCase) || libraryDir.StartsWith(appDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || appDir.StartsWith(libraryDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(T("Папка программы и библиотека должны быть разделены.", "Application and library directories must be separate.")); Directory.CreateDirectory(full); var test = Path.Combine(full, ".localcloud-write-test-" + Guid.NewGuid().ToString("N")); File.WriteAllText(test, ""); File.Delete(test); if (!first && full != settings.StorageRoot && MessageBox.Show(T("Откроется отдельная библиотека в выбранной папке. Старые файлы останутся на месте. Продолжить?", "The selected folder will open as a separate library. Existing files stay in their original folder. Continue?"), "LocalCloud", MessageBoxButtons.YesNo) != DialogResult.Yes) return; DialogResult = DialogResult.OK; Close(); } catch (Exception e) { MessageBox.Show(T("Не удалось использовать эту папку. ", "Unable to use this folder. ") + e.Message, "LocalCloud"); } };
        Controls.AddRange(new Control[] { title, description, label, root, browse, startup, lan, start }); AcceptButton = start;
    }
}
