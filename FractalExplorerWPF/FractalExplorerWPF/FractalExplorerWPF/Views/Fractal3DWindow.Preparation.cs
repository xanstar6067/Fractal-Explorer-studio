using System.Windows;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    // Все режимы получают экран первого кадра здесь, без отдельной разметки для нового вида.
    private void BeginInitialPreparation(string message = "Подготовка первого кадра…")
    {
        InitialPreparationOverlay.Heading = _definition.Title;
        InitialPreparationOverlay.Message = message;
        InitialPreparationOverlay.Detail = "Первый запуск может занять некоторое время: подготавливаем данные и программы для видеокарты.";
        InitialPreparationOverlay.Progress = 0;
        InitialPreparationOverlay.IsIndeterminate = true;
        InitialPreparationOverlay.IsBusy = true;
        InitialPreparationOverlay.ActionText = "Отменить";
        InitialPreparationLayer.Visibility = Visibility.Visible;
    }

    private void BeginSimulationPreparation(PreparationOverlay overlay, string message)
    {
        overlay.Heading = _definition.Title;
        overlay.Message = message;
        overlay.Progress = 0;
        overlay.IsIndeterminate = true;
        overlay.IsBusy = true;
        overlay.IsActionEnabled = true;
        overlay.Visibility = Visibility.Visible;
    }

    private PreparationOverlay? SimulationPreparationOverlay => Kind switch
    {
        Fractal3DKind.Kobayashi3D => KobPreparationOverlay,
        Fractal3DKind.Excitable3D => ExPreparing,
        Fractal3DKind.GrayScott3D => GrayPreparationOverlay,
        Fractal3DKind.Lenia3D => LeniaPreparationOverlay,
        Fractal3DKind.Physarum3D => PhysarumPreparationOverlay,
        Fractal3DKind.CahnHilliard3D => CahnPreparationOverlay,
        Fractal3DKind.Turing3D => TuringPreparationOverlay,
        _ => null
    };

    private void ReportPreparationStage(Fractal3DRenderStage stage)
    {
        if (InitialPreparationLayer.Visibility != Visibility.Visible || !InitialPreparationOverlay.IsBusy) return;
        InitialPreparationOverlay.Message = stage switch
        {
            Fractal3DRenderStage.WaitingForDevice => "Ожидаем видеокарту…",
            Fractal3DRenderStage.PreparingDevice => "Подготовка видеокарты и программ рендера…",
            Fractal3DRenderStage.PreparingData when Kind == Fractal3DKind.Lyapunov3D => "Расчёт объёма Ляпунова…",
            Fractal3DRenderStage.PreparingData => "Подготовка геометрии и данных фрактала…",
            Fractal3DRenderStage.PreparingShaders => "Подготовка программы изображения…",
            _ => "Рендеринг первого кадра…"
        };
        InitialPreparationOverlay.IsIndeterminate = stage != Fractal3DRenderStage.Rendering;
    }

    private void ReportInitialPreparationProgress(int value, bool preparingLyapunov)
    {
        if (InitialPreparationLayer.Visibility != Visibility.Visible || !InitialPreparationOverlay.IsBusy) return;
        InitialPreparationOverlay.IsIndeterminate = false;
        InitialPreparationOverlay.Progress = value;
        InitialPreparationOverlay.Message = preparingLyapunov && value <= 60
            ? $"Расчёт объёма Ляпунова · {Math.Min(100, value * 100 / 60)}%"
            : $"Рендеринг первого кадра · {value}%";
    }

    private void ShowPreparationError(string message)
    {
        if (_isClosing) return;
        if (SimulationPreparationOverlay is { } overlay) overlay.Visibility = Visibility.Collapsed;
        if (CanvasImage.Source is not null)
        {
            InitialPreparationLayer.Visibility = Visibility.Collapsed;
            return;
        }
        InitialPreparationOverlay.Message = message;
        InitialPreparationOverlay.Detail = "Проверьте параметры и запустите рендер снова или выберите готовый вид.";
        InitialPreparationOverlay.IsBusy = false;
        InitialPreparationOverlay.ActionText = "";
        InitialPreparationLayer.Visibility = Visibility.Visible;
    }

    private void InitialPreparationCancel_OnClick(object sender, RoutedEventArgs e)
    {
        // Для растущих фракталов сохраняется остановка на достигнутой форме.
        if (Kind is Fractal3DKind.Dla3D or Fractal3DKind.Lichtenberg3D)
            CancelButton_OnClick(sender, e);
        else
        {
            _frameRequested = false;
            _renderCts?.Cancel();
            ShowPreparationError("Подготовка отменена");
        }
    }
}
