using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class Polytope4DEditor : UserControl
{
    private bool _loading = true, _waiting;
    private Polytope4DSettings? _displayed;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly Stopwatch _clock = new();
    private Slider[] Angles => [XYSlider,XZSlider,XWSlider,YZSlider,YWSlider,ZWSlider];
    private TextBox[] AngleBoxes => [XYBox,XZBox,XWBox,YZBox,YWBox,ZWBox];
    public event EventHandler? SettingsChanged;
    public bool IsPlaying => _timer.IsEnabled;

    public Polytope4DEditor()
    {
        InitializeComponent();
        BodyBox.ItemsSource = Enum.GetValues<Polytope4DKind>().Select(Polytope4DSettings.Name).ToArray();
        ViewBox.ItemsSource = Enum.GetValues<Polytope4DView>().Select(Polytope4DSettings.ViewName).ToArray();
        AnimationBox.ItemsSource = new[] { "Вращение XY", "Вращение XZ", "Вращение XW", "Вращение YZ", "Вращение YW", "Вращение ZW", "Проход сечения" };
        Load(new());
        NumericInput.Real(this,EyeBox,"Расстояние 4D-наблюдателя",1.25,6,active:()=>PerspectiveBox.IsChecked==true && ViewBox.SelectedIndex!=1);
        NumericInput.Real(this,OffsetBox,"Положение сечения",-1.05,1.05,active:()=>ViewBox.SelectedIndex!=0);
        NumericInput.Real(this,ThicknessBox,"Толщина каркаса",.003,.04);
        foreach (var box in AngleBoxes) NumericInput.Real(this,box,"Угол 4D",-180,180);
        NumericInput.Real(this,SpeedBox,"Скорость анимации",-60,60);
        _timer.Tick += Tick;
        AddHandler(Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_,e) =>
        {
            if(IsPlaying && e.NewFocus is TextBox) Stop();
        }));
        Unloaded += (_,_) => Stop();
    }

    public Polytope4DSettings Capture()
    {
        NumericInput.Prepare(this);
        double[] a = AngleBoxes.Select(b=>ReadDouble(b,"Угол 4D",-180,180)).ToArray();
        var s = new Polytope4DSettings
        {
            Body=(Polytope4DKind)Math.Max(0,BodyBox.SelectedIndex), View=(Polytope4DView)Math.Max(0,ViewBox.SelectedIndex),
            Perspective=PerspectiveBox.IsChecked==true, EyeDistance=ViewBox.SelectedIndex!=1 && PerspectiveBox.IsChecked==true ? ReadDouble(EyeBox,"Расстояние наблюдателя",1.25,6) : EyeSlider.Value,
            Offset=ViewBox.SelectedIndex!=0 ? ReadDouble(OffsetBox,"Сечение",-1.05,1.05) : OffsetSlider.Value,
            Faces=FacesBox.IsChecked==true, Edges=EdgesBox.IsChecked==true, Vertices=VerticesBox.IsChecked==true,
            Thickness=ReadDouble(ThicknessBox,"Толщина",.003,.04),
            XY=a[0],XZ=a[1],XW=a[2],YZ=a[3],YW=a[4],ZW=a[5],
            Animation=(Polytope4DAnimation)Math.Max(0,AnimationBox.SelectedIndex), Speed=ReadDouble(SpeedBox,"Скорость",-60,60)
        };
        s.Validate(); return s;
    }

    public void Load(Polytope4DSettings s)
    {
        s.Validate(); _timer.Stop(); _waiting=false; _displayed=null; _loading=true;
        try
        {
            BodyBox.SelectedIndex=(int)s.Body; ViewBox.SelectedIndex=(int)s.View;
            PerspectiveBox.IsChecked=s.Perspective;
            FacesBox.IsChecked=s.Faces; EdgesBox.IsChecked=s.Edges; VerticesBox.IsChecked=s.Vertices;
            Set(EyeSlider,EyeBox,s.EyeDistance); Set(OffsetSlider,OffsetBox,s.Offset); Set(ThicknessSlider,ThicknessBox,s.Thickness);
            double[] a=[s.XY,s.XZ,s.XW,s.YZ,s.YW,s.ZW];
            for(int i=0;i<6;i++) Set(Angles[i],AngleBoxes[i],a[i]);
            AnimationBox.SelectedIndex=(int)s.Animation; Set(SpeedSlider,SpeedBox,s.Speed);
            PlayButton.Content="▶ Запустить"; ErrorText.Text=""; SectionInfo.Text="";
        }
        finally { _loading=false; }
        Refresh();
    }
    private static void Set(Slider slider,TextBox box,double value)
    {
        slider.Value=value;
        box.SetCurrentValue(TextBox.TextProperty,value.ToString("R",CultureInfo.InvariantCulture));
    }
    private void Refresh()
    {
        var body=(Polytope4DKind)Math.Max(0,BodyBox.SelectedIndex);
        var counts = body switch
        {
            Polytope4DKind.Simplex => "5 вершин · 10 рёбер · 10 граней · 5 тетраэдров",
            Polytope4DKind.Tesseract => "16 вершин · 32 ребра · 24 грани · 8 кубов",
            Polytope4DKind.Orthoplex => "8 вершин · 24 ребра · 32 грани · 16 тетраэдров",
            Polytope4DKind.Cell24 => "24 вершины · 96 рёбер · 96 граней · 24 октаэдра",
            Polytope4DKind.Cell120 => "600 вершин · 1200 рёбер · 720 граней · 120 додекаэдров",
            _ => "120 вершин · 720 рёбер · 1200 граней · 600 тетраэдров"
        };
        BodyInfo.Text=counts;
        ProjectionPanel.Visibility=ViewBox.SelectedIndex==1 ? Visibility.Collapsed : Visibility.Visible;
        SectionPanel.Visibility=ViewBox.SelectedIndex==0 ? Visibility.Collapsed : Visibility.Visible;
        EyeBox.IsEnabled=EyeSlider.IsEnabled=PerspectiveBox.IsChecked==true;
    }
    private void Changed(object sender,RoutedEventArgs e)
    {
        if(_loading) return;
        if(FacesBox.IsChecked!=true && EdgesBox.IsChecked!=true && VerticesBox.IsChecked!=true)
            EdgesBox.IsChecked=true;
        Refresh(); ErrorText.Text="";
        SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void CenterClicked(object sender,RoutedEventArgs e)
    {
        Stop(); NumericInput.CommitAll(this); Set(OffsetSlider,OffsetBox,0); SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void ResetClicked(object sender,RoutedEventArgs e)
    {
        Stop(); NumericInput.CommitAll(this); var s=Capture();
        Load(s with { XY=0,XZ=0,XW=0,YZ=0,YW=0,ZW=0 }); SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void DualClicked(object sender,RoutedEventArgs e)
    {
        Stop(); NumericInput.CommitAll(this); var s=Capture();
        var dual=s.Body switch
        {
            Polytope4DKind.Tesseract=>Polytope4DKind.Orthoplex,
            Polytope4DKind.Orthoplex=>Polytope4DKind.Tesseract,
            Polytope4DKind.Cell120=>Polytope4DKind.Cell600,
            Polytope4DKind.Cell600=>Polytope4DKind.Cell120,
            _=>s.Body
        };
        Load(s with { Body=dual });
        ErrorText.Text=s.Body==dual ? "Это самодвойственное тело. Самодвойственность означает совпадение типа, а не ориентации двойственного тела." : "Показан двойственный тип с теми же углами и радиусом 1.";
        SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void PlayClicked(object sender,RoutedEventArgs e)
    {
        if(IsPlaying) { Stop(); SettingsChanged?.Invoke(this,EventArgs.Empty); return; }
        NumericInput.CommitAll(this);
        var s=Capture();
        if(Math.Abs(s.Speed)<1e-9) { ErrorText.Text="Задайте ненулевую скорость."; return; }
        if(s.Animation==Polytope4DAnimation.Section && s.View==Polytope4DView.Projection)
            Load(s with { View=Polytope4DView.Section });
        _waiting=false; _clock.Restart(); _timer.Start(); PlayButton.Content="⏸ Пауза";
        SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
    public void Stop()
    {
        bool playing=IsPlaying; _timer.Stop(); _waiting=false; PlayButton.Content="▶ Запустить";
        // Save/export/pause keep exactly the orientation and section that reached the screen.
        if(playing && _displayed is { } s)
        {
            _loading=true;
            double[] a=[s.XY,s.XZ,s.XW,s.YZ,s.YW,s.ZW];
            for(int i=0;i<6;i++) Set(Angles[i],AngleBoxes[i],a[i]);
            Set(OffsetSlider,OffsetBox,s.Offset); _loading=false;
        }
    }
    private void Tick(object? sender,EventArgs e)
    {
        if(_waiting || !IsVisible) return;
        double dt=_clock.Elapsed.TotalSeconds; _clock.Restart();
        AdvanceAnimation(dt);
    }
    internal void AdvanceAnimation(double seconds)
    {
        if(!IsPlaying || _waiting) return;
        Polytope4DSettings s;
        try { s=Capture(); }
        catch(Exception exception) when(exception is ArgumentException or InvalidOperationException)
        { _timer.Stop(); _waiting=false; PlayButton.Content="▶ Запустить"; return; }
        double dt=Math.Clamp(seconds,0,.15);
        if(Math.Abs(s.Speed)<1e-9) { Stop(); return; }
        _waiting=true;
        if(s.Animation==Polytope4DAnimation.Section)
        {
            double offset=s.Offset+s.Speed*.01*dt;
            if(offset>1.05) offset=-1.05; else if(offset< -1.05) offset=1.05;
            Set(OffsetSlider,OffsetBox,offset);
        }
        else
        {
            int i=(int)s.Animation;
            Set(Angles[i],AngleBoxes[i],HopfSettings.Wrap(Angles[i].Value+s.Speed*dt));
        }
    }
    internal void OnFrameDisplayed(Polytope4DSettings settings,Polytope4DGeometry? geometry)
    {
        _displayed=settings; _waiting=false;
        if(geometry is null) return;
        SectionInfo.Text=settings.View==Polytope4DView.Projection
            ? "Каркасная проекция. Цвет по W помогает различать глубину четвёртого измерения."
            : geometry.SectionVertices.Length==0 ? $"Пустое сечение. Тело занимает w от {geometry.MinW:F3} до {geometry.MaxW:F3}."
            : $"Сечение: {geometry.SectionVertices.Length} вершин · {geometry.SectionEdges.Length} рёбер · {geometry.SectionFaces.Length} граней.\nПлощадь: {geometry.SurfaceArea:G5} · объём: {geometry.Volume:G5}.\nДиапазон тела по W: {geometry.MinW:F3} … {geometry.MaxW:F3}.";
    }
    internal bool IsCurrentFrame(Polytope4DSettings settings)
    {
        try
        {
            return Capture() with { Animation=default,Speed=0 } == settings with { Animation=default,Speed=0 };
        }
        catch(Exception exception) when(exception is ArgumentException or InvalidOperationException) { return false; }
    }
    public void ShowError(string message) { Stop(); ErrorText.Text=message; }
}
