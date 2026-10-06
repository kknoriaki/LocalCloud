using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalCloud.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
namespace LocalCloud.Infrastructure;

public sealed class Features(Library library)
{
    public string? Get(string key) => library.Db.Rows("SELECT value FROM LocalCloudFeatures WHERE key=$k", ("$k",key)).FirstOrDefault()?["value"] as string;
    public void Set(string key,string value) => library.Db.Execute("INSERT INTO LocalCloudFeatures VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v",("$k",key),("$v",value));
    public bool VpnEnabled => Get("radmin-enabled")=="true";
    public static bool Radmin(System.Net.IPAddress ip) { if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();var b=ip.GetAddressBytes();return b.Length==4&&b[0]==26; }
    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public object CreateShare(string folder,string name,bool download,int days)
    {
        if(string.IsNullOrWhiteSpace(folder))throw new ArgumentException("Выберите конкретную папку, а не всё хранилище.");
        if(!Directory.Exists(library.Paths.Resolve(folder)))throw new ArgumentException("Папка не найдена.");
        folder=library.Paths.Relative(library.Paths.Resolve(folder));name=SafePaths.FileName(name);var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();var id=Guid.NewGuid().ToString("N");var expires=DateTime.UtcNow.AddDays(Math.Clamp(days,1,365)).ToString("O");
        library.Db.Execute("INSERT INTO LocalCloudShares VALUES($id,$hash,$name,$folder,$download,$expires,$created)",("$id",id),("$hash",TokenHash(token)),("$name",name),("$folder",folder),("$download",download),("$expires",expires),("$created",DateTime.UtcNow.ToString("O")));
        return new{id,name,folder,download,expires,path="/s/"+token};
    }
    public Dictionary<string,object?> Share(string token)
    {
        if(token.Length!=64||!token.All(Uri.IsHexDigit))throw new FileNotFoundException("Ссылка недоступна или истекла.");
        return library.Db.Rows("SELECT id,name,folder,download,expires FROM LocalCloudShares WHERE tokenHash=$h AND expires>$now",("$h",TokenHash(token)),("$now",DateTime.UtcNow.ToString("O"))).FirstOrDefault()??throw new FileNotFoundException("Ссылка недоступна или истекла.");
    }
    public FileEntry SharedFile(string token,string id,bool download=false)
    {
        var share=Share(token);var f=library.Db.Get(id)??throw new FileNotFoundException("Файл недоступен.");var prefix=(string)share["folder"]!+"/";
        if(f.Trashed||!f.Path.StartsWith(prefix,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new FileNotFoundException("Файл недоступен.");
        if(download&&Convert.ToInt32(share["download"])==0)throw new UnauthorizedAccessException("Скачивание отключено владельцем.");
        return f;
    }
    public object SharedFiles(string token,int offset,int limit)
    {
        var share=Share(token);var prefix=(string)share["folder"]!+"/";var rows=library.Db.Rows("SELECT id,name,kind,size,width,height,duration,livePartner,metadata IS NOT NULL AS ready FROM Files WHERE trashed=0 AND substr(path,1,length($p))=$p ORDER BY COALESCE(takenAt,modifiedAt) DESC,id LIMIT $n OFFSET $o",("$p",prefix),("$n",Math.Clamp(limit,1,120)),("$o",Math.Max(0,offset)));
        var count=library.Db.Rows("SELECT COUNT(*) AS n FROM Files WHERE trashed=0 AND substr(path,1,length($p))=$p",("$p",prefix))[0]["n"];
        return new{name=share["name"],download=Convert.ToInt32(share["download"])==1,total=count,items=rows};
    }
    public PhotoRecipe Recipe(string id) { var json=library.Db.Rows("SELECT recipe FROM LocalCloudEdits WHERE fileId=$id",("$id",id)).FirstOrDefault()?["recipe"] as string;return json==null?new():JsonSerializer.Deserialize<PhotoRecipe>(json)!; }
    public void SaveRecipe(string id,PhotoRecipe r)
    {
        var f=library.Db.Get(id)??throw new FileNotFoundException("Фото не найдено.");if(f.Kind!="photo"||f.Trashed)throw new ArgumentException("Выберите фотографию.");
        if(r.Rotation%90!=0||r.Rotation is <0 or >270||!double.IsFinite(r.Exposure)||r.Exposure is <-2 or >2||!double.IsFinite(r.X)||!double.IsFinite(r.Y)||!double.IsFinite(r.Width)||!double.IsFinite(r.Height)||r.X<0||r.Y<0||r.Width<=0||r.Height<=0||r.X+r.Width>1.001||r.Y+r.Height>1.001)throw new ArgumentException("Некорректные параметры редактирования.");
        library.Db.Execute("INSERT INTO LocalCloudEdits VALUES($id,$r,$now) ON CONFLICT(fileId) DO UPDATE SET recipe=$r,updated=$now",("$id",id),("$r",JsonSerializer.Serialize(r)),("$now",DateTime.UtcNow.ToString("O")));
    }
    public async Task<string> RenderEdit(string id,bool export,CancellationToken ct)
    {
        var f=library.Db.Get(id)??throw new FileNotFoundException("Фото не найдено.");if(f.Kind!="photo"||f.Trashed)throw new ArgumentException("Выберите фотографию.");var r=Recipe(id);var folder=library.Paths.InternalPath("cache/edits");Directory.CreateDirectory(folder);var path=Path.Combine(folder,id+"-"+TokenHash(f.ModifiedAt+JsonSerializer.Serialize(r))[..12]+(export?"-export":"-preview")+".jpg");if(File.Exists(path))return path;
        using Image image=await LoadPhoto(f,export,ct);image.Mutate(x=>{x.AutoOrient();var rect=new Rectangle((int)(r.X*image.Width),(int)(r.Y*image.Height),Math.Max(1,(int)(r.Width*image.Width)),Math.Max(1,(int)(r.Height*image.Height)));rect=Rectangle.Intersect(rect,new Rectangle(0,0,image.Width,image.Height));x.Crop(rect);x.Rotate(r.Rotation);x.Brightness((float)Math.Pow(2,r.Exposure));if(!export)x.Resize(new ResizeOptions{Size=new Size(1600,1600),Mode=ResizeMode.Max});});image.Metadata.ExifProfile=null;image.Metadata.XmpProfile=null;var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {await image.SaveAsJpegAsync(tmp,new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder{Quality=export?95:85},ct);File.Move(tmp,path,true);}finally{File.Delete(tmp);}return path;
    }
    async Task<Image> LoadPhoto(FileEntry f,bool export,CancellationToken ct)
    {
        var source=library.Paths.Resolve(f.Path,false);if(f.Extension is "heic" or "heif" or "avif") { using var im=new ImageMagick.MagickImage(source);im.AutoOrient();using var buffer=new MemoryStream(im.ToByteArray(ImageMagick.MagickFormat.Jpeg));return await Image.LoadAsync(buffer,ct); }
        var info=await Image.IdentifyAsync(source,ct);if((long)info.Width*info.Height>120_000_000)throw new ArgumentException("Фото слишком велико для редактора.");return await Image.LoadAsync(new SixLabors.ImageSharp.Formats.DecoderOptions{MaxFrames=1},source,ct);
    }
    public List<object> OrganizePlan()
    {
        var plans=new List<object>();foreach(var folder in new[]{"Photos","Videos","Music","Files"})
        {var rows=library.Db.Rows("SELECT id,path,name,kind,livePartner FROM Files WHERE trashed=0 AND substr(path,1,length($prefix))=$prefix AND instr(substr(path,length($prefix)+1),'/')=0",("$prefix",folder+"/"));foreach(var row in rows){var kind=(string)row["kind"]!;var destination=kind=="photo"?"Photos":kind=="video"?"Videos":kind=="audio"?"Music":"Files";if(destination==folder||row["livePartner"]!=null)continue;plans.Add(new{id=row["id"],path=row["path"],folder=destination});}}return plans;
    }
    public object RenamePlan(string[] ids,string prefix,int start)
    {
        if(ids.Length is <1 or >500||start<0||start>1000000)throw new ArgumentException("Выберите от 1 до 500 файлов.");SafePaths.FileName(prefix+"1");var files=ids.Distinct().Select(id=>library.Db.Get(id)??throw new FileNotFoundException("Файл не найден.")).OrderBy(f=>f.Name,StringComparer.OrdinalIgnoreCase).ToList();var plans=new List<object>();var targets=new HashSet<string>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
        foreach(var f in files){if(f.Trashed)throw new ArgumentException("Сначала восстановите файлы.");var name=SafePaths.FileName(prefix+(start++).ToString("D3")+Path.GetExtension(f.Name));var path=library.Paths.Resolve(Path.Combine(Path.GetDirectoryName(f.Path)!,name),false);if(!targets.Add(path)||(File.Exists(path)&&!path.Equals(library.Paths.Resolve(f.Path,false),StringComparison.Ordinal)))throw new ArgumentException("Имя уже занято: "+name);plans.Add(new{id=f.Id,name,oldName=f.Name});}return plans;
    }
}
public sealed record PhotoRecipe(int Rotation=0,double Exposure=0,double X=0,double Y=0,double Width=1,double Height=1);
