using System.Security.Cryptography;
using LocalCloud.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
namespace LocalCloud.Infrastructure;

public sealed class VerifiedBackup(Library library,Features features,SettingsStore settings,UpdateActivity activity,ILogger<VerifiedBackup> log) : BackgroundService
{
    readonly object gate=new();CancellationTokenSource? job;Task? running;
    string phase="idle",current="",error="";int completed,total;long bytes;DateTime? last;
    public object Status() { lock(gate)return new{phase,current,error,completed,total,bytes,last=last?.ToString("O")??features.Get("backup-last"),target=features.Get("backup-target"),verified=Convert.ToInt32(library.Db.Rows("SELECT COUNT(*) AS n FROM LocalCloudBackupFiles b JOIN Files f ON f.id=b.fileId WHERE f.trashed=0 AND f.hash=b.hash AND f.size=b.size AND f.path=b.path")[0]["n"])}; }
    public void Start(string target,CancellationToken host)
    {
        lock(gate)
        {
            if(running is {IsCompleted:false})throw new ArgumentException("Резервное копирование уже выполняется.");
            target=Path.GetFullPath(target);var root=library.Paths.Root;var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
            if(Inside(root,target,comparison)||Inside(target,root,comparison))throw new ArgumentException("Папка резервной копии должна быть за пределами библиотеки.");
            RejectLinks(target);Directory.CreateDirectory(target);RejectLinks(target);var marker=Path.Combine(target,".localcloud-backup");RejectLinks(marker);Directory.CreateDirectory(marker);
            if(features.Get("backup-target") is {} prior&&!prior.Equals(target,comparison))library.Db.Execute("DELETE FROM LocalCloudBackupFiles");
            features.Set("backup-target",target);phase="running";current="";error="";completed=0;total=0;bytes=0;job?.Dispose();job=CancellationTokenSource.CreateLinkedTokenSource(host);running=Task.Run(()=>Copy(target,job.Token));
        }
    }
    public void Cancel(){lock(gate)job?.Cancel();}
    static bool Inside(string root,string child,StringComparison c)=>child.Equals(root,c)||child.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,c);
    public static void RejectLinks(string path){for(var p=Path.GetFullPath(path);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p)!)if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Для резервной копии нельзя использовать ссылки или junction.");}
    async Task Copy(string target,CancellationToken ct)
    {
        if(!activity.Enter()){lock(gate){phase="failed";error="Сервер готовится к обновлению.";}return;}
        try
        {
            var files=Directory.EnumerateFiles(library.Paths.Root,"*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false}).Where(p=>!p.StartsWith(library.Paths.Internal+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).ToList();lock(gate)total=files.Count;
            foreach(var source in files)
            {
                ct.ThrowIfCancellationRequested();var relative=library.Paths.Relative(source);library.Paths.Resolve(relative,false);var info=new FileInfo(source);var modified=info.LastWriteTimeUtc;var size=info.Length;lock(gate)current=relative;var f=library.Index(source);var destination=Path.Combine(target,relative.Replace('/',Path.DirectorySeparatorChar));RejectLinks(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var hash=await library.Hash(source,ct);if(File.Exists(destination)&&new FileInfo(destination).Length==size&&await library.Hash(destination,ct)==hash){info.Refresh();if(info.Length!=size||info.LastWriteTimeUtc!=modified)throw new IOException("Файл изменился во время проверки: "+relative);Record(f,hash,size,relative);lock(gate){completed++;bytes+=size;}continue;}
                var temporary=destination+".localcloud-"+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    await using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan))await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan)){await input.CopyToAsync(output,ct);await output.FlushAsync(ct);output.Flush(true);}
                    info.Refresh();if(info.Length!=size||info.LastWriteTimeUtc!=modified||await library.Hash(temporary,ct)!=hash)throw new IOException("Файл изменился при копировании: "+relative+". Повторите резервное копирование.");
                    if(File.Exists(destination)){var previous=Path.Combine(target,".localcloud-backup","versions",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"),relative);RejectLinks(previous);Directory.CreateDirectory(Path.GetDirectoryName(previous)!);File.Move(destination,previous);}
                    File.Move(temporary,destination);File.SetLastWriteTimeUtc(destination,modified);Record(f,hash,size,relative);lock(gate){completed++;bytes+=size;}
                }
                finally{File.Delete(temporary);}
            }
            var snapshot=Path.Combine(target,".localcloud-backup","metadata-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".sqlite");RejectLinks(snapshot);using(var src=library.Db.Open())using(var dst=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=snapshot,Pooling=false}.ToString())){dst.Open();src.BackupDatabase(dst);using var check=dst.CreateCommand();check.CommandText="PRAGMA quick_check";if((string?)check.ExecuteScalar()!="ok")throw new IOException("Проверка копии SQLite не пройдена.");}
            var configCopy=Path.Combine(target,".localcloud-backup","settings.json.backup");RejectLinks(configCopy);File.Copy(settings.ConfigPath,configCopy,true);
            features.Set("backup-last",DateTime.UtcNow.ToString("O"));lock(gate){phase="complete";last=DateTime.UtcNow;current="";}
        }
        catch(OperationCanceledException){lock(gate){phase="cancelled";current="";}}
        catch(Exception e){log.LogWarning(e,"Verified backup failed");lock(gate){phase="failed";error=e is IOException?e.Message:"Не удалось создать копию. Проверьте подключение диска/NAS и свободное место.";}}
        finally{activity.Leave();}
    }
    void Record(FileEntry f,string hash,long size,string path){library.Db.Execute("UPDATE Files SET hash=$h WHERE id=$id AND modifiedAt=$modified",("$h",hash),("$id",f.Id),("$modified",f.ModifiedAt));library.Db.Execute("INSERT INTO LocalCloudBackupFiles VALUES($id,$h,$size,$path,$now) ON CONFLICT(fileId) DO UPDATE SET hash=$h,size=$size,path=$path,verified=$now",("$id",f.Id),("$h",hash),("$size",size),("$path",path),("$now",DateTime.UtcNow.ToString("O")));}
    protected override async Task ExecuteAsync(CancellationToken stoppingToken){try{await Task.Delay(Timeout.Infinite,stoppingToken);}catch(OperationCanceledException){}Cancel();Task? pending;lock(gate)pending=running;if(pending!=null)await pending;}
    public override void Dispose(){job?.Cancel();job?.Dispose();base.Dispose();}
}
