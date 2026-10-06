using LocalCloud.Core;
using LocalCloud.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
var root = Path.Combine(Path.GetTempPath(), "LocalCloud-tests-" + Guid.NewGuid().ToString("N")); var assertions = 0;
void Assert(bool condition, string label) { assertions++; if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { Assert(true, label); return; } throw new Exception("FAIL: " + label); }
try
{
    var paths = new SafePaths(root); var db = new Database(paths); var library = new Library(paths, db);
    foreach (var bad in new[] { "../outside", "Photos/../../escape", "/etc/passwd", "C:\\Windows", "Photos/.localcloud/file", "Photos/CON.txt", "Photos/file:stream", "Photos/a\\..\\b", "Photos/foo.", "Photos/NUL", "Photos/\u0000bad" }) Reject(() => paths.Resolve(bad), "reject path " + bad);
    Assert(paths.Resolve("Photos/Сочи 2014").StartsWith(root), "Cyrillic safe path");
    if (!OperatingSystem.IsWindows()) { var link = Path.Combine(root, "Photos", "link"); Directory.CreateSymbolicLink(link, Path.GetTempPath()); Reject(() => paths.Resolve("Photos/link/outside.txt"), "reject symlink"); Directory.Delete(link); }
    Directory.CreateDirectory(paths.Resolve("Photos/Сочи 2014")); Directory.CreateDirectory(paths.Resolve("Files/Универ"));
    var content = System.Text.Encoding.UTF8.GetBytes("original bytes — ничего не менять"); var full = paths.Resolve("Photos/Сочи 2014/IMG_1234.jpg"); await File.WriteAllBytesAsync(full, content); var file = library.Index(full);
    Assert(db.Query(new()).Total == 1, "filesystem indexing"); Assert(db.Get(file.Id)!.Name == "IMG_1234.jpg", "original filename"); Assert(db.Rows("SELECT version FROM Migrations").Count == 4, "migrations recorded");
    var hash = await library.Hash(full); db.Execute("UPDATE Files SET hash=$hash WHERE id=$id", ("$hash", hash), ("$id", file.Id));
    await library.Operate(file.Id, "favorite"); Assert(db.Query(new("favorites")).Total == 1, "favorite persisted");
    var album = Guid.NewGuid().ToString("N"); db.Execute("INSERT INTO Albums VALUES($id,'Сочи')", ("$id", album)); db.Execute("INSERT INTO AlbumItems VALUES($album,$file)", ("$album", album), ("$file", file.Id)); Assert(db.Query(new("all", Album: album)).Total == 1, "virtual album membership");
    await library.Operate(file.Id, "rename", "Сочи.jpg"); Assert(File.Exists(paths.Resolve("Photos/Сочи 2014/Сочи.jpg")), "rename physical file");
    await library.Operate(file.Id, "move", "Files/Универ"); Assert(File.Exists(paths.Resolve("Files/Универ/Сочи.jpg")), "move physical file");
    var copy = await library.Operate(file.Id, "copy", "Files/Универ"); Assert(copy.Name == "Сочи (1).jpg" && File.Exists(paths.Resolve(copy.Path)), "copy collision suffix");
    await library.Operate(file.Id, "trash"); Assert(!File.Exists(paths.Resolve("Files/Универ/Сочи.jpg")) && db.Get(file.Id)!.Trashed, "trash preserves original"); Assert(db.Query(new("all")).Total == 1, "trash hidden from library"); await library.Operate(file.Id, "restore"); Assert(File.ReadAllBytes(paths.Resolve(db.Get(file.Id)!.Path)).SequenceEqual(content), "restore bytes intact");
    string Prepare(string name, byte[] bytes, long? expected = null) { var id = Guid.NewGuid().ToString("N"); File.WriteAllBytes(Path.Combine(paths.Internal, "uploads", id), bytes); db.Execute("INSERT INTO UploadSessions(id,name,folder,size,createdAt,status) VALUES($id,$name,'Photos/Сочи 2014',$size,$now,'uploaded')", ("$id", id), ("$name", name), ("$size", expected ?? bytes.LongLength), ("$now", DateTime.UtcNow.ToString("O"))); return id; }
    var dup = Prepare("IMG_1234.jpg", content); await library.FinalizeUpload(dup, "ask"); Assert((string)db.Rows("SELECT status FROM UploadSessions WHERE id=$id", ("$id", dup))[0]["status"]! == "duplicate", "duplicate SHA-256 detected"); await library.FinalizeUpload(dup, "skip"); Assert(!File.Exists(Path.Combine(paths.Internal, "uploads", dup)), "skip deletes temporary upload only");
    var unique = Prepare("new.txt", new byte[] { 1, 2, 3, 4 }); await library.FinalizeUpload(unique, "copy"); Assert(File.ReadAllBytes(paths.Resolve("Photos/Сочи 2014/new.txt")).SequenceEqual(new byte[] { 1, 2, 3, 4 }), "atomic upload finalize bytes intact"); await library.FinalizeUpload(unique, "copy"); Assert(Directory.GetFiles(paths.Resolve("Photos/Сочи 2014"), "new*").Length == 1, "finalize idempotent");
    var partial = Prepare("partial.mov", new byte[] { 1, 2 }, 99); try { await library.FinalizeUpload(partial, "copy"); throw new Exception("should reject incomplete"); } catch (ArgumentException) { Assert(!File.Exists(paths.Resolve("Photos/Сочи 2014/partial.mov")), "incomplete original not visible"); }
    var settings = new SettingsStore(Path.Combine(root, "settings.json")); settings.Save(settings.WithPin("123456") with { StorageRoot = root }); Assert(settings.CheckPin("123456") && !settings.CheckPin("123457"), "PIN hash verification");
    File.WriteAllText(settings.ConfigPath,File.ReadAllText(settings.ConfigPath).TrimEnd().TrimEnd('}')+",\"futurePreference\":{\"keep\":true}}");var extended=new SettingsStore(settings.ConfigPath);extended.Save(extended.Current with{AllowLan=true});Assert(System.Text.Json.JsonDocument.Parse(File.ReadAllText(settings.ConfigPath)).RootElement.GetProperty("futurePreference").GetProperty("keep").GetBoolean(),"unknown settings survive additive config migration");
    var media = new MediaProcessor(library, NullLogger<MediaProcessor>.Instance); var indexer = new Indexer(library, media, settings, NullLogger<Indexer>.Instance, new UpdateActivity()); File.WriteAllText(paths.Resolve("Files/Explorer.txt"), "added outside app"); await indexer.Scan(default); Assert(db.GetPath("Files/Explorer.txt") != null, "rescan adds Explorer file"); File.Delete(paths.Resolve("Files/Explorer.txt")); await indexer.Scan(default); Assert(db.GetPath("Files/Explorer.txt") == null, "rescan removes externally deleted file");
    var audioPath = paths.Resolve("Music/старый.mp3"); File.WriteAllBytes(audioPath, new byte[] { 1, 2, 3 }); var audio = library.Index(audioPath);
    Assert(audio.Kind == "audio" && audio.Mime == "audio/mpeg", "MP3 classification and streaming MIME");
    db.Execute("UPDATE Files SET kind='file',mime='application/octet-stream',favorite=1,hash='preserved-audio-hash',metadata='{}',livePartner='legacy-invalid-pair' WHERE id=$id", ("$id", audio.Id));
    db.Execute("INSERT INTO AlbumItems VALUES($album,$file)", ("$album", album), ("$file", audio.Id));
    var refreshed = library.Index(audioPath);
    Assert(refreshed.LivePartner == null && refreshed.Id == audio.Id && refreshed.Kind == "audio" && refreshed.Favorite && refreshed.Hash == "preserved-audio-hash", "existing MP3 reclassification preserves identity favorite and hash");
    Assert(db.Query(new("audio")).Total == 1 && db.Query(new("all", Album: album)).Total == 2, "audio query and existing album membership");
    await library.Operate(audio.Id, "trash"); await library.Operate(audio.Id, "restore"); Assert(db.Get(audio.Id)!.Kind == "audio" && File.Exists(audioPath), "audio trash and restore");
    var reopened = new Database(paths); Assert(reopened.Get(file.Id)!.Favorite && reopened.Albums()[0].Count == 2, "database survives reopen");
    await library.Operate(copy.Id, "trash"); await library.Operate(copy.Id, "purge"); Assert(db.Get(copy.Id) == null, "permanent purge only from trash");
    Assert(Discovery.Private(System.Net.IPAddress.Parse("192.168.1.2")) && !Discovery.Private(System.Net.IPAddress.Parse("8.8.8.8")), "LAN network boundary");
    var crashFile = library.Index(paths.Resolve("Photos/Сочи 2014/new.txt")); db.Execute("INSERT INTO FileJournal VALUES($id,'move','Files/recovered.txt',$now)", ("$id", crashFile.Id), ("$now", DateTime.UtcNow.ToString("O"))); File.Move(paths.Resolve(crashFile.Path), paths.Resolve("Files/recovered.txt")); library.RecoverOperations(); Assert(db.Get(crashFile.Id)!.Path == "Files/recovered.txt", "recover interrupted physical move without losing ID");
    Assert(db.Query(new("all", Search: "СОЧИ")).Total >= 1, "Cyrillic case-insensitive search");
    var features=new Features(library); var shared=System.Text.Json.JsonSerializer.SerializeToElement(features.CreateShare("Photos/Сочи 2014","Сочи",true,7));var token=shared.GetProperty("path").GetString()!.Split('/').Last();Assert(features.Share(token)["folder"] as string=="Photos/Сочи 2014","share resolves only selected folder");
    try{features.SharedFile(token,file.Id);throw new Exception("share escaped scope");}catch(FileNotFoundException){Assert(true,"share denies unrelated file ID");}
    Assert(Features.Radmin(System.Net.IPAddress.Parse("26.1.2.3"))&&!Features.Radmin(System.Net.IPAddress.Parse("8.8.8.8")),"explicit Radmin address boundary");
    Reject(()=>features.SaveRecipe(audio.Id,new()),"photo edits reject audio");
    var defaultNested=paths.Resolve("Photos/Сочи 2014/nested.mp3");File.WriteAllBytes(defaultNested,new byte[]{5,6});library.Index(defaultNested);Assert(!System.Text.Json.JsonSerializer.Serialize(features.OrganizePlan()).Contains("nested.mp3"),"organization preserves custom nested folders");
    var publicRelease = new LocalCloud.Updater.ReleaseManifest("LocalCloud", "1.0.0", 4, 4, "application-update", new(), "public", new() { "1.4.0" });
    Assert(LocalCloud.Updater.UpdateEngine.CanRenumber("1.4.0", null, 4, publicRelease), "documented development-to-public transition allowed");
    Assert(!LocalCloud.Updater.UpdateEngine.CanRenumber("1.4.0", "public", 4, publicRelease), "public downgrade cannot use development renumbering");
    Assert(!LocalCloud.Updater.UpdateEngine.CanRenumber("1.5.0", null, 4, publicRelease), "other development releases cannot downgrade");
    Assert(!LocalCloud.Updater.UpdateEngine.CanRenumber("1.4.0", null, 5, publicRelease), "newer database cannot renumber to older schema");
    Assert(!LocalCloud.Updater.UpdateEngine.CanRenumber("1.4.0", null, 4, publicRelease with { ReleaseChannel = null }), "unmarked package cannot bypass downgrade guard");
    Assert(!LocalCloud.Updater.UpdateEngine.CanRenumber("1.4.0", null, 4, publicRelease with { RenumberFrom = null }), "renumber source must be declared");
    Console.WriteLine($"\n{assertions} assertions passed.");
}
finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
