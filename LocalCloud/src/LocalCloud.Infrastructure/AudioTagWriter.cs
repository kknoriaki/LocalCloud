using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalCloud.Core;
namespace LocalCloud.Infrastructure;

public sealed record AudioTagValues(string Title,string Artist,string Album,string? CoverId);
public sealed class AudioTagWriter(Library library,MediaProcessor media)
{
    sealed record Plan(string Id,string Hash,string Modified,AudioTagValues Values,DateTime Expires);
    readonly ConcurrentDictionary<string,Plan> plans=new();
    void Validate(FileEntry f,AudioTagValues tags)
    {
        if(f.Trashed||f.Extension!="mp3")throw new ArgumentException("Запись встроенных тегов доступна только для MP3.");
        if(media.FFmpeg==null)throw new ArgumentException("Для записи тегов необходим установленный FFmpeg.");
        if(new[]{tags.Title,tags.Artist,tags.Album}.Any(v=>v==null||v.Length>250||v.Contains('\0')))throw new ArgumentException("Теги должны быть не длиннее 250 символов.");
        if(tags.CoverId!=null&&library.Db.Get(tags.CoverId) is not{Kind:"photo",Trashed:false})throw new ArgumentException("Выберите обложку из фотографий библиотеки.");
    }
    public async Task<object> Preview(string id,AudioTagValues tags,CancellationToken ct)
    {
        var f=library.Db.Get(id)??throw new FileNotFoundException("Трек не найден.");Validate(f,tags);
        foreach(var old in plans.Where(p=>p.Value.Expires<DateTime.UtcNow).Select(p=>p.Key))plans.TryRemove(old,out _);
        if(plans.Count>100)throw new ArgumentException("Закройте старые предпросмотры и повторите позже.");
        var hash=await library.Hash(library.Paths.Resolve(f.Path,false),ct);var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        plans[token]=new(id,hash,f.ModifiedAt,tags,DateTime.UtcNow.AddMinutes(10));
        var before=new Dictionary<string,string>();
        if(media.FFprobe!=null){using var probe=JsonDocument.Parse(await MediaProcessor.Run(media.FFprobe,new[]{"-v","error","-show_format","-of","json",library.Paths.Resolve(f.Path,false)},ct));if(probe.RootElement.TryGetProperty("format",out var format)&&format.TryGetProperty("tags",out var originalTags))foreach(var tag in originalTags.EnumerateObject())before[tag.Name.ToLowerInvariant()]=tag.Value.ToString();}
        else before=JsonSerializer.Deserialize<Dictionary<string,string>>(library.Db.Get(id,false)?.Metadata??"{}")??new();
        return new{token,before,after=tags,backup=".localcloud/backups/audio-tags/"+id+"/",message="Сначала будет проверена резервная копия оригинала. Аудиоданные проверяются SHA-256 до замены файла."};
    }
    public async Task<object> Write(string id,string token,bool confirmed,CancellationToken ct)
    {
        if(!confirmed||!plans.TryRemove(token,out var plan)||plan.Id!=id||plan.Expires<DateTime.UtcNow)throw new ArgumentException("Предпросмотр истёк. Проверьте изменения ещё раз и подтвердите запись.");
        await library.Mutation.WaitAsync(ct);
        string? temporary=null;
        try
        {
            var f=library.Db.Get(id)??throw new FileNotFoundException("Трек не найден.");Validate(f,plan.Values);var source=library.Paths.Resolve(f.Path,false);
            if(f.ModifiedAt!=plan.Modified||await library.Hash(source,ct)!=plan.Hash)throw new ArgumentException("Файл изменился после предпросмотра. Создайте новый предпросмотр.");
            var backupFolder=library.Paths.InternalPath("backups/audio-tags/"+id);Directory.CreateDirectory(backupFolder);
            var backup=Path.Combine(backupFolder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".mp3");
            await using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            await using(var output=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None)){await input.CopyToAsync(output,ct);await output.FlushAsync(ct);output.Flush(true);}
            if(await library.Hash(backup,ct)!=plan.Hash)throw new IOException("Резервная копия не прошла проверку. Оригинал не изменён.");
            temporary=Path.Combine(Path.GetDirectoryName(source)!,".localcloud-tags-"+Guid.NewGuid().ToString("N")+".tmp.mp3");
            var args=new List<string>{"-hide_banner","-loglevel","error","-y","-threads","1","-i",backup};
            if(plan.Values.CoverId is {} cover){var image=media.Thumbnail(cover,512);if(!File.Exists(image))throw new ArgumentException("Подождите, пока обложка будет обработана.");args.AddRange(new[]{"-i",image,"-map","0:a","-map","1:v"});}
            else args.AddRange(new[]{"-map","0:a","-map","0:v?"});
            args.AddRange(new[]{"-c","copy","-id3v2_version","3","-metadata","title="+plan.Values.Title,"-metadata","artist="+plan.Values.Artist,"-metadata","album="+plan.Values.Album,"-disposition:v","attached_pic",temporary});
            await MediaProcessor.Run(media.FFmpeg!,args,ct);
            async Task<string> AudioHash(string path)=> (await MediaProcessor.Run(media.FFmpeg!,new[]{"-v","error","-i",path,"-map","0:a","-c","copy","-f","hash","-hash","sha256","-"},ct)).Trim();
            var audioHash=await AudioHash(backup);if(!audioHash.StartsWith("SHA256=",StringComparison.Ordinal)||audioHash!=await AudioHash(temporary))throw new IOException("Аудиоданные не прошли проверку. Оригинал не изменён.");
            if(await library.Hash(source,ct)!=plan.Hash)throw new ArgumentException("Оригинал изменился во время записи. Он не заменён.");
            var newHash=await library.Hash(temporary,ct);ct.ThrowIfCancellationRequested();File.Move(temporary,source,true);temporary=null;
            library.Index(source);library.Db.Execute("UPDATE Files SET hash=$hash WHERE id=$id",("$hash",newHash),("$id",id));
            var values=new Dictionary<string,string>{{"title",plan.Values.Title},{"artist",plan.Values.Artist},{"album",plan.Values.Album}};
            if(plan.Values.CoverId!=null){values["cover"]="custom";values["coverId"]=plan.Values.CoverId;}
            library.Db.Execute("INSERT INTO LocalCloudAudioLabels VALUES($id,$tags) ON CONFLICT(fileId) DO UPDATE SET tags=$tags",("$id",id),("$tags",JsonSerializer.Serialize(values)));library.Notify();
            return new{backup=library.Paths.Relative(backup),originalHash=plan.Hash,hash=newHash,audioVerified=true};
        }
        finally{if(temporary!=null)File.Delete(temporary);library.Mutation.Release();}
    }
}
