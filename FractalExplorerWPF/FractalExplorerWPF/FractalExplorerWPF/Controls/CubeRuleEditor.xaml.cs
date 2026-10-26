using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

public partial class CubeRuleEditor : UserControl
{
    private readonly ToggleButton[] _cells = new ToggleButton[27];
    private readonly List<uint?> _history = [];
    private Fractal3DKind _kind = Fractal3DKind.MengerSponge;
    private uint? _mask;
    private bool _loading = true;
    public event EventHandler? SettingsChanged;
    public event EventHandler? AtlasRequested;
    public event Action<int>? LevelStepRequested;

    public CubeRuleEditor()
    {
        InitializeComponent();
        for (int z = 0; z < 3; z++)
        {
            var layer = new StackPanel { Margin = new Thickness(2) };
            layer.Children.Add(new TextBlock { Text = $"Z = {z - 1:+0;-0;0}", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11 });
            var grid = new UniformGrid { Columns = 3, Rows = 3 };
            for (int row = 0; row < 3; row++)
            for (int x = 0; x < 3; x++)
            {
                int y = 2 - row;
                int index = CubeRule.Index(x, y, z);
                var cell = new ToggleButton
                {
                    Content = "·", MinWidth = 0, MinHeight = 0, Height = 26,
                    Padding = new Thickness(0), Margin = new Thickness(1), Tag = index,
                    ToolTip = $"X = {x - 1}, Y = {y - 1}, Z = {z - 1}"
                };
                cell.SetResourceReference(StyleProperty, "ThemeToggleButtonStyle");
                cell.Click += CellClicked;
                _cells[index] = cell;
                grid.Children.Add(cell);
            }
            layer.Children.Add(grid);
            Layers.Children.Add(layer);
        }
        Refresh();
    }

    public uint? Capture() => _mask;

    public void ApplyAtlasRule(uint? mask)
    {
        if (mask > CubeRule.All) throw new ArgumentException("Кубическое правило содержит больше 27 бит.");
        Change(mask);
    }

    private void AtlasClicked(object sender, RoutedEventArgs e) => AtlasRequested?.Invoke(this, EventArgs.Empty);

    public void Load(Fractal3DKind kind, uint? mask)
    {
        _kind = kind;
        _mask = mask is uint value ? value & CubeRule.All : null;
        _history.Clear();
        Refresh();
    }

    private uint EffectiveMask => _mask ?? CubeRule.Classic(_kind);

    private void Change(uint? mask)
    {
        if (mask == _mask) { Refresh(); return; }
        if (_history.Count == 32) _history.RemoveAt(0);
        _history.Add(_mask);
        _mask = mask;
        Refresh();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh()
    {
        _loading = true;
        try
        {
            uint mask = EffectiveMask;
            for (int i = 0; i < 27; i++)
            {
                bool included = (mask & (1u << i)) != 0;
                _cells[i].IsChecked = included;
                _cells[i].Content = included ? "■" : "·";
                _cells[i].SetValue(AutomationProperties.NameProperty,
                    $"Кубик {i % 3 - 1}; {i / 3 % 3 - 1}; {i / 9 - 1}: {(included ? "оставлен" : "убран")}");
            }
            RuleBox.SelectedIndex = mask == CubeRule.Menger ? 0 : mask == CubeRule.Vicsek ? 1 : mask == CubeRule.Cantor ? 2 :
                mask == (CubeRule.Cantor | (1u << 13)) ? 3 : mask == (CubeRule.All ^ (1u << 13)) ? 4 : 5;
            int count = CubeRule.Count(mask);
            RuleStatus.Text = count == 0 ? "Пустое правило — сцена без кубиков." :
                count == 27 ? "27 из 27 — сплошной куб на любом поколении." :
                $"Оставлено {count} из 27 кубиков. Правило повторяется в каждом из них.";
            UndoButton.IsEnabled = _history.Count > 0;
            ResetButton.IsEnabled = _mask.HasValue;
        }
        finally { _loading = false; }
    }

    private void CellClicked(object sender, RoutedEventArgs e)
    {
        int index = (int)((ToggleButton)sender).Tag;
        bool included = (EffectiveMask & (1u << index)) == 0;
        Change(CubeRule.Set(EffectiveMask, index % 3, index / 3 % 3, index / 9, included,
            MirrorXBox.IsChecked == true, MirrorYBox.IsChecked == true, MirrorZBox.IsChecked == true));
    }

    private void RuleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RuleBox.SelectedIndex < 0) return;
        uint mask = RuleBox.SelectedIndex switch
        {
            0 => CubeRule.Menger, 1 => CubeRule.Vicsek, 2 => CubeRule.Cantor,
            3 => CubeRule.Cantor | (1u << 13), 4 => CubeRule.All ^ (1u << 13), _ => EffectiveMask
        };
        Change(mask == CubeRule.Classic(_kind) ? null : mask);
    }

    private void RandomClicked(object sender, RoutedEventArgs e)
    {
        uint mask = 0;
        var visited = new HashSet<int>();
        for (int i = 0; i < 27; i++)
        {
            if (!visited.Add(i)) continue;
            uint orbit = CubeRule.Set(0, i % 3, i / 3 % 3, i / 9, true,
                MirrorXBox.IsChecked == true, MirrorYBox.IsChecked == true, MirrorZBox.IsChecked == true);
            for (int j = 0; j < 27; j++) if ((orbit & (1u << j)) != 0) visited.Add(j);
            if (Random.Shared.Next(2) == 0) mask |= orbit;
        }
        // Исключаем две вырожденные случайные формы; ручное пустое/полное правило доступно.
        if (mask == 0 || mask == CubeRule.All) mask = CubeRule.Vicsek;
        Change(mask);
    }

    private void UndoClicked(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0) return;
        _mask = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        Refresh();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearClicked(object sender, RoutedEventArgs e) => Change(0);
    private void FillClicked(object sender, RoutedEventArgs e) => Change(CubeRule.All);
    private void ResetClicked(object sender, RoutedEventArgs e) => Change(null);
    private void PreviousLevelClicked(object sender, RoutedEventArgs e) => LevelStepRequested?.Invoke(-1);
    private void NextLevelClicked(object sender, RoutedEventArgs e) => LevelStepRequested?.Invoke(1);
}
