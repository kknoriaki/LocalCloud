using System.Runtime.InteropServices;

namespace LocalCloud.Updater;

internal static class ProcessTree
{
    public static Dictionary<int, int> Parents()
    {
        var result = new Dictionary<int, int>();
        if (!OperatingSystem.IsWindows())
        {
            foreach (var path in Directory.GetDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(path), out var pid)) continue;
                try { var stat = File.ReadAllText(Path.Combine(path, "stat")); var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' '); result[pid] = int.Parse(fields[1]); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { }
            }
            return result;
        }
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) throw new IOException("Не удалось проверить дерево процессов. Update отменён.");
        try
        {
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>(), Exe = "" };
            if (Process32First(snapshot, ref entry)) do { result[(int)entry.Pid] = (int)entry.Parent; } while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return result;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Entry
    {
        public uint Size, Usage, Pid; public IntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")][return: MarshalAs(UnmanagedType.Bool)] static extern bool Process32First(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")][return: MarshalAs(UnmanagedType.Bool)] static extern bool Process32Next(IntPtr snapshot, ref Entry entry);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] static extern bool CloseHandle(IntPtr handle);
}
