using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Microsoft.Win32;
using Point = System.Windows.Point;

namespace FractalExplorerWPF.Views;

public partial class FlameWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval=TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _memoryReliefTimer = new() { Interval=TimeSpan.FromSeconds(2) };
    private const long MemoryReliefThresholdBytes = 512L*1024*1024;
    private readonly FlameSaveStore _saves = new(); private readonly List<FlameTransform> _transforms = FlameState.CreateDefaults();
    private readonly TransformGroup _transform = new(); private readonly ScaleTransform _imageScale = new(1,1); private readonly TranslateTransform _translation = new();
    private readonly TransformGroup _coverageTransform = new(); private readonly ScaleTransform _coverageScale = new(1,1); private readonly TranslateTransform _coverageTranslation = new();
    private CancellationTokenSource? _cts; private bool _rendering,_panning,_controls=true,_hasFrame,_syncing; private Point _panStart;
    private double _centerX,_centerY,_worldScale=4,_renderedCenterX,_renderedCenterY,_renderedScale=4,_activeCenterX,_activeCenterY,_activeScale=4; private readonly FullscreenToggle _fullscreen = new();

    public FlameWindow()
    {
        InitializeComponent(); _transform.Children.Add(_imageScale); _transform.Children.Add(_translation); StableImage.RenderTransformOrigin=new Point(.5,.5); StableImage.RenderTransform=_transform;
        _coverageTransform.Children.Add(_coverageScale);_coverageTransform.Children.Add(_coverageTranslation);CoverageImage.RenderTransformOrigin=new Point(.5,.5);CoverageImage.RenderTransform=_coverageTransform;
        SamplesBox.Text="1000000";IterationsBox.Text="20";WarmupBox.Text="20";ScaleBox.Text="4";CenterXBox.Text="0";CenterYBox.Text="0";ExposureBox.Text="1.35";GammaBox.Text="2.2";
        ThreadsBox.Items.Add("Auto");for(int i=1;i<=Environment.ProcessorCount;i++)ThreadsBox.Items.Add(i);ThreadsBox.SelectedIndex=0;
        _timer.Tick+=(_,_)=>{_timer.Stop();_ = RenderAsync();};Loaded+=(_,_)=>Schedule();
        _memoryReliefTimer.Tick+=(_,_)=>{_memoryReliefTimer.Stop();_ = ReleaseMemoryIfIdleAsync();};
        InitializeNumericInput();
    }
    public FlameState CaptureState(string name)
    {
        NumericInput.Prepare(this);
        if(!int.TryParse(SamplesBox.Text,out int samples)||samples is<1000 or>20000000)throw new InvalidOperationException("Сэмплы должны быть от 1 000 до 20 000 000.");
        if(!int.TryParse(IterationsBox.Text,out int iterations)||iterations<1)throw new InvalidOperationException("Число итераций должно быть положительным.");
        if(!int.TryParse(WarmupBox.Text,out int warmup)||warmup<0)throw new InvalidOperationException("Прогрев не может быть отрицательным.");
        if(!Read(ScaleBox.Text,out double scale)||Math.Abs(scale)<1e-9||!Read(CenterXBox.Text,out double cx)||!Read(CenterYBox.Text,out double cy))throw new InvalidOperationException("Проверьте масштаб и координаты центра.");
        if(!Read(ExposureBox.Text,out double exposure)||exposure is<.1 or>10||!Read(GammaBox.Text,out double gamma)||gamma is<.1 or>5)throw new InvalidOperationException("Экспозиция: 0.1–10, гамма: 0.1–5.");
        if(_transforms.All(t=>t.Weight<=0))throw new InvalidOperationException("Добавьте хотя бы одну трансформацию с положительным весом.");
        return new FlameState{SaveName=name,Timestamp=DateTime.Now,CenterX=cx,CenterY=cy,Scale=Math.Abs(scale),Samples=samples,IterationsPerSample=iterations,WarmupIterations=warmup,Exposure=exposure,Gamma=gamma,Transforms=_transforms.Select(t=>t.Clone()).ToList()};
    }
    public void LoadState(FlameState state)
    {
        _cts?.Cancel();_syncing=true;try{_centerX=state.CenterX;_centerY=state.CenterY;_worldScale=Math.Max(1e-9,Math.Abs(state.Scale));CenterXBox.Text=F(_centerX);CenterYBox.Text=F(_centerY);ScaleBox.Text=F(_worldScale);SamplesBox.Text=state.Samples.ToString();IterationsBox.Text=state.IterationsPerSample.ToString();WarmupBox.Text=state.WarmupIterations.ToString();ExposureBox.Text=F(state.Exposure);GammaBox.Text=F(state.Gamma);_transforms.Clear();_transforms.AddRange(state.Transforms.Select(t=>t.Clone()));}finally{_syncing=false;}UpdateTransform();Schedule();
    }
    public BitmapSource? CaptureCurrentPreview(int width, int height) =>
        SavePreviewCapture.Capture(SavePreviewLayer, CanvasHost.Background, width, height, StableImage, CoverageImage);

    public Task<BitmapSource> RenderStatePreviewAsync(
        FlameState state, int width, int height, CancellationToken token, IProgress<int>? progress = null) =>
        RenderBitmapAsync(state.Clone(), width, height, token, progress);
    private void Parameter_OnChanged(object sender,EventArgs e){if(!_syncing)Schedule();}
    private void Viewport_OnChanged(object sender,EventArgs e){if (sender is System.Windows.Controls.TextBox edited && !NumericInput.IsValid(edited)) return;if(_syncing)return;if(Read(CenterXBox.Text,out double x))_centerX=x;if(Read(CenterYBox.Text,out double y))_centerY=y;if(Read(ScaleBox.Text,out double scale)&&Math.Abs(scale)>=1e-9)_worldScale=Math.Abs(scale);UpdateTransform();Schedule();}
    private void Coverage_OnChanged(object sender,RoutedEventArgs e){if(!IsInitialized||CoverageImage is null)return;if(CoverageCheck.IsChecked!=true)CoverageImage.Source=null;}
    private void Schedule(){if(!IsLoaded)return;_cts?.Cancel();_memoryReliefTimer.Stop();_timer.Stop();_timer.Start();}
    private async Task ReleaseMemoryIfIdleAsync()
    {
        if(_rendering||MemorySaverCheck.IsChecked!=true)return;
        if(GC.GetTotalMemory(false)<MemoryReliefThresholdBytes)return;
        await MemoryPressureRelief.CompactAsync();
    }
    private void Render_OnClick(object sender,RoutedEventArgs e){ NumericInput.CommitAll(this);_timer.Stop();_cts?.Cancel();_ = RenderAsync();} private void Cancel_OnClick(object sender,RoutedEventArgs e)=>_cts?.Cancel();
    private async Task RenderAsync()
    {
        _memoryReliefTimer.Stop();
        if(_rendering){Schedule();return;}FlameState state;try{state=CaptureState("preview");}catch(Exception ex){StatusText.Text=ex.Message;return;}
        _cts?.Dispose();_cts=new CancellationTokenSource();CancellationToken token=_cts.Token;_rendering=true;CancelButton.IsEnabled=true;RenderBadge.Visibility=Visibility.Visible;var watch=Stopwatch.StartNew();
        FlameRenderer? renderer=null;WriteableBitmap? coverage=null;byte[]? coveragePixels=null;byte[]? pixels=null;BitmapSource? done=null;
        try
        {
            RenderSurfaceMetrics surface=RenderSurfaceMetrics.Measure(CanvasHost);DpiScale dpi=surface.Dpi;int width=surface.PixelWidth,height=surface.PixelHeight;
            _activeCenterX=state.CenterX;_activeCenterY=state.CenterY;_activeScale=state.Scale;UpdateTransform();coverage=new WriteableBitmap(width,height,dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Bgra32,null);CoverageImage.Source=CoverageCheck.IsChecked==true?coverage:null;
            renderer=new FlameRenderer(state,width,height,Threads());int batch=Math.Clamp(state.Samples/24,1000,100000);
            while(renderer.ProcessedSamples<state.Samples)
            {
                token.ThrowIfCancellationRequested();await Task.Run(()=>renderer.Accumulate(Math.Min(batch,state.Samples-renderer.ProcessedSamples),token),token);int percent=(int)(renderer.ProcessedSamples*100d/state.Samples);ProgressBar.Value=percent;ProgressText.Text=$"Накопление HDR: {percent}%";RenderBadgeText.Text=$"{renderer.ProcessedSamples:N0} / {state.Samples:N0} сэмплов";
                if(CoverageCheck.IsChecked==true){coveragePixels=await Task.Run(renderer.CreateCoverageFrame,token);coverage.WritePixels(new Int32Rect(0,0,width,height),coveragePixels,width*4,0);coveragePixels=null;}
            }
            pixels=await Task.Run(renderer.CreateFinalFrame,token);done=BitmapSource.Create(width,height,dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Bgra32,null,pixels,width*4);done.Freeze();StableImage.Source=done;CoverageImage.Source=null;_renderedCenterX=state.CenterX;_renderedCenterY=state.CenterY;_renderedScale=state.Scale;_hasFrame=true;UpdateTransform();ProgressBar.Value=100;ProgressText.Text="HDR-рендер завершён";StatusText.Text=$"Готово за {watch.Elapsed.TotalSeconds:F3} сек.";
        }
        catch(OperationCanceledException){CoverageImage.Source=null;StatusText.Text="Рендер отменён";}catch(Exception ex){CoverageImage.Source=null;MessageBox.Show(this,ex.Message,"Flame",MessageBoxButton.OK,MessageBoxImage.Error);}
        finally
        {
            CoverageImage.Source=null;renderer?.Dispose();renderer=null;coverage=null;coveragePixels=null;pixels=null;done=null;
            _rendering=false;CancelButton.IsEnabled=false;RenderBadge.Visibility=Visibility.Collapsed;
            // Тяжёлый MemoryPressureRelief убран с интерактивного пути: он выполнялся на
            // каждый дебаунс-предпросмотр и давал просадку без нагрузки на ЦП. Вместо этого
            // лёгкая компакция откладывается на паузу в навигации (см. _memoryReliefTimer).
            if(MemorySaverCheck.IsChecked==true){_memoryReliefTimer.Stop();_memoryReliefTimer.Start();}
        }
    }
    private void Transforms_OnClick(object sender,RoutedEventArgs e){var editor=new FlameTransformEditorWindow(_transforms){Owner=this};editor.TransformsApplied+=ApplyTransforms;editor.ShowDialog();}
    private void ApplyTransforms(IReadOnlyList<FlameTransform> transforms){_transforms.Clear();_transforms.AddRange(transforms.Select(t=>t.Clone()));Schedule();}
    private void Saves_OnClick(object sender,RoutedEventArgs e){ NumericInput.CommitAll(this); SaveManagerWindow.Open(this,SaveManagerConfigurations.ForFlame(this,_saves)); }
    private void Export_OnClick(object sender,RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        RenderSurfaceMetrics surface=RenderSurfaceMetrics.Measure(CanvasHost);int sourceW=surface.PixelWidth,sourceH=surface.PixelHeight;_cts?.Cancel();try{_=CaptureState("export");}catch(Exception ex){MessageBox.Show(this,ex.Message,"Параметры экспорта",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        ImageExportManagerWindow.Open(this,new ImageExportConfiguration{FileNamePrefix="flame",InitialWidth=sourceW,InitialHeight=sourceH,MaxSsaaFactor=4,HasNativeSsaa=false,ReleaseMemoryAfterExport=true,RenderAsync=(request,token,progress)=>{FlameState state=CaptureState("export");double factor=Math.Max(1,request.Width*(double)request.Height/(sourceW*(double)sourceH));state.Samples=(int)Math.Min(int.MaxValue,Math.Ceiling(state.Samples*factor));return RenderBitmapAsync(state,request.Width,request.Height,token,progress);}});
    }
    private async Task<BitmapSource> RenderBitmapAsync(FlameState state,int width,int height,CancellationToken token,IProgress<int>? progress)
    {
        using var renderer=new FlameRenderer(state,width,height,Threads());
        while(renderer.ProcessedSamples<state.Samples){await Task.Run(()=>renderer.Accumulate(Math.Min(100000,state.Samples-renderer.ProcessedSamples),token),token);progress?.Report((int)(renderer.ProcessedSamples*100d/state.Samples));}
        byte[] pixels=await Task.Run(renderer.CreateFinalFrame,token);BitmapSource bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);bitmap.Freeze();return bitmap;
    }
    private int Threads()=>ThreadsBox.SelectedItem?.ToString()=="Auto"?Environment.ProcessorCount:Convert.ToInt32(ThreadsBox.SelectedItem);
    private void CanvasHost_OnSizeChanged(object sender,SizeChangedEventArgs e){UpdateTransform();Schedule();}
    private void CanvasHost_OnMouseWheel(object sender,MouseWheelEventArgs e){Point p=e.GetPosition(CanvasHost);var before=ScreenToWorld(p);_worldScale=Math.Clamp(_worldScale*(e.Delta>0?.85:1.18),1e-9,200000);var after=ScreenToWorld(p);_centerX+=before.X-after.X;_centerY+=before.Y-after.Y;SyncViewportBoxes();UpdateTransform();Schedule();}
    private void CanvasHost_OnMouseLeftButtonDown(object sender,MouseButtonEventArgs e){_cts?.Cancel();_panning=true;_panStart=e.GetPosition(CanvasHost);CanvasHost.CaptureMouse();}
    private void CanvasHost_OnMouseMove(object sender,MouseEventArgs e){if(!_panning)return;Point p=e.GetPosition(CanvasHost);var a=ScreenToWorld(_panStart);var b=ScreenToWorld(p);_centerX+=a.X-b.X;_centerY+=a.Y-b.Y;_panStart=p;SyncViewportBoxes();UpdateTransform();}
    private void CanvasHost_OnMouseLeftButtonUp(object sender,MouseButtonEventArgs e){if(!_panning)return;_panning=false;CanvasHost.ReleaseMouseCapture();Schedule();}
    private (double X,double Y) ScreenToWorld(Point p){double h=Math.Max(1,CanvasHost.ActualHeight),w=Math.Max(1,CanvasHost.ActualWidth),worldH=_worldScale*h/w;return(_centerX+(p.X/w-.5)*_worldScale,_centerY-(p.Y/h-.5)*worldH);}
    private void SyncViewportBoxes(){_syncing=true;CenterXBox.Text=F(_centerX);CenterYBox.Text=F(_centerY);ScaleBox.Text=F(_worldScale);_syncing=false;}
    private void UpdateTransform(){if(CanvasHost.ActualWidth<=0)return;if(_hasFrame)ApplyViewportTransform(_imageScale,_translation,_renderedCenterX,_renderedCenterY,_renderedScale);ApplyViewportTransform(_coverageScale,_coverageTranslation,_activeCenterX,_activeCenterY,_activeScale);}
    private void ApplyViewportTransform(ScaleTransform scale,TranslateTransform translation,double sourceX,double sourceY,double sourceScale){double w=CanvasHost.ActualWidth,h=CanvasHost.ActualHeight,worldH=_worldScale*h/w;scale.ScaleX=scale.ScaleY=sourceScale/_worldScale;translation.X=(sourceX-_centerX)/_worldScale*w;translation.Y=(_centerY-sourceY)/worldH*h;}
    private void Toggle_OnClick(object sender,RoutedEventArgs e)=>FractalControlPanel.Toggle(ref _controls,ControlsColumn,ControlsHost,ToggleButton,300,Schedule);
    private void Window_OnKeyDown(object sender,KeyEventArgs e){_fullscreen.HandleKey(this,e.Key);}
        private void Window_OnClosing(object? sender,System.ComponentModel.CancelEventArgs e){_timer.Stop();_memoryReliefTimer.Stop();_cts?.Cancel();_cts?.Dispose();}
    private static bool Read(string text,out double value)=> (double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out value)) && double.IsFinite(value);private static string F(double value)=>value.ToString("G15",CultureInfo.InvariantCulture);
}
