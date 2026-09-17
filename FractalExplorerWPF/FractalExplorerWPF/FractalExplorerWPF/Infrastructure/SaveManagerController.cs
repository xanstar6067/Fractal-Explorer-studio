using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FractalExplorerWPF.Controls;

namespace FractalExplorerWPF.Infrastructure;

public sealed class SaveManagerConfiguration<TState> where TState : class
{
    public required string WindowTitle { get; init; }
    /// <summary>Хранилище режима: по файлу на сохранение, превью рядом, удаление — в Корзину.</summary>
    public required FractalSaveStore<TState> Store { get; init; }
    public required Func<string, TState> CaptureState { get; init; }
    public required Func<int, int, BitmapSource?> CapturePreview { get; init; }
    public required Action<TState> LoadState { get; init; }
    public required Func<TState, int, int, CancellationToken, IProgress<int>?, Task<BitmapSource>> RenderPreviewAsync { get; init; }
    public required Func<TState, string> GetName { get; init; }
    public required Func<TState, DateTime> GetTimestamp { get; init; }
    public required Func<TState, string> GetDetails { get; init; }
    public IReadOnlyList<TState> PointsOfInterest { get; init; } = [];
    public int PreviewWidth { get; init; } = 480;
    public int PreviewHeight { get; init; } = 320;
}

/// <param name="Slot">Файл сохранения; <c>null</c> — встроенная точка интереса.</param>
/// <param name="PreviewPath">PNG-превью: рядом с файлом сохранения или в каталоге точек интереса.</param>
public sealed record SaveManagerEntry<TState>(TState State, string DisplayName, SaveSlot<TState>? Slot, string PreviewPath)
    where TState : class
{
    public bool IsPointOfInterest => Slot is null;
}

public sealed class SaveManagerController<TState> : IDisposable where TState : class
{
    private readonly Window _window;
    private readonly SaveManagerControl _view;
    private readonly SaveManagerConfiguration<TState> _configuration;
    private List<SaveSlot<TState>> _slots = [];
    private List<SaveManagerEntry<TState>> _entries = [];
    private CancellationTokenSource? _previewCts;
    private bool _isRendering;
    private bool _disposed;

    public SaveManagerController(Window window, SaveManagerControl view, SaveManagerConfiguration<TState> configuration)
    {
        _window = window;
        _view = view;
        _configuration = configuration;
        _window.Title = configuration.WindowTitle;

        _view.SelectionChanged += View_OnSelectionChanged;
        _view.ItemDoubleClicked += View_OnItemDoubleClicked;
        _view.SaveRequested += View_OnSaveRequested;
        _view.DeleteRequested += View_OnDeleteRequested;
        _view.LoadRequested += View_OnLoadRequested;
        _view.RenderPreviewRequested += View_OnRenderPreviewRequested;
        _view.CancelPreviewRequested += View_OnCancelPreviewRequested;
        _view.PointsOfInterestModeChanged += View_OnPointsOfInterestModeChanged;
        _view.CloseRequested += View_OnCloseRequested;
        _view.CloudRequested += View_OnCloudRequested;
        _view.SetPointsOfInterestAvailable(configuration.PointsOfInterest.Count > 0);
        RefreshStates();
    }

    private void RefreshStates(string? selectName = null)
    {
        string? damagedWarning = null;
        try
        {
            SaveLoadResult<TState> result = _configuration.Store.LoadSlots();
            _slots = result.Slots
                .OrderByDescending(slot => _configuration.GetTimestamp(slot.State))
                .ToList();
            if (result.DamagedFiles.Count > 0)
            {
                damagedWarning = $"Не удалось прочитать файлы сохранений ({result.DamagedFiles.Count}): " +
                                 string.Join(", ", result.DamagedFiles.Take(3).Select(Path.GetFileName)) +
                                 (result.DamagedFiles.Count > 3 ? ", …" : string.Empty);
            }
            PopulateEntries(selectName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(_window, ex.Message, "Ошибка загрузки сохранений", MessageBoxButton.OK, MessageBoxImage.Error);
            _slots = [];
            PopulateEntries();
        }

        if (damagedWarning is not null && !_view.IsPointsOfInterestMode) _view.SetStatus(damagedWarning);
    }

    private void PopulateEntries(string? selectName = null)
    {
        _entries = _view.IsPointsOfInterestMode
            ? _configuration.PointsOfInterest.OrderBy(_configuration.GetName).Select(state =>
                new SaveManagerEntry<TState>(state, _configuration.GetName(state), null,
                    _configuration.Store.GetPointOfInterestPreviewPath(_configuration.GetName(state)))).ToList()
            : _slots.Select(slot => new SaveManagerEntry<TState>(slot.State,
                $"{_configuration.GetName(slot.State)} ({_configuration.GetTimestamp(slot.State):yyyy-MM-dd HH:mm:ss})",
                slot, slot.PreviewPath)).ToList();

        _view.SetItems(_entries);
        _view.SelectedItem = selectName is null
            ? _entries.FirstOrDefault()
            : _entries.FirstOrDefault(entry => _configuration.GetName(entry.State).Equals(selectName, StringComparison.OrdinalIgnoreCase));

        if (_entries.Count == 0) ClearSelection();
        UpdateButtonStates();
    }

    private async void View_OnSelectionChanged(object? sender, EventArgs e)
    {
        bool wasRendering = _isRendering;
        CancelPreview();
        if (SelectedEntry is not { } entry)
        {
            ClearSelection();
            UpdateButtonStates();
            return;
        }

        _view.SaveName = _configuration.GetName(entry.State);
        _view.SetDetails(_configuration.GetDetails(entry.State));
        _view.SetStatus(wasRendering ? "Рендер отменён при смене сохранения." : string.Empty);
        UpdateButtonStates();

        if (TryLoadCachedPreview(entry, out BitmapSource? cached))
        {
            _view.SetPreview(cached);
            return;
        }

        if (entry.IsPointOfInterest)
        {
            _view.SetPreview(null, "Рендер превью...");
            await RenderSelectedPreviewAsync();
            return;
        }

        _view.SetPreview(null, "Превью отсутствует. Нажмите «Рендер превью».");
    }

    private void View_OnItemDoubleClicked(object? sender, EventArgs e) => LoadSelected();

    private void View_OnSaveRequested(object? sender, EventArgs e)
    {
        if (_view.IsPointsOfInterestMode) return;
        string name = _view.SaveName.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(_window, "Введите имя сохранения.", "Сохранение", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int existingIndex = _slots.FindIndex(slot =>
            _configuration.GetName(slot.State).Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0 && MessageBox.Show(_window,
                $"Сохранение с именем «{name}» уже существует. Перезаписать?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            CancelPreview();
            UpdateButtonStates();
            TState state = _configuration.CaptureState(name);
            BitmapSource? snapshot = null;
            string? captureError = null;
            try
            {
                snapshot = _configuration.CapturePreview(_configuration.PreviewWidth, _configuration.PreviewHeight);
            }
            catch (Exception ex)
            {
                captureError = ex.Message;
            }

            // При перезаписи прежние JSON и превью уходят в Корзину внутри Save.
            SaveSlot<TState> slot = _configuration.Store.Save(state, existingIndex >= 0 ? _slots[existingIndex] : null);
            bool previewSaved = snapshot is not null && SaveCachedPreview(slot.PreviewPath, snapshot);
            // Без свежего кадра под тем же именем не должна остаться чужая картинка.
            if (!previewSaved) RecycleBin.TrySend(slot.PreviewPath);
            RefreshStates(name);
            if (snapshot is not null) _view.SetPreview(snapshot);
            _view.SetStatus(previewSaved ? "Сохранено с текущим кадром."
                : captureError is not null ? $"Сохранено без превью: {captureError}"
                : snapshot is null ? "Сохранено без превью: на полотне нет кадра."
                : "Состояние сохранено, но PNG-превью записать не удалось.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(_window, ex.Message, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void View_OnDeleteRequested(object? sender, EventArgs e)
    {
        if (SelectedEntry is not { Slot: { } slot } entry) return;
        string name = _configuration.GetName(entry.State);
        if (MessageBox.Show(_window, $"Удалить сохранение «{name}»?\n\nФайл сохранения и его превью будут перемещены в Корзину.",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            CancelPreview();
            UpdateButtonStates();
            _configuration.Store.Delete(slot);
            RefreshStates();
            _view.SetStatus($"Сохранение «{name}» перемещено в Корзину.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(_window, ex.Message, "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void View_OnLoadRequested(object? sender, EventArgs e) => LoadSelected();

    private void LoadSelected()
    {
        if (SelectedEntry is not { } entry) return;
        try
        {
            _configuration.LoadState(entry.State);
            _window.DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(_window, ex.Message, "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void View_OnRenderPreviewRequested(object? sender, EventArgs e) =>
        await RenderSelectedPreviewAsync();

    private void View_OnCancelPreviewRequested(object? sender, EventArgs e)
    {
        if (_previewCts is not { } cts || cts.IsCancellationRequested) return;
        cts.Cancel();
        _view.SetCancelling();
        _view.SetStatus("Отмена рендера...");
    }

    private void View_OnPointsOfInterestModeChanged(object? sender, EventArgs e)
    {
        PopulateEntries();
    }

    private void View_OnCloseRequested(object? sender, EventArgs e) => _window.Close();

    private void View_OnCloudRequested(object? sender, EventArgs e)
    {
        CancelPreview();
        Views.CloudSaveManagerWindow.Open(_window, _configuration.Store.Category);
        RefreshStates();
    }

    private async Task RenderSelectedPreviewAsync()
    {
        if (_disposed || _isRendering || SelectedEntry is not { } entry) return;

        CancelPreview();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        _isRendering = true;
        _view.SetBusy(true);
        _view.SetStatus("Рендер превью...");
        UpdateButtonStates();
        var stopwatch = Stopwatch.StartNew();
        int? percent = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            if (ReferenceEquals(_previewCts, cts)) _view.SetRenderProgress(percent, stopwatch.Elapsed);
        };
        var progress = new Progress<int>(value =>
        {
            if (!ReferenceEquals(_previewCts, cts) || cts.IsCancellationRequested) return;
            percent = Math.Max(percent ?? 0, Math.Clamp(value, 0, 100));
            _view.SetRenderProgress(percent, stopwatch.Elapsed);
        });
        timer.Start();

        try
        {
            BitmapSource preview = await _configuration.RenderPreviewAsync(
                entry.State, _configuration.PreviewWidth, _configuration.PreviewHeight, cts.Token, progress);
            cts.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_previewCts, cts)) return;

            if (!preview.IsFrozen && preview.CanFreeze) preview.Freeze();
            _view.SetPreview(preview);
            bool cacheSaved = SaveCachedPreview(entry.PreviewPath, preview);
            _view.SetStatus(cacheSaved
                ? $"Превью обновлено за {stopwatch.Elapsed.TotalSeconds:F1} сек."
                : "Превью показано, но PNG записать не удалось.");
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_previewCts, cts)) _view.SetStatus("Рендер отменён. Превью не изменено.");
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_previewCts, cts))
            {
                _view.SetStatus($"Ошибка рендера превью: {ex.Message}. Прежнее превью сохранено.");
            }
        }
        finally
        {
            timer.Stop();
            stopwatch.Stop();
            if (ReferenceEquals(_previewCts, cts))
            {
                _previewCts = null;
                _isRendering = false;
                _view.SetBusy(false);
                UpdateButtonStates();
            }
            cts.Dispose();
        }
    }

    private SaveManagerEntry<TState>? SelectedEntry => _view.SelectedItem as SaveManagerEntry<TState>;

    private void UpdateButtonStates()
    {
        bool hasSelection = SelectedEntry is not null;
        _view.SetButtonStates(hasSelection, !_view.IsPointsOfInterestMode, _isRendering);
    }

    private void ClearSelection()
    {
        _view.SaveName = string.Empty;
        _view.SetPreview(null);
        _view.SetDetails(string.Empty);
        _view.SetStatus(string.Empty);
    }

    private void CancelPreview()
    {
        CancellationTokenSource? cts = _previewCts;
        _previewCts = null;
        if (cts is null) return;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { }
        _isRendering = false;
        _view.SetBusy(false);
    }

    private static bool TryLoadCachedPreview(SaveManagerEntry<TState> entry, out BitmapSource? preview)
    {
        preview = null;
        string path = entry.PreviewPath;
        if (!File.Exists(path)) return false;

        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            preview = image;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Записывает PNG; прежнее превью по этому пути уходит в Корзину, а не стирается.</summary>
    private static bool SaveCachedPreview(string path, BitmapSource preview)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is null) return false;

        try
        {
            Directory.CreateDirectory(directory);
            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(preview));
                using (FileStream stream = File.Create(temporaryPath)) encoder.Save(stream);
                RecycleBin.ReplaceWith(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPreview();
        _view.SelectionChanged -= View_OnSelectionChanged;
        _view.ItemDoubleClicked -= View_OnItemDoubleClicked;
        _view.SaveRequested -= View_OnSaveRequested;
        _view.DeleteRequested -= View_OnDeleteRequested;
        _view.LoadRequested -= View_OnLoadRequested;
        _view.RenderPreviewRequested -= View_OnRenderPreviewRequested;
        _view.CancelPreviewRequested -= View_OnCancelPreviewRequested;
        _view.PointsOfInterestModeChanged -= View_OnPointsOfInterestModeChanged;
        _view.CloseRequested -= View_OnCloseRequested;
        _view.CloudRequested -= View_OnCloudRequested;
    }
}
