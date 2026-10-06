using System.Security.Cryptography;
using LocalCloud.Core;
namespace LocalCloud.Infrastructure;

public sealed class Library
{
    public SafePaths Paths { get; }
    public Database Db { get; }
    public SemaphoreSlim Mutation { get; } = new(1, 1);
    public event Action? Changed;
    public Library(SafePaths paths, Database db) { Paths = paths; Db = db; foreach (var d in new[] { "Photos", "Videos", "Music", "Files" }) Directory.CreateDirectory(Paths.Resolve(d)); foreach (var d in new[] { "uploads", "trash", "cache/thumbnails/256", "cache/thumbnails/512", "cache/thumbnails/1024", "logs" }) Directory.CreateDirectory(Path.Combine(Paths.Internal, d)); RecoverOperations(); }
    public void Notify() => Changed?.Invoke();
    public static (string Kind, string Mime) Type(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ("photo", "image/jpeg"),
        ".png" => ("photo", "image/png"),
        ".webp" => ("photo", "image/webp"),
        ".gif" => ("photo", "image/gif"),
        ".heic" or ".heif" => ("photo", "image/heic"),
        ".tif" or ".tiff" => ("photo", "image/tiff"),
        ".bmp" => ("photo", "image/bmp"),
        ".avif" => ("photo", "image/avif"),
        ".mp4" or ".m4v" => ("video", "video/mp4"),
        ".mov" => ("video", "video/quicktime"),
        ".webm" => ("video", "video/webm"),
        ".mkv" => ("video", "video/x-matroska"),
        ".avi" => ("video", "video/x-msvideo"),
        ".mp3" => ("audio", "audio/mpeg"),
        ".m4a" => ("audio", "audio/mp4"),
        ".aac" => ("audio", "audio/aac"),
        ".wav" => ("audio", "audio/wav"),
        ".flac" => ("audio", "audio/flac"),
        ".ogg" or ".oga" => ("audio", "audio/ogg"),
        ".opus" => ("audio", "audio/ogg"),
        ".aiff" or ".aif" => ("audio", "audio/aiff"),
        ".pdf" => ("file", "application/pdf"),
        ".txt" => ("file", "text/plain"),
        _ => ("file", "application/octet-stream")
    };
    public FileEntry Index(string full)
    {
        var info = new FileInfo(full); var rel = Paths.Relative(full); Paths.Resolve(rel, false); var previous = Db.GetPath(rel,false); var type = Type(info.Extension);
        var f = previous ?? new FileEntry(Guid.NewGuid().ToString("N"), rel, info.Name, info.Extension.TrimStart('.').ToLowerInvariant(), type.Mime, info.Length, type.Kind, info.LastWriteTimeUtc.ToString("O"), DateTime.UtcNow.ToString("O"), null, null, false, false, null, null, null, null, null);
        // Refresh classifications after an application update without changing IDs, hashes or collections.
        if (previous != null && (previous.Kind != type.Kind || previous.Mime != type.Mime))
        {
            Db.Execute("UPDATE Files SET kind=$kind,mime=$mime,metadata=NULL WHERE id=$id", ("$kind", type.Kind), ("$mime", type.Mime), ("$id", previous.Id));
            if (type.Kind == "audio") Db.Execute("UPDATE Files SET livePartner=NULL WHERE id=$id OR livePartner=$id", ("$id", previous.Id));
            previous = Db.Get(previous.Id);
        }
        if (previous != null && previous.Size == info.Length && previous.ModifiedAt == info.LastWriteTimeUtc.ToString("O")) return previous;
        if (previous != null) { ClearThumbnails(previous.Id); Db.Execute("DELETE FROM LocalCloudLiveAssets WHERE fileId=$id; UPDATE Files SET livePartner=NULL WHERE id=$id OR livePartner=$id", ("$id",previous.Id)); } Db.Upsert(f with { Size = info.Length, ModifiedAt = info.LastWriteTimeUtc.ToString("O") }); return Db.Get(f.Id,false)!;
    }
    public List<FolderEntry> Folders(string folder = "") => Directory.EnumerateDirectories(Paths.Resolve(folder)).Where(d => Path.GetFileName(d) != ".localcloud" && (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0).Select(d => new FolderEntry(Paths.Relative(d), Path.GetFileName(d))).OrderBy(d => d.Name).ToList();
    public async Task<FileEntry> Operate(string id, string action, string? target = null)
    {
        await Mutation.WaitAsync(); try
        {
            var original = Db.Get(id) ?? throw new FileNotFoundException("Файл не найден."); var companion = original.LivePartner == null ? null : Db.Get(original.LivePartner);
            var result = OperateSingle(id, action, target);
            if (companion != null && action is "trash" or "restore" or "move" or "copy" or "purge")
            {
                if (action != "trash" || !companion.Trashed) OperateSingle(companion.Id, action, target);
            }
            return result;
        }
        finally { Mutation.Release(); }
    }
    FileEntry OperateSingle(string id, string action, string? target = null)
    {
        {
            var f = Db.Get(id) ?? throw new FileNotFoundException("Файл не найден.");
            if (action == "favorite") { Db.Execute("UPDATE Files SET favorite=1-favorite WHERE id=$id", ("$id", id)); Notify(); return Db.Get(id)!; }
            if (action == "restore")
            {
                var trash = Db.Rows("SELECT * FROM TrashEntries WHERE fileId=$id", ("$id", id)).FirstOrDefault() ?? throw new ArgumentException("Файл не находится в корзине.");
                var original = (string)trash["originalPath"]!; var parent = Path.GetDirectoryName(original)!.Replace('\\', '/'); Directory.CreateDirectory(Paths.Resolve(parent)); var dest = Paths.Unique(parent, Path.GetFileName(original));
                Journal(id, "restore", Paths.Relative(dest)); File.Move(Path.Combine(Paths.Internal, "trash", (string)trash["storedPath"]!), dest);
                using var c = Db.Open(); using var tx = c.BeginTransaction(); using var cmd = Database.Command(c, "UPDATE Files SET trashed=0,path=$path,name=$name WHERE id=$id; DELETE FROM TrashEntries WHERE fileId=$id", ("$path", Paths.Relative(dest)), ("$name", Path.GetFileName(dest)), ("$id", id)); cmd.Transaction = tx; cmd.ExecuteNonQuery(); tx.Commit();
            }
            else if (action == "purge")
            {
                if (!f.Trashed) throw new ArgumentException("Сначала переместите файл в корзину.");
                var t = Db.Rows("SELECT storedPath FROM TrashEntries WHERE fileId=$id", ("$id", id)).First(); Journal(id, "purge", (string)t["storedPath"]!); File.Delete(Path.Combine(Paths.Internal, "trash", (string)t["storedPath"]!)); Db.Execute("DELETE FROM Files WHERE id=$id", ("$id", id)); ClearThumbnails(id); Notify(); return f;
            }
            else
            {
                if (f.Trashed) throw new ArgumentException("Сначала восстановите файл.");
                var source = Paths.Resolve(f.Path, false);
                if (action == "trash")
                {
                    var stored = id + "_" + f.Name; Journal(id, "trash", stored); File.Move(source, Path.Combine(Paths.Internal, "trash", stored));
                    using var c = Db.Open(); using var tx = c.BeginTransaction(); using var cmd = Database.Command(c, "INSERT INTO TrashEntries VALUES($id,$path,$stored,$now); UPDATE Files SET trashed=1 WHERE id=$id", ("$id", id), ("$path", f.Path), ("$stored", stored), ("$now", DateTime.UtcNow.ToString("O"))); cmd.Transaction = tx; cmd.ExecuteNonQuery(); tx.Commit();
                }
                else
                {
                    if (target == null) throw new ArgumentException("Выберите папку или имя.");
                    var dest = action == "rename" ? Paths.Resolve(Path.Combine(Path.GetDirectoryName(f.Path)!, SafePaths.FileName(target)), false) : Paths.Unique(target, f.Name);
                    if (!Directory.Exists(Path.GetDirectoryName(dest))) throw new DirectoryNotFoundException("Папка не найдена.");
                    if (action == "copy") { File.Copy(source, dest, false); f = Index(dest); } else if (action == "move" || action == "rename") { Journal(id, "move", Paths.Relative(dest)); File.Move(source, dest, false); var type = Type(Path.GetExtension(dest)); Db.Execute("UPDATE Files SET path=$path,name=$name,extension=$ext,kind=$kind,mime=$mime WHERE id=$id", ("$path", Paths.Relative(dest)), ("$name", Path.GetFileName(dest)), ("$ext", Path.GetExtension(dest).TrimStart('.').ToLowerInvariant()), ("$kind", type.Kind), ("$mime", type.Mime), ("$id", id)); } else throw new ArgumentException("Неизвестная операция.");
                }
            }
            Db.Execute("DELETE FROM FileJournal WHERE fileId=$id", ("$id", id)); Notify(); return Db.Get(f.Id)!;
        }
    }
    public async Task<object> FolderOperate(string path, string action, string? target)
    {
        await Mutation.WaitAsync(); try
        {
            var source = Paths.Resolve(path, false); if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Папка не найдена.");
            var physical = Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }).ToList();
            var indexed = physical.Select(Index).ToList();
            if (action == "trash")
            {
                foreach (var f in indexed) OperateSingle(f.Id, "trash"); Directory.Delete(source, true); Notify(); return new { ids = indexed.Select(f => f.Id).ToArray() };
            }
            if (target == null) throw new ArgumentException("Выберите папку или имя.");
            var destination = action == "rename" ? Paths.Resolve(Path.Combine(Path.GetDirectoryName(path)!, SafePaths.FileName(target)), false) : Paths.Unique(target, Path.GetFileName(path));
            if (destination.StartsWith(source + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new ArgumentException("Нельзя переместить папку внутрь себя.");
            if (action == "copy")
            {
                var stage = Path.Combine(Paths.Internal, "uploads", "folder-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
                try { foreach (var original in physical) { Paths.Resolve(Paths.Relative(original), false); var dest = Path.Combine(stage, Path.GetRelativePath(source, original)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(original, dest, false); } foreach (var d in Directory.EnumerateDirectories(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })) Directory.CreateDirectory(Path.Combine(stage, Path.GetRelativePath(source, d))); Directory.Move(stage, destination); foreach (var f in physical) Index(Path.Combine(destination, Path.GetRelativePath(source, f))); } finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            }
            else if (action is "move" or "rename")
            {
                foreach (var f in indexed) Journal(f.Id, "move", Paths.Relative(Path.Combine(destination, Path.GetRelativePath(source, Paths.Resolve(f.Path, false)))));
                Directory.Move(source, destination); RecoverOperations();
            }
            else throw new ArgumentException("Неизвестная операция.");
            Notify(); return new { path = Paths.Relative(destination) };
        }
        finally { Mutation.Release(); }
    }
    void Journal(string id, string action, string destination) => Db.Execute("INSERT OR REPLACE INTO FileJournal VALUES($id,$action,$destination,$now)", ("$id", id), ("$action", action), ("$destination", destination), ("$now", DateTime.UtcNow.ToString("O")));
    public void RecoverOperations()
    {
        foreach (var row in Db.Rows("SELECT * FROM FileJournal"))
        {
            var id = (string)row["fileId"]!; var f = Db.Get(id); if (f == null) { Db.Execute("DELETE FROM FileJournal WHERE fileId=$id", ("$id", id)); continue; }
            var action = (string)row["action"]!; var destination = (string)row["destination"]!;
            if (action == "purge") { SafePaths.FileName(destination); if (!File.Exists(Path.Combine(Paths.Internal, "trash", destination))) Db.Execute("DELETE FROM Files WHERE id=$id", ("$id", id)); }
            else if (action == "trash")
            {
                SafePaths.FileName(destination); if (!File.Exists(Paths.Resolve(f.Path, false)) && File.Exists(Path.Combine(Paths.Internal, "trash", destination))) { Db.Execute("INSERT OR IGNORE INTO TrashEntries VALUES($id,$path,$stored,$now); UPDATE Files SET trashed=1 WHERE id=$id", ("$id", id), ("$path", f.Path), ("$stored", destination), ("$now", row["startedAt"])); }
            }
            else if (File.Exists(Paths.Resolve(destination, false)) && (action == "move" ? !File.Exists(Paths.Resolve(f.Path, false)) : !Db.Rows("SELECT storedPath FROM TrashEntries WHERE fileId=$id", ("$id", id)).Any(t => File.Exists(Path.Combine(Paths.Internal, "trash", (string)t["storedPath"]!))))) { var type = Type(Path.GetExtension(destination)); Db.Execute("UPDATE Files SET path=$path,name=$name,extension=$ext,kind=$kind,mime=$mime,trashed=0 WHERE id=$id; DELETE FROM TrashEntries WHERE fileId=$id", ("$path", destination), ("$name", Path.GetFileName(destination)), ("$ext", Path.GetExtension(destination).TrimStart('.')), ("$kind", type.Kind), ("$mime", type.Mime), ("$id", id)); }
            Db.Execute("DELETE FROM FileJournal WHERE fileId=$id", ("$id", id));
        }
        foreach (var row in Db.Rows("SELECT * FROM UploadSessions WHERE status='finalizing' AND finalPath IS NOT NULL"))
        {
            var destination = (string)row["finalPath"]!; var full = Paths.Resolve(destination, false); if (File.Exists(full) && !File.Exists(Path.Combine(Paths.Internal, "uploads", (string)row["id"]!))) { var f = Index(full); Db.Execute("UPDATE Files SET hash=$hash WHERE id=$file; UPDATE UploadSessions SET status='complete',resultId=$file WHERE id=$id", ("$hash", row["hash"]), ("$file", f.Id), ("$id", row["id"])); CleanupUpload((string)row["id"]!); }
        }
    }
    public void ClearThumbnails(string id) { foreach (var size in new[] { 256, 512, 1024 }) File.Delete(Path.Combine(Paths.Internal, "cache", "thumbnails", size.ToString(), id + ".jpg")); }
    public async Task<string> Hash(string path, CancellationToken ct = default) { await using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan); return Convert.ToHexString(await SHA256.HashDataAsync(s, ct)); }
    public async Task<object> FinalizeUpload(string id, string policy)
    {
        var initial = Db.Rows("SELECT * FROM UploadSessions WHERE id=$id", ("$id", id)).FirstOrDefault() ?? throw new FileNotFoundException("Загрузка не найдена.");
        var initialStatus = (string)initial["status"]!; string? verifiedHash = initial["hash"] as string;
        if (initialStatus is not ("uploaded" or "duplicate" or "finalizing" or "complete" or "skipped")) throw new ArgumentException("Загрузка ещё не завершена.");
        if (verifiedHash == null && initialStatus == "uploaded") verifiedHash = await Hash(Path.Combine(Paths.Internal, "uploads", id));
        await Mutation.WaitAsync(); try
        {
            var row = Db.Rows("SELECT * FROM UploadSessions WHERE id=$id", ("$id", id)).FirstOrDefault() ?? throw new FileNotFoundException("Загрузка не найдена.");
            if ((string)row["status"]! == "finalizing") { RecoverOperations(); row = Db.Rows("SELECT * FROM UploadSessions WHERE id=$id", ("$id", id)).First(); }
            if ((string)row["status"]! is "complete" or "skipped") return row;
            var source = Path.Combine(Paths.Internal, "uploads", id);
            var size = Convert.ToInt64(row["size"]); if (!File.Exists(source) || new FileInfo(source).Length != size) throw new ArgumentException("Загрузка ещё не завершена.");
            var hash = row["hash"] as string ?? verifiedHash ?? await Hash(source);
            var duplicate = Db.Rows("SELECT id,path FROM Files WHERE hash=$hash AND size=$size AND trashed=0 LIMIT 1", ("$hash", hash), ("$size", size)).FirstOrDefault();
            if (duplicate != null && policy == "ask") { Db.Execute("UPDATE UploadSessions SET status='duplicate',hash=$hash,duplicateId=$dup WHERE id=$id", ("$id", id), ("$hash", hash), ("$dup", duplicate["id"])); return new { status = "duplicate", path = duplicate["path"], id, duplicateId = duplicate["id"] }; }
            if (duplicate != null && policy == "skip") { Db.Execute("UPDATE UploadSessions SET status='skipped',hash=$hash,resultId=$result WHERE id=$id", ("$id", id), ("$hash", hash), ("$result", duplicate["id"])); CleanupUpload(id); return new { status = "skipped", id, resultId = duplicate["id"] }; }
            var folder = (string)row["folder"]!; if (!Directory.Exists(Paths.Resolve(folder))) throw new DirectoryNotFoundException("Папка загрузки не найдена."); var destination = Paths.Unique(folder, (string)row["name"]!);
            // Same-volume rename: complete original becomes visible in one operation, never as a partial file.
            Db.Execute("UPDATE UploadSessions SET status='finalizing',hash=$hash,finalPath=$path WHERE id=$id", ("$hash", hash), ("$path", Paths.Relative(destination)), ("$id", id));
            File.Move(source, destination, false); var file = Index(destination); Db.Execute("UPDATE Files SET hash=$hash WHERE id=$id", ("$id", file.Id), ("$hash", hash)); Db.Execute("UPDATE UploadSessions SET status='complete',hash=$hash,resultId=$result WHERE id=$id", ("$id", id), ("$hash", hash), ("$result", file.Id)); CleanupUpload(id); Notify(); return new { status = "complete", id, resultId = file.Id, path = file.Path };
        }
        finally { Mutation.Release(); }
    }
    public void CleanupUpload(string id) { if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-fA-F0-9]{32}$")) throw new ArgumentException("Недопустимая загрузка."); foreach (var path in Directory.EnumerateFiles(Path.Combine(Paths.Internal, "uploads"), id + "*")) File.Delete(path); }
}
