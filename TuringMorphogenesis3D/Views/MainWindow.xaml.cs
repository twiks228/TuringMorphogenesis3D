using HelixToolkit.Wpf;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Media3D;
using TuringMorphogenesis3D.ViewModels;

namespace TuringMorphogenesis3D.Views;

public partial class MainWindow : Window
{
    private bool _isPainting;

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateWindowState();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var model = new GeometryModel3D();

        BindingOperations.SetBinding(model, GeometryModel3D.GeometryProperty,
            new Binding(nameof(MainViewModel.Mesh3D)) { Source = vm, Mode = BindingMode.OneWay });
        BindingOperations.SetBinding(model, GeometryModel3D.MaterialProperty,
            new Binding(nameof(MainViewModel.SurfaceMaterial)) { Source = vm, Mode = BindingMode.OneWay });
        BindingOperations.SetBinding(model, GeometryModel3D.BackMaterialProperty,
            new Binding(nameof(MainViewModel.SurfaceMaterial)) { Source = vm, Mode = BindingMode.OneWay });
        BindingOperations.SetBinding(SpinRotation, AxisAngleRotation3D.AngleProperty,
            new Binding(nameof(MainViewModel.RotationAngle)) { Source = vm, Mode = BindingMode.OneWay });

        ModelHost.Content = model;

        vm.PropertyChanged += OnVmPropertyChanged;

        // Тяжёлая инициализация вне конструктора XAML
        vm.Startup();

        // Окно уже имеет hwnd — включаем zero-readback GPU-рендер
        var hwnd = new WindowInteropHelper(this).Handle;
        vm.InitGpuBridge(hwnd);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.PresentationMode)) return;
        if (DataContext is not MainViewModel vm) return;

        WindowState = vm.PresentationMode ? WindowState.Maximized : WindowState.Normal;
    }

    // ─── Кисть: Shift + ЛКМ ───

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        if (DataContext is not MainViewModel) return;

        _isPainting = true;
        PaintAtScreenPosition(e.GetPosition(Viewport.Viewport));
        e.Handled = true;
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPainting) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        PaintAtScreenPosition(e.GetPosition(Viewport.Viewport));
        e.Handled = true;
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e) => _isPainting = false;

    private void PaintAtScreenPosition(System.Windows.Point screenPos)
    {
        if (DataContext is not MainViewModel vm) return;

        var hits = Viewport3DHelper.FindHits(Viewport.Viewport, screenPos);
        if (hits.Count > 0)
            vm.PaintAtWorldPosition(hits[0].Position);
    }

    // ─── Window chrome ───

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateWindowState()
    {
        bool max = WindowState == WindowState.Maximized;
        MaximizeButton.Content = max ? "\uE923" : "\uE922";
        RootBorder.Padding = max
            ? SystemParameters.WindowResizeBorderThickness
            : new Thickness(0);
    }
}