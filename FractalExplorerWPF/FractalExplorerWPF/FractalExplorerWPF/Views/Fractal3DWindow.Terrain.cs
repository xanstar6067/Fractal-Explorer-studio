using System.Windows;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Microsoft.Win32;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private CancellationTokenSource? _heightMapExportCts;

    private TerrainSettings CaptureTerrain() => Kind == Fractal3DKind.Terrain ? TerrainPanel.Capture() : new();

    private void LoadTerrain(TerrainSettings settings)
    {
        if (Kind == Fractal3DKind.Terrain) TerrainPanel.Load(settings);
    }

    private void Terrain_OnChanged(object sender, EventArgs e)
    {
        if (!_updatingUi && Kind == Fractal3DKind.Terrain) Parameter_OnChanged(sender, e);
    }

    private void TerrainSlope_OnRequested(object sender, EventArgs e)
    {
        if (!_updatingUi && Kind == Fractal3DKind.Terrain)
            ColoringModeBox.SelectedIndex = (int)Fractal3DColoringMode.OrbitTrap;
    }

    private async void TerrainExport_OnClick(object sender, EventArgs e)
    {
        if (_heightMapExportCts is not null) { _heightMapExportCts.Cancel(); return; }
        try
        {
            NumericInput.CommitAll(TerrainPanel);
            TerrainSettings settings = CaptureTerrain();
            var dialog = new SaveFileDialog
            {
                Title = "Экспорт карты высот — PNG 16 бит", Filter = "PNG 16 бит (*.png)|*.png",
                FileName = $"terrain-{settings.Seed}-{settings.Resolution}.png", DefaultExt = ".png"
            };
            if (dialog.ShowDialog(this) != true) return;
            using var cancellation = new CancellationTokenSource();
            _heightMapExportCts = cancellation;
            TerrainPanel.SetExportState("Отменить экспорт");
            EventHandler closed = (_, _) => cancellation.Cancel();
            Closed += closed;
            try
            {
                var bitmap = await Task.Run(() => TerrainHeightMapExport.Create(settings, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                TerrainPanel.SetExportState("Сохранение PNG…", enabled: false);
                await Task.Run(() => TerrainHeightMapExport.Save(dialog.FileName, bitmap));
                if (!_isClosing) MessageBox.Show(this, $"Сохранена карта {settings.Resolution} × {settings.Resolution}, 16 бит.\n" +
                    $"Участок: {settings.Size} × {settings.Size}. Высоты: 0…{settings.Height}.\nX слева направо, Z сверху вниз.",
                    "Карта высот", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally { Closed -= closed; }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!_isClosing) MessageBox.Show(this, exception.Message, "Экспорт карты высот", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _heightMapExportCts = null;
            TerrainPanel.SetExportState("Экспорт карты высот…");
        }
    }
}
