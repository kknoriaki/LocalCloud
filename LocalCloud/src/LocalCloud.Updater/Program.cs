using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using LocalCloud.Core;
using Microsoft.Data.Sqlite;

namespace LocalCloud.Updater;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try { await new UpdateEngine(args).Run(); return 0; }
        catch (Exception e) { Console.Error.WriteLine("Обновление не завершено: " + e.Message); if (Environment.GetEnvironmentVariable("LOCALCLOUD_UPDATER_DIAGNOSTICS") == "1") Console.Error.WriteLine(e); return 1; }
    }
}

public sealed record ReleaseFile(string Path, long Size, string Sha256, bool Existing = false);
public sealed record ReleaseManifest(string Product, string Version, int SchemaVersion, int RollbackCompatibleSchema, string Kind, List<ReleaseFile> Files, string? ReleaseChannel = null, List<string>? RenumberFrom = null);
public sealed record UpdateSnapshot(string Install, string Config, string Storage, string Version, int SchemaBefore, int RollbackCompatibleSchema, List<ReleaseFile> PreviousFiles, List<string> NewFiles, string DatabaseSha256, string Phase);

public sealed class UpdateEngine
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, PropertyNameCaseInsensitive = true };
    static readonly StringComparison Comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    readonly Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);
    readonly bool unattended;
    string install = "", config = "", storage = "", database = "", backup = "";
    int port;
    Process? started;
    readonly Dictionary<int, DateTime> workers = new();
    bool holdingLock;

    public UpdateEngine(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) throw new ArgumentException("Неизвестный параметр: " + args[i]);
            var key = args[i][2..];
            if (key is "plan" or "non-interactive" or "restore-database") options[key] = "true";
            else if (i + 1 < args.Length) options[key] = args[++i];
            else throw new ArgumentException("Нет значения параметра: " + key);
        }
        unattended = options.ContainsKey("non-interactive");
    }

    public async Task Run()
    {
        install = Path.GetFullPath(Required("install"));
        if (Inside(install, AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))) throw new IOException("Запускайте updater из отдельной распакованной папки Update вне установки, чтобы не заменять выполняемый updater.");
        config = Path.GetFullPath(options.GetValueOrDefault("config") ?? Environment.GetEnvironmentVariable("LOCALCLOUD_CONFIG") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCloud", "settings.json"));
        if (!File.Exists(Path.Combine(install, "LocalCloud.exe")) || !File.Exists(Path.Combine(install, "server", "LocalCloud.Server.dll"))) throw new IOException("Выберите существующую папку программы с LocalCloud.exe и server. Папка библиотеки не подходит.");
        if (!File.Exists(config)) throw new IOException("Настройки существующей библиотеки не найдены: " + config + ". Запустите старую версию или укажите -ConfigPath; новая пустая библиотека не будет создана.");
        using var settings = JsonDocument.Parse(File.ReadAllText(config));
        storage = Path.GetFullPath(Property(settings.RootElement, "StorageRoot").GetString()!);
        port = Property(settings.RootElement, "Port").GetInt32();
        database = Path.Combine(storage, ".localcloud", "database.sqlite");
        if (!File.Exists(database)) throw new IOException("Существующая SQLite не найдена в выбранном хранилище. Обновление отменено.");
        RejectReparse(install); RejectReparse(config); RejectReparse(database);
        if (Inside(install, storage) || Inside(storage, install)) throw new IOException("Папка программы и папка библиотеки пересекаются. Автоматическое обновление запрещено. Сначала отделите программу от данных по UPDATE-USER.txt.");
        Console.WriteLine($"Программа: {install}\nНастройки: {config}\nБиблиотека: {storage}\nSQLite: {database}");
        if (options.TryGetValue("rollback", out var rollback)) { await Rollback(Path.GetFullPath(rollback)); return; }
        var payload = Path.GetFullPath(Required("package"));
        if (Inside(install, payload) || Inside(payload, install) || Inside(storage, payload)) throw new IOException("Распакуйте Update в отдельную папку вне программы и библиотеки.");
        var manifest = Read<ReleaseManifest>(Path.Combine(payload, "release.json"));
        ValidateManifest(manifest);
        var currentVersion = CurrentVersion();
        var renumber = CanRenumber(currentVersion, CurrentReleaseChannel(), Schema(database), manifest);
        if (Schema(database) > manifest.SchemaVersion || (Version.Parse(currentVersion) > Version.Parse(manifest.Version) && !renumber)) throw new IOException("Пакет старее установленной программы/схемы. Используйте совместимый пакет или явный Rollback.");
        if (renumber) Console.WriteLine("Переход с разработки 1.4.0 на публичный релиз 1.0.0; схема 4 сохраняется.");
        foreach (var f in manifest.Files)
        {
            var source = SafeFile(payload, f.Path); var target = SafeFile(install, f.Path);
            if (target.Equals(config, Comparison)) throw new IOException("Пакет пытается заменить настройки.");
            await Verify(f.Existing ? target : source, f);
        }
        var oldFiles = ExistingApplicationFiles().Where(p => !p.StartsWith("server/tools/", StringComparison.OrdinalIgnoreCase) || manifest.Files.Any(f => f.Path.Equals(p, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var f in manifest.Files)
            if (File.Exists(SafeFile(install, f.Path)) && !oldFiles.Contains(f.Path, StringComparer.OrdinalIgnoreCase)) throw new IOException("Неизвестный файл назначения: " + f.Path + ". Он не будет перезаписан.");
        var bytes = manifest.Files.Where(f=>!f.Existing).Sum(f => f.Size) * 2 + oldFiles.Sum(p => new FileInfo(SafeFile(install, p)).Length) + new FileInfo(database).Length * 2 + 64 * 1024 * 1024;
        RequireSpace(install, bytes);
        RequireSpace(storage, new FileInfo(database).Length * 2 + 32 * 1024 * 1024);
        Console.WriteLine($"Версия пакета: {manifest.Version}. Нужно свободного места не менее {bytes / 1024 / 1024} МБ. Будут обновлены только файлы приложения; originals и settings сохраняются.");
        if (options.ContainsKey("plan")) { Console.WriteLine("PLAN OK — изменений нет."); return; }
        if (!Confirm("Начать обновление? [Д/н]", true)) throw new OperationCanceledException("Отменено пользователем.");
        using var updateLock = new FileStream(Path.Combine(install, ".localcloud-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        holdingLock = true;
        var settingsHash = await Hash(config);
        await WaitUntilIdle();
        await StopOwnedProcesses();
        EnsureNoListener();
        if (await Hash(config) != settingsHash) throw new IOException("Настройки изменились во время подготовки. Запустите обновление заново.");
        Console.WriteLine("Незавершённые resumable-сессии сохраняются: " + UnfinishedUploads());
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        backup = Path.Combine(install, "_update_backup", stamp);
        RejectReparse(Path.GetDirectoryName(backup)!); Directory.CreateDirectory(backup);
        var oldEntries = new List<ReleaseFile>();
        Console.WriteLine("Создаю backup приложения, настроек и SQLite (без медиа)…");
        foreach (var path in oldFiles)
        {
            var source = SafeFile(install, path); var target = SafeFile(Path.Combine(backup, "application"), path);
            Copy(source, target); oldEntries.Add(new(path, new FileInfo(target).Length, await Hash(target)));
        }
        Copy(config, Path.Combine(backup, "settings.json.backup"));
        BackupDatabase(database, Path.Combine(backup, "database.sqlite.backup"));
        var oldVersion = CurrentVersion();
        var snapshot = new UpdateSnapshot(install, config, storage, oldVersion, Schema(database), Math.Max(manifest.RollbackCompatibleSchema,Schema(database)), oldEntries, manifest.Files.Select(f => f.Path).ToList(), await Hash(Path.Combine(backup, "database.sqlite.backup")), "backed-up");
        Write(Path.Combine(backup, "update-state.json"), snapshot);
        var stage = Path.Combine(install, "_update_stage-" + stamp);
        try
        {
            foreach (var f in manifest.Files.Where(f=>!f.Existing)) { Copy(SafeFile(payload, f.Path), SafeFile(stage, f.Path)); await Verify(SafeFile(stage, f.Path), f); }
            snapshot = snapshot with { Phase = "replacing" }; Write(Path.Combine(backup, "update-state.json"), snapshot);
            foreach (var f in manifest.Files.Where(f=>!f.Existing))
            {
                var target = SafeFile(install, f.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(SafeFile(stage, f.Path), target, true);
            }
            foreach (var old in oldFiles.Except(manifest.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase)) File.Delete(SafeFile(install, old));
            snapshot = snapshot with { Phase = "starting" }; Write(Path.Combine(backup, "update-state.json"), snapshot);
            await StartAndCheck(manifest.Version);
            if (Schema(database) != manifest.SchemaVersion) throw new IOException("Не подтверждена версия схемы после migration.");
            if (await Hash(config) != settingsHash && !NetworkRepairOnly(Path.Combine(backup, "settings.json.backup"),config)) throw new IOException("Настройки неожиданно изменились. Backup сохранён.");
            snapshot = snapshot with { Phase = "complete" }; Write(Path.Combine(backup, "update-state.json"), snapshot);
            Console.WriteLine("Обновление успешно. Версия " + manifest.Version + ". Backup: " + backup + "\nЗапущена установленная программа: " + Path.Combine(install, "LocalCloud.exe") + "\nТеперь временную папку Update можно удалить. Открывайте LocalCloud.exe из установленной папки.");
        }
        catch
        {
            await StopOwnedProcesses();
            snapshot = snapshot with { Phase = "failed" }; Write(Path.Combine(backup, "update-state.json"), snapshot);
            Console.Error.WriteLine("Startup/migration не подтверждены. Backup сохранён: " + backup + "\nДля отката: Update-LocalCloud.ps1 -InstallPath \"" + install + "\" -Rollback -BackupPath \"" + backup + "\"");
            if (!unattended && Confirm("Вернуть предыдущие файлы приложения сейчас? [д/Н]", false)) await Rollback(backup);
            throw;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    static bool NetworkRepairOnly(string oldPath,string newPath)
    {
        var before=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(oldPath))!;var after=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(newPath))!;
        var key=before.AsObject().Select(p=>p.Key).FirstOrDefault(k=>k.Equals("AllowLan",StringComparison.OrdinalIgnoreCase));
        if(key==null||before[key]?.GetValue<bool>()!=false||after[key]?.GetValue<bool>()!=true)return false;
        before[key]=true;return System.Text.Json.Nodes.JsonNode.DeepEquals(before,after);
    }
    async Task Rollback(string backupPath)
    {
        if (!Inside(Path.Combine(install, "_update_backup"), backupPath)) throw new IOException("Backup должен принадлежать выбранной программе.");
        RejectReparse(backupPath);
        var snapshot = Read<UpdateSnapshot>(Path.Combine(backupPath, "update-state.json"));
        if (!snapshot.Install.Equals(install, Comparison) || !snapshot.Storage.Equals(storage, Comparison) || !snapshot.Config.Equals(config, Comparison)) throw new IOException("Backup принадлежит другой программе/библиотеке.");
        foreach (var f in snapshot.PreviousFiles) { ValidateApplicationPath(f.Path); await Verify(SafeFile(Path.Combine(backupPath, "application"), f.Path), f); }
        foreach (var path in snapshot.NewFiles) ValidateApplicationPath(path);
        var restoreDb = options.ContainsKey("restore-database");
        var currentSchema = Schema(database);
        if (!restoreDb && currentSchema > snapshot.RollbackCompatibleSchema) throw new IOException("Текущая схема новее совместимой с предыдущей версией. Откат приложения запрещён; нужен осознанный full metadata restore из DB backup. Медиа не удаляются.");
        if (restoreDb && unattended) throw new IOException("Восстановление DB нельзя выполнять без интерактивного подтверждения.");
        if (options.ContainsKey("plan")) { Console.WriteLine("ROLLBACK PLAN OK — изменений нет."); return; }
        if (!Confirm("Вернуть предыдущую программу? Пользовательские медиа не изменятся. [Д/н]", true)) throw new OperationCanceledException("Откат отменён.");
        await WaitUntilIdle(); await StopOwnedProcesses(); EnsureNoListener();
        using var rollbackLock = holdingLock ? null : new FileStream(Path.Combine(install, ".localcloud-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (restoreDb)
        {
            Console.WriteLine("DB restore вернёт альбомы, избранное и metadata на момент backup; более поздние изменения индекса будут потеряны. Originals не изменяются. Введите RESTORE для подтверждения:");
            if (Console.ReadLine() != "RESTORE") throw new OperationCanceledException("DB restore отменён.");
            var saved = Path.Combine(backupPath, "database.sqlite.backup");
            if (await Hash(saved) != snapshot.DatabaseSha256) throw new IOException("DB backup повреждён.");
            BackupDatabase(database, Path.Combine(backupPath, "database.sqlite.before-rollback-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")));
            BackupDatabase(saved, database, true);
        }
        foreach (var path in snapshot.NewFiles.Except(snapshot.PreviousFiles.Select(f => f.Path), StringComparer.OrdinalIgnoreCase)) File.Delete(SafeFile(install, path));
        foreach (var f in snapshot.PreviousFiles) Copy(SafeFile(Path.Combine(backupPath, "application"), f.Path), SafeFile(install, f.Path));
        await StartAndCheck(snapshot.Version);
        Console.WriteLine("Откат программы завершён. DB " + (restoreDb ? "восстановлена после явного подтверждения" : "и настройки сохранены без отката") + ". Backup сохранён: " + backupPath);
    }

    async Task WaitUntilIdle()
    {
        DateTime? idleSince=null; var automaticWaitUntil=DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var response = await LocalControl.Send("server", config, "state");
            if (response != null)
            {
                using var running = JsonDocument.Parse(response);
                if (!running.RootElement.GetProperty("serverPath").GetString()!.Equals(Path.Combine(install, "server"), Comparison)) throw new IOException("Этот конфиг использует другая установка LocalCloud. Выберите реально запущенную папку программы.");
                response = await LocalControl.Send("server", config, "prepare");
            }
            if (response != null)
            {
                using var state = JsonDocument.Parse(response);
                if (state.RootElement.GetProperty("prepared").GetBoolean()) return;
                // 1.1/1.2 incorrectly classify persisted interrupted sessions as active uploads forever.
                var idle=state.RootElement.TryGetProperty("requests",out var requests)&&requests.GetInt32()==0&&!state.RootElement.GetProperty("indexing").GetBoolean();
                var old=state.RootElement.TryGetProperty("version",out var oldVersion)&&Version.Parse(oldVersion.GetString()!)<new Version(1,3,0);
                if(idle&&old){idleSince??=DateTime.UtcNow;if(DateTime.UtcNow-idleSince>TimeSpan.FromSeconds(3)){Console.WriteLine("Передача данных не активна. Незавершённые сессии старой версии сохраняются; закрываю программу для обновления.");return;}await Task.Delay(400);continue;}idleSince=null;
            }
            else if (OwnedProcesses().Count == 0) return;
            if(response!=null&&DateTime.UtcNow<automaticWaitUntil){await Task.Delay(300);continue;}
            if (unattended) { await LocalControl.Send("server",config,"resume"); throw new IOException("LocalCloud занят или старый процесс ещё работает. Обновление не начато. Закройте старую версию через трей после завершения загрузок."); }
            Console.WriteLine("Сейчас выполняются загрузки/обработка либо старая версия ещё открыта. Для 1.0 сначала завершите загрузки и выберите tray → Выход. [П]одождать / [О]тменить");
            if (!(Console.ReadLine()?.Trim().ToLowerInvariant() is "п" or "p" or "wait")) { await LocalControl.Send("server",config,"resume");throw new OperationCanceledException("Обновление отменено; файлы не заменялись."); }
            await Task.Delay(2000);
        }
    }

    async Task StopOwnedProcesses()
    {
        var owned = OwnedProcesses();
        if (started != null && !started.HasExited && !owned.Any(p => p.Id == started.Id)) owned.Add(started);
        await LocalControl.Send("desktop", install, "exit");
        await LocalControl.Send("server", config, "stop");
        foreach (var p in owned)
        {
            try
            {
                if (p.HasExited) continue;
                if (OperatingSystem.IsWindows()) p.CloseMainWindow();

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await p.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    if (!Confirm("LocalCloud не завершился. Принудительно остановить только его дерево процессов? [д/Н]", false)) throw new IOException("Процесс ещё работает. Replacement запрещён.");
                    p.Kill(true); await p.WaitForExitAsync();
                }
            }
            catch (InvalidOperationException) { }
        }
        if (OwnedProcesses().Count != 0) throw new IOException("Остались процессы этой установки LocalCloud/MediaTools. Replacement запрещён.");
    }

    List<Process> OwnedProcesses()
    {
        var result = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            if (p.Id == Environment.ProcessId) continue;
            try
            {
                var exe = p.MainModule?.FileName;
                var name = exe == null ? "" : Path.GetFileName(exe);
                if (exe != null && (exe.Equals(Path.Combine(install, "LocalCloud.exe"), Comparison) || exe.Equals(Path.Combine(install, "server", "LocalCloud.Server.exe"), Comparison) || exe.Equals(Path.Combine(install, "server", "LocalCloud.Server"), Comparison))) result.Add(p);
                else if (!OperatingSystem.IsWindows() && File.Exists($"/proc/{p.Id}/cmdline"))
                {
                    var argv = File.ReadAllText($"/proc/{p.Id}/cmdline").Split('\0');
                    if (argv.Any(a => a.Equals(Path.Combine(install, "server", "LocalCloud.Server.dll"), Comparison))) result.Add(p);
                }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException) { }
        }
        var parents = ProcessTree.Parents();
        var ids = result.Select(p => p.Id).ToHashSet();
        bool added;
        do { added = false; foreach (var pair in parents) if (ids.Contains(pair.Value) && ids.Add(pair.Key)) added = true; } while (added);
        foreach (var id in ids) { try { workers[id] = Process.GetProcessById(id).StartTime; } catch (ArgumentException) { } }
        foreach (var pair in workers)
        {
            try { var p = Process.GetProcessById(pair.Key); if (!p.HasExited && p.StartTime == pair.Value && !result.Any(r => r.Id == p.Id)) result.Add(p); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }
        }
        return result;
    }

    async Task StartAndCheck(string version)
    {
        EnsureNoListener();
        var start = OperatingSystem.IsWindows() ? new ProcessStartInfo(Path.Combine(install, "LocalCloud.exe")) : new ProcessStartInfo(options.GetValueOrDefault("dotnet") ?? "dotnet");
        if (!OperatingSystem.IsWindows()) start.ArgumentList.Add(Path.Combine(install, "server", "LocalCloud.Server.dll"));
        start.WorkingDirectory = install; start.UseShellExecute = false;
        start.Environment["LOCALCLOUD_CONFIG"] = config; start.Environment.Remove("LOCALCLOUD_STORAGE"); start.Environment.Remove("LOCALCLOUD_PORT");
        start.RedirectStandardOutput = false; start.RedirectStandardError = false;
        started = Process.Start(start) ?? throw new IOException("Не удалось запустить новую версию.");
        Console.WriteLine("LocalCloud PID: " + started.Id);
        if (start.RedirectStandardOutput) { started.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); }; started.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); }; started.BeginOutputReadLine(); started.BeginErrorReadLine(); }
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until)
        {
            if (started.HasExited) throw new IOException("Приложение завершилось до проверки startup. Логи: " + Path.Combine(storage, ".localcloud", "logs"));
            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}/api/status"); response.EnsureSuccessStatusCode();
                using var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (status.RootElement.GetProperty("version").GetString() == version && status.RootElement.GetProperty("configured").GetBoolean()) return;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { }
            await Task.Delay(250);
        }
        throw new IOException("Сервер не подтвердил health/версию за 60 секунд.");
    }

    List<string> ExistingApplicationFiles()
    {
        List<string> paths;
        var legacy = !File.Exists(Path.Combine(install, "version.json"));
        var marker = Path.Combine(install, "version.json");
        if (File.Exists(marker))
        {
            using var release = JsonDocument.Parse(File.ReadAllText(marker));
            paths = release.RootElement.GetProperty("applicationFiles").EnumerateArray().Select(e => e.GetString()!).ToList();
            paths.Add("version.json");
        }
        else
        {
            using var resource = typeof(UpdateEngine).Assembly.GetManifestResourceStream("LocalCloud.Updater.legacy-layout-1.0.json") ?? throw new IOException("Не найден layout версии 1.0.");
            paths = JsonSerializer.Deserialize<List<string>>(resource)!;
        }
        var result = new List<string>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { ValidateApplicationPath(path); } catch (IOException) when (legacy) { continue; }
            if (File.Exists(SafeFile(install, path))) result.Add(path);
        }
        return result;
    }

    static IEnumerable<string> Walk(string directory)
    {
        foreach (var f in Directory.GetFiles(directory)) { RejectReparse(f); yield return f; }
        foreach (var d in Directory.GetDirectories(directory)) { RejectReparse(d); foreach (var f in Walk(d)) yield return f; }
    }
    static bool KnownRootFile(string name) => name is "LocalCloud.exe" or "LocalCloud" or "createdump.exe" or "README.md" or "README.ru.md" or "LICENSE" or "LICENSE.ru.md" or "NOTICE.md" or "THIRD_PARTY_NOTICES.md" or "UPDATE-USER.txt" or "version.json" or "Update-LocalCloud.ps1" or "Update-LocalCloud.cmd" or "Microsoft.Web.WebView2.Core.xml" or "Microsoft.Web.WebView2.WinForms.xml" || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase);
    static void ValidateApplicationPath(string path)
    {
        var parts = path.Split('/');
        if (Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || parts.Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' '))) throw new IOException("Небезопасный путь пакета: " + path);
        var reserved = new HashSet<string>(new[] { ".localcloud", "Photos", "Videos", "Music", "Files", "trash", "uploads", "settings.json", "database.sqlite", "_update_backup" }, StringComparer.OrdinalIgnoreCase);
        if (parts.Any(p => reserved.Contains(p)) || parts.Any(p => p.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) || p.Contains(".sqlite-", StringComparison.OrdinalIgnoreCase)) || path.Contains(".backup", StringComparison.OrdinalIgnoreCase)) throw new IOException("Пакет содержит пользовательские данные: " + path);
        if (parts.Length == 1 && KnownRootFile(path)) return;
        if (parts.Length == 2 && new[] { "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase) && parts[1].EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)) return;
        if (path == "runtimes/win-x64/native/WebView2Loader.dll") return;
        if (parts[0] is "docs" or "LICENSES") return;
        if (path == "scripts/firewall.ps1" || path == "server/web.config" || path == "server/LocalCloud.Server.staticwebassets.endpoints.json") return;
        if (parts[0] is "server" or "updater")
        {
            if (path.StartsWith("server/wwwroot/", StringComparison.Ordinal)) return;
            if (path.StartsWith("server/tools/", StringComparison.Ordinal) && parts.Length == 3 && parts[2] is "ffmpeg.exe" or "ffprobe.exe" or "LICENSE" or "README.txt") return;
            var name = parts[^1];
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".a", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) || name is "LocalCloud.Server" or "LocalCloud.Updater") return;
        }
        throw new IOException("Файл не входит в разрешённый application layout: " + path);
    }
    static void ValidateManifest(ReleaseManifest manifest)
    {
        if (manifest.Product != "LocalCloud" || manifest.Kind != "application-update" || !Version.TryParse(manifest.Version, out _) || manifest.Files.Count == 0 || manifest.SchemaVersion < 2 || manifest.RollbackCompatibleSchema > manifest.SchemaVersion) throw new IOException("Некорректный release manifest.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files) { ValidateApplicationPath(file.Path); if (!seen.Add(file.Path) || file.Size < 0 || file.Sha256.Length != 64) throw new IOException("Повтор или некорректный файл manifest."); }
        foreach (var required in new[] { "LocalCloud.exe", "server/LocalCloud.Server.dll", "server/wwwroot/index.html", "version.json" }) if (!seen.Contains(required)) throw new IOException("Пакет неполон: " + required);
    }
    int UnfinishedUploads()
    {
        using var db = Open(database, SqliteOpenMode.ReadOnly); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM UploadSessions WHERE status NOT IN ('complete','skipped','cancelled')";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
    static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 10 }.ToString()); connection.Open(); return connection;
    }
    static int Schema(string path) { using var db = Open(path, SqliteOpenMode.ReadOnly); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT MAX(version) FROM Migrations"; return Convert.ToInt32(cmd.ExecuteScalar()); }
    static void BackupDatabase(string source, string destination, bool overwrite = false)
    {
        if (!overwrite && File.Exists(destination)) throw new IOException("Backup уже существует; он не будет перезаписан.");
        using var original = Open(source, SqliteOpenMode.ReadOnly); using var backup = Open(destination, overwrite ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate);
        original.BackupDatabase(backup); using var cmd = backup.CreateCommand(); cmd.CommandText = "PRAGMA quick_check";
        if ((string?)cmd.ExecuteScalar() != "ok") throw new IOException("Проверка SQLite backup не прошла.");
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)"; cmd.ExecuteNonQuery();
    }
    string CurrentVersion()
    {
        var file = Path.Combine(install, "version.json"); if (!File.Exists(file)) return "1.0.0";
        using var json = JsonDocument.Parse(File.ReadAllText(file)); return json.RootElement.GetProperty("version").GetString()!;
    }
    string? CurrentReleaseChannel()
    {
        var file = Path.Combine(install, "version.json");
        if (!File.Exists(file)) return null;
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        return json.RootElement.TryGetProperty("releaseChannel", out var channel) ? channel.GetString() : null;
    }
    // A single documented numbering transition, never a general downgrade override.
    public static bool CanRenumber(string currentVersion, string? currentChannel, int currentSchema, ReleaseManifest manifest) =>
        currentVersion == "1.4.0" && currentChannel != "public" && currentSchema == 4 &&
        manifest.Version == "1.0.0" && manifest.ReleaseChannel == "public" && manifest.SchemaVersion == 4 &&
        manifest.RenumberFrom?.Contains("1.4.0", StringComparer.Ordinal) == true;

    void EnsureNoListener()
    {
        using var client = new TcpClient();
        try { if (client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(500)) && client.Connected) throw new IOException("Порт сервера ещё занят. Не запускаю вторую копию и не заменяю binaries."); }
        catch (AggregateException e) when (e.InnerException is SocketException) { }
    }
    bool Confirm(string message, bool defaultYes)
    {
        if (unattended) return defaultYes;
        Console.WriteLine(message); var value = Console.ReadLine()?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(value) ? defaultYes : value is "д" or "y" or "yes" or "да";
    }
    string Required(string key) => options.GetValueOrDefault(key) ?? throw new ArgumentException("Требуется --" + key);
    static JsonElement Property(JsonElement value, string name) { foreach (var p in value.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return p.Value; throw new IOException("В настройках нет " + name); }
    static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new IOException("Не удалось прочитать " + path);
    static void Write<T>(string path, T value) { var temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json)); File.Move(temporary, path, true); }
    static void Copy(string source, string target) { RejectReparse(source); RejectReparse(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, true); }
    static bool Inside(string parent, string child) => child.Equals(parent, Comparison) || child.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, Comparison);
    static string SafeFile(string root, string relative) { var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))); if (!Inside(root, full)) throw new IOException("Путь выходит за каталог."); RejectReparse(full); return full; }
    static void RejectReparse(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)!)
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Symlink/junction в update path запрещён: " + current);
    }
    static async Task<string> Hash(string path) { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant(); }
    static async Task Verify(string path, ReleaseFile file) { if (!File.Exists(path) || new FileInfo(path).Length != file.Size || await Hash(path) != file.Sha256.ToLowerInvariant()) throw new IOException("Файл повреждён/не соответствует SHA-256: " + file.Path); }
    static void RequireSpace(string path, long bytes) { var drive = new DriveInfo(Path.GetPathRoot(path)!); if (drive.AvailableFreeSpace < bytes) throw new IOException("Недостаточно места для staging/backup: требуется " + bytes / 1024 / 1024 + " МБ."); }
}
