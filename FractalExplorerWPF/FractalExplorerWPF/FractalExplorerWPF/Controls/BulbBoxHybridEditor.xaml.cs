using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Controls.PanelFields;

namespace FractalExplorerWPF.Controls;

public partial class BulbBoxHybridEditor : UserControl
{
    private bool _loading = true;
    private int _selected;
    private BulbBoxSequence? _sequence;
    private readonly List<BulbBoxSequence> _history = [];
    public Func<Fractal3DState>? ClassicState { get; set; }
    public event EventHandler? SettingsChanged;
    public bool IsCustom => EnabledBox.IsChecked == true;
    private BulbBoxStage? Selected => _sequence?.Stages.ElementAtOrDefault(_selected);
    private bool FormulaActive => IsCustom && Selected?.Operation is BulbBoxOperation.Bulb or BulbBoxOperation.Box;

    public BulbBoxHybridEditor()
    {
        InitializeComponent();
        NumericInput.Integer(this, RepeatsBox, "Повторов этапа", 1, 6, active: () => IsCustom);
        NumericInput.Real(this, PowerBox, "Степень бульба", 2, 12, active: () => FormulaActive);
        NumericInput.Real(this, ScaleBox, "Масштаб бокса", -8, 8, active: () => FormulaActive);
        NumericInput.Real(this, RadiusBox, "Минимальный радиус", .01, 4, active: () => FormulaActive);
        NumericInput.Real(this, FoldBox, "Предел свёртки", .1, 8, active: () => FormulaActive);
        NumericInput.Real(this, MixBox, "Доля другой операции", 0, 1, active: () => FormulaActive);
        NumericInput.Real(this, StretchBox, "Вытяжение инверсии", .5, 2, active: () => FormulaActive);
        NumericInput.Real(this, InversionPowerBox, "Округлость инверсии", 2, 16, active: () => FormulaActive);
        RegisterVector(180);
        _loading = false;
    }

    private void RegisterVector(double limit)
    {
        foreach (var (box, name) in new[] { (XBox, "X"), (YBox, "Y"), (ZBox, "Z") })
            NumericInput.Real(this, box, name, -limit, limit, active: () => IsCustom && !FormulaActive);
    }

    public BulbBoxSequence? Capture()
    {
        if (!IsCustom || _sequence is null) return null;
        NumericInput.Prepare(this);
        var sequence = _sequence.Clone();
        sequence.Stages[_selected] = ReadStage();
        sequence.Validate();
        return sequence;
    }

    public void Load(BulbBoxSequence? sequence)
    {
        sequence?.Validate();
        _sequence = sequence?.Clone(); _selected = 0; _history.Clear();
        Refresh();
    }

    private BulbBoxStage ReadStage()
    {
        var stage = Selected! with { Repeats = ReadInt(RepeatsBox, "Повторов этапа", 1, 6) };
        if (FormulaActive)
        {
            stage.Power = ReadDouble(PowerBox, "Степень", 2, 12);
            stage.Scale = ReadDouble(ScaleBox, "Масштаб бокса", -8, 8);
            stage.MinRadius = ReadDouble(RadiusBox, "Минимальный радиус", .01, 4);
            stage.FoldLimit = ReadDouble(FoldBox, "Предел свёртки", .1, 8);
            stage.Mix = ReadDouble(MixBox, "Подмешивание", 0, 1);
            stage.Inversion = (BoxInversionShape)InversionBox.SelectedIndex;
            stage.Stretch = ReadDouble(StretchBox, "Вытяжение", .5, 2);
            stage.InversionPower = ReadDouble(InversionPowerBox, "Округлость", 2, 16);
        }
        else
        {
            double limit = stage.Operation == BulbBoxOperation.Rotate ? 180 : 3;
            stage.X = ReadDouble(XBox, "X", -limit, limit);
            stage.Y = ReadDouble(YBox, "Y", -limit, limit);
            stage.Z = ReadDouble(ZBox, "Z", -limit, limit);
        }
        stage.Validate(); return stage;
    }

    private void SaveHistory()
    {
        if (_sequence is null) return;
        if (_history.Count == 32) _history.RemoveAt(0);
        _history.Add(_sequence.Clone());
    }

    private void Refresh()
    {
        _loading = true;
        try
        {
            EnabledBox.IsChecked = _sequence is not null;
            ProgramPanel.Visibility = _sequence is null ? Visibility.Collapsed : Visibility.Visible;
            if (_sequence is null) return;
            _selected = Math.Clamp(_selected, 0, _sequence.Stages.Count - 1);
            StagesList.ItemsSource = _sequence.Stages.Select((s, i) => $"{i + 1}. {BulbBoxStage.Name(s.Operation)} ×{s.Repeats}").ToArray();
            StagesList.SelectedIndex = _selected;
            FillStage();
        }
        finally { _loading = false; }
    }

    private void FillStage()
    {
        if (Selected is not { } s) return;
        _loading = true;
        try
        {
            SelectedText.Text = $"Этап {_selected + 1}: {BulbBoxStage.Name(s.Operation)}";
            FormulaPanel.Visibility = FormulaActive ? Visibility.Visible : Visibility.Collapsed;
            VectorPanel.Visibility = FormulaActive ? Visibility.Collapsed : Visibility.Visible;
            VectorCaption.Text = s.Operation == BulbBoxOperation.Rotate
                ? "Углы вокруг X, Y, Z (−180…180°), в этом порядке" : "Сдвиг орбиты по X, Y, Z (−3…3)";
            RepeatsBox.Text = s.Repeats.ToString(CultureInfo.InvariantCulture);
            foreach (var (box, value) in new[] { (PowerBox, s.Power), (ScaleBox, s.Scale), (RadiusBox, s.MinRadius),
                (FoldBox, s.FoldLimit), (MixBox, s.Mix), (StretchBox, s.Stretch), (InversionPowerBox, s.InversionPower),
                (XBox, s.X), (YBox, s.Y), (ZBox, s.Z) }) box.Text = value.ToString("R", CultureInfo.InvariantCulture);
            InversionBox.SelectedIndex = (int)s.Inversion;
            RegisterVector(s.Operation == BulbBoxOperation.Rotate ? 180 : 3);
            UpdateButtons(); ErrorText.Text = "";
        }
        finally { _loading = false; }
    }

    private void UpdateButtons()
    {
        AddButton.IsEnabled = _sequence?.Stages.Count < BulbBoxSequence.MaxStages;
        UpButton.IsEnabled = _selected > 0;
        DownButton.IsEnabled = _selected < _sequence?.Stages.Count - 1;
        RemoveButton.IsEnabled = _sequence is { Stages.Count: > 1 } &&
            (Selected?.Operation is BulbBoxOperation.Rotate or BulbBoxOperation.Translate ||
             _sequence.Stages.Count(s => s.Operation is BulbBoxOperation.Bulb or BulbBoxOperation.Box) > 1);
        UndoButton.IsEnabled = _history.Count > 0;
    }

    private void FieldChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsCustom || Selected is null) return;
        try
        {
            var stage = ReadStage();
            if (stage == Selected) return;
            SaveHistory(); _sequence!.Stages[_selected] = stage;
            UpdateButtons(); ErrorText.Text = "";
            // Keep the focused text and caret intact while typing.
            var items = (string[])StagesList.ItemsSource;
            items[_selected] = $"{_selected + 1}. {BulbBoxStage.Name(stage.Operation)} ×{stage.Repeats}";
            StagesList.Items.Refresh();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ErrorText.Text = ex.Message; }
    }

    private void EnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ErrorText.Text = "";
        if (IsCustom)
        {
            _sequence ??= BulbBoxSequence.FromClassic(ClassicState?.Invoke() ?? Fractal3DCatalog.CreateDefaultState(Fractal3DKind.BulbBoxHybrid));
            _sequence.Validate(); Refresh();
        }
        else ProgramPanel.Visibility = Visibility.Collapsed;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || StagesList.SelectedIndex < 0) return;
        int next = StagesList.SelectedIndex;
        NumericInput.CommitAll(this);
        _sequence!.Stages[_selected] = ReadStage();
        _selected = next; FillStage();
    }

    private void Edit(Action<List<BulbBoxStage>> edit)
    {
        NumericInput.CommitAll(this);
        if (_sequence is null) return;
        _sequence.Stages[_selected] = ReadStage();
        SaveHistory(); edit(_sequence.Stages); _sequence.Validate();
        Refresh(); SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    private void AddClicked(object sender, RoutedEventArgs e)
    {
        if (_sequence?.Stages.Count >= BulbBoxSequence.MaxStages) return;
        Edit(stages => { stages.Add(new() { Operation = (BulbBoxOperation)AddKindBox.SelectedIndex }); _selected = stages.Count - 1; });
    }
    private void UpClicked(object sender, RoutedEventArgs e)
    {
        if (_selected > 0) Edit(stages => { (stages[_selected - 1], stages[_selected]) = (stages[_selected], stages[_selected - 1]); _selected--; });
    }
    private void DownClicked(object sender, RoutedEventArgs e)
    {
        if (_selected < _sequence?.Stages.Count - 1) Edit(stages => { (stages[_selected + 1], stages[_selected]) = (stages[_selected], stages[_selected + 1]); _selected++; });
    }
    private void RemoveClicked(object sender, RoutedEventArgs e)
    {
        if (RemoveButton.IsEnabled) Edit(stages => { stages.RemoveAt(_selected); _selected = Math.Min(_selected, stages.Count - 1); });
    }
    private void UndoClicked(object sender, RoutedEventArgs e)
    {
        NumericInput.CommitAll(this);
        if (_history.Count == 0) return;
        _sequence = _history[^1]; _history.RemoveAt(_history.Count - 1);
        Refresh(); SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
