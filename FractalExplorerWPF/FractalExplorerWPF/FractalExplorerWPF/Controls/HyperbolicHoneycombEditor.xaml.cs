using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

public partial class HyperbolicHoneycombEditor : UserControl
{
    private bool _loading = true;
    private (double X,double Y,double Z) _observer;
    public event EventHandler? SettingsChanged;

    public HyperbolicHoneycombEditor() { InitializeComponent(); _loading=false; UpdateControls(); }

    public HyperbolicHoneycombSettings Capture()
    {
        var s=new HyperbolicHoneycombSettings {
            Tessellation=(HyperbolicHoneycombKind)Math.Max(0,KindBox.SelectedIndex), Depth=(int)DepthSlider.Value,
            ShowEdges=EdgesCheck.IsChecked==true, ShowFaces=FacesCheck.IsChecked==true,
            EdgeRadius=Read(EdgeBox),FaceThickness=Read(ThicknessBox),FaceOpacity=Read(OpacityBox),
            CutAxis=Math.Max(0,CutBox.SelectedIndex),CutPosition=Read(CutPositionBox),
            ObserverX=_observer.X,ObserverY=_observer.Y,ObserverZ=_observer.Z };
        s.Validate(); return s;
    }

    public void Load(HyperbolicHoneycombSettings s)
    {
        s.Validate(); _loading=true;
        try
        {
            KindBox.SelectedIndex=(int)s.Tessellation; DepthSlider.Value=s.Depth;
            EdgesCheck.IsChecked=s.ShowEdges; FacesCheck.IsChecked=s.ShowFaces; CutBox.SelectedIndex=s.CutAxis;
            Set(EdgeSlider,EdgeBox,s.EdgeRadius); Set(ThicknessSlider,ThicknessBox,s.FaceThickness);
            Set(OpacitySlider,OpacityBox,s.FaceOpacity); Set(CutSlider,CutPositionBox,s.CutPosition);
            _observer=(s.ObserverX,s.ObserverY,s.ObserverZ); WriteObserver(); UpdateControls();
        }
        finally { _loading=false; }
    }

    private static void Set(Slider slider,TextBox box,double value)
    {
        slider.Value=value;
        box.SetCurrentValue(TextBox.TextProperty,value.ToString("R",CultureInfo.InvariantCulture));
    }
    private static double Read(TextBox box)
    {
        if(!double.TryParse(box.Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double value) || !double.IsFinite(value))
            throw new ArgumentException("Введите конечное число в параметрах гиперболических сот.");
        return value;
    }
    private void UpdateControls()
    {
        FacesPanel.IsEnabled=FacesCheck.IsChecked==true;
        EdgeSlider.IsEnabled=EdgeBox.IsEnabled=EdgesCheck.IsChecked==true;
        CutSlider.IsEnabled=CutPositionBox.IsEnabled=CutBox.SelectedIndex>0;
        var geometry=new HyperbolicHoneycombGeometry((HyperbolicHoneycombKind)Math.Max(0,KindBox.SelectedIndex));
        int depth=(int)DepthSlider.Value;
        DepthText.Text=$"{depth} · радиус в H³: {geometry.VertexDistance+.12+(depth-1)*.45:F2}; в шаре: {geometry.ClipRadius(depth):F3}. Радиальная область вокруг текущего наблюдателя.";
    }
    private void Changed(object sender,EventArgs e)
    {
        if(_loading) return;
        UpdateControls(); SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void WriteObserver()
    {
        ObserverXBox.Text=_observer.X.ToString("R",CultureInfo.InvariantCulture);
        ObserverYBox.Text=_observer.Y.ToString("R",CultureInfo.InvariantCulture);
        ObserverZBox.Text=_observer.Z.ToString("R",CultureInfo.InvariantCulture);
        PositionStatus.Text="";
    }
    private void ApplyPosition(object sender,RoutedEventArgs e)
    {
        try
        {
            var current=Capture() with { ObserverX=Read(ObserverXBox),ObserverY=Read(ObserverYBox),ObserverZ=Read(ObserverZBox) };
            current.Validate(); _observer=(current.ObserverX,current.ObserverY,current.ObserverZ);
            WriteObserver(); SettingsChanged?.Invoke(this,EventArgs.Empty);
        }
        catch(ArgumentException error) { PositionStatus.Text=error.Message; }
    }
    private void ResetPosition(object sender,RoutedEventArgs e)
    {
        _observer=(0,0,0); WriteObserver(); SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
    private void MoveClick(object sender,RoutedEventArgs e)
    {
        string tag=(string)((Button)sender).Tag;
        Vector3 direction=tag[0] switch { 'X'=>Vector3.UnitX,'Y'=>Vector3.UnitY,_=>Vector3.UnitZ };
        if(tag[1]=='-') direction=-direction;
        Vector3 moved=HyperbolicHoneycombGeometry.Move(new((float)_observer.X,(float)_observer.Y,(float)_observer.Z),direction,.3);
        _observer=(moved.X,moved.Y,moved.Z); WriteObserver(); SettingsChanged?.Invoke(this,EventArgs.Empty);
    }
}
