using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Controls;

public partial class HopfEditor : UserControl
{
    private bool _loading = true;
    private HopfSettings _settings = new();
    private readonly Stopwatch _animationClock = new();
    public bool IsPlaying { get; private set; }
    public event EventHandler? SettingsChanged;
    public HopfEditor()
    {
        InitializeComponent();
        AnimationBox.ItemsSource = Enum.GetValues<HopfRotationPlane>();
        BaseSphere.PointPicked += PickPoint;
        Unloaded += (_,_) => Stop();
        Load(new());
    }

    public HopfSettings Capture()
    {
        var result = _settings with
        {
            Family = (HopfFamily)Math.Max(0,FamilyBox.SelectedIndex),
            Fibers = (int)CountSlider.Value, Layers = (int)LayersSlider.Value,
            Latitude = LatitudeSlider.Value, Spread = SpreadSlider.Value, Phase = PhaseSlider.Value,
            Thickness = ThicknessSlider.Value, ClipRadius = ClipSlider.Value,
            XY = XYSlider.Value, XZ = XZSlider.Value, XW = XWSlider.Value,
            YZ = YZSlider.Value, YW = YWSlider.Value, ZW = ZWSlider.Value,
            OnlySelected = OnlyBox.IsChecked == true,
            AnimationPlane = (HopfRotationPlane)Math.Max(0,AnimationBox.SelectedIndex), AnimationSpeed = SpeedSlider.Value,
            Points = _settings.Points.ToList()
        };
        result.Validate(); return result;
    }

    public void Load(HopfSettings settings)
    {
        settings.Validate(); Stop(); _loading = true; _settings = settings.Copy();
        FamilyBox.SelectedIndex = (int)settings.Family;
        CountSlider.Value = settings.Fibers; LayersSlider.Value = settings.Layers;
        LatitudeSlider.Value = settings.Latitude; SpreadSlider.Value = settings.Spread; PhaseSlider.Value = settings.Phase;
        ThicknessSlider.Value = settings.Thickness; ClipSlider.Value = settings.ClipRadius;
        XYSlider.Value = settings.XY; XZSlider.Value = settings.XZ; XWSlider.Value = settings.XW;
        YZSlider.Value = settings.YZ; YWSlider.Value = settings.YW; ZWSlider.Value = settings.ZW;
        OnlyBox.IsChecked = settings.OnlySelected;
        AnimationBox.SelectedIndex = (int)settings.AnimationPlane; SpeedSlider.Value = settings.AnimationSpeed;
        _loading = false; RefreshSphere();
    }

    private void RefreshSphere()
    {
        var s = Capture(); var points = s.BasePoints();
        BaseSphere.SetPoints(points,s.SelectedFiber);
        bool selected=s.SelectedFiber>=0;
        OnlyBox.IsEnabled=RemoveButton.IsEnabled=selected;
        SelectionText.Text = selected
            ? $"Кольцо {s.SelectedFiber+1} / {points.Count} · долгота {points[s.SelectedFiber].Longitude:F1}°, широта {points[s.SelectedFiber].Latitude:F1}°"
            : $"{points.Count} колец · выберите точку на сфере";
        FamilyParameters.Visibility = s.Family == HopfFamily.Custom ? Visibility.Collapsed : Visibility.Visible;
        LayersPanel.Visibility = s.Family is HopfFamily.Latitudes or HopfFamily.LinkedTori ? Visibility.Visible : Visibility.Collapsed;
        LatitudePanel.Visibility = s.Family == HopfFamily.Sphere ? Visibility.Collapsed : Visibility.Visible;
        SpreadPanel.Visibility = s.Family == HopfFamily.Latitudes ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        RefreshSphere(); ErrorText.Text=""; SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void FamilyChanged(object sender, SelectionChangedEventArgs e) => FamilyParameterChanged(sender,e);
    private void FamilyParameterChanged(object sender, RoutedEventArgs e)
    {
        if(_loading) return;
        _loading=true; _settings=_settings with { SelectedFiber=-1 }; OnlyBox.IsChecked=false; _loading=false;
        Changed(sender,e);
    }
    private void HemisphereChanged(object sender,RoutedEventArgs e)
    {
        if(BaseSphere is null) return;
        BaseSphere.Back=BackBox.IsChecked==true; BaseSphere.InvalidateVisual();
    }

    public void PickPoint(int index, HopfPoint? added)
    {
        var s=Capture();
        if(added is not null)
        {
            var points=s.BasePoints().ToList();
            if(points.Count>=256) { ErrorText.Text="На сфере уже 256 точек. Удалите одну перед добавлением."; return; }
            points.Add(added);
            s=s with { Family=HopfFamily.Custom, Points=points, SelectedFiber=points.Count-1 };
        }
        else
        {
            if(index<0 || index>=s.BasePoints().Count) return;
            s=s with { SelectedFiber=index };
        }
        bool playing=IsPlaying;
        Load(s); if(playing) Start();
        SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void RemoveClicked(object sender,RoutedEventArgs e)
    {
        var s=Capture(); if(s.SelectedFiber<0) return;
        var points=s.BasePoints().ToList(); points.RemoveAt(s.SelectedFiber);
        Load(s with { Family=HopfFamily.Custom, Points=points, SelectedFiber=-1, OnlySelected=false });
        SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void ClearSelectionClicked(object sender,RoutedEventArgs e)
    { Load(Capture() with { SelectedFiber=-1,OnlySelected=false }); SettingsChanged?.Invoke(this,EventArgs.Empty); }
    private void ResetClicked(object sender,RoutedEventArgs e)
    { Load(Capture() with { XY=0,XZ=0,XW=0,YZ=0,YW=0,ZW=0 }); SettingsChanged?.Invoke(this,EventArgs.Empty); }
    private void PlayClicked(object sender,RoutedEventArgs e)
    { if(IsPlaying) Stop(); else Start(); SettingsChanged?.Invoke(this,EventArgs.Empty); }
    public void Start() { IsPlaying=true; _animationClock.Restart(); PlayButton.Content="Ⅱ Пауза"; }
    public void Stop() { IsPlaying=false; _animationClock.Stop(); if(PlayButton is not null) PlayButton.Content="▶ Вращать в 4D"; }

    // Advance only after a complete displayed frame. No timer queues work faster than the GPU can finish.
    public void OnFrameDisplayed()
    {
        if(!IsPlaying || Visibility!=Visibility.Visible) return;
        double seconds=Math.Clamp(_animationClock.Elapsed.TotalSeconds, .001,.2);
        _animationClock.Restart();
        Slider slider=(HopfRotationPlane)AnimationBox.SelectedIndex switch
        { HopfRotationPlane.XY=>XYSlider, HopfRotationPlane.XZ=>XZSlider, HopfRotationPlane.YZ=>YZSlider,
          HopfRotationPlane.YW=>YWSlider, HopfRotationPlane.ZW=>ZWSlider, _=>XWSlider };
        slider.Value=HopfSettings.Wrap(slider.Value+SpeedSlider.Value*seconds);
        // With zero speed the slider doesn't raise ValueChanged, so stop instead of wasting frames.
        if(Math.Abs(SpeedSlider.Value)<1e-9) Stop();
    }
    public void ShowError(string message) { Stop(); ErrorText.Text=message; }

    public void SetColoring(Fractal3DState state)
    {
        var palette=state.ResolvePalette().Clone();
        BaseSphere.ColorAt = index =>
        {
            if(index>=BaseSphere.Points.Count) return state.SurfaceColor;
            HopfPoint point=BaseSphere.Points[index];
            double value=state.ColoringMode switch
            {
                Fractal3DColoringMode.OrbitTrap=>(point.Longitude+180)/360,
                Fractal3DColoringMode.CrossTrap=>(point.Latitude+90)/180,
                Fractal3DColoringMode.IterationIndex=>index/(double)Math.Max(BaseSphere.Points.Count-1,1),
                _=>double.NaN
            };
            if(double.IsNaN(value)) return state.SurfaceColor;
            value=value*state.ColorScale+state.ColorOffset;
            double t=state.ColorRepeat switch
            {
                Fractal3DColorRepeat.Cycle=>value-Math.Floor(value),
                Fractal3DColorRepeat.Mirror=>1-Math.Abs(2*(value-Math.Floor(value))-1),
                _=>Math.Clamp(value,0,1)
            };
            if(state.ColorRepeat==Fractal3DColorRepeat.Mirror) t=t*t*(3-2*t);
            t=Math.Pow(t,Math.Clamp(palette.Gamma,.05,8));
            int count=Math.Min(palette.Colors.Count,Fractal3DPalette.MaxColors);
            if(count==0) return Colors.White;
            if(!palette.IsGradient) return palette.Colors[Math.Min((int)(t*count),count-1)];
            bool cycle=state.ColorRepeat==Fractal3DColorRepeat.Cycle;
            double span=t*(cycle?count:count-1);
            int lo=Math.Min((int)span,count-1),hi=cycle?(lo+1)%count:Math.Min(lo+1,count-1);
            double f=span-lo; Color a=palette.Colors[lo],b=palette.Colors[hi];
            return Color.FromScRgb(1,Mix(a.R,b.R),Mix(a.G,b.G),Mix(a.B,b.B));
            float Mix(byte first,byte last) => (float)(Linear(first)*(1-f)+Linear(last)*f);
        };
        BaseSphere.InvalidateVisual();
        static double Linear(byte channel)
        { double v=channel/255.0; return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4); }
    }
}
