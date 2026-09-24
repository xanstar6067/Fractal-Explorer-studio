using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Microsoft.Win32;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private static readonly int[] TerrainResolutions = [257, 513, 1025, 2049];
    private CancellationTokenSource? _heightMapExportCts;

    private TerrainSettings CaptureTerrain() => Kind != Fractal3DKind.Terrain ? new() : new()
    {
        Type = (TerrainKind)Math.Max(0, TerrainTypeBox.SelectedIndex),
        Seed = ReadInt(TerrainSeedBox, "Начальное число", 0, int.MaxValue),
        Octaves = ReadInt(TerrainOctavesBox, "Уровни деталей", 1, 12),
        Roughness = ReadDouble(TerrainRoughnessBox, "Шероховатость", 0.05, 0.95),
        Lacunarity = ReadDouble(TerrainLacunarityBox, "Отношение масштабов", 1.5, 3),
        Scale = ReadDouble(TerrainScaleBox, "Масштаб деталей", 0.25, 16),
        Height = ReadDouble(TerrainHeightBox, "Высота", 0.05, 8),
        Size = ReadDouble(TerrainSizeBox, "Размер участка", 1, 20),
        Resolution = TerrainResolutions[Math.Clamp(TerrainResolutionBox.SelectedIndex, 0, 3)]
    };

    private void LoadTerrain(TerrainSettings settings)
    {
        TerrainTypeBox.SelectedIndex = (int)settings.Type;
        TerrainSeedBox.Text = settings.Seed.ToString(CultureInfo.InvariantCulture);
        TerrainOctavesBox.Text = settings.Octaves.ToString(CultureInfo.InvariantCulture);
        TerrainRoughnessBox.Text = settings.Roughness.ToString(CultureInfo.InvariantCulture);
        TerrainLacunarityBox.Text = settings.Lacunarity.ToString(CultureInfo.InvariantCulture);
        TerrainScaleBox.Text = settings.Scale.ToString(CultureInfo.InvariantCulture);
        TerrainHeightBox.Text = settings.Height.ToString(CultureInfo.InvariantCulture);
        TerrainSizeBox.Text = settings.Size.ToString(CultureInfo.InvariantCulture);
        TerrainResolutionBox.SelectedIndex = Array.IndexOf(TerrainResolutions, settings.Resolution);
    }

    private void Terrain_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingUi) ScheduleRender();
    }

    private void TerrainRandom_OnClick(object sender, RoutedEventArgs e) =>
        TerrainSeedBox.Text = Random.Shared.Next(int.MaxValue).ToString(CultureInfo.InvariantCulture);

    private async void TerrainExport_OnClick(object sender, RoutedEventArgs e)
    {
        if (_heightMapExportCts is not null) { _heightMapExportCts.Cancel(); return; }
        try
        {
            TerrainSettings settings = CaptureTerrain();
            var dialog = new SaveFileDialog
            {
                Title = "Экспорт карты высот — PNG 16 бит", Filter = "PNG 16 бит (*.png)|*.png",
                FileName = $"terrain-{settings.Seed}-{settings.Resolution}.png", DefaultExt = ".png"
            };
            if (dialog.ShowDialog(this) != true) return;
            using var cancellation = new CancellationTokenSource();
            _heightMapExportCts = cancellation;
            TerrainExportButton.Content = "Отменить экспорт";
            EventHandler closed = (_, _) => cancellation.Cancel();
            Closed += closed;
            try
            {
                var bitmap = await Task.Run(() => TerrainHeightMapExport.Create(settings, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                TerrainExportButton.Content = "Сохранение PNG…";
                TerrainExportButton.IsEnabled = false;
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
            TerrainExportButton.Content = "Экспорт карты высот…";
            TerrainExportButton.IsEnabled = true;
        }
    }
}
