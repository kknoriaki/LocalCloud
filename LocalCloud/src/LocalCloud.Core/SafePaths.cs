namespace LocalCloud.Core;

public sealed class SafePaths
{
    public string Root { get; }
    public string Internal => InternalPath("");
    public string InternalPath(string relative)
    {
        var internalRoot = System.IO.Path.Combine(Root, ".localcloud"); var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(internalRoot, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full != internalRoot && !full.StartsWith(internalRoot + System.IO.Path.DirectorySeparatorChar, comparison)) throw new ArgumentException("Недопустимый внутренний путь.");
        var current = Root; foreach (var part in System.IO.Path.GetRelativePath(Root, full).Split(System.IO.Path.DirectorySeparatorChar)) { current = System.IO.Path.Combine(current, part); if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Ссылки во внутреннем хранилище запрещены."); }
        return full;
    }
    public SafePaths(string root) { Root = System.IO.Path.GetFullPath(root); Directory.CreateDirectory(Root); Resolve(""); }
    public static string FileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 220 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') || name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c))) throw new ArgumentException("Недопустимое имя файла.");
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem) || name.Equals(".localcloud", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Это имя зарезервировано системой.");
        return name;
    }
    public string Resolve(string relative, bool allowRoot = true)
    {
        relative = relative.Replace('\\', '/');
        if (System.IO.Path.IsPathRooted(relative) || relative.Contains(':')) throw new ArgumentException("Путь должен быть внутри хранилища.");
        foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries)) FileName(segment);
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!(full.Equals(Root, comparison) && allowRoot) && !full.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, comparison)) throw new ArgumentException("Путь выходит за пределы хранилища.");
        var current = Root;
        if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Ссылки на другие каталоги запрещены.");
        foreach (var part in System.IO.Path.GetRelativePath(Root, full).Split(System.IO.Path.DirectorySeparatorChar))
        {
            current = System.IO.Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Ссылки на другие каталоги запрещены.");
        }
        return full;
    }
    public string Relative(string full) => System.IO.Path.GetRelativePath(Root, full).Replace('\\', '/');
    public string Unique(string folder, string name)
    {
        FileName(name); var path = Resolve(System.IO.Path.Combine(folder, name), false); var n = 1;
        while (File.Exists(path) || Directory.Exists(path)) path = Resolve(System.IO.Path.Combine(folder, $"{System.IO.Path.GetFileNameWithoutExtension(name)} ({n++}){System.IO.Path.GetExtension(name)}"), false);
        return path;
    }
}
