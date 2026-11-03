using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class PhaseFieldCrystal3DEditor : UserControl, IFractal3DSimulationEditor<PhaseFieldCrystal3DSettings>
{
    private readonly IFractal3DSimulationHost _host;
    private readonly List<PhaseFieldCrystal3DSettings> _atlasHistory = [];
    private bool _loading = true;
    internal PhaseFieldCrystal3DEditor(IFractal3DSimulationHost host)
    {
        InitializeComponent(); _host=host;
        Simulation=new(host,this,new()
        {
            Title="фазового поля кристалла 3D", Create=(device,s)=>new PhaseFieldCrystal3DGpuSimulation(device,s),
            Get=state=>state.PhaseFieldCrystal, Set=(state,s)=>state.PhaseFieldCrystal=s,
            Live=s=>s.Live, Field=s=>s.Field, With=(s,live,field)=>s with { Live=live,Field=field },
            InitialSteps=s=>s.Field is null ? s.WarmupSteps : 0,
            PreparationMessage=_=>"Подготовка кристаллической плотности…",
            Time=(shown,s)=>$"Шаг {shown?.Step ?? s.Field?.Step ?? 0:N0} · Δt = {s.TimeStep:G6} · ψ̄ = {s.Mean:G4}",
            ProgressMessage=(done,steps)=>$"Подготовка кристалла · {(int)(100L*done/Math.Max(1,steps))}%"
        });
        _loading=false;
    }
    internal Fractal3DLiveSimulation<PhaseFieldCrystal3DGpuSimulation,PhaseFieldCrystal3DSettings,PhaseFieldCrystal3DField,PhaseFieldCrystal3DVolume> Simulation { get; }
    public FrameworkElement View=>this;
    public int StepsPerFrame=>(int)PfcSpeedSlider.Value;
    public bool IsSearching=>false;
    public void CancelSearch() { }

    public event EventHandler? AtlasRequested;
    private void Atlas_OnClick(object sender, RoutedEventArgs e) => AtlasRequested?.Invoke(this, EventArgs.Empty);

    internal void ApplyAtlasResult(PhaseFieldCrystal3DSettings result, PhaseFieldCrystal3DSettings previous)
    {
        if (result.Field is null || result.Live is not null) throw new ArgumentException("Нужно точное поле выбранного прогона PFC со средним.");
        result.Validate(); Simulation.Restart(result);
        _atlasHistory.Add(previous with { Live = null });
        if (_atlasHistory.Count > 4) _atlasHistory.RemoveAt(0);
        Refresh(); _host.ShowStatus($"Вариант атласа · шаг {result.Field.Step:N0}, ψ̄ = {result.Field.Mean:G6}. Исходное поле доступно через «Вернуть предыдущий».");
    }

    private void AtlasUndo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_atlasHistory.Count == 0 || Simulation.Busy || Simulation.Preparing || _host.IsSuspended || _host.IsClosing) return;
        var previous = _atlasHistory[^1];
        Simulation.Restart(previous); _atlasHistory.RemoveAt(_atlasHistory.Count - 1);
        Refresh(); _host.ShowStatus("Восстановлено предыдущее фазовое поле кристалла 3D.");
    }

    public void Show(PhaseFieldCrystal3DSettings s)
    {
        _loading=true;
        try
        {
            PfcSizeBox.SelectedIndex=s.Size switch { 32=>0,128=>2,_=>1 };
            PfcEpsilonBox.Text=s.Undercooling.ToString("R", CultureInfo.InvariantCulture); PfcMeanBox.Text=s.Mean.ToString("R", CultureInfo.InvariantCulture); PfcQBox.Text=s.WaveNumber.ToString("R", CultureInfo.InvariantCulture);
            PfcSpacingBox.Text=s.Spacing.ToString("R", CultureInfo.InvariantCulture); PfcMobilityBox.Text=s.Mobility.ToString("R", CultureInfo.InvariantCulture); PfcDtBox.Text=s.TimeStep.ToString("R", CultureInfo.InvariantCulture);
            PfcSeedBox.SelectedIndex=(int)s.SeedShape; PfcNoiseBox.Text=s.Noise.ToString("R", CultureInfo.InvariantCulture);
            PfcSeedNumberBox.Text=s.Seed.ToString(); PfcWarmupBox.Text=s.WarmupSteps.ToString();
            PfcSpeedSlider.Value=s.StepsPerFrame; PfcViewBox.SelectedIndex=(int)s.View;
            PfcRangeSlider.Value=s.DisplayRange; PfcThresholdSlider.Value=s.Threshold;
            PfcCutBox.SelectedIndex=s.CutAxis; PfcCutSlider.Value=s.CutPosition;
        }
        finally { _loading=false; }
        InitializeNumericInput();
    }
    public PhaseFieldCrystal3DSettings CaptureView(PhaseFieldCrystal3DSettings applied)=>applied with
    {
        StepsPerFrame=StepsPerFrame, View=(PhaseFieldCrystal3DView)Math.Max(0,PfcViewBox.SelectedIndex),
        DisplayRange=PfcRangeSlider.Value, Threshold=PfcThresholdSlider.Value,
        CutAxis=Math.Max(0,PfcCutBox.SelectedIndex), CutPosition=PfcCutSlider.Value
    };
    public void Refresh()
    {
        PfcDeviceText.Text=Simulation.Device; PfcCutSlider.IsEnabled=PfcCutBox.SelectedIndex>0;
        PfcBrushButton.IsEnabled=Simulation.CanBrush;
        bool ready = Simulation.Shown is not null && !Simulation.ResetPending && !Simulation.Preparing && !_host.IsSuspended && !_host.IsClosing;
        AtlasButton.IsEnabled = ready;
        AtlasUndoButton.IsEnabled = ready && !Simulation.Busy && _atlasHistory.Count > 0;
    }
    private void PfcRestart_OnClick(object sender,RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        try
        {
            var s=CaptureView(new()) with
            {
                Size=PfcSizeBox.SelectedIndex switch { 0=>32,2=>128,_=>64 },
                Undercooling=ReadDouble(PfcEpsilonBox,"Переохлаждение ε",-.2,.6), Mean=ReadDouble(PfcMeanBox,"Средняя плотность",-.6,.6),
                WaveNumber=ReadDouble(PfcQBox,"Волновое число",.6,1.4), Spacing=ReadDouble(PfcSpacingBox,"Шаг сетки",.5,1.5),
                Mobility=ReadDouble(PfcMobilityBox,"Подвижность",.1,2), TimeStep=ReadDouble(PfcDtBox,"Шаг времени",.002,.03),
                SeedShape=(PhaseFieldCrystal3DSeed)Math.Max(0,PfcSeedBox.SelectedIndex), Noise=ReadDouble(PfcNoiseBox,"Шум",0,.2),
                Seed=ReadInt(PfcSeedNumberBox,"Начальное число",int.MinValue,int.MaxValue), WarmupSteps=ReadInt(PfcWarmupBox,"Подготовка",0,6000)
            };
            s.Validate(); Simulation.Restart(s);
        }
        catch(Exception exception) { _host.ShowStatus(exception.Message); }
    }
    private void PfcView_OnChanged(object sender,RoutedEventArgs e)
    {
        if(_loading) return;
        if(e.Source==PfcViewBox) PfcThresholdSlider.Value=PfcViewBox.SelectedIndex==1 ? .15 : PfcViewBox.SelectedIndex==2 ? .3 : .35;
        Simulation.NotifyChanged(); _host.ScheduleRender();
    }
    private void PfcBrush_OnClick(object sender,RoutedEventArgs e)=>Brush(.5,.5,.5);
    private void Brush(double x,double y,double z)
    {
        double radius=PfcBrushSlider.Value; var mode=(PhaseFieldCrystal3DBrush)Math.Max(0,PfcBrushBox.SelectedIndex);
        Simulation.Brush(simulation=>simulation.Inject(x,y,z,radius,mode));
    }
    public bool TryBrush(MouseButtonEventArgs e)
    {
        if(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return false;
        e.Handled=true;
        if(PfcCutBox.SelectedIndex==0) { _host.ShowStatus("Для кисти выберите срез X, Y или Z."); return true; }
        if(_host.TryPickCutPlane(e,PfcCutBox.SelectedIndex-1,PfcCutSlider.Value,out var point)) Brush(point.X,point.Y,point.Z);
        return true;
    }
}
