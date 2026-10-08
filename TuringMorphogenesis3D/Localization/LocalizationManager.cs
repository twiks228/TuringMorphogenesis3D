using System.Collections.Generic;
using System.ComponentModel;

namespace TuringMorphogenesis3D.Localization;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();
    private LocalizationManager() { }

    private bool _isEnglish = true;
    public bool IsEnglish
    {
        get => _isEnglish;
        set
        {
            if (_isEnglish == value) return;
            _isEnglish = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnglish)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public void Toggle() => IsEnglish = !IsEnglish;

    public string this[string key] =>
        Table.TryGetValue(key, out var p) ? (_isEnglish ? p.En : p.Ru) : $"[{key}]";

    public string Format(string key, params object[] args) =>
        string.Format(this[key], args);

    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly Dictionary<string, (string En, string Ru)> Table = new()
    {
        ["AppTitle"] = ("Turing Morphogenesis 3D", "Морфогенез Тьюринга 3D"),
        ["AppSubtitle"] = ("Gray-Scott Reaction-Diffusion", "Реакция–диффузия Грея–Скотта"),

        ["BtnStart"] = ("▶  Start", "▶  Старт"),
        ["BtnStop"] = ("⏹  Stop", "⏹  Стоп"),
        ["BtnReset"] = ("↺  Reset", "↺  Сброс"),
        ["BtnRandom"] = ("🎲  Random", "🎲  Случайно"),
        ["BtnLanguage"] = ("🌐 Русский", "🌐 English"),

        ["HeaderControl"] = ("SIMULATION", "СИМУЛЯЦИЯ"),
        ["HeaderPresets"] = ("PRESET PATTERNS", "ГОТОВЫЕ ПАТТЕРНЫ"),
        ["HeaderCustomPresets"] = ("MY PATTERNS", "МОИ ПАТТЕРНЫ"),
        ["HeaderParameters"] = ("PARAMETERS", "ПАРАМЕТРЫ"),
        ["HeaderSurface"] = ("SURFACE SHAPE", "ФОРМА ПОВЕРХНОСТИ"),
        ["HeaderGradient"] = ("MORPHOGENETIC GRADIENT", "МОРФОГЕНЕТИЧЕСКИЙ ГРАДИЕНТ"),
        ["HeaderColorScheme"] = ("COLOR SCHEME", "ЦВЕТОВАЯ СХЕМА"),
        ["HeaderResolution"] = ("GRID RESOLUTION", "РАЗРЕШЕНИЕ СЕТКИ"),
        ["HeaderRender"] = ("RENDERING", "ОТОБРАЖЕНИЕ"),

        ["LblFeed"] = ("Feed rate (f)", "Скорость подачи (f)"),
        ["LblKill"] = ("Kill rate (k)", "Скорость убыли (k)"),
        ["LblDu"] = ("Diffusion U (Du)", "Диффузия U (Du)"),
        ["LblDv"] = ("Diffusion V (Dv)", "Диффузия V (Dv)"),
        ["LblIterations"] = ("Iterations / frame", "Итераций / кадр"),
        ["LblRelief"] = ("Relief height", "Высота рельефа"),
        ["LblGlow"] = ("Glow", "Свечение"),
        ["LblAutoRotate"] = ("Auto-rotate", "Автовращение"),
        ["LblAutoRespawn"] = ("Auto-respawn on extinction", "Автореспавн при вымирании"),
        ["LblStep"] = ("Step", "Шаг"),
        ["LblHistory"] = ("V̄ HISTORY", "ИСТОРИЯ V̄"),
        ["LblPresetName"] = ("Pattern name…", "Название паттерна…"),
        ["BtnSavePreset"] = ("⭐ Save current as pattern", "⭐ Сохранить как паттерн"),

        ["PresetSpots"] = ("🐆 Leopard Spots", "🐆 Пятна леопарда"),
        ["PresetStripes"] = ("🦓 Zebra Stripes", "🦓 Полосы зебры"),
        ["PresetLabyrinth"] = ("🌀 Labyrinth", "🌀 Лабиринт"),
        ["PresetCoral"] = ("🌺 Coral", "🌺 Коралл"),
        ["PresetMitosis"] = ("🔬 Mitosis", "🔬 Митоз"),
        ["PresetMaze"] = ("🧩 Maze", "🧩 Лабиринт-сетка"),
        ["PresetHoles"] = ("🫧 Holes", "🫧 Пузыри"),
        ["PresetWaves"] = ("🌊 Waves", "🌊 Волны"),

        ["MeshSphere"] = ("🌍 Sphere", "🌍 Сфера"),
        ["MeshTorus"] = ("🍩 Torus", "🍩 Тор"),
        ["MeshPlane"] = ("📄 Plane", "📄 Плоскость"),
        ["MeshCylinder"] = ("🧪 Cylinder", "🧪 Цилиндр"),

        ["GradientNone"] = ("None", "Нет"),
        ["GradientLinear"] = ("Linear (head ↔ tail)", "Линейный (голова ↔ хвост)"),
        ["GradientRadial"] = ("Radial (center → edge)", "Радиальный (центр → край)"),

        ["SchemeOcean"] = ("🌊 Ocean", "🌊 Океан"),
        ["SchemeFire"] = ("🔥 Fire", "🔥 Огонь"),
        ["SchemeJungle"] = ("🌿 Jungle", "🌿 Джунгли"),
        ["SchemeGrayscale"] = ("⬛ Grayscale", "⬛ Оттенки серого"),
        ["SchemeNeon"] = ("💜 Neon", "💜 Неон"),

        ["StatusReady"] = ("Ready", "Готов"),
        ["StatusRunning"] = ("Simulating… step {0:N0}", "Симуляция… шаг {0:N0}"),
        ["StatusStopped"] = ("Paused at step {0:N0}", "Пауза на шаге {0:N0}"),
        ["StatusExtinct"] = ("Pattern extinct — respawning…", "Паттерн вымер — респавн…"),
        ["StatusPresetSaved"] = ("Pattern saved: {0}", "Паттерн сохранён: {0}"),

        ["HintControls"] = ("LMB: rotate  •  RMB: pan  •  Wheel: zoom  •  F5: start/stop  •  F6: reset  •  Shift+Drag: paint",
                            "ЛКМ: вращение  •  ПКМ: сдвиг  •  Колесо: масштаб  •  F5: старт/стоп  •  F6: сброс  •  Shift+тянем: кисть"),

        ["ToolsHeader"] = ("TOOLS", "ИНСТРУМЕНТЫ"),
        ["LblGpu"] = ("GPU acceleration", "GPU-ускорение"),
        ["BtnOpenFolder"] = ("📁 Open output folder", "📁 Папка с результатом"),
        ["DlgExportTitle"] = ("Export texture as PNG", "Экспорт текстуры в PNG"),
        ["DlgRecordTitle"] = ("Choose folder for frame sequence", "Папка для серии кадров"),

        ["TipMinimize"] = ("Minimize", "Свернуть"),
        ["TipMaximize"] = ("Maximize / Restore", "Развернуть / Восстановить"),
        ["TipClose"] = ("Close", "Закрыть"),
        ["TipDeletePreset"] = ("Delete", "Удалить"),
        ["TabSim"] = ("Simulation", "Симуляция"),
        ["TabPatterns"] = ("Patterns", "Паттерны"),
        ["TabSurface"] = ("Surface", "Поверхность"),
        ["TabTools"] = ("Tools", "Инструменты"),

        ["BrushPaint"] = ("🖌 Paint", "🖌 Кисть"),
        ["BrushErase"] = ("🧽 Erase", "🧽 Ластик"),
        ["BrushSmooth"] = ("🌫 Smooth", "🌫 Сглаживание"),
        ["BrushStamp"] = ("⭐ Stamp", "⭐ Штамп"),

        ["LblAudio"] = ("Ambient sound", "Эмбиент-звук"),
        ["BtnExportMesh"] = ("🧊 Export mesh OBJ", "🧊 Экспорт меша OBJ"),
        ["TipTogglePanel"] = ("Hide / show panel", "Скрыть / показать панель"),
        ["BtnPresent"] = ("Presentation mode (Esc to exit)", "Презентационный режим (Esc — выход)"),
        ["LblPearson"] = ("PEARSON VIABILITY MAP", "КАРТА ЖИЗНЕСПОСОБНОСТИ ПИРСОНА"),
        ["StatusMeshExported"] = ("Mesh exported: {0}", "Меш экспортирован: {0}"),
    };
}