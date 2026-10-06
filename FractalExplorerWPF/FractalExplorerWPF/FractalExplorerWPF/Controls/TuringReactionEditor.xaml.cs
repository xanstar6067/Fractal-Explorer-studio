using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

public partial class TuringReactionEditor : UserControl
{
    private bool _loading;
    public TuringReactionSettings Settings { get; private set; } = new();
    public event EventHandler? Changed;

    public TuringReactionEditor() { InitializeComponent(); Load(Settings); }

    public void Load(TuringReactionSettings settings)
    {
        _loading = true; Settings = settings;
        ModelBox.SelectedIndex = (int)settings.Model;
        ABox.Text = settings.A.ToString("G6", CultureInfo.InvariantCulture); BBox.Text = settings.B.ToString("G6", CultureInfo.InvariantCulture);
        DuBox.Text = settings.DiffusionU.ToString("G6", CultureInfo.InvariantCulture); DvBox.Text = settings.DiffusionV.ToString("G6", CultureInfo.InvariantCulture);
        ReactionPanel.Visibility = settings.IsClassical ? Visibility.Visible : Visibility.Collapsed;
        FormulaText.Text = settings.Model switch
        {
            TuringReactionModel.Brusselator => "U′ = DᵤΔU + A − (B+1)U + U²V\nV′ = DᵥΔV + BU − U²V",
            TuringReactionModel.Schnakenberg => "U′ = DᵤΔU + A − U + U²V\nV′ = DᵥΔV + B − U²V",
            _ => "U′ = DᵤΔU + A − U + U²/V\nV′ = DᵥΔV + U² − BV"
        };
        AText.Text = settings.Model == TuringReactionModel.GiererMeinhardt ? "A · фоновая активация" : "A · приток";
        BText.Text = settings.Model == TuringReactionModel.GiererMeinhardt ? "B · распад V" : "B · приток";
        DescriptionText.Text = settings.Model switch
        {
            TuringReactionModel.Brusselator => "Автокаталитическая реакция: пятна, полосы и объёмные структуры.",
            TuringReactionModel.Schnakenberg => "Два реагента формируют пятна, каналы и слои. Для полос попробуйте A = 0,025, B = 1,55, Dᵤ = 1, Dᵥ = 20.",
            TuringReactionModel.GiererMeinhardt => "Локальная активация и дальнее подавление. Семейство моделей применяется к биологическим орнаментам; узоры растущих раковин требуют также моделирования роста.",
            _ => "Художественная модель: в каждой клетке соревнуются несколько размеров деталей."
        };
        ErrorText.Text = string.Empty; _loading = false;
    }

    private void Model_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModelBox.SelectedIndex < 0) return;
        Load(TuringReactionSettings.Default((TuringReactionModel)ModelBox.SelectedIndex));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            double Read(TextBox box) => double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value : throw new ArgumentException("Введите числа для A, B и диффузии.");
            var settings = Settings with { A = Read(ABox), B = Read(BBox), DiffusionU = Read(DuBox), DiffusionV = Read(DvBox) };
            settings.Validate(); Settings = settings; ErrorText.Text = "Параметры применены.";
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex) { ErrorText.Text = ex.Message; }
    }
}
