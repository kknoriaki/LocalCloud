using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using LocalCloud.Core;
using LocalCloud.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Stores;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
var settings = new SettingsStore(); var paths = new SafePaths(settings.Current.StorageRoot); var database = new Database(paths); var library = new Library(paths, database);
var networkFeatures = new Features(library);
if(networkFeatures.Get("network-policy-1.3")==null) { if(!settings.Current.AllowLan) settings.Save(settings.Current with{AllowLan=true}); networkFeatures.Set("network-policy-1.3","applied"); }
var listeningLan = settings.Current.AllowLan; var listeningPort = settings.Current.Port;
builder.WebHost.UseUrls($"http://{(settings.Current.AllowLan ? "0.0.0.0" : "127.0.0.1")}:{settings.Current.Port}");
builder.WebHost.ConfigureKestrel(k => { k.Limits.MaxRequestBodySize = 64 * 1024 * 1024; k.Limits.MinRequestBodyDataRate = null; });
builder.Services.AddSingleton(settings); builder.Services.AddSingleton(paths); builder.Services.AddSingleton(database); builder.Services.AddSingleton(library);
builder.Services.AddSingleton<MediaProcessor>(); builder.Services.AddSingleton<Indexer>(); builder.Services.AddHostedService(s => s.GetRequiredService<Indexer>()); builder.Services.AddSingleton<Discovery>(); builder.Services.AddHostedService(s => s.GetRequiredService<Discovery>());
builder.Services.AddSingleton<AudioTagWriter>();builder.Services.AddSingleton<Features>(); builder.Services.AddSingleton<VerifiedBackup>(); builder.Services.AddHostedService(s => s.GetRequiredService<VerifiedBackup>()); builder.Services.AddSingleton<UpdateActivity>(); builder.Services.AddHostedService<UpdateControl>();
builder.Services.AddSignalR(); builder.Services.AddRateLimiter(o => { o.AddPolicy("pin", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); o.RejectionStatusCode = 429; });
builder.Logging.AddProvider(new LocalLogging(Path.Combine(paths.Internal, "logs"))); builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
var app = builder.Build(); var sessions = new ConcurrentDictionary<string, DateTime>(); var proxyLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
var features = app.Services.GetRequiredService<Features>(); var backupService=app.Services.GetRequiredService<VerifiedBackup>();
var updateActivity = app.Services.GetRequiredService<UpdateActivity>();
app.Use(async (context, next) => { if (!context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/uploads")) { await next(); return; } if (!updateActivity.Enter()) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { message = "LocalCloud готовится к обновлению. Повторите после перезапуска." }); return; } try { await next(); } finally { updateActivity.Leave(); } });
var hub = app.Services.GetRequiredService<IHubContext<LibraryHub>>(); library.Changed += () => { _ = hub.Clients.All.SendAsync("changed"); };
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["X-Frame-Options"] = "DENY"; context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; connect-src 'self' ws: wss:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'";
    var remote = context.Connection.RemoteIpAddress;
    var vpn = remote != null && Features.Radmin(remote);
    if (remote == null || !(Discovery.Private(remote) || vpn && features.VpnEnabled)) { context.Response.StatusCode = 403; return; }
    var host = context.Request.Host.Host;
    var validHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals("cloud.local", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var hostIp) && (Discovery.Private(hostIp) || features.VpnEnabled && Features.Radmin(hostIp))) || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    if (!validHost) { context.Response.StatusCode = 403; return; }
    var isApi = context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/uploads") || context.Request.Path.StartsWithSegments("/hubs");
    if (isApi) context.Response.Headers.CacheControl = "no-store";
    var origin = context.Request.Headers.Origin.ToString(); if (isApi && origin.Length > 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}") { context.Response.StatusCode = 403; return; }
    if (isApi && context.Request.Headers["Sec-Fetch-Site"] == "cross-site") { context.Response.StatusCode = 403; return; }
    if(vpn && (context.Request.Path.StartsWithSegments("/api")||context.Request.Path.StartsWithSegments("/uploads")||context.Request.Path.StartsWithSegments("/hubs")) && !context.Request.Path.StartsWithSegments("/api/shared")) { context.Response.StatusCode=403; return; }
    var publicApi = context.Request.Path is var p && (p == "/api/status" || p == "/api/unlock" || p.StartsWithSegments("/api/shared"));
    if (isApi && !publicApi && settings.Current.PinHash != null) { var token = context.Request.Cookies["lc_session"]; if (token == null || !sessions.TryGetValue(token, out var expires) || expires < DateTime.UtcNow) { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { message = "Введите PIN для доступа к LocalCloud." }); return; } }
    try { await next(); } catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException) { app.Logger.LogWarning(e, "Request failed"); if (!context.Response.HasStarted) { context.Response.StatusCode = e is FileNotFoundException ? 404 : e is ArgumentException ? 400 : 409; await context.Response.WriteAsJsonAsync(new { message = e is ArgumentException || e is FileNotFoundException ? e.Message : "Не удалось выполнить операцию. Проверьте свободное место и доступ к папке.", details = e.GetType().Name }); } }
});
app.UseRateLimiter(); app.UseDefaultFiles(); var contentTypes=new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();contentTypes.Mappings[".bcmap"]="application/octet-stream";contentTypes.Mappings[".pfb"]="application/octet-stream";app.UseStaticFiles(new StaticFileOptions{ContentTypeProvider=contentTypes});
app.MapGet("/api/status", (HttpContext c) => new { version = "1.0.0", processId = Environment.ProcessId, configured = settings.Configured, requiresPin = settings.Current.PinHash != null && !ValidSession(c), local = IPAddress.IsLoopback(c.Connection.RemoteIpAddress!), online = true });
bool ValidSession(HttpContext c) => c.Request.Cookies["lc_session"] is { } t && sessions.TryGetValue(t, out var expiry) && expiry > DateTime.UtcNow;
app.MapPost("/api/unlock", (PinRequest request, HttpContext c) => { if (!settings.CheckPin(request.Pin)) return Results.Json(new { message = "Неверный PIN." }, statusCode: 401); var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); sessions[token] = DateTime.UtcNow.AddDays(7); c.Response.Cookies.Append("lc_session", token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = c.Request.IsHttps, MaxAge = TimeSpan.FromDays(7) }); return Results.Ok(); }).RequireRateLimiting("pin");
app.MapPost("/api/logout", (HttpContext c) => { if (c.Request.Cookies["lc_session"] is { } t) sessions.TryRemove(t, out _); c.Response.Cookies.Delete("lc_session"); return Results.Ok(); });
app.MapGet("/api/settings", (HttpContext c) => { var local = IPAddress.IsLoopback(c.Connection.RemoteIpAddress!); return Results.Ok(new { storageRoot = local ? settings.Current.StorageRoot : null, settings.Current.Port, settings.Current.AllowLan, settings.Current.LaunchAtStartup, settings.Current.ConcurrentUploads, settings.Current.DuplicateBehavior, settings.Current.TrashRetentionDays, pinEnabled = settings.Current.PinHash != null, local, addresses = Discovery.Addresses(settings.Current.Port), mdns = app.Services.GetRequiredService<Discovery>().Active, version = "1.0.0", updateMethod = "local-package", ffmpeg = app.Services.GetRequiredService<MediaProcessor>().FFmpeg != null, indexing = app.Services.GetRequiredService<Indexer>().Scanning, lastScan = app.Services.GetRequiredService<Indexer>().LastScan }); });
app.MapGet("/api/network", async (HttpContext c) => IPAddress.IsLoopback(c.Connection.RemoteIpAddress!) ? Results.Ok(await NetworkSupport.Diagnose(listeningLan, listeningPort, c.RequestAborted)) : Results.StatusCode(403));
app.MapPost("/api/server/restart", async (HttpContext c)=>{if(!IPAddress.IsLoopback(c.Connection.RemoteIpAddress!))return Results.StatusCode(403);var response=await LocalControl.Send("desktop",Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..")),"restart");return response==null?Results.Json(new{message="Перезапуск доступен из Windows-приложения. Закройте сервер и откройте LocalCloud.exe."},statusCode:409):Results.Ok(new{port=settings.Current.Port});});
app.MapPost("/api/network/allow", (HttpContext c) => { if (!IPAddress.IsLoopback(c.Connection.RemoteIpAddress!)) return Results.StatusCode(403); NetworkSupport.Allow(listeningPort); return Results.Ok(new { message = "Подтвердите запрос Windows на компьютере. Затем нажмите «Проверить снова»." }); });
app.MapPost("/api/setup", (SetupRequest request, HttpContext c) => { if (!IPAddress.IsLoopback(c.Connection.RemoteIpAddress!)) return Results.Json(new { message = "Выберите папку на компьютере с LocalCloud." }, statusCode: 403); var root = Path.GetFullPath(request.StorageRoot); if (settings.Configured && !root.Equals(paths.Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return Results.BadRequest(new { message = "Изменение папки выполняется в Windows-приложении после остановки сервера." }); new SafePaths(root).Resolve(""); var restart = root != paths.Root || request.AllowLan != settings.Current.AllowLan; settings.Save(settings.Current with { StorageRoot = root, AllowLan = request.AllowLan, LaunchAtStartup = request.LaunchAtStartup }); return Results.Ok(new { restartRequired = restart }); });
app.MapPost("/api/settings", (SettingsRequest request, HttpContext c) => { if (!IPAddress.IsLoopback(c.Connection.RemoteIpAddress!)) return Results.StatusCode(403); var next = settings.Current with { Port = request.Port, AllowLan = request.AllowLan, ConcurrentUploads = request.ConcurrentUploads, DuplicateBehavior = request.DuplicateBehavior, TrashRetentionDays = request.TrashRetentionDays }; if (next.DuplicateBehavior is not ("ask" or "skip" or "copy")) throw new ArgumentException("Неизвестное поведение дубликатов."); if (request.Pin != null) { settings.Save(settings.WithPin(request.Pin)); next = next with { PinHash = settings.Current.PinHash, PinSalt = settings.Current.PinSalt }; sessions.Clear(); } var restart = next.Port != settings.Current.Port || next.AllowLan != settings.Current.AllowLan; settings.Save(next); return Results.Ok(new { restartRequired = restart }); });
app.MapGet("/api/files", (HttpRequest r) =>
{
    int Int(string name, int fallback) => int.TryParse(r.Query[name], out var v) ? v : fallback; long? Long(string name) => long.TryParse(r.Query[name], out var v) ? v : null; string? Str(string name) => r.Query.ContainsKey(name) ? r.Query[name].ToString() : null;
    return library.Db.Query(new LibraryQuery(Str("view") ?? "photos", Str("folder"), Str("search"), Str("album"), Int("offset", 0), Int("limit", 120), Str("sort") ?? "date", Str("extension"), Str("after"), Str("before"), Long("minSize"), Long("maxSize"), Str("missingTags")=="true"));
});
app.MapGet("/api/folders", (string? path) => library.Folders(path ?? ""));
app.MapGet("/api/folders/tree", () => { var result = new List<FolderEntry>(); void Walk(string path, int depth) { if (depth > 30 || result.Count > 3000) return; foreach (var f in library.Folders(path)) { result.Add(f); Walk(f.Path, depth + 1); } } Walk("", 0); return result; });
app.MapPost("/api/folders", async (FolderRequest request) => { await library.Mutation.WaitAsync(); try { var full = paths.Resolve(Path.Combine(request.Parent, SafePaths.FileName(request.Name)), false); if (Directory.Exists(full) || File.Exists(full)) throw new ArgumentException("Папка с таким именем уже существует."); Directory.CreateDirectory(full); library.Notify(); return new FolderEntry(paths.Relative(full), request.Name); } finally { library.Mutation.Release(); } });
app.MapPost("/api/folders/operate", (FolderOperation request) => library.FolderOperate(request.Path, request.Action, request.Target));
app.MapPost("/api/files/{id}/operate", (string id, FileOperation request) => library.Operate(id, request.Action, request.Target));
app.MapGet("/api/files/{id}", (string id) => database.Get(id) is { } f ? Results.Ok(f) : Results.NotFound());
app.MapGet("/api/files/{id}/original", (string id, bool? download) =>
{
    var f = database.Get(id) ?? throw new FileNotFoundException("Файл не найден."); if (f.Trashed) return Results.NotFound(); var full = paths.Resolve(f.Path, false);
    // Unknown documents are attachment-only, never execute uploaded HTML/SVG in the app origin.
    var attachment = download == true || f.Kind == "file";
    return Results.File(full, attachment ? "application/octet-stream" : f.Mime, fileDownloadName: attachment ? f.Name : null, enableRangeProcessing: true);
});
app.MapGet("/api/files/{id}/thumbnail", (string id, int? size, MediaProcessor media) => { var f = database.Get(id); if (f == null || f.Trashed) return Results.NotFound(); var thumb = media.Thumbnail(id, size is 256 or 512 or 1024 ? size.Value : 512); return f.Metadata != null && File.Exists(thumb) ? Results.File(thumb, "image/jpeg") : Results.File(Path.Combine(app.Environment.WebRootPath, "preview.svg"), "image/svg+xml"); });
app.MapPost("/api/files/{id}/proxy", async (string id, MediaProcessor media, HttpContext c) => { var f = database.Get(id) ?? throw new FileNotFoundException("Файл не найден."); if (f.Trashed || f.Kind != "video") return Results.BadRequest(); var gate = proxyLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1)); await gate.WaitAsync(c.RequestAborted); try { await media.Proxy(f, c.RequestAborted); return Results.Ok(new { url = $"/api/files/{id}/proxy" }); } finally { gate.Release(); } });
app.MapGet("/api/files/{id}/proxy", (string id) => { var f = database.Get(id); if (f == null || f.Trashed) return Results.NotFound(); var proxy = Path.Combine(paths.Internal, "cache", "proxies", f.Id + ".mp4"); return File.Exists(proxy) ? Results.File(proxy, "video/mp4", enableRangeProcessing: true) : Results.NotFound(); });
app.MapGet("/api/downloads", async (string ids, HttpContext c) =>
{
    var selected = ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray(); if (selected.Length == 0 || selected.Length > 120) throw new ArgumentException("Выберите от 1 до 120 файлов.");
    var entries = selected.Select(id => database.Get(id) ?? throw new FileNotFoundException("Файл не найден.")).ToList(); foreach (var f in entries.ToArray()) if (f.LivePartner != null && database.Get(f.LivePartner) is { } partner && !entries.Any(e => e.Id == partner.Id)) entries.Add(partner);
    if (entries.Any(f => f.Trashed)) throw new ArgumentException("Сначала восстановите файлы из корзины.");
    c.Response.ContentType = "application/zip"; c.Response.Headers.ContentDisposition = "attachment; filename=LocalCloud-files.zip"; c.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>()!.AllowSynchronousIO = true;
    using var archive = new System.IO.Compression.ZipArchive(c.Response.Body, System.IO.Compression.ZipArchiveMode.Create, true);
    foreach (var f in entries) { var entry = archive.CreateEntry(f.Path, System.IO.Compression.CompressionLevel.NoCompression); await using var output = entry.Open(); await using var input = new FileStream(paths.Resolve(f.Path, false), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan); await input.CopyToAsync(output, c.RequestAborted); }
});
app.MapGet("/api/albums", () => database.Albums());
app.MapPost("/api/albums", (NameRequest request) => { var name = SafePaths.FileName(request.Name); var id = Guid.NewGuid().ToString("N"); database.Execute("INSERT INTO Albums VALUES($id,$name)", ("$id", id), ("$name", name)); library.Notify(); return new { id, name }; });
app.MapPost("/api/albums/{id}/items", (string id, AlbumItemsRequest request) => { foreach (var file in request.Ids) database.Execute("INSERT OR IGNORE INTO AlbumItems VALUES($album,$file)", ("$album", id), ("$file", file)); library.Notify(); return Results.Ok(); });
app.MapDelete("/api/albums/{id}/items/{fileId}", (string id, string fileId) => { database.Execute("DELETE FROM AlbumItems WHERE albumId=$album AND fileId=$file", ("$album", id), ("$file", fileId)); library.Notify(); return Results.Ok(); });
app.MapDelete("/api/albums/{id}", (string id) => { database.Execute("DELETE FROM Albums WHERE id=$id", ("$id", id)); library.Notify(); return Results.Ok(); });
app.MapPost("/api/albums/{id}/rename", (string id, NameRequest request) => { database.Execute("UPDATE Albums SET name=$name WHERE id=$id", ("$name", SafePaths.FileName(request.Name)), ("$id", id)); library.Notify(); return Results.Ok(); });
app.MapPost("/api/rescan", (Indexer indexer) => { indexer.Request(); return Results.Accepted(); });
app.MapPost("/api/rebuild-thumbnails", (Indexer indexer) => { indexer.Rebuild(); return Results.Accepted(); });
app.MapPost("/api/trash/empty", async () => { foreach (var r in database.Rows("SELECT id FROM Files WHERE trashed=1")) if(database.Get((string)r["id"]!) is {Trashed:true}) await library.Operate((string)r["id"]!, "purge"); return Results.Ok(); });
app.MapGet("/api/storage", () => { var drive = new DriveInfo(Path.GetPathRoot(paths.Root)!); var groups = database.Rows("SELECT kind,SUM(size) AS bytes,COUNT(*) AS count FROM Files WHERE trashed=0 GROUP BY kind"); return new { total = drive.TotalSize, free = drive.AvailableFreeSpace, used = drive.TotalSize - drive.AvailableFreeSpace, libraryBytes = groups.Sum(r => Convert.ToInt64(r["bytes"])), groups }; });
app.MapGet("/api/uploads/{id}/status", (string id) => database.Rows("SELECT * FROM UploadSessions WHERE id=$id", ("$id", id)).FirstOrDefault() is { } row ? Results.Ok(row) : Results.NotFound());
app.MapPost("/api/uploads/{id}/finalize", (string id, FinalizeRequest r) => { if (r.Policy is not ("ask" or "skip" or "copy")) throw new ArgumentException("Недопустимая стратегия дубликатов."); return library.FinalizeUpload(id, r.Policy); });
app.MapDelete("/api/uploads/{id}", (string id) => { var row = database.Rows("SELECT status FROM UploadSessions WHERE id=$id", ("$id", id)).FirstOrDefault(); if (row == null) return Results.NotFound(); library.CleanupUpload(id); database.Execute("UPDATE UploadSessions SET status='cancelled' WHERE id=$id AND status NOT IN ('complete','skipped')", ("$id", id)); return Results.Ok(); });
var tusStore = new TusDiskStore(Path.Combine(paths.Internal, "uploads"));
app.MapGet("/api/uploads", async () =>
{
    var uploads = database.Rows("SELECT u.id,u.name,u.folder,u.size,u.status,f.path AS duplicatePath FROM UploadSessions u LEFT JOIN Files f ON f.id=u.duplicateId WHERE u.status NOT IN ('complete','skipped','cancelled') ORDER BY u.createdAt DESC LIMIT 100");
    foreach (var upload in uploads) { var id = (string)upload["id"]!; try { upload["offset"] = await tusStore.GetUploadOffsetAsync(id, CancellationToken.None); } catch (FileNotFoundException) { upload["offset"] = 0L; } }
    return uploads;
});
app.MapTus("/uploads", _ => Task.FromResult(new DefaultTusConfiguration
{
    Store = tusStore,
    AllowedExtensions = new TusExtensions(TusExtensions.Creation, TusExtensions.Termination, TusExtensions.Checksum),
    MaxAllowedUploadSizeInBytesLong = 1024L * 1024 * 1024 * 1024,
    Events = new()
    {
        OnBeforeCreateAsync = c => { try { string Value(string key) => c.Metadata.TryGetValue(key, out var m) ? m.GetString(Encoding.UTF8) : ""; var name = SafePaths.FileName(Value("filename")); var folder = Value("folder"); if (!Directory.Exists(paths.Resolve(folder))) c.FailRequest("Папка не найдена."); if (c.UploadLength < 0) c.FailRequest("Укажите размер файла."); if (c.UploadLength > new DriveInfo(Path.GetPathRoot(paths.Root)!).AvailableFreeSpace) c.FailRequest("Недостаточно свободного места."); } catch (ArgumentException e) { c.FailRequest(e.Message); } return Task.CompletedTask; },
        OnCreateCompleteAsync = c => { string Value(string key) => c.Metadata.TryGetValue(key, out var m) ? m.GetString(Encoding.UTF8) : ""; database.Execute("INSERT INTO UploadSessions(id,name,folder,size,createdAt) VALUES($id,$name,$folder,$size,$now)", ("$id", c.FileId), ("$name", Value("filename")), ("$folder", Value("folder")), ("$size", c.UploadLength), ("$now", DateTime.UtcNow.ToString("O"))); return Task.CompletedTask; },
        OnFileCompleteAsync = c => { library.Notify(); database.Execute("UPDATE UploadSessions SET status='uploaded' WHERE id=$id", ("$id", c.FileId)); return Task.CompletedTask; }
    }
}));
bool Owner(HttpContext c) => IPAddress.IsLoopback(c.Connection.RemoteIpAddress!);
app.MapGet("/api/uploads/history", (int? offset,int? limit) => database.Rows("SELECT u.id,u.name,u.folder,u.size,u.status,u.createdAt,u.resultId,u.hash,u.error,f.path AS savedPath,d.path AS duplicatePath FROM UploadSessions u LEFT JOIN Files f ON f.id=u.resultId LEFT JOIN Files d ON d.id=u.duplicateId ORDER BY u.createdAt DESC LIMIT $limit OFFSET $offset",("$limit",Math.Clamp(limit??60,1,200)),("$offset",Math.Max(0,offset??0))));
app.MapGet("/api/organize",(HttpContext c)=>Owner(c)?Results.Ok(features.OrganizePlan()):Results.StatusCode(403));
app.MapPost("/api/organize",async(HttpContext c)=>{if(!Owner(c))return Results.StatusCode(403);var plans=System.Text.Json.JsonSerializer.SerializeToElement(features.OrganizePlan());var count=0;foreach(var plan in plans.EnumerateArray()){c.RequestAborted.ThrowIfCancellationRequested();await library.Operate(plan.GetProperty("id").GetString()!,"move",plan.GetProperty("folder").GetString());count++;}return Results.Ok(new{count});});
app.MapGet("/api/processes",(HttpContext c)=>Owner(c)?Results.Ok(new{server=new{pid=Environment.ProcessId,name="LocalCloud.Server.exe — сервер",path=Environment.ProcessPath},application=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","LocalCloud.exe")),storage=paths.Root,settings=settings.ConfigPath,port=listeningPort,lan=listeningLan}):Results.StatusCode(403));
app.MapGet("/api/backup", (HttpContext c)=>Owner(c)?Results.Ok(backupService.Status()):Results.StatusCode(403));
app.MapPost("/api/backup", (BackupRequest r,HttpContext c)=>{if(!Owner(c))return Results.StatusCode(403);backupService.Start(r.Target,app.Lifetime.ApplicationStopping);return Results.Accepted();});
app.MapPost("/api/backup/cancel",(HttpContext c)=>{if(!Owner(c))return Results.StatusCode(403);backupService.Cancel();return Results.Ok();});
app.MapGet("/api/shares",(HttpContext c)=>Owner(c)?Results.Ok(new{enabled=features.VpnEnabled,lanAddresses=Discovery.Addresses(listeningPort),addresses=System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==System.Net.NetworkInformation.OperationalStatus.Up&&(n.Name+" "+n.Description).Contains("radmin",StringComparison.OrdinalIgnoreCase)).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Where(a=>Features.Radmin(a.Address)).Select(a=>$"http://{a.Address}:{listeningPort}").ToArray(),items=database.Rows("SELECT id,name,folder,download,expires FROM LocalCloudShares ORDER BY created DESC")}):Results.StatusCode(403));
app.MapPost("/api/shares",(ShareRequest r,HttpContext c)=>Owner(c)?Results.Ok(features.CreateShare(r.Folder,r.Name,r.Download,r.Days)):Results.StatusCode(403));
app.MapDelete("/api/shares/{id}",(string id,HttpContext c)=>{if(!Owner(c))return Results.StatusCode(403);database.Execute("DELETE FROM LocalCloudShares WHERE id=$id",("$id",id));return Results.Ok();});
app.MapPost("/api/shares/vpn",(VpnRequest r,HttpContext c)=>{if(!Owner(c))return Results.StatusCode(403);features.Set("radmin-enabled",r.Enabled?"true":"false");return Results.Ok();});
app.MapGet("/api/shared/{token}",(string token,int? offset,int? limit)=>features.SharedFiles(token,offset??0,limit??60));
app.MapGet("/api/shared/{token}/files/{id}/thumbnail",(string token,string id,MediaProcessor media)=>{var f=features.SharedFile(token,id);var path=media.Thumbnail(f.Id,512);return File.Exists(path)?Results.File(path,"image/jpeg"):Results.NotFound();});
app.MapGet("/api/shared/{token}/files/{id}/content",(string token,string id,bool? download)=>{var f=features.SharedFile(token,id,download==true||database.Get(id)?.Kind=="file");if(download!=true&&f.Kind=="photo") {var path=app.Services.GetRequiredService<MediaProcessor>().Thumbnail(f.Id,1024);return File.Exists(path)?Results.File(path,"image/jpeg"):Results.NotFound();}return Results.File(paths.Resolve(f.Path,false),f.Mime,download==true||f.Kind=="file"?f.Name:null,enableRangeProcessing:true);});
app.MapGet("/api/files/{id}/pdf",(string id)=>{var f=database.Get(id)??throw new FileNotFoundException("Файл не найден.");if(f.Trashed||f.Extension!="pdf")throw new ArgumentException("Выберите PDF.");return Results.File(paths.Resolve(f.Path,false),"application/pdf",enableRangeProcessing:true);});
app.MapPost("/api/files/{id}/audio-tags",(string id,AudioTagsRequest r)=>{var f=database.Get(id)??throw new FileNotFoundException("Трек не найден.");if(f.Kind!="audio"||f.Trashed||new[]{r.Title,r.Artist,r.Album}.Any(t=>t.Length>250))throw new ArgumentException("Некорректные теги трека.");if(r.CoverId!=null&&database.Get(r.CoverId) is not{Kind:"photo",Trashed:false})throw new ArgumentException("Обложкой может быть фото из библиотеки.");var tags=new Dictionary<string,string>{{"title",r.Title},{"artist",r.Artist},{"album",r.Album}};if(r.CoverId!=null){tags["cover"]="custom";tags["coverId"]=r.CoverId;}database.Execute("INSERT INTO LocalCloudAudioLabels VALUES($id,$tags) ON CONFLICT(fileId) DO UPDATE SET tags=$tags",("$id",id),("$tags",System.Text.Json.JsonSerializer.Serialize(tags)));library.Notify();return Results.Ok();});
app.MapPost("/api/files/{id}/audio-tags/preview",async(string id,AudioTagValues r,HttpContext c,AudioTagWriter writer)=>Owner(c)?Results.Ok(await writer.Preview(id,r,c.RequestAborted)):Results.StatusCode(403));
app.MapPost("/api/files/{id}/audio-tags/write",async(string id,WriteTagsRequest r,HttpContext c,AudioTagWriter writer)=>Owner(c)?Results.Ok(await writer.Write(id,r.Token,r.Confirmed,c.RequestAborted)):Results.StatusCode(403));
app.MapGet("/api/background",(HttpContext c,Indexer indexer)=>Owner(c)?Results.Ok(indexer.Status()):Results.StatusCode(403));
app.MapPost("/api/background",(BackgroundRequest r,HttpContext c,Indexer indexer)=>{if(!Owner(c))return Results.StatusCode(403);if(r.Paused)indexer.Pause();else indexer.Resume();return Results.Ok(indexer.Status());});
app.MapPost("/api/network/radmin",(HttpContext c)=>{if(!Owner(c)||!features.VpnEnabled)return Results.StatusCode(403);NetworkSupport.Allow(listeningPort,true);return Results.Ok();});
app.MapGet("/api/files/{id}/edit",(string id)=>features.Recipe(id));
app.MapPost("/api/files/{id}/edit",(string id,PhotoRecipe r)=>{features.SaveRecipe(id,r);library.Notify();return Results.Ok();});
app.MapGet("/api/files/{id}/edited",async(string id,bool? export,HttpContext c)=>Results.File(await features.RenderEdit(id,export==true,c.RequestAborted),"image/jpeg",export==true?Path.GetFileNameWithoutExtension(database.Get(id)!.Name)+"-edited.jpg":null));
app.MapPost("/api/files/rename-plan",(RenameRequest r)=>features.RenamePlan(r.Ids,r.Prefix,r.Start));
app.MapPost("/api/files/rename-batch",async(RenameRequest r)=>{var plans=System.Text.Json.JsonSerializer.SerializeToElement(features.RenamePlan(r.Ids,r.Prefix,r.Start));var renamed=new List<string>();foreach(var plan in plans.EnumerateArray()){var id=plan.GetProperty("id").GetString()!;await library.Operate(id,"rename",plan.GetProperty("name").GetString());renamed.Add(id);}return new{renamed};});
app.MapGet("/api/collections",()=>database.Rows("SELECT * FROM LocalCloudCollections ORDER BY name"));
app.MapPost("/api/collections",(CollectionRequest r)=>{SafePaths.FileName(r.Name);if(r.Query.View is not("photos" or "videos" or "audio" or "files" or "favorites" or "recent"))throw new ArgumentException("Неверный тип коллекции.");var id=Guid.NewGuid().ToString("N");database.Execute("INSERT INTO LocalCloudCollections VALUES($id,$name,$query)",("$id",id),("$name",r.Name),("$query",System.Text.Json.JsonSerializer.Serialize(r.Query)));library.Notify();return new{id};});
app.MapDelete("/api/collections/{id}",(string id)=>{database.Execute("DELETE FROM LocalCloudCollections WHERE id=$id",("$id",id));library.Notify();return Results.Ok();});
app.MapGet("/api/collections/{id}/files",(string id,int? offset)=>{var row=database.Rows("SELECT query FROM LocalCloudCollections WHERE id=$id",("$id",id)).FirstOrDefault()??throw new FileNotFoundException("Коллекция не найдена.");var q=System.Text.Json.JsonSerializer.Deserialize<LibraryQuery>((string)row["query"]!)!;return database.Query(q with{Offset=Math.Max(0,offset??0)});});
app.MapHub<LibraryHub>("/hubs/library");
app.MapFallback(async c => { if (c.Request.Path.StartsWithSegments("/api") || c.Request.Path.StartsWithSegments("/uploads")) { c.Response.StatusCode = 404; return; } c.Response.ContentType = "text/html"; await c.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html")); });
await app.RunAsync();
public class LibraryHub : Hub { }
public record PinRequest(string Pin);
public record SetupRequest(string StorageRoot, bool AllowLan = true, bool LaunchAtStartup = true);
public record SettingsRequest(int Port, bool AllowLan, int ConcurrentUploads, string DuplicateBehavior, int TrashRetentionDays, string? Pin = null);
public record FileOperation(string Action, string? Target = null);
public record FolderRequest(string Parent, string Name);
public record FolderOperation(string Path, string Action, string? Target);
public record NameRequest(string Name);
public record AlbumItemsRequest(string[] Ids);
public record FinalizeRequest(string Policy = "ask");

public record BackupRequest(string Target);
public record ShareRequest(string Folder,string Name,bool Download=true,int Days=30);
public record VpnRequest(bool Enabled);
public record RenameRequest(string[] Ids,string Prefix,int Start=1);
public record CollectionRequest(string Name,LibraryQuery Query);

public record AudioTagsRequest(string Title,string Artist,string Album,string? CoverId);


public record WriteTagsRequest(string Token,bool Confirmed);
public record BackgroundRequest(bool Paused);
