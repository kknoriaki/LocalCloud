using System.Diagnostics;
using System.Text.Json;
namespace LocalCloud.Infrastructure;

// Read-only diagnostics and an explicit, elevated Private-profile firewall action.
public static class NetworkSupport
{
    public static async Task<object> Diagnose(bool listeningLan, int port, CancellationToken ct)
    {
        JsonElement? windows = null; string? error = null;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var script = "$profiles=@(Get-NetConnectionProfile -ErrorAction Stop | Select-Object InterfaceAlias,InterfaceIndex,@{n='Category';e={$_.NetworkCategory.ToString()}});" +
                    "$rule=Get-NetFirewallRule -Name 'LocalCloud-LAN' -ErrorAction SilentlyContinue;" +
                    "$allowed=$false;if($rule -and $rule.Enabled -eq 'True' -and $rule.Action -eq 'Allow' -and $rule.Profile.ToString() -eq 'Private'){" +
                    "$p=$rule|Get-NetFirewallPortFilter;$a=$rule|Get-NetFirewallApplicationFilter;" +
                    $"$allowed=($p.LocalPort -eq '{port}' -and $a.Program -eq (Join-Path '{AppContext.BaseDirectory.Replace("'", "''")}' 'LocalCloud.Server.exe'));" +
                    "};@{profiles=$profiles;firewallAllowed=$allowed}|ConvertTo-Json -Depth 4 -Compress";
                var json = await MediaProcessor.Run("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", script }, ct, TimeSpan.FromSeconds(12));
                using var doc = JsonDocument.Parse(json); windows = doc.RootElement.Clone();
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested) { error = "Не удалось прочитать настройки Windows. Проверьте профиль сети и брандмауэр вручную."; }
        }
        var addresses=Discovery.Addresses(port); var probes=new List<object>();
        foreach(var address in addresses.Take(8)){bool connected=false;try{var uri=new Uri(address);using var tcp=new System.Net.Sockets.TcpClient();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(1000);await tcp.ConnectAsync(uri.Host,uri.Port,timeout.Token);connected=true;}catch(Exception e)when(e is System.Net.Sockets.SocketException or OperationCanceledException){if(ct.IsCancellationRequested)throw;}probes.Add(new{address,connected});}
        return new { listeningLan, port, addresses, probes, probeScope="Проверка с компьютера сервера. Подключение самого iPhone нужно проверить на телефоне.", windows, error, platform = OperatingSystem.IsWindows() ? "windows" : "other" };
    }
    public static void Allow(int port,bool radmin=false)
    {
        if (!OperatingSystem.IsWindows()) throw new ArgumentException("Разрешение брандмауэра доступно в Windows-приложении.");
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "scripts", "firewall.ps1"));
        if (!File.Exists(script)) throw new FileNotFoundException("Скрипт брандмауэра не найден. Распакуйте полный пакет приложения.");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas" };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Port", port.ToString() }) start.ArgumentList.Add(arg);
        if(radmin)start.ArgumentList.Add("-Radmin");
        try { Process.Start(start); }
        catch (System.ComponentModel.Win32Exception) { throw new ArgumentException("Разрешение не изменено. Подтвердите запрос Windows на компьютере или разрешите LocalCloud вручную."); }
    }
}
