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
            Time=(shown,s)=>$"Шаг {shown?.Step ?? s.Field?.Step ?? 0:N0} · t = {(shown?.Step ?? s.Field?.Step ?? 0)*s.TimeStep:F2} · ψ̄ = {s.Mean:G4}",
            ProgressMessage=(done,steps)=>$"Подготовка кристалла · {(int)(100L*done/Math.Max(1,steps))}%"
        });
        _loading=false;
    }
    internal Fractal3DLiveSimulation<PhaseFieldCrystal3DGpuSimulation,PhaseFieldCrystal3DSettings,PhaseFieldCrystal3DField,PhaseFieldCrystal3DVolume> Simulation { get; }
    public FrameworkElement View=>this;
    public int StepsPerFrame=>(int)PfcSpeedSlider.Value;
    public bool IsSearching=>false;
    public void CancelSearch() { }
    public void Show(PhaseFieldCrystal3DSettings s)
    {
        _loading=true;
        try
        {
            PfcSizeBox.SelectedIndex=s.Size switch { 32=>0,128=>2,_=>1 };
            PfcEpsilonBox.Text=Format(s.Undercooling); PfcMeanBox.Text=Format(s.Mean); PfcQBox.Text=Format(s.WaveNumber);
            PfcSpacingBox.Text=Format(s.Spacing); PfcMobilityBox.Text=Format(s.Mobility); PfcDtBox.Text=Format(s.TimeStep);
            PfcSeedBox.SelectedIndex=(int)s.SeedShape; PfcNoiseBox.Text=Format(s.Noise);
            PfcSeedNumberBox.Text=s.Seed.ToString(); PfcWarmupBox.Text=s.WarmupSteps.ToString();
            PfcSpeedSlider.Value=s.StepsPerFrame; PfcViewBox.SelectedIndex=(int)s.View;
            PfcRangeSlider.Value=s.DisplayRange; PfcThresholdSlider.Value=s.Threshold;
            PfcCutBox.SelectedIndex=s.CutAxis; PfcCutSlider.Value=s.CutPosition;
        }
        finally { _loading=false; }
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
    }
    private void PfcRestart_OnClick(object sender,RoutedEventArgs e)
    {
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
