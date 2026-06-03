using System.Text.Json;

namespace DolphinRoulette;

public class GameEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public int PlayCount { get; set; } = 0;
    public bool Finished { get; set; } = false;
}

public class AppSettings
{
    public string DolphinPath { get; set; } = @"C:\Program Files\Dolphin\Dolphin.exe";
    public int SavestateSlot { get; set; } = 1;
    public int MinPlaySeconds { get; set; } = 10;
    public int MaxPlaySeconds { get; set; } = 200;
    public int GracePeriodMs { get; set; } = 500;
    public List<GameEntry> Games { get; set; } = new();

    private static readonly string SettingsPath =
        Path.Combine(AppContext.BaseDirectory, "roulette_settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* fall through to default */ }
        return new AppSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
