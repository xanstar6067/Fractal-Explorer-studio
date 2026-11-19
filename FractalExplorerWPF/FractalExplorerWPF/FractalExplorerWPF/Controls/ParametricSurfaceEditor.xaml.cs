using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

public partial class ParametricSurfaceEditor : UserControl
{
    private readonly TextBox[] _longitude, _latitude, _coefficients, _domain, _segments;
    private readonly TextBox _gridLines;
    private readonly TextBox[] _formulas;
    private int _usedCoefficients;
    private bool _loading = true;
    private ParametricSurfaceSettings _saved = new();
    private bool UsesEquations => FamilyBox.SelectedIndex == (int)ParametricSurfaceFamily.Equations;
    private bool Linked => LinkBox.IsChecked == true;
    private bool ShowsGrid => GridBox.IsChecked == true;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public event EventHandler? SettingsChanged;

    public ParametricSurfaceEditor()
    {
        InitializeComponent();
        _formulas = [XBox, YBox, ZBox];
        _longitude = CreateProfileFields(LongitudePanel, "Долгота", _saved.Longitude, () => !UsesEquations);
        _latitude = CreateProfileFields(LatitudePanel, "Широта", _saved.Latitude, () => !UsesEquations && !Linked);
        _coefficients = new[] { _saved.A, _saved.B, _saved.C }.Select((value, i) =>
            AddField(CoefficientsPanel, $"{(char)('a' + i)}", value, -ParametricSurfaceSettings.CoefficientLimit,
                ParametricSurfaceSettings.CoefficientLimit, () => UsesEquations && UsesCoefficient(i), slider: true)).ToArray();
        _domain =
        [
            DomainField("u от", _saved.Domain.UMin), DomainField("u до", _saved.Domain.UMax),
            DomainField("v от", _saved.Domain.VMin), DomainField("v до", _saved.Domain.VMax)
        ];
        _segments =
        [
            AddField(MeshPanel, "Разбиение u", _saved.USegments, ParametricSurfaceSettings.MinSegments, ParametricSurfaceSettings.MaxSegments, integer: true),
            AddField(MeshPanel, "Разбиение v", _saved.VSegments, ParametricSurfaceSettings.MinSegments, ParametricSurfaceSettings.MaxSegments, integer: true)
        ];
        _gridLines = AddField(GridPanel, "Делений вдоль каждой оси", _saved.GridLines,
            ParametricSurfaceSettings.MinGridLines, ParametricSurfaceSettings.MaxGridLines, () => ShowsGrid, integer: true);
        Load(_saved);
    }

    private TextBox DomainField(string title, double value) => AddField(DomainPanel, title, value,
        -ParametricDomain.Limit, ParametricDomain.Limit, () => UsesEquations);

    private TextBox[] CreateProfileFields(StackPanel panel, string profile, SuperformulaProfile initial, Func<bool> active)
    {
        var advanced = new UniformGrid { Columns = 2 };
        TextBox[] fields =
        [
            AddField(panel, "m · повторение", initial.M, 0, SuperformulaProfile.MaxSymmetry, active, slider: true, accessible: profile),
            Exponent("n₁ · сила", initial.N1), Exponent("n₂ · косинус", initial.N2), Exponent("n₃ · синус", initial.N3),
            AddField(advanced, "a · масштаб cos", initial.A, SuperformulaProfile.MinScale, SuperformulaProfile.MaxScale, active, accessible: profile),
            AddField(advanced, "b · масштаб sin", initial.B, SuperformulaProfile.MinScale, SuperformulaProfile.MaxScale, active, accessible: profile)
        ];
        panel.Children.Add(new Expander { Header = "Масштабы профиля a/b", Content = advanced, Margin = new Thickness(0, 4, 0, 0) });
        return fields;

        TextBox Exponent(string title, double value) => AddField(panel, title, value, SuperformulaProfile.MinExponent,
            SuperformulaProfile.MaxExponent, active, slider: true, logarithmic: true, accessible: profile);
    }

    private TextBox AddField(Panel parent, string title, double initial, double min, double max,
        Func<bool>? active = null, bool slider = false, bool integer = false, bool logarithmic = false, string? accessible = null)
    {
        string name = accessible is null ? title : $"{accessible}, {title}";
        var row = new StackPanel { Margin = new Thickness(0, 0, parent is UniformGrid ? 5 : 0, 4) };
        var box = new TextBox { Text = initial.ToString("R", Invariant), MinWidth = 66 };
        NumericSpinner.SetIsEnabled(box, true);
        AutomationProperties.SetName(box, name);
        row.Children.Add(new Label { Content = title, Target = box, Padding = new Thickness(0, 3, 0, 2) });
        if (integer) NumericInput.Integer(this, box, name, (int)min, (int)max, active: active);
        else NumericInput.Real(this, box, name, min, max, active);
        if (slider)
        {
            double ToSlider(double value) => logarithmic ? Math.Log(value) : value;
            var track = new Slider
            {
                Minimum = ToSlider(min), Maximum = ToSlider(max), Value = ToSlider(initial),
                SmallChange = logarithmic ? .055 : (max - min) / 100, LargeChange = (ToSlider(max) - ToSlider(min)) / 10,
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                IsMoveToPointEnabled = true
            };
            AutomationProperties.SetName(track, name);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(84) });
            Grid.SetColumn(box, 1);
            grid.Children.Add(track);
            grid.Children.Add(box);
            row.Children.Add(grid);

            bool syncing = false;
            track.ValueChanged += (_, _) =>
            {
                if (syncing) return;
                Write(box, Math.Round(logarithmic ? Math.Exp(track.Value) : track.Value, 3));
            };
            box.TextChanged += (_, _) =>
            {
                if (!PanelFields.TryReadDouble(box.Text, out var value) || !double.IsFinite(value) || value < min || value > max) return;
                syncing = true;
                track.Value = ToSlider(value);
                syncing = false;
            };
        }
        else row.Children.Add(box);
        box.TextChanged += (_, _) => RaiseChanged();
        parent.Children.Add(row);
        return box;
    }

    public void Load(ParametricSurfaceSettings settings)
    {
        settings.Validate();
        _loading = true;
        try
        {
            var defaults = new ParametricSurfaceSettings();
            _saved = settings with
            {
                Longitude = settings.Longitude ?? defaults.Longitude, Latitude = settings.Latitude ?? defaults.Latitude,
                Domain = settings.Domain ?? defaults.Domain, Equations = settings.Equations ?? defaults.Equations
            };
            WriteProfile(_longitude, _saved.Longitude);
            WriteProfile(_latitude, _saved.Latitude);
            WriteMany(_coefficients, _saved.A, _saved.B, _saved.C);
            var domain = _saved.Domain;
            WriteMany(_domain, domain.UMin, domain.UMax, domain.VMin, domain.VMax);
            WriteMany(_segments, _saved.USegments, _saved.VSegments);
            Write(_gridLines, Math.Clamp(_saved.GridLines, ParametricSurfaceSettings.MinGridLines, ParametricSurfaceSettings.MaxGridLines));
            LinkBox.IsChecked = _saved.LinkProfiles;
            FitBox.IsChecked = _saved.Fit;
            GridBox.IsChecked = _saved.ShowGrid;
            FamilyBox.SelectedIndex = (int)_saved.Family;
            LoadDraft();
            SetDomainStatus("");
            ShowMeshStatus("");
        }
        finally { _loading = false; }
        Refresh();
    }

    public ParametricSurfaceSettings Capture()
    {
        NumericInput.Prepare(this);
        ParametricDomain domain = UsesEquations ? ReadDomain() : ReadOrPrevious(ReadDomainBounds, _saved.Domain);
        var state = _saved with
        {
            Family = UsesEquations ? ParametricSurfaceFamily.Equations : ParametricSurfaceFamily.Superformula,
            Longitude = UsesEquations ? ReadOrPrevious(() => ReadProfile(_longitude), _saved.Longitude) : ReadProfile(_longitude),
            Latitude = UsesEquations || Linked ? ReadOrPrevious(() => ReadProfile(_latitude), _saved.Latitude) : ReadProfile(_latitude),
            LinkProfiles = Linked, Domain = domain,
            A = ReadCoefficient(0, _saved.A), B = ReadCoefficient(1, _saved.B), C = ReadCoefficient(2, _saved.C),
            USegments = ReadInt(_segments[0], ParametricSurfaceSettings.MinSegments, ParametricSurfaceSettings.MaxSegments),
            VSegments = ReadInt(_segments[1], ParametricSurfaceSettings.MinSegments, ParametricSurfaceSettings.MaxSegments),
            Fit = FitBox.IsChecked == true, ShowGrid = ShowsGrid,
            GridLines = ShowsGrid ? ReadGridLines() : ReadOrPrevious(ReadGridLines, _saved.GridLines)
        };
        state.Validate();
        _saved = state;
        return state;

        int ReadGridLines() => ReadInt(_gridLines, ParametricSurfaceSettings.MinGridLines, ParametricSurfaceSettings.MaxGridLines);
    }

    private ParametricDomain ReadDomain()
    {
        try
        {
            var domain = ReadDomainBounds();
            SetDomainStatus("");
            return domain;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            DomainExpander.IsExpanded = true;
            SetDomainStatus(error.Message);
            throw;
        }
    }

    private ParametricDomain ReadDomainBounds()
    {
        var domain = new ParametricDomain(ReadBound(0), ReadBound(1), ReadBound(2), ReadBound(3));
        domain.Validate();
        return domain;

        double ReadBound(int i) => Read(_domain[i], -ParametricDomain.Limit, ParametricDomain.Limit);
    }

    private void SetDomainStatus(string message)
    {
        DomainStatus.Text = message;
        DomainStatus.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private double ReadCoefficient(int index, double previous)
    {
        return UsesEquations && UsesCoefficient(index) ? ReadValue() : ReadOrPrevious(ReadValue, previous);
        double ReadValue() => Read(_coefficients[index], -ParametricSurfaceSettings.CoefficientLimit, ParametricSurfaceSettings.CoefficientLimit);
    }

    private static T ReadOrPrevious<T>(Func<T> read, T previous)
    {
        try { return read(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return previous; }
    }

    private void OnFamilyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Refresh();
        RaiseChanged();
    }

    private void OnViewChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Refresh();
        RaiseChanged();
    }

    private void Refresh()
    {
        SuperPanel.Visibility = UsesEquations ? Visibility.Collapsed : Visibility.Visible;
        EquationsPanel.Visibility = UsesEquations ? Visibility.Visible : Visibility.Collapsed;
        LatitudeExpander.Visibility = Linked ? Visibility.Collapsed : Visibility.Visible;
        GridPanel.Visibility = ShowsGrid ? Visibility.Visible : Visibility.Collapsed;
        CoefficientsExpander.Visibility = _usedCoefficients == 0 ? Visibility.Collapsed : Visibility.Visible;
        for (int i = 0; i < _coefficients.Length; i++)
            ((FrameworkElement)((FrameworkElement)_coefficients[i].Parent).Parent).Visibility =
                UsesCoefficient(i) ? Visibility.Visible : Visibility.Collapsed;
        RefreshDraftStatus();
    }

    private bool UsesCoefficient(int i) => (_usedCoefficients & (1 << i)) != 0;

    private void LoadDraft()
    {
        XBox.Text = _saved.Equations.X;
        YBox.Text = _saved.Equations.Y;
        ZBox.Text = _saved.Equations.Z;
        foreach (var box in _formulas) NumericInput.ShowIssue(box, "");
        try { SetUsedCoefficients(_saved.Equations.Compile()); }
        catch (ArgumentException) when (!UsesEquations) { _usedCoefficients = 0; }
    }

    private void SetUsedCoefficients(Core.NewtonMath.CompiledRealSpaceExpression[] formulas)
    {
        _usedCoefficients = 0;
        for (int i = 0; i < 3; i++)
            if (formulas.Any(f => f.UsesParameter((char)('a' + i)))) _usedCoefficients |= 1 << i;
    }

    private void OnDraftChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        NumericInput.ShowIssue((TextBox)sender, "");
        RefreshDraftStatus();
    }

    private ParametricEquations Draft() => new(XBox.Text.Trim(), YBox.Text.Trim(), ZBox.Text.Trim());

    private void RefreshDraftStatus()
    {
        bool changed = Draft() != _saved.Equations;
        ApplyButton.IsEnabled = RevertButton.IsEnabled = changed;
        FormulaStatus.Text = changed
            ? "Есть черновик. Кадр и сохранения используют применённые формулы."
            : "Формулы применены. Активные коэффициенты и границы обновляют форму автоматически.";
    }

    private void ApplyClicked(object sender, RoutedEventArgs e) => ApplyDraft();

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (UsesEquations && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && ApplyButton.IsEnabled)
        {
            ApplyDraft();
            e.Handled = true;
        }
    }

    private void ApplyDraft()
    {
        NumericInput.CommitAll(this);
        try { ReadDomain(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            FormulaStatus.Text = error.Message;
            return;
        }

        var draft = Draft();
        var compiled = new Core.NewtonMath.CompiledRealSpaceExpression[3];
        string[] text = [draft.X, draft.Y, draft.Z];
        for (int i = 0; i < text.Length; i++)
        {
            try { compiled[i] = ParametricEquations.CompileAxis(text[i], "xyz"[i].ToString()); }
            catch (ArgumentException error)
            {
                NumericInput.ShowIssue(_formulas[i], error.Message);
                FormulaStatus.Text = error.Message;
                _formulas[i].Focus();
                return;
            }
        }
        _saved = _saved with { Equations = draft };
        SetUsedCoefficients(compiled);
        foreach (var box in _formulas) NumericInput.ShowIssue(box, "");
        Refresh();
        RaiseChanged();
    }

    private void RevertClicked(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try { LoadDraft(); }
        finally { _loading = false; }
        Refresh();
    }

    private void PeriodicDomainClicked(object sender, RoutedEventArgs e) => SetDomain(new(0, 2 * Math.PI, 0, 2 * Math.PI));
    private void SphericalDomainClicked(object sender, RoutedEventArgs e) => SetDomain(new());
    private void SquareDomainClicked(object sender, RoutedEventArgs e) => SetDomain(new(-1, 1, -1, 1));

    private void SetDomain(ParametricDomain domain) =>
        SetValues(_domain, domain.UMin, domain.UMax, domain.VMin, domain.VMax);

    private void CoarseMeshClicked(object sender, RoutedEventArgs e) => SetValues(_segments, 48, 32);
    private void DefaultMeshClicked(object sender, RoutedEventArgs e) => SetValues(_segments, 112, 72);
    private void FineMeshClicked(object sender, RoutedEventArgs e) => SetValues(_segments, 192, 144);

    private void SetValues(TextBox[] boxes, params double[] values)
    {
        NumericInput.CommitAll(this);
        _loading = true;
        try { WriteMany(boxes, values); }
        finally { _loading = false; }
        RaiseChanged();
    }

    private void ResetProfiles(object sender, RoutedEventArgs e)
    {
        var defaults = new ParametricSurfaceSettings();
        ChangeProfiles(defaults.Longitude, defaults.Latitude, false);
    }

    private void RandomizeProfiles(object sender, RoutedEventArgs e)
    {
        var random = Random.Shared;
        SuperformulaProfile Profile(int max) => new(2 * random.Next(2, max),
            Math.Round(.35 + random.NextDouble() * 2, 2),
            Math.Round(1.1 + random.NextDouble() * 3.4, 2),
            Math.Round(1.1 + random.NextDouble() * 3.4, 2));
        ChangeProfiles(Profile(11), Profile(7), Linked);
    }

    private void ChangeProfiles(SuperformulaProfile longitude, SuperformulaProfile latitude, bool link)
    {
        NumericInput.CommitAll(this);
        _loading = true;
        try
        {
            WriteProfile(_longitude, longitude);
            if (!link) WriteProfile(_latitude, latitude);
            LinkBox.IsChecked = link;
        }
        finally { _loading = false; }
        Refresh();
        RaiseChanged();
    }

    internal void ShowMeshStatus(string message)
    {
        MeshStatus.Text = message;
        MeshStatus.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RaiseChanged()
    {
        if (!_loading) SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double Read(TextBox box, double min, double max) =>
        PanelFields.ReadDouble(box, AutomationProperties.GetName(box), min, max);
    private static int ReadInt(TextBox box, int min, int max) =>
        PanelFields.ReadInt(box, AutomationProperties.GetName(box), min, max);
    private static void Write(TextBox box, double value) => box.Text = value.ToString("R", Invariant);
    private static void WriteMany(TextBox[] boxes, params double[] values)
    {
        for (int i = 0; i < boxes.Length; i++) Write(boxes[i], values[i]);
    }
    private static void WriteProfile(TextBox[] boxes, SuperformulaProfile p) => WriteMany(boxes, p.M, p.N1, p.N2, p.N3, p.A, p.B);
    private static SuperformulaProfile ReadProfile(TextBox[] boxes)
    {
        return new(Read(boxes[0], 0, SuperformulaProfile.MaxSymmetry), Exponent(1), Exponent(2), Exponent(3),
            Read(boxes[4], SuperformulaProfile.MinScale, SuperformulaProfile.MaxScale),
            Read(boxes[5], SuperformulaProfile.MinScale, SuperformulaProfile.MaxScale));
        double Exponent(int index) => Read(boxes[index], SuperformulaProfile.MinExponent, SuperformulaProfile.MaxExponent);
    }
}
