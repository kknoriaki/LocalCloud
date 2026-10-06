using LocalCloud.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace LocalCloud.Infrastructure;

public sealed class Indexer(Library library, MediaProcessor media, SettingsStore settings, ILogger<Indexer> log, UpdateActivity activity) : BackgroundService
{
    volatile bool requested = true; volatile bool paused;
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,DateTime> changes=new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,bool> knownDirectories=new();
    public string CurrentTask { get; private set; } = "Готов";
    public string CurrentFile { get; private set; } = "";
    public long FullScans { get; private set; }
    public long IncrementalFiles { get; private set; }
    readonly object metricsGate=new(); DateTime lastMetrics=DateTime.UtcNow; TimeSpan lastCpu=System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
    public object Status()
    {
        lock(metricsGate){using var p=System.Diagnostics.Process.GetCurrentProcess();var now=DateTime.UtcNow;var elapsed=(now-lastMetrics).TotalSeconds;var cpu=elapsed>0?(p.TotalProcessorTime-lastCpu).TotalSeconds/elapsed/Environment.ProcessorCount*100:0;lastCpu=p.TotalProcessorTime;lastMetrics=now;
        return new{paused,scanning=Scanning,task=paused?"Фоновые задачи приостановлены":CurrentTask,file=CurrentFile,lastScan=LastScan,fullScans=FullScans,incrementalFiles=IncrementalFiles,cpuPercent=Math.Round(Math.Clamp(cpu,0,100),1),memoryBytes=p.WorkingSet64,processId=p.Id,queuedChanges=changes.Count,mediaProcesses=MediaProcessor.ActiveProcesses()};}
    }
    async Task ProcessChanges(CancellationToken ct)
    {
        var changed=false;
        foreach(var pair in changes.Where(p=>DateTime.UtcNow-p.Value>TimeSpan.FromSeconds(2)).Take(32))
        {
            if(!changes.TryRemove(pair.Key,out _))continue;
            await library.Mutation.WaitAsync(ct);
            try { var relative=library.Paths.Relative(pair.Key);library.Paths.Resolve(relative,false);
                if(File.Exists(pair.Key)){library.Index(pair.Key);IncrementalFiles++;changed=true;}
                else if(library.Db.GetPath(relative) is {} f){library.Db.Execute("DELETE FROM Files WHERE id=$id AND trashed=0",("$id",f.Id));changed=true;}
                else if(knownDirectories.TryRemove(pair.Key,out _))requested=true;
            }catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){changes[pair.Key]=DateTime.UtcNow;log.LogDebug(e,"File is still being written");}
            finally{library.Mutation.Release();}
        }
        if(changed)library.Notify();
    }
    public void Pause() => paused=true; public void Resume() => paused=false;
    readonly Dictionary<string,DateTime> retries=new();
    static bool Temporary(string path)=>new[]{".part",".partial",".crdownload",".download",".tmp"}.Any(ext=>path.EndsWith(ext,StringComparison.OrdinalIgnoreCase)) || Path.GetFileName(path).StartsWith(".localcloud-tags-",StringComparison.OrdinalIgnoreCase);
    readonly System.Collections.Concurrent.ConcurrentQueue<(string Old, string New)> renames = new();
    bool incomplete;
    public bool Scanning { get; private set; }
    public DateTime? LastScan { get; private set; }
    public void Request() => requested = true;
    public void Rebuild() { library.Db.Execute("UPDATE Files SET metadata=NULL WHERE kind!='file' AND trashed=0"); Request(); }
    public async Task Scan(CancellationToken ct)
    {
        await library.Mutation.WaitAsync(ct); Scanning = true; FullScans++; CurrentTask="Обход библиотеки";
        try
        {
            incomplete = false; while (renames.TryDequeue(out var rename)) { try { var old = library.Paths.Relative(rename.Old); var next = library.Paths.Relative(rename.New); library.Paths.Resolve(next); var f = library.Db.GetPath(old); if (f != null && File.Exists(rename.New)) { var atNew=library.Db.GetPath(next); if(atNew!=null&&atNew.Id!=f.Id) { using var merge=library.Db.Open();using var tx=merge.BeginTransaction();using var command=Database.Command(merge,"INSERT OR IGNORE INTO AlbumItems SELECT albumId,$old FROM AlbumItems WHERE fileId=$new; UPDATE Files SET favorite=MAX(favorite,$fav) WHERE id=$old; DELETE FROM Files WHERE id=$new",("$old",f.Id),("$new",atNew.Id),("$fav",atNew.Favorite?1:0));command.Transaction=tx;command.ExecuteNonQuery();tx.Commit(); } var type = Library.Type(Path.GetExtension(rename.New)); library.Db.Execute("UPDATE Files SET path=$new,name=$name,extension=$ext,kind=$kind,mime=$mime WHERE id=$id", ("$new", next), ("$name", Path.GetFileName(rename.New)), ("$ext", Path.GetExtension(rename.New).TrimStart('.').ToLowerInvariant()), ("$kind", type.Kind), ("$mime", type.Mime), ("$id", f.Id)); } else if (Directory.Exists(rename.New)) { library.Db.Execute("UPDATE Files SET path=$new || substr(path,length($old)+1) WHERE substr(path,1,length($old))=$old", ("$new", next.TrimEnd('/') + "/"), ("$old", old.TrimEnd('/') + "/")); } } catch (ArgumentException) { } }
            using var db = library.Db.Open(); using var create = db.CreateCommand(); create.CommandText = "DROP TABLE IF EXISTS temp.Seen; CREATE TEMP TABLE Seen(path TEXT PRIMARY KEY)"; create.ExecuteNonQuery();
            foreach (var full in Walk(library.Paths.Root))
            {
                ct.ThrowIfCancellationRequested(); try { var f = library.Index(full); using var seen = Database.Command(db, "INSERT INTO Seen VALUES($path)", ("$path", f.Path)); seen.ExecuteNonQuery(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { incomplete = true; log.LogWarning(e, "Indexing skipped an inaccessible file"); }
            }
            using var remove = db.CreateCommand(); remove.CommandText = "DELETE FROM Files WHERE trashed=0 AND path NOT IN (SELECT path FROM Seen)"; if (!incomplete) remove.ExecuteNonQuery(); LastScan = DateTime.UtcNow;
        }
        finally { library.Mutation.Release(); Scanning = false; CurrentTask="Готов"; library.Notify(); }
    }
    IEnumerable<string> Walk(string dir)
    {
        knownDirectories[dir]=true;
        string[] files, dirs; try { files = System.IO.Directory.GetFiles(dir); dirs = System.IO.Directory.GetDirectories(dir); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { incomplete = true; log.LogWarning(e, "Could not scan directory"); yield break; }
        foreach (var f in files) if (!Temporary(f) && (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0) yield return f;
        foreach (var d in dirs) if (Path.GetFileName(d) != ".localcloud" && (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0) foreach (var f in Walk(d)) yield return f;
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var watcher = new FileSystemWatcher(library.Paths.Root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size, InternalBufferSize = 64 * 1024 };
        void Changed(object? sender, FileSystemEventArgs e) { if (e.FullPath.StartsWith(library.Paths.Internal + Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)||Temporary(e.FullPath))return; if(Directory.Exists(e.FullPath)){if(e.ChangeType==WatcherChangeTypes.Created && knownDirectories.TryAdd(e.FullPath,true))requested=true;}else changes[e.FullPath]=DateTime.UtcNow; }
        watcher.Changed += Changed; watcher.Created += Changed; watcher.Deleted += Changed;
        watcher.Renamed += (_, e) => { if(e.FullPath.StartsWith(library.Paths.Internal + Path.DirectorySeparatorChar)||Temporary(e.FullPath))return; if(Temporary(e.OldFullPath)||e.OldFullPath.StartsWith(library.Paths.Internal + Path.DirectorySeparatorChar))changes[e.FullPath]=DateTime.UtcNow;else {renames.Enqueue((e.OldFullPath,e.FullPath));requested=true;} };
        watcher.Error += (_, _) => requested = true; watcher.EnableRaisingEvents = true;
        while (!ct.IsCancellationRequested)
        {
            var cycleDelay = 2000;
            if (paused || !activity.Enter()) { await Task.Delay(250, ct); continue; }
            try
            {
                if (requested || LastScan == null || DateTime.UtcNow - LastScan > TimeSpan.FromMinutes(30)) { requested = false; await Scan(ct); }
                await ProcessChanges(ct);
                foreach(var expiredRetry in retries.Where(p=>p.Value<=DateTime.UtcNow).Select(p=>p.Key).ToList())retries.Remove(expiredRetry);
                var skip=retries.Select((p,i)=>(Key:p.Key,Param:"$retry"+i)).ToList();var exclude=skip.Count==0?"":" AND id NOT IN ("+string.Join(",",skip.Select(p=>p.Param))+")";
                var pending = library.Db.Rows("SELECT id FROM Files WHERE trashed=0 AND (hash IS NULL OR (kind!='file' AND metadata IS NULL) OR (kind IN ('photo','video','audio') AND id NOT IN (SELECT fileId FROM LocalCloudLiveAssets)))"+exclude+" LIMIT 16",skip.Select(p=>(p.Param,(object?)p.Key)).ToArray());
                foreach (var row in pending) { if(paused)break;ct.ThrowIfCancellationRequested(); var f = library.Db.Get((string)row["id"]!); if(f==null || retries.TryGetValue(f.Id,out var retryAt)&&retryAt>DateTime.UtcNow)continue; try { CurrentFile=f.Name;CurrentTask=f.Hash==null?"Проверка оригинала":"Создание превью"; if (f.Hash == null) { var hash = await library.Hash(library.Paths.Resolve(f.Path, false), ct); library.Db.Execute("UPDATE Files SET hash=$hash WHERE id=$id AND modifiedAt=$modified", ("$hash", hash), ("$id", f.Id), ("$modified", f.ModifiedAt)); } if ((f.Kind != "file" && f.Metadata == null || f.Kind is "photo" or "video" or "audio" && library.Db.Rows("SELECT fileId FROM LocalCloudLiveAssets WHERE fileId=$id",("$id",f.Id)).Count==0)) await media.Process(f, ct); await Task.Delay(150,ct); } catch (Exception e) when (e is IOException or ArgumentException) { retries[f.Id]=DateTime.UtcNow.AddSeconds(15); log.LogWarning(e, "File changed while indexing"); changes[library.Paths.Resolve(f.Path,false)]=DateTime.UtcNow; } }
                if (pending.Count > 0) library.Notify();
                var expired = library.Db.Rows("SELECT fileId FROM TrashEntries WHERE deletedAt<$date", ("$date", DateTime.UtcNow.AddDays(-settings.Current.TrashRetentionDays).ToString("O")));
                foreach (var row in expired) if(library.Db.Get((string)row["fileId"]!) is {Trashed:true}) await library.Operate((string)row["fileId"]!, "purge");
                CurrentFile="";CurrentTask="Готов";cycleDelay = pending.Count > 0 ? 750 : 3000;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { log.LogError(e, "Background indexing failed"); await Task.Delay(3000, ct); }
            finally { activity.Leave(); }
            await Task.Delay(cycleDelay, ct);
        }
    }
}
