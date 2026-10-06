namespace LocalCloud.Core;

public record FileEntry(string Id, string Path, string Name, string Extension, string Mime, long Size, string Kind, string ModifiedAt, string ImportedAt, string? TakenAt, string? Hash, bool Favorite, bool Trashed, int? Width, int? Height, double? Duration, string? Metadata, string? LivePartner);
public record Album(string Id, string Name, int Count, string? Cover, bool CoverReady = false);
public record FolderEntry(string Path, string Name);
public record LibraryQuery(string View = "photos", string? Folder = null, string? Search = null, string? Album = null, int Offset = 0, int Limit = 120, string Sort = "date", string? Extension = null, string? After = null, string? Before = null, long? MinSize = null, long? MaxSize = null, bool MissingTags = false);
public record PagedFiles(IReadOnlyList<FileEntry> Items, long Total, int Offset, int Limit);
public record AppSettings(string StorageRoot, int Port = 43110, bool AllowLan = true, bool LaunchAtStartup = true, string? PinHash = null, string? PinSalt = null, int ConcurrentUploads = 2, string DuplicateBehavior = "ask", int TrashRetentionDays = 30) { [System.Text.Json.Serialization.JsonExtensionData] public Dictionary<string,System.Text.Json.JsonElement>? AdditionalSettings { get; init; } }
public interface IBackupProvider { string Name { get; } Task BackupAsync(string sourceRoot, CancellationToken cancellationToken); }
