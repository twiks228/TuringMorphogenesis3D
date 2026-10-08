using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TuringMorphogenesis3D.Helpers;

/// <summary>Пользовательский пресет параметров.</summary>
public sealed class UserPreset
{
    public string Name { get; set; } = "Preset";
    public double F { get; set; }
    public double K { get; set; }
    public double Du { get; set; }
    public double Dv { get; set; }
}

public sealed class AppConfig
{
    public double FeedRate { get; set; } = 0.030;
    public double KillRate { get; set; } = 0.062;
    public double DiffusionU { get; set; } = 0.16;
    public double DiffusionV { get; set; } = 0.08;
    public int IterationsPerFrame { get; set; } = 8;
    public int GridResolution { get; set; } = 128;
    public int MeshType { get; set; } = 0;
    public int Gradient { get; set; } = 0;
    public int ColorScheme { get; set; } = 0;
    public double Relief { get; set; } = 0.0;
    public double Glow { get; set; } = 0.30;
    public bool AutoRotate { get; set; } = true;
    public double BrushSize { get; set; } = 0.05;
    public double BrushStrength { get; set; } = 0.3;
    public int Seed { get; set; } = 42;
    public bool UseGpu { get; set; } = true;
    public bool AutoRespawn { get; set; } = true;
    public List<UserPreset> UserPresets { get; set; } = new();
}

public static class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TuringMorphogenesis3D");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            }
        }
        catch { }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        try
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
        }
        catch { }
    }
}