using System.Security.Cryptography;
using System.Text.Json;
using LocalCloud.Core;
namespace LocalCloud.Infrastructure;

public sealed class SettingsStore
{
    public string ConfigPath { get; }
    public AppSettings Current { get; private set; }
    public bool Configured { get; private set; }
    public SettingsStore(string? configPath = null)
    {
        ConfigPath = configPath ?? Environment.GetEnvironmentVariable("LOCALCLOUD_CONFIG") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalCloud", "settings.json");
        Configured = File.Exists(ConfigPath);
        Current = Configured ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ConfigPath))! : new AppSettings(Environment.GetEnvironmentVariable("LOCALCLOUD_STORAGE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "LocalCloud"));
        if (Environment.GetEnvironmentVariable("LOCALCLOUD_PORT") is { } port) Current = Current with { Port = int.Parse(port) };
    }
    public void Save(AppSettings settings)
    {
        if (settings.Port < 1024 || settings.Port > 65535 || settings.ConcurrentUploads < 1 || settings.ConcurrentUploads > 4 || settings.TrashRetentionDays < 1 || settings.TrashRetentionDays > 365) throw new ArgumentException("Проверьте настройки порта, загрузок и корзины.");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!); var tmp = ConfigPath + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); File.Move(tmp, ConfigPath, true); Current = settings; Configured = true;
    }
    public AppSettings WithPin(string? pin)
    {
        if (string.IsNullOrEmpty(pin)) return Current with { PinHash = null, PinSalt = null };
        if (pin.Length < 6 || pin.Length > 64) throw new ArgumentException("PIN должен содержать от 6 до 64 символов.");
        var salt = RandomNumberGenerator.GetBytes(16); return Current with { PinSalt = Convert.ToBase64String(salt), PinHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(pin, salt, 150000, HashAlgorithmName.SHA256, 32)) };
    }
    public bool CheckPin(string pin) => Current.PinHash == null || CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Current.PinHash), Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(Current.PinSalt!), 150000, HashAlgorithmName.SHA256, 32));
}
