using System.Diagnostics;
using ImageMagick;
using System.Text.Json;
using LocalCloud.Core;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
namespace LocalCloud.Infrastructure;

public sealed class MediaProcessor(Library library, ILogger<MediaProcessor> log)
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<int,string> active=new();
    public static object[] ActiveProcesses()=>active.Select(p=>(object)new{pid=p.Key,name=Path.GetFileName(p.Value)}).ToArray();
    public string? FFmpeg => FindTool("ffmpeg");
    public string? FFprobe => FindTool("ffprobe");
    static string? FindTool(string name) { var bundled = Path.Combine(AppContext.BaseDirectory, "tools", name + (OperatingSystem.IsWindows() ? ".exe" : "")); if (File.Exists(bundled)) return bundled; foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) { var p = Path.Combine(d, name + (OperatingSystem.IsWindows() ? ".exe" : "")); if (File.Exists(p)) return p; } return null; }
    public string Thumbnail(string id, int size) => library.Paths.InternalPath(Path.Combine("cache", "thumbnails", size.ToString(), id + ".jpg"));
    public async Task Process(FileEntry f, CancellationToken ct)
    {
        if (f.Trashed) return; var source = library.Paths.Resolve(f.Path, false); if (!File.Exists(source)) return;
        try
        {
            var metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(f.Metadata??"{}")??new(); string? taken = null; int? width = null, height = null; double? duration = null;
            if (f.Kind == "photo")
            {
                try
                {
                    var dirs = ImageMetadataReader.ReadMetadata(source); foreach (var d in dirs) foreach (var t in d.Tags.Where(t => t.HasName && t.Description != null)) metadata[d.Name + ": " + t.Name] = t.Description!; foreach(var d in dirs) if(d.Name.Contains("Apple",StringComparison.OrdinalIgnoreCase)&&d.ContainsTag(17)) { var value=d.GetString(17)?.Trim('\0',' ');if(Guid.TryParse(value,out var identifier))metadata["AppleContentIdentifier"]=identifier.ToString(); }
                    var exif = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault(); if (exif?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var date) == true) taken = DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime().ToString("O");
                }
                catch (Exception e) when (e is MetadataExtractor.ImageProcessingException or IOException) { log.LogDebug(e, "Metadata unavailable for {Id}", f.Id); }
                try
                {
                    var decoder = new SixLabors.ImageSharp.Formats.DecoderOptions { MaxFrames = 1 }; var imageInfo = await Image.IdentifyAsync(source, ct); if ((long)imageInfo.Width * imageInfo.Height > 120_000_000) throw new IOException("Изображение слишком велико для безопасного превью.");
                    using var image = await Image.LoadAsync(decoder, source, ct); image.Mutate(x => x.AutoOrient()); width = image.Width; height = image.Height;
                    foreach (var size in new[] { 256, 512, 1024 }) { using var small = image.Clone(x => x.Resize(new ResizeOptions { Size = new Size(size, size), Mode = ResizeMode.Max })); small.Metadata.ExifProfile = null; small.Metadata.XmpProfile = null; await small.SaveAsJpegAsync(Thumbnail(f.Id, size), new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 82 }, ct); }
                }
                catch (UnknownImageFormatException) when (f.Extension is "heic" or "heif" or "avif")
                {
                    ResourceLimits.Memory = 512UL * 1024 * 1024; ResourceLimits.Disk = 1024UL * 1024 * 1024; ResourceLimits.Thread = 2;
                    using var heic = new MagickImage(source, new MagickReadSettings { Format = f.Extension == "avif" ? MagickFormat.Avif : MagickFormat.Heic });
                    heic.AutoOrient(); width = (int)heic.Width; height = (int)heic.Height;
                    foreach (var size in new[] { 256, 512, 1024 }) { using var small = heic.Clone(); small.Resize((uint)size, (uint)size); small.Strip(); small.Quality = 82; small.Format = MagickFormat.Jpeg; small.Write(Thumbnail(f.Id, size)); }
                }
            }
            else if (f.Kind == "video" && FFprobe != null)
            {
                var json = await Run(FFprobe, new[] { "-v", "error", "-show_format", "-show_streams", "-of", "json", source }, ct);
                using var data = JsonDocument.Parse(json); var streams = data.RootElement.GetProperty("streams"); foreach (var stream in streams.EnumerateArray()) if (stream.TryGetProperty("codec_type", out var type) && type.GetString() == "video")
                {
                    if (stream.TryGetProperty("width", out var w)) width = w.GetInt32(); if (stream.TryGetProperty("height", out var h)) height = h.GetInt32(); if (stream.TryGetProperty("codec_name", out var codec)) metadata["Codec"] = codec.GetString()!; if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("creation_time", out var created)) taken = created.GetString(); break;
                }
                if (data.RootElement.TryGetProperty("format", out var format)) { if(format.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var seconds)) duration = seconds; if(format.TryGetProperty("tags",out var tags))foreach(var tag in tags.EnumerateObject()) { metadata[tag.Name]=tag.Value.ToString();if(tag.Name.Contains("content.identifier",StringComparison.OrdinalIgnoreCase)&&Guid.TryParse(tag.Value.ToString(),out var asset))metadata["AppleContentIdentifier"]=asset.ToString(); } }
                await ExternalThumbnail(source, f.Id, ct);
            }
            else if (f.Kind == "audio" && FFprobe != null)
            {
                var json = await Run(FFprobe, new[] { "-v", "error", "-show_format", "-show_streams", "-of", "json", source }, ct);
                using var data = JsonDocument.Parse(json);
                var cover = false;
                foreach (var stream in data.RootElement.GetProperty("streams").EnumerateArray())
                {
                    if (stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio")
                    {
                        foreach (var key in new[] { "codec_name", "sample_rate", "channels", "bit_rate" })
                            if (stream.TryGetProperty(key, out var value)) metadata[key] = value.ToString();
                    }
                    if (stream.TryGetProperty("disposition", out var disposition) && disposition.TryGetProperty("attached_pic", out var attached)) cover |= attached.GetInt32() == 1;
                }
                if (data.RootElement.TryGetProperty("format", out var format))
                {
                    if (format.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var seconds)) duration = seconds;
                    if (format.TryGetProperty("tags", out var tags)) foreach (var tag in tags.EnumerateObject()) metadata[tag.Name] = tag.Value.ToString();
                }
                if (cover) { metadata["cover"] = "embedded"; await ExternalThumbnail(source, f.Id, ct); }
            }
            var fresh = library.Db.Get(f.Id); if (fresh == null || fresh.Trashed || fresh.ModifiedAt != f.ModifiedAt) return;
            library.Db.Execute("UPDATE Files SET width=$w,height=$h,duration=$d,takenAt=$t,metadata=$m WHERE id=$id", ("$w", width), ("$h", height), ("$d", duration), ("$t", taken), ("$m", JsonSerializer.Serialize(metadata)), ("$id", f.Id));
            Pair(f,metadata);

        }
        catch (Exception e) when (e is not OperationCanceledException) { library.Db.Execute("INSERT OR IGNORE INTO LocalCloudLiveAssets(fileId,method) VALUES($id,'unavailable')",("$id",f.Id)); log.LogWarning(e, "Media processing failed {Id}", f.Id); library.Db.Execute("UPDATE Files SET metadata=$m WHERE id=$id", ("$id", f.Id), ("$m", JsonSerializer.Serialize(new { PreviewError = "Не удалось создать превью. Оригинал сохранён." }))); }
    }
    void Pair(FileEntry f,Dictionary<string,string> metadata)
    {
        var identifier=metadata.GetValueOrDefault("AppleContentIdentifier");
        library.Db.Execute("INSERT INTO LocalCloudLiveAssets(fileId,identifier,method) VALUES($id,$cid,'none') ON CONFLICT(fileId) DO UPDATE SET identifier=$cid",("$id",f.Id),("$cid",identifier));
        if(f.Kind is not("photo" or "video"))return;
        var opposite=f.Kind=="photo"?"video":"photo";
        var candidates=identifier==null?new List<Dictionary<string,object?>>():library.Db.Rows("SELECT f.id,f.path FROM Files f JOIN LocalCloudLiveAssets a ON a.fileId=f.id WHERE f.trashed=0 AND f.kind=$kind AND a.identifier=$cid",("$kind",opposite),("$cid",identifier));
        var method="metadata";
        if(candidates.Count!=1){ method="inferred";var basePath=Path.ChangeExtension(f.Path,null); candidates=library.Db.Rows("SELECT f.id,f.path FROM Files f LEFT JOIN LocalCloudLiveAssets a ON a.fileId=f.id WHERE f.trashed=0 AND f.kind=$kind AND f.path LIKE $prefix AND (f.livePartner IS NULL OR f.livePartner=$id) AND ($cid IS NULL OR a.identifier IS NULL OR a.identifier=$cid)",("$kind",opposite),("$prefix",basePath+".%"),("$id",f.Id),("$cid",identifier)).Where(r=>Path.ChangeExtension((string)r["path"]!,null)==basePath).ToList(); }
        if(candidates.Count!=1)return;var partnerId=(string)candidates[0]["id"]!;var partner=library.Db.Get(partnerId);if(partner?.LivePartner is {} existing&&existing!=f.Id)return;
        library.Db.Execute("UPDATE Files SET livePartner=$partner WHERE id=$id; UPDATE Files SET livePartner=$id WHERE id=$partner; UPDATE LocalCloudLiveAssets SET method=$method WHERE fileId=$id OR fileId=$partner",("$id",f.Id),("$partner",partnerId),("$method",method));
    }
    async Task ExternalThumbnail(string source, string id, CancellationToken ct)
    {
        if (FFmpeg == null) return;
        await Run(FFmpeg, new[] { "-hide_banner", "-loglevel", "error", "-y", "-threads", "1", "-i", source, "-frames:v", "1", "-threads", "1", "-filter_threads", "1", "-vf", "scale=1024:1024:force_original_aspect_ratio=decrease", "-q:v", "3", Thumbnail(id, 1024) }, ct);
        using var image=await Image.LoadAsync(Thumbnail(id,1024),ct);
        foreach(var size in new[]{256,512}){using var small=image.Clone(x=>x.Resize(new ResizeOptions{Size=new Size(size,size),Mode=ResizeMode.Max}));await small.SaveAsJpegAsync(Thumbnail(id,size),new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder{Quality=82},ct);}
    }
    public async Task<string> Proxy(FileEntry f, CancellationToken ct)
    {
        if (FFmpeg == null) throw new ArgumentException("Для совместимого видео-превью установите FFmpeg. Оригинал можно скачать.");
        var dir = Path.Combine(library.Paths.Internal, "cache", "proxies"); System.IO.Directory.CreateDirectory(dir); var output = Path.Combine(dir, f.Id + ".mp4"); if (File.Exists(output)) return output;
        var temporary = output + ".tmp.mp4";
        try { await Run(FFmpeg, new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", library.Paths.Resolve(f.Path, false), "-vf", "scale=1280:720:force_original_aspect_ratio=decrease:force_divisible_by=2", "-c:v", "libx264", "-threads", "2", "-preset", "veryfast", "-crf", "26", "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+faststart", temporary }, ct, TimeSpan.FromHours(6)); File.Move(temporary, output, true); return output; } finally { File.Delete(temporary); }
    }
    public static async Task<string> Run(string exe, IEnumerable<string> args, CancellationToken ct, TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo(exe) { RedirectStandardError = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8, UseShellExecute = false, CreateNoWindow = true }; foreach (var arg in args) start.ArgumentList.Add(arg); using var p = System.Diagnostics.Process.Start(start)!; active[p.Id]=exe; if(OperatingSystem.IsWindows()){try{p.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(Exception e)when(e is System.ComponentModel.Win32Exception or InvalidOperationException){}} using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct); linked.CancelAfter(timeout ?? TimeSpan.FromMinutes(3));
        var stdout = p.StandardOutput.ReadToEndAsync(linked.Token); var stderr = p.StandardError.ReadToEndAsync(linked.Token);
        try { await p.WaitForExitAsync(linked.Token); var result = await stdout; var error = await stderr; if (p.ExitCode != 0) throw new IOException("Media tool failed: " + error[..Math.Min(error.Length, 1000)]); return result; } catch { if (!p.HasExited) {p.Kill(true);await p.WaitForExitAsync(CancellationToken.None);} throw; } finally {active.TryRemove(p.Id,out _);}
    }
}
