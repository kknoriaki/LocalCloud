using Microsoft.Data.Sqlite;
using LocalCloud.Core;
namespace LocalCloud.Infrastructure;

public sealed class Database
{
    readonly string connection;
    public Database(SafePaths paths)
    {
        Directory.CreateDirectory(paths.Internal);
        connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(paths.Internal, "database.sqlite"), Mode = SqliteOpenMode.ReadWriteCreate, Pooling = true }.ToString();
        using var db = Open();
        using (var exists = db.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Migrations'";
            if (Convert.ToInt64(exists.ExecuteScalar()) > 0)
            {
                exists.CommandText = "SELECT COALESCE(MAX(version),0) FROM Migrations";
                var current = Convert.ToInt32(exists.ExecuteScalar());
                if (current > 4) throw new InvalidOperationException("Эта база создана более новой версией LocalCloud. Обновите приложение; база не изменена.");
                if (current < 4)
                {
                    var backupPath = Path.Combine(paths.Internal, "database.sqlite.backup-upgrade-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
                    using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false }.ToString());
                    backup.Open(); db.BackupDatabase(backup);
                    using var check = backup.CreateCommand(); check.CommandText = "PRAGMA quick_check";
                    if ((string?)check.ExecuteScalar() != "ok") throw new IOException("Не удалось проверить резервную копию базы. Миграция не запущена.");
                }
            }
        }
        using (var pragmas = db.CreateCommand()) { pragmas.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=10000;"; pragmas.ExecuteNonQuery(); }
        using var transaction = db.BeginTransaction(); using var cmd = db.CreateCommand(); cmd.Transaction = transaction;
        cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS Migrations(version INTEGER PRIMARY KEY, applied TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS Files(id TEXT PRIMARY KEY,path TEXT NOT NULL UNIQUE,name TEXT NOT NULL,extension TEXT NOT NULL,mime TEXT NOT NULL,size INTEGER NOT NULL,kind TEXT NOT NULL,modifiedAt TEXT NOT NULL,importedAt TEXT NOT NULL,takenAt TEXT,hash TEXT,favorite INTEGER NOT NULL DEFAULT 0,trashed INTEGER NOT NULL DEFAULT 0,width INTEGER,height INTEGER,duration REAL,metadata TEXT,livePartner TEXT);
        CREATE INDEX IF NOT EXISTS IX_Files_Kind_Date ON Files(trashed,kind,takenAt,modifiedAt);
        CREATE INDEX IF NOT EXISTS IX_Files_Hash ON Files(size,hash);
        CREATE INDEX IF NOT EXISTS IX_Files_Favorite ON Files(trashed,favorite);
        CREATE INDEX IF NOT EXISTS IX_Files_Path ON Files(path);
        CREATE INDEX IF NOT EXISTS IX_Files_Media_Order ON Files(trashed,kind,COALESCE(takenAt,modifiedAt) DESC,id);
        CREATE INDEX IF NOT EXISTS IX_Files_All_Order ON Files(trashed,COALESCE(takenAt,modifiedAt) DESC,id);
        CREATE INDEX IF NOT EXISTS IX_Files_Favorite_Order ON Files(trashed,favorite,COALESCE(takenAt,modifiedAt) DESC,id);
        CREATE INDEX IF NOT EXISTS IX_Files_Recent_Order ON Files(trashed,importedAt DESC,id);
        CREATE TABLE IF NOT EXISTS Albums(id TEXT PRIMARY KEY,name TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS AlbumItems(albumId TEXT REFERENCES Albums(id) ON DELETE CASCADE,fileId TEXT REFERENCES Files(id) ON DELETE CASCADE,PRIMARY KEY(albumId,fileId));
        CREATE INDEX IF NOT EXISTS IX_AlbumItems_File ON AlbumItems(fileId);
        CREATE TABLE IF NOT EXISTS TrashEntries(fileId TEXT PRIMARY KEY REFERENCES Files(id) ON DELETE CASCADE,originalPath TEXT NOT NULL,storedPath TEXT NOT NULL,deletedAt TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS UploadSessions(id TEXT PRIMARY KEY,name TEXT NOT NULL,folder TEXT NOT NULL,size INTEGER NOT NULL,status TEXT NOT NULL DEFAULT 'uploading',hash TEXT,duplicateId TEXT,resultId TEXT,error TEXT,createdAt TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS FileJournal(fileId TEXT PRIMARY KEY,action TEXT NOT NULL,destination TEXT NOT NULL,startedAt TEXT NOT NULL);
        INSERT OR IGNORE INTO Migrations VALUES(1,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
        """; cmd.ExecuteNonQuery();
        using var columns = Command(db, "PRAGMA table_info(UploadSessions)"); columns.Transaction = transaction; using var columnReader = columns.ExecuteReader(); var hasFinalPath = false; while (columnReader.Read()) if (columnReader.GetString(1) == "finalPath") hasFinalPath = true; columnReader.Close();
        if (!hasFinalPath) { using var migration = Command(db, "ALTER TABLE UploadSessions ADD COLUMN finalPath TEXT;"); migration.Transaction = transaction; migration.ExecuteNonQuery(); }
        using var release = Command(db, "INSERT OR IGNORE INTO Migrations VALUES(2,strftime('%Y-%m-%dT%H:%M:%fZ','now')); CREATE TABLE IF NOT EXISTS ApplicationReleases(version TEXT PRIMARY KEY,firstStartedAt TEXT NOT NULL); INSERT OR IGNORE INTO ApplicationReleases VALUES('1.2.0',strftime('%Y-%m-%dT%H:%M:%fZ','now')); INSERT OR IGNORE INTO Migrations VALUES(3,strftime('%Y-%m-%dT%H:%M:%fZ','now'));");
        release.Transaction = transaction; release.ExecuteNonQuery();
        using var features = Command(db, """
        CREATE TABLE IF NOT EXISTS LocalCloudFeatures(key TEXT PRIMARY KEY,value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS LocalCloudShares(id TEXT PRIMARY KEY,tokenHash TEXT NOT NULL UNIQUE,name TEXT NOT NULL,folder TEXT NOT NULL,download INTEGER NOT NULL,expires TEXT NOT NULL,created TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS LocalCloudEdits(fileId TEXT PRIMARY KEY REFERENCES Files(id) ON DELETE CASCADE,recipe TEXT NOT NULL,updated TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS LocalCloudAudioLabels(fileId TEXT PRIMARY KEY REFERENCES Files(id) ON DELETE CASCADE,tags TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS LocalCloudCollections(id TEXT PRIMARY KEY,name TEXT NOT NULL,query TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS LocalCloudLiveAssets(fileId TEXT PRIMARY KEY REFERENCES Files(id) ON DELETE CASCADE,identifier TEXT,method TEXT NOT NULL DEFAULT 'none');
        CREATE INDEX IF NOT EXISTS IX_LocalCloudLiveAssets_Id ON LocalCloudLiveAssets(identifier);
        CREATE TABLE IF NOT EXISTS LocalCloudBackupFiles(fileId TEXT PRIMARY KEY,hash TEXT NOT NULL,size INTEGER NOT NULL,path TEXT NOT NULL,verified TEXT NOT NULL);
        INSERT OR IGNORE INTO Migrations VALUES(4,strftime('%Y-%m-%dT%H:%M:%fZ','now'));
        INSERT OR IGNORE INTO ApplicationReleases VALUES('1.0.0',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
        """); features.Transaction = transaction; features.ExecuteNonQuery(); transaction.Commit();

    }
    public SqliteConnection Open() { var c = new SqliteConnection(connection); c.Open(); c.CreateFunction<string?, string?>("lc_lower", text => text?.ToLowerInvariant(), true); using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;"; cmd.ExecuteNonQuery(); return c; }
    public int Execute(string sql, params (string, object?)[] parameters) { using var c = Open(); using var cmd = Command(c, sql, parameters); return cmd.ExecuteNonQuery(); }
    public static SqliteCommand Command(SqliteConnection c, string sql, params (string, object?)[] parameters) { var cmd = c.CreateCommand(); cmd.CommandText = sql; foreach (var (key, value) in parameters) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value); return cmd; }
    public List<Dictionary<string, object?>> Rows(string sql, params (string, object?)[] parameters) { using var c = Open(); using var cmd = Command(c, sql, parameters); using var r = cmd.ExecuteReader(); var rows = new List<Dictionary<string, object?>>(); while (r.Read()) { var row = new Dictionary<string, object?>(); for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i); rows.Add(row); } return rows; }
    static FileEntry Read(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt64(5), r.GetString(6), r.GetString(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.GetBoolean(11), r.GetBoolean(12), r.IsDBNull(13) ? null : r.GetInt32(13), r.IsDBNull(14) ? null : r.GetInt32(14), r.IsDBNull(15) ? null : r.GetDouble(15), r.IsDBNull(16) ? null : r.GetString(16), r.IsDBNull(17) ? null : r.GetString(17));
    public FileEntry? Get(string id,bool enrich=true) { using var c = Open(); using var cmd = Command(c, "SELECT * FROM Files WHERE id=$id", ("$id", id)); using var r = cmd.ExecuteReader(); if(!r.Read())return null;var result=Read(r);return enrich?Enrich(new[]{result})[0]:result; }
    public FileEntry? GetPath(string path,bool enrich=true) { using var c = Open(); using var cmd = Command(c, "SELECT * FROM Files WHERE path=$path", ("$path", path)); using var r = cmd.ExecuteReader(); if(!r.Read())return null;var result=Read(r);return enrich?Enrich(new[]{result})[0]:result; }
    public PagedFiles Query(LibraryQuery q)
    {
        var where = new List<string> { q.View == "trash" ? "trashed=1" : "trashed=0" }; var p = new List<(string, object?)>();
        if (q.View is "photos" or "videos" or "audio") { where.Add(q.View == "audio" ? "kind=$kind" : "kind=$kind AND (livePartner IS NULL OR kind='photo')"); p.Add(("$kind", q.View == "photos" ? "photo" : q.View == "audio" ? "audio" : "video")); }
        if (q.View == "favorites") where.Add("favorite=1");
        if (q.Folder != null) { where.Add("substr(path,1,length($prefix))=$prefix AND instr(substr(path,length($prefix)+1),'/')=0"); p.Add(("$prefix", q.Folder.Length > 0 ? q.Folder.TrimEnd('/') + "/" : "")); }
        if (!string.IsNullOrEmpty(q.Search)) { where.Add("(lc_lower(path) LIKE lc_lower($search) ESCAPE '!' OR EXISTS (SELECT 1 FROM json_each(CASE WHEN json_valid(metadata) THEN metadata ELSE '{}' END) WHERE lc_lower(CAST(value AS TEXT)) LIKE lc_lower($search) ESCAPE '!') OR id IN (SELECT fileId FROM LocalCloudAudioLabels,json_each(tags) WHERE lc_lower(CAST(json_each.value AS TEXT)) LIKE lc_lower($search) ESCAPE '!'))"); p.Add(("$search", "%" + q.Search.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_") + "%")); }
        if (q.Album != null) { where.Add("id IN (SELECT fileId FROM AlbumItems WHERE albumId=$album)"); p.Add(("$album", q.Album)); }
        if (q.Extension != null) { where.Add("extension=$ext"); p.Add(("$ext", q.Extension.ToLowerInvariant().TrimStart('.'))); }
        if (q.After != null) { where.Add("COALESCE(takenAt,modifiedAt)>=$after"); p.Add(("$after", q.After)); }
        if (q.Before != null) { where.Add("COALESCE(takenAt,modifiedAt)<=$before"); p.Add(("$before", q.Before)); }
        if (q.MinSize != null) { where.Add("size>=$min"); p.Add(("$min", q.MinSize)); }
        if (q.MaxSize != null) { where.Add("size<=$max"); p.Add(("$max", q.MaxSize)); }
        string Tag(string name)=>"COALESCE(NULLIF((SELECT json_extract(tags,'$."+name+"') FROM LocalCloudAudioLabels WHERE fileId=Files.id),''),NULLIF(json_extract(CASE WHEN json_valid(metadata) THEN metadata ELSE '{}' END,'$."+name+"'),''))";
        if(q.MissingTags)where.Add("kind='audio' AND ("+Tag("title")+" IS NULL OR "+Tag("artist")+" IS NULL)");
        var clause = string.Join(" AND ", where); using var c = Open(); using var count = Command(c, "SELECT COUNT(*) FROM Files WHERE " + clause, p.ToArray()); var total = (long)count.ExecuteScalar()!;
        var order = q.Sort switch { "title" => "lc_lower(COALESCE("+Tag("title")+",name)) ASC,id", "artist" => "lc_lower(COALESCE("+Tag("artist")+",'')) ASC,lc_lower(COALESCE("+Tag("title")+",name)),id", "album" => "lc_lower(COALESCE("+Tag("album")+",'')) ASC,lc_lower(COALESCE("+Tag("title")+",name)),id", "name" => "name COLLATE NOCASE ASC,id", "size" => "size DESC,id", "oldest" => "COALESCE(takenAt,modifiedAt) ASC,id", _ => q.View == "recent" ? "importedAt DESC,id" : "COALESCE(takenAt,modifiedAt) DESC,id" };
        var limit = Math.Clamp(q.Limit, 1, 240); p.Add(("$limit", limit)); p.Add(("$offset", Math.Max(0, q.Offset)));
        using var cmd = Command(c, $"SELECT * FROM Files WHERE {clause} ORDER BY {order} LIMIT $limit OFFSET $offset", p.ToArray()); using var reader = cmd.ExecuteReader(); var items = new List<FileEntry>(); while (reader.Read()) items.Add(Read(reader)); return new(Enrich(items), total, q.Offset, limit);
    }
    List<FileEntry> Enrich(IEnumerable<FileEntry> input)
    {
        var items=input.ToList();if(items.Count==0)return items;var keys=items.Select((f,i)=>("$p"+i,(object?)f.Id)).ToArray();var clauses=string.Join(",",keys.Select(k=>k.Item1));
        var tags=Rows("SELECT fileId,tags FROM LocalCloudAudioLabels WHERE fileId IN ("+clauses+")",keys).ToDictionary(r=>(string)r["fileId"]!,r=>(string)r["tags"]!);
        var live=Rows("SELECT fileId,method FROM LocalCloudLiveAssets WHERE fileId IN ("+clauses+")",keys).ToDictionary(r=>(string)r["fileId"]!,r=>(string)r["method"]!);
        return items.Select(f=>{var m=System.Text.Json.Nodes.JsonNode.Parse(f.Metadata??"{}");if(m is not System.Text.Json.Nodes.JsonObject obj)return f;if(tags.TryGetValue(f.Id,out var json)&&System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonObject overlay)foreach(var kv in overlay)obj[kv.Key]=kv.Value?.DeepClone();if(live.TryGetValue(f.Id,out var method))obj["LivePairMethod"]=method;return f with{Metadata=f.Metadata==null&&obj.Count==0?null:obj.ToJsonString()};}).ToList();
    }
    public void Upsert(FileEntry f) => Execute("""
    INSERT INTO Files VALUES($id,$path,$name,$extension,$mime,$size,$kind,$modified,$imported,$taken,$hash,$favorite,$trashed,$width,$height,$duration,$metadata,$live)
    ON CONFLICT(path) DO UPDATE SET size=excluded.size,modifiedAt=excluded.modifiedAt,hash=CASE WHEN Files.size!=excluded.size OR Files.modifiedAt!=excluded.modifiedAt THEN NULL ELSE Files.hash END,width=CASE WHEN Files.modifiedAt!=excluded.modifiedAt THEN NULL ELSE Files.width END,metadata=CASE WHEN Files.modifiedAt!=excluded.modifiedAt THEN NULL ELSE Files.metadata END
    """, ("$id", f.Id), ("$path", f.Path), ("$name", f.Name), ("$extension", f.Extension), ("$mime", f.Mime), ("$size", f.Size), ("$kind", f.Kind), ("$modified", f.ModifiedAt), ("$imported", f.ImportedAt), ("$taken", f.TakenAt), ("$hash", f.Hash), ("$favorite", f.Favorite), ("$trashed", f.Trashed), ("$width", f.Width), ("$height", f.Height), ("$duration", f.Duration), ("$metadata", f.Metadata), ("$live", f.LivePartner));
    public List<Album> Albums() => Rows("SELECT a.id,a.name,COUNT(f.id) AS count,(SELECT f2.id FROM AlbumItems ai2 JOIN Files f2 ON f2.id=ai2.fileId WHERE ai2.albumId=a.id AND f2.trashed=0 LIMIT 1) AS cover,(SELECT f2.metadata IS NOT NULL FROM AlbumItems ai2 JOIN Files f2 ON f2.id=ai2.fileId WHERE ai2.albumId=a.id AND f2.trashed=0 LIMIT 1) AS coverReady FROM Albums a LEFT JOIN AlbumItems ai ON ai.albumId=a.id LEFT JOIN Files f ON f.id=ai.fileId AND f.trashed=0 GROUP BY a.id ORDER BY a.name").Select(r => new Album((string)r["id"]!, (string)r["name"]!, Convert.ToInt32(r["count"]), r["cover"] as string, Convert.ToInt32(r["coverReady"] ?? 0) == 1)).ToList();
}
