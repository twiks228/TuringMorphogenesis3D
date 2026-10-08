using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using TuringMorphogenesis3D.Gpu;
using TuringMorphogenesis3D.Helpers;
using TuringMorphogenesis3D.Localization;
using TuringMorphogenesis3D.Models;

namespace TuringMorphogenesis3D.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly object _sync = new();

    private ReactionDiffusionSolver _cpuSolver = null!;
    private GpuSolver? _gpuSolver;
    private bool _useGpu;
    private string _gpuAdapter = "";

    private PatternTexture _texture = null!;
    private float[] _vBuffer = Array.Empty<float>();
    private CancellationTokenSource? _cts;
    private int _seed = 42;
    private long _frameCounter;

    private Point3D[] _basePositions = Array.Empty<Point3D>();
    private Vector3D[] _baseNormals = Array.Empty<Vector3D>();
    private int _meshRes;
    private ImageBrush? _glowBrush;
    private readonly DispatcherTimer _rotationTimer;
    private readonly DispatcherTimer _presentTimer;
    private readonly AudioEngine _audio = new();

    private bool _isLoadingConfig;
    private bool _isRecording;
    private int _recordFrame;
    private string _recordFolder = "";
    private string _lastOutputDir = "";

    private readonly Queue<double> _history = new();
    private const int HistoryMax = 140;
    private DateTime _flashUntil = DateTime.MinValue;
    private string _flashText = "";

    private DateTime _nextRespawnAllowed = DateTime.MinValue;
    private int _respawnFails;
    private int _presentIndex;

    private double _rangeLo, _rangeHi = 0.3;
    private bool _hasRangeVm;

    private AppConfig _config = new();

    public LocalizationManager Loc => LocalizationManager.Instance;

    // ─── Списки ───

    public LocalizedItem<SimulationPreset>[] PresetItems { get; } =
        SimulationPreset.All.Select(p => new LocalizedItem<SimulationPreset>(p, p.LocKey)).ToArray();

    public LocalizedItem<MeshType>[] MeshItems { get; } =
    [
        new(MeshType.Sphere,   "MeshSphere"),
        new(MeshType.Torus,    "MeshTorus"),
        new(MeshType.Plane,    "MeshPlane"),
        new(MeshType.Cylinder, "MeshCylinder")
    ];

    public LocalizedItem<GradientType>[] GradientItems { get; } =
    [
        new(GradientType.None,   "GradientNone"),
        new(GradientType.Linear, "GradientLinear"),
        new(GradientType.Radial, "GradientRadial")
    ];

    public LocalizedItem<ColorScheme>[] SchemeItems { get; } =
    [
        new(ColorScheme.Ocean,     "SchemeOcean"),
        new(ColorScheme.Fire,      "SchemeFire"),
        new(ColorScheme.Jungle,    "SchemeJungle"),
        new(ColorScheme.Grayscale, "SchemeGrayscale"),
        new(ColorScheme.Neon,      "SchemeNeon")
    ];

    public LocalizedItem<BrushTool>[] BrushItems { get; } =
    [
        new(BrushTool.Paint,  "BrushPaint"),
        new(BrushTool.Erase,  "BrushErase"),
        new(BrushTool.Smooth, "BrushSmooth"),
        new(BrushTool.Stamp,  "BrushStamp")
    ];

    public int[] Resolutions { get; } = [64, 96, 128, 192, 256, 384, 512];

    // ─── Параметры ──

    private double _feedRate = 0.030;
    public double FeedRate { get => _feedRate; set => Set(ref _feedRate, value); }

    private double _killRate = 0.062;
    public double KillRate { get => _killRate; set => Set(ref _killRate, value); }

    private double _diffusionU = 0.16;
    public double DiffusionU { get => _diffusionU; set => Set(ref _diffusionU, value); }

    private double _diffusionV = 0.08;
    public double DiffusionV { get => _diffusionV; set => Set(ref _diffusionV, value); }

    private int _iterationsPerFrame = 8;
    public int IterationsPerFrame
    {
        get => _iterationsPerFrame;
        set => Set(ref _iterationsPerFrame, Math.Clamp(value, 1, 128));
    }

    private int _gridResolution = 128;
    public int GridResolution
    {
        get => _gridResolution;
        set
        {
            if (!Set(ref _gridResolution, Math.Clamp(value, 16, 512))) return;
            if (!_isLoadingConfig) RebuildAll();
        }
    }

    // ─── Отображение ───

    private MeshType _selectedMeshType = MeshType.Sphere;
    public MeshType SelectedMeshType
    {
        get => _selectedMeshType;
        set
        {
            if (!Set(ref _selectedMeshType, value)) return;
            if (!_isLoadingConfig) BuildMesh();
        }
    }

    private GradientType _selectedGradient = GradientType.None;
    public GradientType SelectedGradient
    {
        get => _selectedGradient;
        set { Set(ref _selectedGradient, value); }
    }

    private ColorScheme _selectedColorScheme = ColorScheme.Ocean;
    public ColorScheme SelectedColorScheme
    {
        get => _selectedColorScheme;
        set
        {
            if (!Set(ref _selectedColorScheme, value)) return;
            if (!_isLoadingConfig)
            {
                _texture.Scheme = value;
                RenderFrame();
            }
        }
    }

    private double _relief = 0.0;
    public double Relief
    {
        get => _relief;
        set
        {
            if (!Set(ref _relief, value)) return;
            if (value > 0.001) UpdateRelief();
            else FlattenMesh();
        }
    }

    private double _glow = 0.30;
    public double Glow
    {
        get => _glow;
        set
        {
            if (!Set(ref _glow, value)) return;
            if (_glowBrush != null) _glowBrush.Opacity = value;
        }
    }

    private bool _autoRotate = true;
    public bool AutoRotate
    {
        get => _autoRotate;
        set
        {
            if (!Set(ref _autoRotate, value)) return;
            if (value) _rotationTimer.Start(); else _rotationTimer.Stop();
        }
    }

    private double _rotationAngle;
    public double RotationAngle { get => _rotationAngle; private set => Set(ref _rotationAngle, value); }

    private double _brushSize = 0.05;
    public double BrushSize { get => _brushSize; set => Set(ref _brushSize, value); }

    private double _brushStrength = 0.3;
    public double BrushStrength { get => _brushStrength; set => Set(ref _brushStrength, value); }

    private BrushTool _selectedBrushTool = BrushTool.Paint;
    public BrushTool SelectedBrushTool { get => _selectedBrushTool; set => Set(ref _selectedBrushTool, value); }

    private bool _autoRespawn = true;
    public bool AutoRespawn { get => _autoRespawn; set => Set(ref _autoRespawn, value); }

    // ─── GPU / панель / презентация / звук ───

    private bool _gpuEnabled = true;
    public bool GpuEnabled
    {
        get => _gpuEnabled;
        set
        {
            if (!Set(ref _gpuEnabled, value)) return;
            if (!_isLoadingConfig) RebuildAll();
        }
    }

    public string EngineLabel => _useGpu ? $"GPU: {_gpuAdapter}" : "CPU: float32 SIMD";

    private bool _isPanelVisible = true;
    public bool IsPanelVisible
    {
        get => _isPanelVisible;
        set
        {
            if (!Set(ref _isPanelVisible, value)) return;
            OnPropertyChanged(nameof(PanelVisibility));
        }
    }

    private bool _presentationMode;
    public bool PresentationMode
    {
        get => _presentationMode;
        set
        {
            if (!Set(ref _presentationMode, value)) return;
            OnPropertyChanged(nameof(PanelVisibility));
            OnPropertyChanged(nameof(HudVisibility));
            if (value && !IsRunning) Start();
        }
    }

    public Visibility PanelVisibility =>
        PresentationMode || !IsPanelVisible ? Visibility.Collapsed : Visibility.Visible;

    public Visibility HudVisibility =>
        PresentationMode ? Visibility.Collapsed : Visibility.Visible;

    private bool _audioEnabled;
    public bool AudioEnabled
    {
        get => _audioEnabled;
        set
        {
            if (!Set(ref _audioEnabled, value)) return;
            if (value) _audio.Start(); else _audio.Stop();
        }
    }

    // ─── Пользовательские пресеты ───

    public ObservableCollection<UserPreset> UserPresets { get; } = new();

    private string _newPresetName = "";
    public string NewPresetName { get => _newPresetName; set => Set(ref _newPresetName, value); }

    // ─── Состояние ───

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set { if (Set(ref _isRunning, value)) UpdateStatusText(); }
    }

    private long _currentStep;
    public long CurrentStep { get => _currentStep; private set => Set(ref _currentStep, value); }

    private double _fps;
    public double Fps { get => _fps; private set => Set(ref _fps, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private MeshGeometry3D? _mesh3D;
    public MeshGeometry3D? Mesh3D { get => _mesh3D; private set => Set(ref _mesh3D, value); }

    private Material? _surfaceMaterial;
    public Material? SurfaceMaterial { get => _surfaceMaterial; private set => Set(ref _surfaceMaterial, value); }

    public Point PhasePoint => new(FeedRate * 1000.0, KillRate * 1000.0);

    private double _avgConcentration;
    public double AverageConcentration
    {
        get => _avgConcentration;
        private set
        {
            if (Set(ref _avgConcentration, value))
                OnPropertyChanged(nameof(StatsText));
        }
    }

    private double _entropy;
    public string StatsText => $"V̄ {AverageConcentration:F4}  S {_entropy:F2}";

    public bool HasOutput => !string.IsNullOrEmpty(_lastOutputDir);

    private PointCollection _sparklinePoints = new();
    public PointCollection SparklinePoints => _sparklinePoints;

    private ImageSource? _phaseMap;
    public ImageSource? PhaseMap => _phaseMap;

    public string? ViabilityWarning
    {
        get
        {
            double ratio = DiffusionU / Math.Max(DiffusionV, 1e-6);
            if (ratio < 1.5 || ratio > 2.5) return Loc["WarnRatio"];
            if (FeedRate < 0.010 || FeedRate > 0.090 || KillRate < 0.040 || KillRate > 0.070)
                return Loc["WarnFK"];
            return null;
        }
    }

    public bool HasViabilityWarning => ViabilityWarning != null;

    // ─── Команды ───

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ToggleRunCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand RandomizeCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand ApplyPresetCommand { get; }
    public ICommand PaintCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ExportMeshCommand { get; }
    public ICommand SaveConfigCommand { get; }
    public ICommand ToggleRecordCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand SavePresetCommand { get; }
    public ICommand ApplyUserPresetCommand { get; }
    public ICommand DeleteUserPresetCommand { get; }
    public ICommand TogglePanelCommand { get; }
    public ICommand TogglePresentationCommand { get; }

    public MainViewModel()
    {
        StartCommand = new RelayCommand(Start, () => !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        ToggleRunCommand = new RelayCommand(() => { if (IsRunning) Stop(); else Start(); });
        ResetCommand = new RelayCommand(ResetSimulation);
        RandomizeCommand = new RelayCommand(Randomize);
        ToggleLanguageCommand = new RelayCommand(Loc.Toggle);
        ApplyPresetCommand = new RelayCommand<SimulationPreset>(ApplyPreset);
        PaintCommand = new RelayCommand<(int x, int y)>(OnPaint);
        ExportCommand = new RelayCommand(ExportState);
        ExportMeshCommand = new RelayCommand(ExportMesh);
        SaveConfigCommand = new RelayCommand(SaveConfig);
        ToggleRecordCommand = new RelayCommand(ToggleRecording);
        OpenFolderCommand = new RelayCommand(OpenOutputFolder, () => HasOutput);
        SavePresetCommand = new RelayCommand(SaveCurrentAsPreset);
        ApplyUserPresetCommand = new RelayCommand<UserPreset>(ApplyUserPreset);
        DeleteUserPresetCommand = new RelayCommand<UserPreset>(DeleteUserPreset);
        TogglePanelCommand = new RelayCommand(() => IsPanelVisible = !IsPanelVisible);
        TogglePresentationCommand = new RelayCommand(() => PresentationMode = !PresentationMode);

        _rotationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _rotationTimer.Tick += (_, _) =>
            RotationAngle = (RotationAngle + 0.35) % 360.0;

        _presentTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
        _presentTimer.Tick += (_, _) =>
        {
            if (!PresentationMode) return;
            _presentIndex = (_presentIndex + 1) % PresetItems.Length;
            ApplyPreset(PresetItems[_presentIndex].Value);
        };
        _presentTimer.Start();

        Loc.PropertyChanged += (_, _) => UpdateStatusText();

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FeedRate) or nameof(KillRate))
                OnPropertyChanged(nameof(PhasePoint));

            if (e.PropertyName is nameof(FeedRate) or nameof(KillRate)
                                   or nameof(DiffusionU) or nameof(DiffusionV))
            {
                OnPropertyChanged(nameof(ViabilityWarning));
                OnPropertyChanged(nameof(HasViabilityWarning));
            }
        };

        LoadConfig();
        // Тяжёлая инициализация (GPU, солвер, карта Пирсона) вынесена
        // в Startup(): конструктор XAML не должен бросать исключений,
        // иначе WPF завернёт всё в XamlParseException без внятной причины.
    }

    private bool _started;

    /// <summary>Вызывается один раз из MainWindow.OnLoaded.</summary>
    public void Startup()
    {
        if (_started) return;
        _started = true;

        try
        {
            RebuildAll();
            if (AutoRotate) _rotationTimer.Start();
            _ = BuildPearsonMapAsync();
        }
        catch (Exception ex)
        {
            // Аварийный путь: глушим GPU и пересобираемся на CPU
            _gpuEnabled = false;
            try
            {
                RebuildAll();
            }
            catch (Exception ex2)
            {
                StatusText = $"Startup failed: {ex2.Message}";
                return;
            }
            Flash($"GPU init error → CPU fallback: {ex.Message}");
        }
    }

    private async Task BuildPearsonMapAsync()
    {
        try
        {
            var bmp = await PearsonMap.BuildAsync();
            _phaseMap = bmp;
            OnPropertyChanged(nameof(PhaseMap));
        }
        catch { }
    }

    // ─── Конфиг ───

    private void LoadConfig()
    {
        _isLoadingConfig = true;
        _config = ConfigManager.Load();

        _seed = _config.Seed;
        _feedRate = _config.FeedRate;
        _killRate = _config.KillRate;
        _diffusionU = _config.DiffusionU;
        _diffusionV = _config.DiffusionV;
        _iterationsPerFrame = Math.Clamp(_config.IterationsPerFrame, 1, 128);
        _gridResolution = Math.Clamp(_config.GridResolution, 16, 512);
        _selectedMeshType = (MeshType)_config.MeshType;
        _selectedGradient = (GradientType)_config.Gradient;
        _selectedColorScheme = (ColorScheme)_config.ColorScheme;
        _relief = _config.Relief;
        _glow = _config.Glow;
        _autoRotate = _config.AutoRotate;
        _brushSize = _config.BrushSize;
        _brushStrength = _config.BrushStrength;
        _gpuEnabled = _config.UseGpu;
        _autoRespawn = _config.AutoRespawn;

        UserPresets.Clear();
        foreach (var p in _config.UserPresets)
            UserPresets.Add(p);

        _isLoadingConfig = false;

        foreach (var name in new[]
        {
            nameof(FeedRate), nameof(KillRate), nameof(DiffusionU), nameof(DiffusionV),
            nameof(IterationsPerFrame), nameof(GridResolution), nameof(SelectedMeshType),
            nameof(SelectedGradient), nameof(SelectedColorScheme), nameof(Relief),
            nameof(Glow), nameof(AutoRotate), nameof(BrushSize), nameof(BrushStrength),
            nameof(GpuEnabled), nameof(AutoRespawn)
        })
            OnPropertyChanged(name);
    }

    private void SaveConfig()
    {
        _config.FeedRate = FeedRate;
        _config.KillRate = KillRate;
        _config.DiffusionU = DiffusionU;
        _config.DiffusionV = DiffusionV;
        _config.IterationsPerFrame = IterationsPerFrame;
        _config.GridResolution = GridResolution;
        _config.MeshType = (int)SelectedMeshType;
        _config.Gradient = (int)SelectedGradient;
        _config.ColorScheme = (int)SelectedColorScheme;
        _config.Relief = Relief;
        _config.Glow = Glow;
        _config.AutoRotate = AutoRotate;
        _config.BrushSize = BrushSize;
        _config.BrushStrength = BrushStrength;
        _config.Seed = _seed;
        _config.UseGpu = GpuEnabled;
        _config.AutoRespawn = AutoRespawn;
        _config.UserPresets = UserPresets.ToList();

        ConfigManager.Save(_config);
        StatusText = "Config saved";
    }

    // ─── Пользовательские пресеты ───

    private void SaveCurrentAsPreset()
    {
        string name = string.IsNullOrWhiteSpace(NewPresetName)
            ? $"Pattern {UserPresets.Count + 1}"
            : NewPresetName.Trim();

        UserPresets.Add(new UserPreset
        {
            Name = name,
            F = FeedRate,
            K = KillRate,
            Du = DiffusionU,
            Dv = DiffusionV
        });
        NewPresetName = "";
        SaveConfig();
        Flash(Loc.Format("StatusPresetSaved", name));
    }

    private void ApplyUserPreset(UserPreset? p)
    {
        if (p == null) return;
        FeedRate = p.F; KillRate = p.K; DiffusionU = p.Du; DiffusionV = p.Dv;
        ResetSimulation();
        _ = WarmupAsync(400);
        if (!IsRunning) Start();
    }

    private void DeleteUserPreset(UserPreset? p)
    {
        if (p == null) return;
        UserPresets.Remove(p);
        SaveConfig();
    }

    // ─── Жизненный цикл ───

    private void RebuildAll()
    {
        int res = GridResolution;

        _gpuSolver?.Dispose();
        _gpuSolver = null;
        _useGpu = false;
        _gpuAdapter = "";

        if (_gpuEnabled)
        {
            try
            {
                _gpuSolver = new GpuSolver(res, res);
                _useGpu = true;
                _gpuAdapter = _gpuSolver.AdapterName;
            }
            catch { _useGpu = false; }
        }

        lock (_sync)
        {
            _cpuSolver = new ReactionDiffusionSolver(res, res, _seed)
            {
                FeedRate = (float)FeedRate,
                KillRate = (float)KillRate,
                DiffusionU = (float)DiffusionU,
                DiffusionV = (float)DiffusionV
            };
            _cpuSolver.SetGradient(SelectedGradient);

            _vBuffer = new float[_cpuSolver.Width * _cpuSolver.Height];

            if (_useGpu && _gpuSolver != null)
            {
                try
                {
                    InitializeGpuFromSeed(res, _seed);
                }
                catch
                {
                    // GPU-устройство создалось, но загрузка данных упала —
                    // откатываемся на CPU, не роняя приложение
                    _gpuSolver.Dispose();
                    _gpuSolver = null;
                    _useGpu = false;
                }
            }
        }

        _texture = new PatternTexture(_cpuSolver.Width, _cpuSolver.Height)
        {
            Scheme = SelectedColorScheme
        };

        BuildMaterial();
        BuildMesh();

        _history.Clear();
        UpdateSparkline();

        CurrentStep = 0;
        RenderFrame();
        UpdateStatusText();
        OnPropertyChanged(nameof(EngineLabel));
    }

    private void InitializeGpuFromSeed(int res, int seed)
    {
        if (_gpuSolver == null) return;

        var rng = new Random(seed);
        var u = new double[res * res];
        var v = new double[res * res];

        Array.Fill(u, 1.0);
        Array.Clear(v);

        int blobCount = Math.Max(8, res * res / 400);
        for (int b = 0; b < blobCount; b++)
        {
            int cx = rng.Next(res), cy = rng.Next(res);
            int radius = rng.Next(3, Math.Max(6, res / 12));
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius) continue;
                    int px = ((cx + dx) % res + res) % res;
                    int py = ((cy + dy) % res + res) % res;
                    int i = py * res + px;
                    u[i] = 0.50 + rng.NextDouble() * 0.1;
                    v[i] = 0.25 + rng.NextDouble() * 0.1;
                }
        }

        _gpuSolver.Initialize(u, v);
    }

    private void BuildMaterial()
    {
        ImageSource src = (_useGpu && _gpuSolver != null && _gpuSolver.BridgeActive)
            ? _gpuSolver.BridgeImage!
            : _texture.Bitmap;

        var diffuseBrush = new ImageBrush(src)
        { Stretch = Stretch.Fill, TileMode = TileMode.None };
        _glowBrush = new ImageBrush(src)
        { Stretch = Stretch.Fill, TileMode = TileMode.None, Opacity = Glow };

        RenderOptions.SetBitmapScalingMode(diffuseBrush, BitmapScalingMode.Linear);
        RenderOptions.SetBitmapScalingMode(_glowBrush, BitmapScalingMode.Linear);

        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(diffuseBrush));
        group.Children.Add(new EmissiveMaterial(_glowBrush));
        group.Children.Add(new SpecularMaterial(
            new SolidColorBrush(Color.FromRgb(255, 255, 255)), 90));
        group.Children.Add(new SpecularMaterial(
            new SolidColorBrush(Color.FromRgb(70, 70, 95)), 14));

        SurfaceMaterial = group;
    }

    /// <summary>Вызывается из code-behind после загрузки окна.</summary>
    public void InitGpuBridge(IntPtr hwnd)
    {
        if (!_useGpu || _gpuSolver == null) return;
        if (_gpuSolver.TryInitBridge(hwnd))
            BuildMaterial();
    }

    private void BuildMesh()
    {
        _meshRes = Math.Min(GridResolution, 256);
        var mesh = MeshGenerator.Create(SelectedMeshType, _meshRes, _meshRes);

        _basePositions = mesh.Positions.ToArray();
        _baseNormals = mesh.Normals.Select(n => { n.Normalize(); return n; }).ToArray();

        Mesh3D = mesh;
        if (Relief > 0.001) UpdateRelief();
    }

    private void FlattenMesh()
    {
        if (Mesh3D is not { } mesh || _basePositions.Length == 0) return;
        mesh.Positions = new Point3DCollection(_basePositions);
        mesh.Normals = new Vector3DCollection(_baseNormals);
    }

    private void UpdateRelief()
    {
        if (Mesh3D is not { } mesh || _texture == null || _basePositions.Length == 0)
            return;

        int rU = _meshRes, rV = _meshRes;
        int w = _texture.Width, h = _texture.Height;
        var norm = _texture.Normalized;
        double amp = Relief;

        bool wrapU = SelectedMeshType != MeshType.Plane;
        bool wrapV = SelectedMeshType == MeshType.Torus;
        bool fadePoles = SelectedMeshType == MeshType.Sphere;

        int stride = rU + 1;
        var pts = new Point3D[_basePositions.Length];

        for (int j = 0; j <= rV; j++)
        {
            int sy = (wrapV && j == rV) ? 0 : Math.Min(h - 1, j * h / rV);
            int syM = sy == 0 ? (wrapV ? h - 1 : 0) : sy - 1;
            int syP = sy == h - 1 ? (wrapV ? 0 : h - 1) : sy + 1;
            double fade = fadePoles ? Math.Sin(Math.PI * j / rV) : 1.0;

            for (int i = 0; i <= rU; i++)
            {
                int sx = (wrapU && i == rU) ? 0 : Math.Min(w - 1, i * w / rU);
                int sxM = sx == 0 ? (wrapU ? w - 1 : 0) : sx - 1;
                int sxP = sx == w - 1 ? (wrapU ? 0 : w - 1) : sx + 1;

                double sum = norm[sy * w + sx] * 4.0
                           + norm[sy * w + sxM] + norm[sy * w + sxP]
                           + norm[syM * w + sx] + norm[syP * w + sx];
                double height = (sum / 8.0) * amp * fade;

                int idx = j * stride + i;
                pts[idx] = _basePositions[idx] + _baseNormals[idx] * height;
            }
        }

        var nrm = new Vector3D[pts.Length];
        for (int j = 0; j <= rV; j++)
        {
            int jm = j == 0 ? (wrapV ? rV - 1 : 0) : j - 1;
            int jp = j == rV ? (wrapV ? 1 : rV) : j + 1;

            for (int i = 0; i <= rU; i++)
            {
                int im = i == 0 ? (wrapU ? rU - 1 : 0) : i - 1;
                int ip = i == rU ? (wrapU ? 1 : rU) : i + 1;

                var tu = pts[j * stride + ip] - pts[j * stride + im];
                var tv = pts[jp * stride + i] - pts[jm * stride + i];

                var n = Vector3D.CrossProduct(tu, tv);
                int idx = j * stride + i;

                if (n.LengthSquared < 1e-12) n = _baseNormals[idx];
                n.Normalize();
                if (Vector3D.DotProduct(n, _baseNormals[idx]) < 0) n = -n;

                nrm[idx] = n;
            }
        }

        mesh.Positions = new Point3DCollection(pts);
        mesh.Normals = new Vector3DCollection(nrm);
    }

    private void RenderFrame()
    {
        if (_texture == null) return;

        bool bridge = _useGpu && _gpuSolver != null && _gpuSolver.BridgeActive;

        // Readback: каждый кадр без моста; раз в 10 кадров с мостом
        // (нужен только для статистики/рельефа/экспорта)
        bool needReadback = !bridge
                         || (_frameCounter % 10 == 0)
                         || Relief > 0.001;

        if (needReadback)
        {
            lock (_sync)
            {
                if (_useGpu && _gpuSolver != null)
                    _gpuSolver.CopyVTo(_vBuffer);
                else
                    _cpuSolver.CopyVTo(_vBuffer);
            }
            _texture.Update(_vBuffer);   // теневая текстура для экспорта/рельефа
        }

        if (bridge)
        {
            lock (_sync)
                _gpuSolver!.RenderToTexture(
                    (float)_rangeLo, (float)_rangeHi, (int)SelectedColorScheme);
        }

        _frameCounter++;

        if (Relief > 0.001 && (_frameCounter & 1) == 0)
            UpdateRelief();

        if (_isRecording)
        {
            SaveRecordFrame();
            _recordFrame++;
        }
    }

    private void StepSolver(int iters)
    {
        if (_useGpu && _gpuSolver != null)
        {
            _gpuSolver.Step(iters, FeedRate, KillRate,
                            DiffusionU, DiffusionV, GradientModeInt);
        }
        else
        {
            _cpuSolver.FeedRate = (float)FeedRate;
            _cpuSolver.KillRate = (float)KillRate;
            _cpuSolver.DiffusionU = (float)DiffusionU;
            _cpuSolver.DiffusionV = (float)DiffusionV;
            _cpuSolver.Step(iters);
        }
    }

    private int GradientModeInt => SelectedGradient switch
    {
        GradientType.None => 0,
        GradientType.Linear => 1,
        GradientType.Radial => 2,
        _ => 0
    };

    private async Task WarmupAsync(int steps)
    {
        try
        {
            await Task.Run(() => { lock (_sync) StepSolver(steps); });

            CurrentStep = _useGpu && _gpuSolver != null
                ? _gpuSolver.StepCount : _cpuSolver.StepCount;

            RenderFrame();
            UpdateStats();
            UpdateStatusText();
        }
        catch (Exception ex)
        {
            Flash($"Warmup error: {ex.Message}");
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var frameWatch = new Stopwatch();
        var fpsWatch = Stopwatch.StartNew();
        int frames = 0;

        while (!ct.IsCancellationRequested)
        {
            frameWatch.Restart();
            int iters = IterationsPerFrame;

            await Task.Run(() => { lock (_sync) StepSolver(iters); }, ct);
            if (ct.IsCancellationRequested) break;

            CurrentStep = _useGpu && _gpuSolver != null
                ? _gpuSolver.StepCount : _cpuSolver.StepCount;

            RenderFrame();

            frames++;
            if (fpsWatch.ElapsedMilliseconds >= 1000)
            {
                Fps = frames * 1000.0 / fpsWatch.ElapsedMilliseconds;
                frames = 0;
                fpsWatch.Restart();
            }

            if (_frameCounter % 10 == 0)
            {
                UpdateStats();
                UpdateStatusText();
            }

            int wait = 16 - (int)frameWatch.ElapsedMilliseconds;
            if (wait > 0) await Task.Delay(wait, ct);
        }
    }

    private void UpdateStats()
    {
        if (_vBuffer.Length == 0) return;

        double sum = 0;
        var hist = new int[16];
        for (int i = 0; i < _vBuffer.Length; i++)
        {
            double x = _vBuffer[i];
            sum += x;
            int bin = (int)(x * 16);
            if (bin > 15) bin = 15;
            hist[bin]++;
        }
        double avg = sum / _vBuffer.Length;
        AverageConcentration = avg;

        double e = 0;
        foreach (int c in hist)
        {
            if (c == 0) continue;
            double p = (double)c / _vBuffer.Length;
            e -= p * Math.Log(p, 2);
        }
        _entropy = e / 4.0;
        // Адаптивный диапазон для GPU-колоризации
        double min = double.MaxValue, max = double.MinValue;
        for (int i = 0; i < _vBuffer.Length; i++)
        {
            double x = _vBuffer[i];
            if (x < min) min = x;
            if (x > max) max = x;
        }
        if (!_hasRangeVm) { _rangeLo = min; _rangeHi = max; _hasRangeVm = true; }
        else
        {
            _rangeLo += (min - _rangeLo) * 0.2;
            _rangeHi += (max - _rangeHi) * 0.2;
        }
        _audio.Update(avg, _entropy);

        _history.Enqueue(avg);
        if (_history.Count > HistoryMax) _history.Dequeue();
        UpdateSparkline();

        // Вымирание + автореспавн с кулдауном
        if (IsRunning && AutoRespawn && avg < 0.004 && CurrentStep > 300)
        {
            if (DateTime.Now >= _nextRespawnAllowed)
            {
                _nextRespawnAllowed = DateTime.Now.AddSeconds(4);
                _respawnFails++;

                if (_respawnFails >= 5)
                {
                    Stop();
                    Flash(Loc["StatusNonViable"]);
                }
                else
                {
                    _seed = Random.Shared.Next();
                    ResetSimulation();
                    Flash(Loc["StatusExtinct"]);
                }
            }
        }
        else if (avg > 0.01)
        {
            _respawnFails = 0;
        }
    }

    private void UpdateSparkline()
    {
        var pts = new PointCollection();
        int i = 0;
        foreach (double v in _history)
        {
            double y = 36.0 - Math.Clamp(v / 0.35, 0.0, 1.0) * 34.0;
            pts.Add(new Point(i, y));
            i++;
        }
        _sparklinePoints = pts;
        OnPropertyChanged(nameof(SparklinePoints));
    }

    private void Flash(string text)
    {
        _flashText = text;
        _flashUntil = DateTime.Now.AddSeconds(2.5);
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (DateTime.Now < _flashUntil)
        {
            StatusText = _flashText;
            return;
        }

        string engine = _useGpu ? "[GPU] " : "[CPU] ";
        StatusText = IsRunning
            ? engine + Loc.Format("StatusRunning", CurrentStep)
            : CurrentStep == 0
                ? engine + Loc["StatusReady"]
                : engine + Loc.Format("StatusStopped", CurrentStep);
    }

    // ─── Кисть ───

    public void PaintAtWorldPosition(Point3D pos)
    {
        double u, v;

        switch (SelectedMeshType)
        {
            case MeshType.Sphere:
                u = (Math.Atan2(pos.Z, pos.X) / (2.0 * Math.PI) + 0.5) % 1.0;
                if (u < 0) u += 1.0;
                double r = Math.Sqrt(pos.X * pos.X + pos.Y * pos.Y + pos.Z * pos.Z);
                v = r > 1e-6 ? Math.Acos(Math.Clamp(pos.Y / r, -1.0, 1.0)) / Math.PI : 0.5;
                break;

            case MeshType.Torus:
                u = (Math.Atan2(pos.Z, pos.X) / (2.0 * Math.PI) + 0.5) % 1.0;
                if (u < 0) u += 1.0;
                double dist = Math.Sqrt(pos.X * pos.X + pos.Z * pos.Z);
                double local = dist - 2.0;
                v = (Math.Atan2(pos.Y, local) / (2.0 * Math.PI) + 0.5) % 1.0;
                if (v < 0) v += 1.0;
                break;

            case MeshType.Plane:
                u = Math.Clamp((pos.X + 2.5) / 5.0, 0.0, 1.0);
                v = Math.Clamp((pos.Z + 2.5) / 5.0, 0.0, 1.0);
                break;

            case MeshType.Cylinder:
                u = (Math.Atan2(pos.Z, pos.X) / (2.0 * Math.PI) + 0.5) % 1.0;
                if (u < 0) u += 1.0;
                v = Math.Clamp((pos.Y + 2.0) / 4.0, 0.0, 1.0);
                break;

            default:
                u = v = 0.5;
                break;
        }

        int tx = ((int)(u * GridResolution) % GridResolution + GridResolution) % GridResolution;
        int ty = ((int)(v * GridResolution) % GridResolution + GridResolution) % GridResolution;

        OnPaint((tx, ty));
    }

    private void OnPaint((int x, int y) coords)
    {
        int radius = Math.Max(2, (int)(BrushSize * GridResolution));
        int tool = (int)SelectedBrushTool;

        lock (_sync)
        {
            if (_useGpu && _gpuSolver != null)
                _gpuSolver.Modify(coords.x, coords.y, radius, BrushStrength, tool);
            else
                _cpuSolver.Modify(coords.x, coords.y, radius, BrushStrength, SelectedBrushTool);
        }

        if (!IsRunning) { RenderFrame(); UpdateStatusText(); }
    }

    // ─── Экспорт ───

    private void ExportState()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG image|*.png",
            FileName = $"rd_export_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            Title = Loc["DlgExportTitle"],
            AddExtension = true
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_texture.Bitmap));
            using var fs = new FileStream(dlg.FileName, FileMode.Create);
            encoder.Save(fs);

            _lastOutputDir = Path.GetDirectoryName(dlg.FileName) ?? "";
            OnPropertyChanged(nameof(HasOutput));
            CommandManager.InvalidateRequerySuggested();
            StatusText = $"Exported: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex) { StatusText = $"Export failed: {ex.Message}"; }
    }

    private void ExportMesh()
    {
        if (Mesh3D == null) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Wavefront OBJ|*.obj",
            FileName = $"rd_mesh_{DateTime.Now:yyyyMMdd_HHmmss}.obj",
            AddExtension = true
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            ObjExporter.Export(Mesh3D, dlg.FileName);
            _lastOutputDir = Path.GetDirectoryName(dlg.FileName) ?? "";
            OnPropertyChanged(nameof(HasOutput));
            CommandManager.InvalidateRequerySuggested();
            Flash(Loc.Format("StatusMeshExported", Path.GetFileName(dlg.FileName)));
        }
        catch (Exception ex) { StatusText = $"Mesh export failed: {ex.Message}"; }
    }

    private void ToggleRecording()
    {
        if (!_isRecording)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc["DlgRecordTitle"] };
            if (dlg.ShowDialog() != true) return;

            _recordFolder = dlg.FolderName;
            _recordFrame = 0;
            _isRecording = true;
            StatusText = $"Recording to {Path.GetFileName(_recordFolder)}...";
        }
        else
        {
            _isRecording = false;
            _lastOutputDir = _recordFolder;
            OnPropertyChanged(nameof(HasOutput));
            CommandManager.InvalidateRequerySuggested();
            StatusText = $"Saved {_recordFrame} frames → {_recordFolder}";
        }
    }

    private void SaveRecordFrame()
    {
        try
        {
            string path = Path.Combine(_recordFolder, $"frame_{_recordFrame:D6}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_texture.Bitmap));
            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);
        }
        catch { }
    }

    private void OpenOutputFolder()
    {
        if (!HasOutput) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_lastOutputDir}\"")
            { UseShellExecute = false });
        }
        catch { }
    }

    // ─── Жизненный цикл команд ───

    private void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    private void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        _cts = null;
        Fps = 0;
        _audio.Mute();
    }

    private void ResetSimulation()
    {
        lock (_sync)
        {
            _cpuSolver.Initialize(_seed);
            if (_useGpu && _gpuSolver != null)
                InitializeGpuFromSeed(GridResolution, _seed);
        }

        _texture.ResetRange();
        _history.Clear();
        UpdateSparkline();
        CurrentStep = 0;
        RenderFrame();
        UpdateStatusText();
    }

    private void Randomize()
    {
        _seed = Random.Shared.Next();
        ResetSimulation();
    }

    private void ApplyPreset(SimulationPreset? p)
    {
        if (p == null) return;
        FeedRate = p.FeedRate;
        KillRate = p.KillRate;
        DiffusionU = p.DiffusionU;
        DiffusionV = p.DiffusionV;
        ResetSimulation();
        _ = WarmupAsync(400);
        if (!IsRunning) Start();      // пресет всегда оживает сразу
    }

    public void Dispose()
    {
        _audio.Dispose();
        _gpuSolver?.Dispose();
    }

    // ─── INPC ───

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}