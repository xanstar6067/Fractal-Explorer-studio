using FractalExplorerWPF.Controls;
using System.Globalization; using System.Windows; using System.Windows.Controls; using System.Windows.Media;
using FractalExplorerWPF.Infrastructure; using FractalExplorerWPF.Infrastructure.ColorPicking; using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;
namespace FractalExplorerWPF.Views;
public partial class BuddhabrotPaletteWindow : Window
{
    private readonly BuddhabrotPaletteManager _manager; private readonly ColorSelectionService _picker = ColorSelectionService.Default;
    private readonly List<Color> _colors=[]; private BuddhabrotColorPalette? _selected; private bool CanEdit => _selected is { IsBuiltIn:false };
    public event EventHandler? PaletteApplied;
    public BuddhabrotPaletteWindow(BuddhabrotPaletteManager manager) { InitializeComponent(); _manager=manager; CustomPaletteFile.ShowLoadWarning(this, manager.LoadWarning); Refresh(manager.ActivePalette); InitializeNumericInput();
    }
    private void Refresh(BuddhabrotColorPalette select) { PaletteList.ItemsSource=null; PaletteList.ItemsSource=_manager.Palettes; PaletteList.SelectedItem=select; }
    private void PaletteList_OnSelectionChanged(object s, SelectionChangedEventArgs e) { if(PaletteList.SelectedItem is not BuddhabrotColorPalette p)return; _selected=p; NameBox.Text=p.Name; ModeBox.SelectedIndex=(int)p.ColoringMode; GammaBox.Text=p.Gamma.ToString(CultureInfo.InvariantCulture); GradientBox.IsChecked=p.IsGradient; AlignBox.IsChecked=p.AlignWithRenderIterations; StepsBox.Text=p.MaxColorIterations.ToString(); _colors.Clear();_colors.AddRange(p.Colors);ReloadColors();UpdateState();UpdatePreview(); }
    private void UpdateState(){bool edit=CanEdit;NameBox.IsEnabled=ModeBox.IsEnabled=GammaBox.IsEnabled=GradientBox.IsEnabled=AlignBox.IsEnabled=StepsBox.IsEnabled=edit;DeleteButton.IsEnabled=edit;EditButton.IsEnabled=edit&&ColorList.SelectedIndex>=0;RemoveButton.IsEnabled=edit&&_colors.Count>1&&ColorList.SelectedIndex>=0;}
    private void ReloadColors(int index=0){ColorList.ItemsSource=null;ColorList.ItemsSource=_colors;ColorList.SelectedIndex=_colors.Count==0?-1:Math.Clamp(index,0,_colors.Count-1);UpdatePreview();}
    private string Unique(string seed){string n=seed;int i=1;while(_manager.Palettes.Any(p=>p.Name.Equals(n,StringComparison.OrdinalIgnoreCase)))n=$"{seed} {i++}";return n;}
    private void New_OnClick(object s,RoutedEventArgs e){var p=_manager.Palettes[0].Clone(Unique("Новая палитра"));_manager.Palettes.Add(p);Refresh(p);}
    private void Copy_OnClick(object s,RoutedEventArgs e){if(_selected is null)return;var p=_selected.Clone(Unique($"{_selected.Name} копия"));_manager.Palettes.Add(p);Refresh(p);}
    private void Delete_OnClick(object s,RoutedEventArgs e){if(!CanEdit||_selected is null)return;_manager.Palettes.Remove(_selected);_manager.ActivePalette=_manager.Palettes[0];_manager.Save();Refresh(_manager.ActivePalette);}
    private bool SaveEdits(){NumericInput.CommitAll(this);if(!CanEdit||_selected is null)return false;if(!double.TryParse(GammaBox.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double g)||g is<.1 or>5)return false;if(!int.TryParse(StepsBox.Text,out int steps)||steps is<2 or>100000)return false;_selected.Name=NameBox.Text.Trim();_selected.Gamma=g;_selected.MaxColorIterations=steps;_selected.IsGradient=GradientBox.IsChecked==true;_selected.AlignWithRenderIterations=AlignBox.IsChecked==true;_selected.ColoringMode=(BuddhabrotColoringMode)Math.Clamp(ModeBox.SelectedIndex,0,2);_selected.Colors=[.._colors];_manager.Save();return true;}
    private void Save_OnClick(object s,RoutedEventArgs e){SaveEdits();Refresh(_selected??_manager.ActivePalette);}
    private void Apply_OnClick(object s,RoutedEventArgs e){if(_selected is null)return;if(CanEdit&&!SaveEdits())return;_manager.ActivePalette=_selected;PaletteApplied?.Invoke(this,EventArgs.Empty);}
    private bool Choose(Color initial,out Color color)=>_picker.TrySelectColor(this,initial,out color);
    private void Add_OnClick(object s,RoutedEventArgs e){if(CanEdit&&Choose(Colors.White,out Color c)){_colors.Add(c);ReloadColors(_colors.Count-1);}}
    private void Edit_OnClick(object s,RoutedEventArgs e){int i=ColorList.SelectedIndex;if(CanEdit&&i>=0&&Choose(_colors[i],out Color c)){_colors[i]=c;ReloadColors(i);}}
    private void Remove_OnClick(object s,RoutedEventArgs e){int i=ColorList.SelectedIndex;if(CanEdit&&i>=0&&_colors.Count>1){_colors.RemoveAt(i);ReloadColors(i);}}
    private void Up_OnClick(object s,RoutedEventArgs e)=>Move(-1);private void Down_OnClick(object s,RoutedEventArgs e)=>Move(1);
    private void Move(int d){int i=ColorList.SelectedIndex,j=i+d;if(!CanEdit||i<0||j<0||j>=_colors.Count)return;(_colors[i],_colors[j])=(_colors[j],_colors[i]);ReloadColors(j);}
    private void Random_OnClick(object s,RoutedEventArgs e){if(!CanEdit)return;_colors.Clear();for(int i=0;i<6;i++)_colors.Add(Color.FromRgb((byte)Random.Shared.Next(256),(byte)Random.Shared.Next(256),(byte)Random.Shared.Next(256)));ReloadColors();}
    private void ColorList_OnSelectionChanged(object s,SelectionChangedEventArgs e)=>UpdateState();
    private void UpdatePreview(){if(_selected is null)return;var p=_selected.Clone("preview");p.Colors=[.._colors];p.ColoringMode=(BuddhabrotColoringMode)Math.Clamp(ModeBox.SelectedIndex,0,2);p.IsGradient=GradientBox.IsChecked==true;if(NumericInput.IsValid(GammaBox)&&double.TryParse(GammaBox.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double g))p.Gamma=g;var stops=new GradientStopCollection();for(int i=0;i<=32;i++){double t=i/32d;stops.Add(new GradientStop(BuddhabrotPaletteManager.Evaluate(p,t,500),t));}PreviewRect.Fill=new LinearGradientBrush(stops,0);}
}
