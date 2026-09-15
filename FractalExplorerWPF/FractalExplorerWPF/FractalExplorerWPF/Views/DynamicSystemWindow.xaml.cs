using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.ColorPicking;
using FractalExplorerWPF.Models;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class DynamicSystemWindow : Window
{
    private readonly DynamicSystemKind _kind;
    private readonly DynamicSystemSaveStore _saves;
    private readonly DynamicPaletteStore? _paletteStore;
    private readonly Dictionary<string, TextBox> _boxes = [];
    private readonly Dictionary<string, ComboBox> _choices = [];
    private readonly Dictionary<string, StackPanel> _fieldPanels = [];
    private readonly Dictionary<string, TextBlock> _fieldLabels = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _visualizationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly ConcurrentQueue<DynamicTileRenderEvent> _visualizationEvents = new();
    private readonly TransformGroup _previewTransform = new();
    private readonly ScaleTransform _previewScale = new(1, 1);
    private readonly TranslateTransform _previewTranslation = new();
    private DynamicSystemState _state;
    private List<DynamicPalette> _palettes = [];
    private CancellationTokenSource? _cts;
    private WriteableBitmap? _progressiveBitmap;
    private bool _rendering, _syncing, _panning, _controls = true, _fullscreen, _hasRenderedFrame;
    private Point _panStart;
    private double _renderedCenterX, _renderedCenterY, _renderedZoom = 1;
    private double _renderedAMin, _renderedAMax, _renderedBMin, _renderedBMax;
    private WindowStyle _oldStyle;
    private WindowState _oldState;
    private TextBlock? _attractorFormulaText;

    public DynamicSystemWindow(DynamicSystemKind kind)
    {
        _kind = kind; _state = DynamicSystemState.CreateDefault(kind); _saves = new(kind);
        if (kind is DynamicSystemKind.Lyapunov or DynamicSystemKind.LogisticMap) _paletteStore = new(kind);
        InitializeComponent();
        _previewTransform.Children.Add(_previewScale);
        _previewTransform.Children.Add(_previewTranslation);
        StableImage.RenderTransformOrigin = new Point(0.5, 0.5);
        StableImage.RenderTransform = _previewTransform;
        Title = DisplayName(kind);
        BuildParameterPanel(); LoadPalettes(); SyncControls(); UpdateSwatches();
        _timer.Tick += (_, _) => { _timer.Stop(); _ = RenderAsync(); };
        _visualizationTimer.Tick += (_, _) => FlushVisualizationEvents(false);
        Loaded += (_, _) => Schedule();
    }

    private void BuildParameterPanel()
    {
        if (_kind == DynamicSystemKind.Attractors2D)
        {
            AddChoice("Формула", "Attractor2DMode",
            new ChoiceOption[]
            {
                new("Клиффорд", nameof(Attractor2DKind.Clifford)),
                new("Питер де Йонг", nameof(Attractor2DKind.PeterDeJong)),
                new("Tinkerbell", nameof(Attractor2DKind.Tinkerbell)),
                new("Gumowski–Mira", nameof(Attractor2DKind.GumowskiMira))
            });
            _attractorFormulaText = new TextBlock
            {
                Margin = new Thickness(0, 2, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            _attractorFormulaText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.SecondaryTextBrush");
            ParameterPanel.Children.Add(_attractorFormulaText);
        }
        foreach ((string label, string key) in Fields(_kind)) AddField(label, key);
        if (_kind is DynamicSystemKind.Lorenz or DynamicSystemKind.Rossler) AddChoice("Проекция", "ProjectionMode", ["XY", "XZ", "YZ"]);
        if (_kind == DynamicSystemKind.LogisticMap) AddChoice("Режим", "VisualizationMode", ["Orbit", "Bifurcation", "Cobweb"]);
        if (_kind is DynamicSystemKind.Lyapunov or DynamicSystemKind.Attractors2D) AddChoice("Сглаживание", "SsaaFactor", ["1", "2", "4"]);
        AddThreadChoice();
        PaletteButton.Visibility = _paletteStore is null ? Visibility.Collapsed : Visibility.Visible;
        FractalColorPanel.Visibility = _kind is DynamicSystemKind.Bifurcation or DynamicSystemKind.Attractors2D ? Visibility.Visible : Visibility.Collapsed;
        BackgroundColorPanel.Visibility = _kind is DynamicSystemKind.Lyapunov or DynamicSystemKind.Henon or DynamicSystemKind.Ikeda ? Visibility.Collapsed : Visibility.Visible;
        FractalColorButton.Content = _kind == DynamicSystemKind.Attractors2D ? "Цвет плотности" : "Цвет фрактала";
        UpdateAttractorPresentation();
    }

    private static IEnumerable<(string, string)> Fields(DynamicSystemKind kind) => kind switch
    {
        DynamicSystemKind.Lyapunov => [("Мин. A","AMin"),("Макс. A","AMax"),("Мин. B","BMin"),("Макс. B","BMax"),("Паттерн A/B","Pattern"),("Итерации","Iterations"),("Прогрев","TransientIterations")],
        DynamicSystemKind.Lorenz => [("σ","Sigma"),("ρ","Rho"),("β","Beta"),("dt","Dt"),("Шаги","Steps"),("Старт X","StartX"),("Старт Y","StartY"),("Старт Z","StartZ"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        DynamicSystemKind.Rossler => [("a","A"),("b","B"),("c","C"),("dt","Dt"),("Шаги","Steps"),("Старт X","StartX"),("Старт Y","StartY"),("Старт Z","StartZ"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        DynamicSystemKind.LogisticMap => [("Параметр r","R"),("Нач. x₀","X0"),("Итерации","Iterations"),("Прогрев","TransientIterations"),("Bif r min","BifurcationRMin"),("Bif r max","BifurcationRMax"),("Bif samples","BifurcationSamples"),("Bif transient","BifurcationTransient"),("Bif plotted","BifurcationPlottedPoints"),("Cobweb шаги","CobwebSteps"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        DynamicSystemKind.Bifurcation => [("r min","RMin"),("r max","RMax"),("x min","XMin"),("x max","XMax"),("Прогрев","TransientIterations"),("Samples / r","SamplesPerR"),("Итерации","Iterations"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        DynamicSystemKind.Henon => [("Параметр a","A"),("Параметр b","B"),("Нач. x₀","X0"),("Нач. y₀","Y0"),("Итерации","Iterations"),("Пропуск","DiscardIterations"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        DynamicSystemKind.Attractors2D => [("a","A"),("b","B"),("c","C"),("d","D"),("Нач. x₀","X0"),("Нач. y₀","Y0"),("Число точек","Iterations"),("Прогрев","DiscardIterations"),("Гамма плотности","DensityGamma"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")],
        _ => [("Параметр u","U"),("Нач. x₀","X0"),("Нач. y₀","Y0"),("Итерации","Iterations"),("Пропуск","DiscardIterations"),("X min","RangeXMin"),("X max","RangeXMax"),("Y min","RangeYMin"),("Y max","RangeYMax"),("Центр X","CenterX"),("Центр Y","CenterY"),("Масштаб","Zoom")]
    };

    private void AddField(string label, string key)
    {
        var panel = new StackPanel();
        var labelBlock = new TextBlock { Text = label };
        panel.Children.Add(labelBlock);
        var box = new TextBox { Tag = key }; panel.Children.Add(box); _boxes[key] = box;
        _fieldPanels[key] = panel; _fieldLabels[key] = labelBlock;
        box.TextChanged += (_, _) => { if (!_syncing) Schedule(); };
        ParameterPanel.Children.Add(panel);
    }

    private void AddChoice(string label, string key, string[] values)
        => AddChoice(label, key, values.Select(value => new ChoiceOption(value, value)).ToArray());

    private void AddThreadChoice()
    {
        ChoiceOption[] values =
        [
            .. Enumerable.Range(1, Environment.ProcessorCount)
                .Select(value => new ChoiceOption(value.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture))),
            new("Auto", "Auto")
        ];
        AddChoice("Потоки ЦП", "Threads", values);
    }

    private void AddChoice(string label, string key, ChoiceOption[] values)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label });
        var combo = new ComboBox { Name = key + "Box", ItemsSource=values, DisplayMemberPath=nameof(ChoiceOption.Display), Tag=key };
        _choices[key]=combo; combo.SelectionChanged += Choice_OnChanged; panel.Children.Add(combo); ParameterPanel.Children.Add(panel);
    }

    private void Choice_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || sender is not ComboBox { Tag: string key, SelectedItem: ChoiceOption option }) return;
        PropertyInfo? property = typeof(DynamicSystemState).GetProperty(key); if (property is null) return;
        object value = key == "Threads" && option.Value == "Auto"
            ? Environment.ProcessorCount
            : property.PropertyType == typeof(int) ? int.Parse(option.Value, CultureInfo.InvariantCulture) : option.Value;
        property.SetValue(_state, value);
        if (key == "Attractor2DMode")
        {
            _state.ApplyAttractor2DPreset(Attractor2DRenderer.ParseKind(option.Value));
            SyncControls();
            UpdateAttractorPresentation();
        }
        Schedule();
    }

    private void UpdateAttractorPresentation()
    {
        if (_kind != DynamicSystemKind.Attractors2D || _attractorFormulaText is null) return;
        Attractor2DKind kind = Attractor2DRenderer.ParseKind(_state.Attractor2DMode);
        if (_fieldPanels.TryGetValue("D", out StackPanel? dPanel))
            dPanel.Visibility = kind == Attractor2DKind.GumowskiMira ? Visibility.Collapsed : Visibility.Visible;
        if (_fieldLabels.TryGetValue("C", out TextBlock? cLabel))
            cLabel.Text = kind == Attractor2DKind.GumowskiMira ? "μ" : "c";
        _attractorFormulaText.Text = kind switch
        {
            Attractor2DKind.Clifford => "x′ = sin(a·y) + c·cos(a·x)\ny′ = sin(b·x) + d·cos(b·y)",
            Attractor2DKind.PeterDeJong => "x′ = sin(a·y) − cos(b·x)\ny′ = sin(c·x) − cos(d·y)",
            Attractor2DKind.Tinkerbell => "x′ = x² − y² + a·x + b·y\ny′ = 2xy + c·x + d·y",
            _ => "x′ = y + a(1 − b·y²)y + f(x)\ny′ = −x + f(x′),  f(t) = μt + 2(1−μ)t²/(1+t²)"
        } + "\n\nОкраска: монохромная плотность. Основа для многоцветных палитр уже отделена от расчёта орбит.";
    }

    private void LoadPalettes()
    {
        if (_paletteStore is null) return;
        _palettes = _paletteStore.Load();
    }
    private DynamicPalette? ActivePalette => _palettes.FirstOrDefault(p => p.Name == _state.PaletteName) ?? _palettes.FirstOrDefault();

    private DynamicSystemState CaptureState(string name)
    {
        foreach ((string key, TextBox box) in _boxes)
        {
            PropertyInfo p = typeof(DynamicSystemState).GetProperty(key)!;
            if (p.PropertyType == typeof(string)) { p.SetValue(_state, box.Text.Trim()); continue; }
            if (p.PropertyType == typeof(int)) { if (!int.TryParse(box.Text, out int v) || v < 0) throw new InvalidOperationException($"Некорректное значение: {key}"); p.SetValue(_state, v); }
            else { if (!TryDouble(box.Text, out double v) || !double.IsFinite(v)) throw new InvalidOperationException($"Некорректное значение: {key}"); p.SetValue(_state, v); }
        }
        if (_state.Zoom <= 0 || _state.Threads < 1 || _state.Threads > Environment.ProcessorCount) throw new InvalidOperationException($"Масштаб должен быть положительным, число потоков — от 1 до {Environment.ProcessorCount}.");
        if (_kind == DynamicSystemKind.Lyapunov && (_state.AMax <= _state.AMin || _state.BMax <= _state.BMin || !_state.Pattern.Any(c => c is 'A' or 'a' or 'B' or 'b'))) throw new InvalidOperationException("Проверьте диапазоны A/B и паттерн.");
        if (_kind == DynamicSystemKind.Attractors2D && (_state.Iterations < 1 || _state.DensityGamma is < .05 or > 8)) throw new InvalidOperationException("Число точек должно быть положительным, гамма плотности — от 0.05 до 8.");
        DynamicSystemState result = _state.Clone(name); result.Timestamp = DateTime.Now; result.PaletteName = ActivePalette?.Name ?? string.Empty; return result;
    }

    private void LoadState(DynamicSystemState state) { _cts?.Cancel(); EndVisualization(); CurrentImage.Source=null; int threads = _state.Threads; _state = state.Clone(); _state.Kind = _kind; _state.Threads = threads; SyncControls(); UpdateAttractorPresentation(); UpdateSwatches(); UpdatePreviewTransform(); Schedule(); }
    private void SyncControls()
    {
        _syncing=true;
        foreach ((string key, TextBox box) in _boxes) box.Text = Format(typeof(DynamicSystemState).GetProperty(key)!.GetValue(_state));
        foreach ((string key,ComboBox combo) in _choices)
        {
            string value = Format(typeof(DynamicSystemState).GetProperty(key)!.GetValue(_state));
            if (key == "Threads" && _state.Threads == Environment.ProcessorCount) value = "Auto";
            combo.SelectedItem = combo.Items.OfType<ChoiceOption>().FirstOrDefault(option => option.Value == value);
        }
        _syncing=false;
    }

    private async Task RenderAsync()
    {
        if (_rendering) { Schedule(); return; }
        DynamicSystemState state; try { state = CaptureState("preview"); } catch (Exception ex) { StatusText.Text=ex.Message; return; }
        _cts?.Cancel(); _cts?.Dispose(); _cts=new(); CancellationToken token=_cts.Token; _rendering=true; CancelButton.IsEnabled=true; RenderBadge.Visibility=Visibility.Visible; var watch=Stopwatch.StartNew();
        WriteableBitmap? overlay=null;BitmapSource? image=null;
        try
        {
            RenderSurfaceMetrics surface=RenderSurfaceMetrics.Measure(CanvasSurface);DpiScale dpi=surface.Dpi;int width=surface.PixelWidth,height=surface.PixelHeight;
            Action<MandelbrotRenderTile,byte[]>? tileReady=null;
            Action<MandelbrotRenderTile>? tileStarted=null;
            if (_kind==DynamicSystemKind.Lyapunov)
            {
                int factor=Math.Clamp(state.SsaaFactor,1,4);
                int renderWidth=width*factor,renderHeight=height*factor;
                overlay=ProgressiveRenderBitmap.CreateOverlay(renderWidth,renderHeight,dpi.PixelsPerInchX,dpi.PixelsPerInchY); CurrentImage.Source=overlay;
                BeginVisualization(overlay,renderWidth,renderHeight);
                tileStarted=tile=>_visualizationEvents.Enqueue(new(true,tile,null));
                tileReady=(tile,data)=>_visualizationEvents.Enqueue(new(false,tile,data));
            }
            var progress=new Progress<int>(p=>{ProgressBar.Value=p;ProgressText.Text=$"Рендер: {p}%";RenderBadgeText.Text=$"{p}%";});
            image=await DynamicSystemRenderer.RenderAsync(state,width,height,ActivePalette,token,progress,tileReady,tileStarted:tileStarted,dpiX:dpi.PixelsPerInchX,dpiY:dpi.PixelsPerInchY);
            FlushVisualizationEvents(true);
            if(token.IsCancellationRequested){CurrentImage.Source=null;StatusText.Text="Рендер отменён";return;} StableImage.Source=image; CurrentImage.Source=null; RememberRenderedViewport(state); UpdatePreviewTransform(); ProgressBar.Value=100; ProgressText.Text="Готово"; StatusText.Text=$"Готово за {watch.Elapsed.TotalSeconds:F2} сек.";
        }
        catch(OperationCanceledException){CurrentImage.Source=null;StatusText.Text="Рендер отменён";}
        catch(Exception ex){CurrentImage.Source=null;MessageBox.Show(this,ex.Message,DisplayName(_kind),MessageBoxButton.OK,MessageBoxImage.Error);}
        finally
        {
            CurrentImage.Source=null;EndVisualization();overlay=null;image=null;
            if(_kind==DynamicSystemKind.Lyapunov)
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
                await MemoryPressureRelief.ReleaseAsync();
            }
            _rendering=false;CancelButton.IsEnabled=false;RenderBadge.Visibility=Visibility.Collapsed;
        }
    }

    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StableImage, CurrentImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        DynamicSystemState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        DynamicSystemRenderer.RenderAsync(state.Clone(), width, height, FindPalette(state.PaletteName), token, progress, null, false);
    private DynamicPalette? FindPalette(string name)=>_palettes.FirstOrDefault(p=>p.Name==name)??_palettes.FirstOrDefault();

    private void Schedule(){if(!IsLoaded)return;_timer.Stop();_timer.Start();}
    private void Render_OnClick(object sender,RoutedEventArgs e){_timer.Stop();_cts?.Cancel();_=RenderAsync();}
    private void Cancel_OnClick(object sender,RoutedEventArgs e)=>_cts?.Cancel();
    private void Reset_OnClick(object sender,RoutedEventArgs e)
    {
        DynamicSystemState defaults=DynamicSystemState.CreateDefault(_kind);
        if(_kind==DynamicSystemKind.Attractors2D)defaults.ApplyAttractor2DPreset(Attractor2DRenderer.ParseKind(_state.Attractor2DMode));
        _state.CenterX=defaults.CenterX;_state.CenterY=defaults.CenterY;_state.Zoom=1;
        if(_kind==DynamicSystemKind.Lyapunov){_state.AMin=defaults.AMin;_state.AMax=defaults.AMax;_state.BMin=defaults.BMin;_state.BMax=defaults.BMax;}
        SyncControls();UpdatePreviewTransform();Schedule();
    }
    private void Palette_OnClick(object sender, RoutedEventArgs e)
    {
        if (_paletteStore is null) return;
        if (_kind == DynamicSystemKind.Lyapunov)
        {
            var dialog = new LyapunovPaletteWindow(_paletteStore, _palettes, ActivePalette) { Owner = this };
            dialog.PaletteApplied += (_, _) => ApplyPalette(dialog.SelectedPalette?.Name);
            dialog.ShowDialog();
            return;
        }
        var genericDialog = new DynamicPaletteWindow(_paletteStore, _palettes, ActivePalette) { Owner = this };
        if (genericDialog.ShowDialog() == true) ApplyPalette(genericDialog.SelectedPalette?.Name);
    }

    private void ApplyPalette(string? paletteName)
    {
        if (!string.IsNullOrWhiteSpace(paletteName)) _state.PaletteName = paletteName;
        LoadPalettes();
        Schedule();
    }
    private void FractalColor_OnClick(object sender,RoutedEventArgs e){if(ColorSelectionService.Default.TrySelectColor(this,_state.FractalColor,out Color c)){_state.FractalColor=c;UpdateSwatches();Schedule();}}
    private void BackgroundColor_OnClick(object sender,RoutedEventArgs e){if(ColorSelectionService.Default.TrySelectColor(this,_state.BackgroundColor,out Color c)){_state.BackgroundColor=c;UpdateSwatches();Schedule();}}
    private void UpdateSwatches(){FractalColorSwatch.Background=new SolidColorBrush(_state.FractalColor);BackgroundColorSwatch.Background=new SolidColorBrush(_state.BackgroundColor);}

    private void Saves_OnClick(object sender,RoutedEventArgs e)
    {
        IReadOnlyList<DynamicSystemState> presets=_kind switch
        {
            DynamicSystemKind.Lyapunov =>
            [
                new(){Kind=_kind,SaveName="Классический AB",Timestamp=DateTime.MinValue,PointOfInterestId="classic_ab",AMin=2.5,AMax=4,BMin=2.5,BMax=4,Pattern="AB",Iterations=320,TransientIterations=80,PaletteName="Классическая Ляпунова"},
                new(){Kind=_kind,SaveName="ABBA-структуры",Timestamp=DateTime.MinValue,PointOfInterestId="abba",AMin=3.2,AMax=4,BMin=2.6,BMax=3.6,Pattern="ABBA",Iterations=350,TransientIterations=100,PaletteName="Классическая Ляпунова"}
            ],
            DynamicSystemKind.Attractors2D => Attractor2DPointsOfInterest(),
            _ => []
        };
        SaveManagerWindow.Open(this,new SaveManagerConfiguration<DynamicSystemState>{WindowTitle=$"Сохранение/Загрузка: {DisplayName(_kind)}",Store=_saves,CaptureState=CaptureState,CapturePreview=CaptureCurrentPreview,LoadState=LoadState,RenderPreviewAsync=RenderStatePreviewAsync,GetName=s=>s.SaveName,GetTimestamp=s=>s.Timestamp,GetDetails=s=>$"{s.Timestamp:g} · {Details(s)}",PointsOfInterest=presets});
    }

    private static IReadOnlyList<DynamicSystemState> Attractor2DPointsOfInterest() =>
    [
        CreateAttractor2DPoint("Клиффорд — классический", "clifford_classic", Attractor2DKind.Clifford),
        CreateAttractor2DPoint("Питер де Йонг — вихрь", "de_jong_swirl", Attractor2DKind.PeterDeJong),
        CreateAttractor2DPoint("Tinkerbell — классический", "tinkerbell_classic", Attractor2DKind.Tinkerbell),
        CreateAttractor2DPoint("Gumowski–Mira — организм", "gumowski_mira_organism", Attractor2DKind.GumowskiMira)
    ];

    private static DynamicSystemState CreateAttractor2DPoint(string name, string id, Attractor2DKind kind)
    {
        DynamicSystemState state=DynamicSystemState.CreateDefault(DynamicSystemKind.Attractors2D);
        state.ApplyAttractor2DPreset(kind);state.SaveName=name;state.PointOfInterestId=id;state.Timestamp=DateTime.MinValue;
        return state;
    }

    private void Export_OnClick(object sender,RoutedEventArgs e)
    {
        int w=Math.Max(1,(int)CanvasSurface.ActualWidth),h=Math.Max(1,(int)CanvasSurface.ActualHeight);_cts?.Cancel();try{_=CaptureState("export");}catch(Exception ex){MessageBox.Show(this,ex.Message,"Параметры экспорта",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        ImageExportManagerWindow.Open(this,new ImageExportConfiguration{FileNamePrefix=_kind.ToString(),InitialWidth=w,InitialHeight=h,MaxSsaaFactor=4,ReleaseMemoryAfterExport=_kind==DynamicSystemKind.Lyapunov,RenderAsync=(request,token,progress)=>{DynamicSystemState state=CaptureState("export");state.SsaaFactor=request.SsaaFactor;return DynamicSystemRenderer.RenderAsync(state,request.Width,request.Height,ActivePalette,token,progress,null,false);}});
    }

    private void CanvasHost_OnSizeChanged(object sender,SizeChangedEventArgs e){UpdatePreviewTransform();Schedule();}
    private void CanvasHost_OnMouseWheel(object sender,MouseWheelEventArgs e){CommitAndBakePreview();double k=e.Delta>0?.82:1.22;Point p=e.GetPosition(CanvasSurface);if(_kind==DynamicSystemKind.Lyapunov){double ax=_state.AMin+p.X/Math.Max(1,CanvasSurface.ActualWidth)*(_state.AMax-_state.AMin),by=_state.BMax-p.Y/Math.Max(1,CanvasSurface.ActualHeight)*(_state.BMax-_state.BMin);_state.AMin=ax+(_state.AMin-ax)*k;_state.AMax=ax+(_state.AMax-ax)*k;_state.BMin=by+(_state.BMin-by)*k;_state.BMax=by+(_state.BMax-by)*k;}else{_state.Zoom=Math.Clamp(_state.Zoom/k,.01,1_000_000);}SyncControls();UpdatePreviewTransform();Schedule();e.Handled=true;}
    private void CanvasHost_OnMouseLeftButtonDown(object sender,MouseButtonEventArgs e){CommitAndBakePreview();_panning=true;_panStart=e.GetPosition(CanvasSurface);CanvasHost.CaptureMouse();Mouse.OverrideCursor=Cursors.SizeAll;}
    private void CanvasHost_OnMouseMove(object sender,MouseEventArgs e){if(!_panning)return;Point p=e.GetPosition(CanvasSurface);double dx=(p.X-_panStart.X)/Math.Max(1,CanvasSurface.ActualWidth),dy=(p.Y-_panStart.Y)/Math.Max(1,CanvasSurface.ActualHeight);if(_kind==DynamicSystemKind.Lyapunov){double aw=_state.AMax-_state.AMin,bh=_state.BMax-_state.BMin;_state.AMin-=dx*aw;_state.AMax-=dx*aw;_state.BMin+=dy*bh;_state.BMax+=dy*bh;}else{double span=BaseSpan(_state)/_state.Zoom;_state.CenterX-=dx*span;_state.CenterY+=dy*span;}_panStart=p;SyncControls();UpdatePreviewTransform();}
    private void CanvasHost_OnMouseLeftButtonUp(object sender,MouseButtonEventArgs e){if(!_panning)return;_panning=false;CanvasHost.ReleaseMouseCapture();Mouse.OverrideCursor=null;Schedule();}

    private void BeginVisualization(WriteableBitmap bitmap,int renderWidth,int renderHeight)
    {
        while(_visualizationEvents.TryDequeue(out _)){}
        _progressiveBitmap=bitmap;
        RenderOverlay.BeginSession(renderWidth,renderHeight);
        _visualizationTimer.Start();
    }

    private void FlushVisualizationEvents(bool drainAll)
    {
        if(_progressiveBitmap is not{}bitmap)return;
        int processed=0;bool changed=false;
        while((drainAll||processed<512)&&_visualizationEvents.TryDequeue(out DynamicTileRenderEvent visualEvent))
        {
            if(visualEvent.IsStart)RenderOverlay.StartTile(visualEvent.Tile);
            else if(visualEvent.Pixels is not null&&ProgressiveRenderBitmap.WriteTile(bitmap,visualEvent.Tile,visualEvent.Pixels))RenderOverlay.CompleteTile(visualEvent.Tile);
            processed++;changed=true;
        }
        if(changed)RenderOverlay.Refresh();
    }

    private void EndVisualization()
    {
        _visualizationTimer.Stop();
        _progressiveBitmap=null;
        while(_visualizationEvents.TryDequeue(out _)){}
        RenderOverlay.EndSession();
    }

    private void CommitAndBakePreview()
    {
        if(_kind!=DynamicSystemKind.Lyapunov||_progressiveBitmap is null)
        {
            _cts?.Cancel();
            EndVisualization();
            CurrentImage.Source=null;
            return;
        }

        _cts?.Cancel();
        FlushVisualizationEvents(true);
        RenderSurfaceMetrics surface=RenderSurfaceMetrics.Measure(ImageLayer);
        try
        {
            var baked=new RenderTargetBitmap(surface.PixelWidth,surface.PixelHeight,
                surface.Dpi.PixelsPerInchX,surface.Dpi.PixelsPerInchY,PixelFormats.Pbgra32);
            baked.Render(ImageLayer);
            baked.Freeze();
            StableImage.Source=baked;
            RememberRenderedViewport(_state);
            UpdatePreviewTransform();
        }
        catch(InvalidOperationException)
        {
            // Layout can briefly be unavailable during minimization or a resize transition.
        }
        finally
        {
            CurrentImage.Source=null;
            EndVisualization();
        }
    }

    private void RememberRenderedViewport(DynamicSystemState state)
    {
        _renderedCenterX=state.CenterX;_renderedCenterY=state.CenterY;_renderedZoom=state.Zoom;
        _renderedAMin=state.AMin;_renderedAMax=state.AMax;_renderedBMin=state.BMin;_renderedBMax=state.BMax;
        _hasRenderedFrame=true;
    }

    private void UpdatePreviewTransform()
    {
        if(!_hasRenderedFrame||CanvasSurface.ActualWidth<=0||CanvasSurface.ActualHeight<=0)return;
        double width=CanvasSurface.ActualWidth,height=CanvasSurface.ActualHeight;
        if(_kind==DynamicSystemKind.Lyapunov)
        {
            double currentWidth=_state.AMax-_state.AMin,currentHeight=_state.BMax-_state.BMin;
            double renderedWidth=_renderedAMax-_renderedAMin,renderedHeight=_renderedBMax-_renderedBMin;
            if(currentWidth<=0||currentHeight<=0||renderedWidth<=0||renderedHeight<=0)return;
            double currentCenterX=(_state.AMin+_state.AMax)/2,currentCenterY=(_state.BMin+_state.BMax)/2;
            double renderedCenterX=(_renderedAMin+_renderedAMax)/2,renderedCenterY=(_renderedBMin+_renderedBMax)/2;
            _previewScale.ScaleX=renderedWidth/currentWidth;_previewScale.ScaleY=renderedHeight/currentHeight;
            _previewTranslation.X=(renderedCenterX-currentCenterX)/currentWidth*width;
            _previewTranslation.Y=(currentCenterY-renderedCenterY)/currentHeight*height;
            return;
        }
        if(_state.Zoom<=0||_renderedZoom<=0)return;
        double currentSpan=BaseSpan(_state)/_state.Zoom;
        _previewScale.ScaleX=_previewScale.ScaleY=_state.Zoom/_renderedZoom;
        _previewTranslation.X=(_renderedCenterX-_state.CenterX)/currentSpan*width;
        _previewTranslation.Y=(_state.CenterY-_renderedCenterY)/currentSpan*height;
    }
    private void Toggle_OnClick(object sender,RoutedEventArgs e)=>FractalControlPanel.Toggle(ref _controls,ControlsColumn,ControlsHost,ToggleButton,250,Schedule);
    private void Window_OnKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.F11||e.Key==Key.Escape&&_fullscreen){if(!_fullscreen){_oldStyle=WindowStyle;_oldState=WindowState;WindowStyle=WindowStyle.None;WindowState=WindowState.Maximized;}else{WindowStyle=_oldStyle;WindowState=_oldState;}_fullscreen=!_fullscreen;}}
    private void Window_OnClosing(object? sender,System.ComponentModel.CancelEventArgs e){_timer.Stop();EndVisualization();_cts?.Cancel();_cts?.Dispose();}

    private static string DisplayName(DynamicSystemKind k)=>k switch{DynamicSystemKind.Lyapunov=>"Экспонента Ляпунова",DynamicSystemKind.Lorenz=>"Аттрактор Лоренца",DynamicSystemKind.Rossler=>"Аттрактор Рёсслера",DynamicSystemKind.LogisticMap=>"Логистическое отображение",DynamicSystemKind.Bifurcation=>"Диаграмма бифуркации",DynamicSystemKind.Henon=>"Карта Хенона",DynamicSystemKind.Ikeda=>"Отображение Икэды",DynamicSystemKind.Attractors2D=>"Странные аттракторы",_=>"Динамическая система"};
    private static string Details(DynamicSystemState s)=>s.Kind switch{DynamicSystemKind.Lyapunov=>$"{s.Pattern} · {s.Iterations} итераций · {s.PaletteName}",DynamicSystemKind.Attractors2D=>$"{Attractor2DDisplayName(Attractor2DRenderer.ParseKind(s.Attractor2DMode))} · {s.Iterations:N0} точек · масштаб {s.Zoom:G5}",_=>$"Масштаб {s.Zoom:G5} · {Math.Max(s.Iterations,s.Steps):N0} итераций"};
    private static string Attractor2DDisplayName(Attractor2DKind kind)=>kind switch{Attractor2DKind.Clifford=>"Клиффорд",Attractor2DKind.PeterDeJong=>"Питер де Йонг",Attractor2DKind.Tinkerbell=>"Tinkerbell",_=>"Gumowski–Mira"};
    private static double BaseSpan(DynamicSystemState s)=>s.Kind switch{DynamicSystemKind.Lorenz or DynamicSystemKind.Rossler=>80,DynamicSystemKind.Henon=>6,DynamicSystemKind.Ikeda=>Math.Max(.0001,s.RangeXMax-s.RangeXMin),DynamicSystemKind.Attractors2D=>Attractor2DRenderer.GetBaseSpan(Attractor2DRenderer.ParseKind(s.Attractor2DMode)),_=>1};
    private static string Format(object? value)=>value switch{double d=>d.ToString("G15",CultureInfo.InvariantCulture),float f=>f.ToString("G9",CultureInfo.InvariantCulture),null=>string.Empty,_=>Convert.ToString(value,CultureInfo.InvariantCulture)??string.Empty};
    private static bool TryDouble(string text,out double value)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out value);
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){DependencyObject child=VisualTreeHelper.GetChild(root,i);if(child is T match)yield return match;foreach(T nested in FindVisualChildren<T>(child))yield return nested;}}
    private sealed record ChoiceOption(string Display,string Value)
    {
        public override string ToString()=>Display;
    }
    private readonly record struct DynamicTileRenderEvent(bool IsStart,MandelbrotRenderTile Tile,byte[]? Pixels);
}
