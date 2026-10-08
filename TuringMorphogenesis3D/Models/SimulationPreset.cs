namespace TuringMorphogenesis3D.Models;

public enum MeshType { Sphere, Torus, Plane, Cylinder }
public enum GradientType { None, Linear, Radial }
public enum ColorScheme { Ocean, Fire, Jungle, Grayscale, Neon }

/// <summary>
/// Пресет модели Грея–Скотта (значения f, k из работ Pearson 1993 и Karl Sims).
/// </summary>
public sealed class SimulationPreset
{
    public string LocKey { get; }
    public double FeedRate { get; }
    public double KillRate { get; }
    public double DiffusionU { get; }
    public double DiffusionV { get; }

    public SimulationPreset(string locKey, double f, double k, double du = 0.16, double dv = 0.08)
    {
        LocKey = locKey;
        FeedRate = f;
        KillRate = k;
        DiffusionU = du;
        DiffusionV = dv;
    }

    public static SimulationPreset Spots => new("PresetSpots", 0.030, 0.062);
    public static SimulationPreset Stripes => new("PresetStripes", 0.078, 0.061);
    public static SimulationPreset Labyrinth => new("PresetLabyrinth", 0.029, 0.057);
    public static SimulationPreset Coral => new("PresetCoral", 0.0545, 0.062);
    public static SimulationPreset Mitosis => new("PresetMitosis", 0.0367, 0.0649);
    public static SimulationPreset Maze => new("PresetMaze", 0.034, 0.0565);
    public static SimulationPreset Holes => new("PresetHoles", 0.062, 0.061);
    public static SimulationPreset Waves => new("PresetWaves", 0.025, 0.051);

    public static SimulationPreset[] All =>
        [Spots, Stripes, Labyrinth, Coral, Mitosis, Maze, Holes, Waves];
}