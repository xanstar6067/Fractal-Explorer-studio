using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using FractalExplorer.Resources;
using FractalExplorer.Utilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;

using FractalExplorer.Utilities.Theme;
using System.Drawing.Drawing2D;
namespace FractalExplorer.Forms
{
    /// <summary>
    /// Представляет диалоговое окно для сохранения и загрузки состояний фракталов.
    /// Предоставляет функциональность для управления пользовательскими сохранениями и предустановками,
    /// включая асинхронный, мозаичный рендер предварительного просмотра с прогрессивным кэшированием.
    /// </summary>
    public partial class SaveLoadDialogForm : Form
    {
        private readonly ISaveLoadCapableFractal _ownerFractalForm;
        private List<FractalSaveStateBase> _displayedItems;

        // --- Поля для мозаичного рендера ---
        private readonly RenderVisualizerComponent _renderVisualizer;
        private CancellationTokenSource _previewRenderCts;
        private Bitmap _previewBitmap;
        private Bitmap _currentRenderingBitmap;
        private readonly object _bitmapLock = new object();
        private const int TILE_SIZE = 16;
        private bool _isRenderingPreview = false;
        private bool _isManualPreviewRendering = false;
        private const string PreviewScreenshotsFolderName = "SavePrevData";

        // --- Поля для прогрессивного кэширования превью ---

        /// <summary>
        /// Кешированный битмап полного превью. Заполняется по мере рендеринга плиток.
        /// Является статическим для сохранения между открытиями диалогового окна.
        /// </summary>
        private static Bitmap _cachedFullPreviewBitmap;

        /// <summary>
        /// Уникальный идентификатор состояния, для которого был создан текущий кеш.
        /// </summary>
        private static string _cachedPreviewStateIdentifier;

        /// <summary>
        /// Набор для отслеживания уже отрендеренных плиток в кеше, чтобы избежать повторной работы.
        /// Использует Point(X, Y) плитки в качестве ключа.
        /// </summary>
        private static HashSet<Point> _renderedTilesCache;

        /// <summary>
        /// Поколение кеш-сессии превью. Увеличивается при полной пересборке кеша,
        /// чтобы отбрасывать устаревшие результаты асинхронного рендера.
        /// </summary>
        private static long _previewCacheGeneration;

        /// <summary>
        /// Объект для синхронизации доступа к статическим полям кеша из разных потоков.
        /// </summary>
        private static readonly object _previewCacheLock = new object();
        private const float SavedPreviewOverscanFactor = 1.06f;
        private const float SavedPreviewDownscaleFactor = 1.08f;

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="SaveLoadDialogForm"/>.
        /// </summary>
        /// <param name="ownerFractalForm">Экземпляр формы фрактала, поддерживающий сохранение и загрузку.</param>
        /// <exception cref="ArgumentNullException">Вызывается, если <paramref name="ownerFractalForm"/> равен null.</exception>
        public SaveLoadDialogForm(ISaveLoadCapableFractal ownerFractalForm)
        {
            InitializeComponent();
            ThemeManager.RegisterForm(this);
            _ownerFractalForm = ownerFractalForm ?? throw new ArgumentNullException(nameof(ownerFractalForm));
            this.Text = $"Сохранение/Загрузка: {_ownerFractalForm.FractalTypeIdentifier}";

            _renderVisualizer = new RenderVisualizerComponent(TILE_SIZE);
            _renderVisualizer.NeedsRedraw += () =>
            {
                if (pictureBoxPreview.IsHandleCreated)
                {
                    pictureBoxPreview.Invalidate();
                }
            };
            pictureBoxPreview.Paint += PictureBoxPreview_Paint;

            var presetsCheckBox = this.Controls.Find("cbPresets", true).FirstOrDefault() as CheckBox ?? this.Controls.Find("checkBoxShowPresets", true).FirstOrDefault() as CheckBox;
            if (presetsCheckBox != null)
            {
                presetsCheckBox.CheckedChanged += new EventHandler(this.cbPresets_CheckedChanged);
            }
        }

        /// <summary>
        /// Обрабатывает событие загрузки формы. Инициализирует список сохранений.
        /// </summary>
        private void SaveLoadDialogForm_Load(object sender, EventArgs e)
        {
            PopulateList(false);
            UpdateButtonsState();
            SetRenderStatus(string.Empty);
        }

        /// <summary>
        /// Заполняет список в ListBox сохранениями или предустановками.
        /// </summary>
        /// <param name="showPresets">Если true, отображаются предустановки; иначе — пользовательские сохранения.</param>
        private void PopulateList(bool showPresets)
        {
            CancelAndDisposePreviewCts(); // Отменяем текущий рендер, так как список обновляется.

            if (showPresets)
            {
                _displayedItems = PresetManager.GetPresetsFor(_ownerFractalForm.FractalTypeIdentifier).OrderBy(p => p.SaveName).ToList();
            }
            else
            {
                _displayedItems = _ownerFractalForm.LoadAllSavesForThisType().OrderByDescending(s => s.Timestamp).ToList();
            }

            listBoxSaves.Items.Clear();
            if (_displayedItems != null)
            {
                foreach (var item in _displayedItems)
                {
                    string displayText = showPresets ? item.SaveName : $"{item.SaveName} ({item.Timestamp:yyyy-MM-dd HH:mm:ss})";
                    listBoxSaves.Items.Add(displayText);
                }
            }

            if (listBoxSaves.Items.Count > 0)
            {
                listBoxSaves.SelectedIndex = 0;
            }
            else
            {
                ClearPreview();
                textBoxSaveName.Text = "";
            }
        }

        /// <summary>
        /// Обрабатывает изменение выбранного элемента в списке. Запускает рендер нового превью.
        /// </summary>
        private void listBoxSaves_SelectedIndexChanged(object sender, EventArgs e)
        {
            CancelAndDisposePreviewCts();
            SetRenderStatus(string.Empty);

            if (listBoxSaves.SelectedIndex >= 0 && _displayedItems != null && listBoxSaves.SelectedIndex < _displayedItems.Count)
            {
                var selectedState = _displayedItems[listBoxSaves.SelectedIndex];
                textBoxSaveName.Text = selectedState.SaveName;
                if (ShouldUseRenderedPreview())
                {
                    StartTiledPreviewRender(selectedState);
                }
                else
                {
                    LoadScreenshotPreview(selectedState);
                }
            }
            else
            {
                ClearPreview();
            }
            UpdateButtonsState();
        }

        /// <summary>
        /// Запускает асинхронный мозаичный рендер превью для указанного состояния фрактала.
        /// Управляет жизненным циклом рендеринга, включая отмену, обработку ошибок и обновление UI.
        /// </summary>
        /// <param name="state">Состояние фрактала для рендера превью.</param>
        private async void StartTiledPreviewRender(FractalSaveStateBase state)
        {
            if (state == null || pictureBoxPreview.Width <= 0 || pictureBoxPreview.Height <= 0)
            {
                ClearPreview();
                return;
            }

            _isRenderingPreview = true;
            CancelAndDisposePreviewCts();
            _previewRenderCts = new CancellationTokenSource();
            var token = _previewRenderCts.Token;

            try
            {
                using Bitmap renderedPreview = await RenderPreviewBitmapAsync(state, token);

                token.ThrowIfCancellationRequested();
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap != null)
                    {
                        _previewBitmap?.Dispose();
                        _previewBitmap = (Bitmap)renderedPreview.Clone();
                        _currentRenderingBitmap.Dispose();
                        _currentRenderingBitmap = null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Отмена — это штатное поведение, игнорируем исключение.
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap != null)
                    {
                        _currentRenderingBitmap?.Dispose();
                        _currentRenderingBitmap = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка мозаичного рендера превью: {ex.Message}");
            }
            finally
            {
                _isRenderingPreview = false;
                _renderVisualizer.NotifyRenderSessionComplete();
                if (pictureBoxPreview.IsHandleCreated)
                {
                    pictureBoxPreview.Invalidate();
                }
            }
        }

        /// <summary>
        /// Универсальный рендер полноразмерного битмапа превью по выбранному состоянию.
        /// </summary>
        private async Task<Bitmap> RenderPreviewBitmapAsync(
            FractalSaveStateBase state,
            CancellationToken token)
        {
            int width = pictureBoxPreview.Width;
            int height = pictureBoxPreview.Height;
            if (state == null || width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Невозможно запустить рендер превью: некорректные входные параметры.");
            }

            _renderVisualizer.NotifyRenderSessionStart();
            var newRenderingBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            lock (_bitmapLock)
            {
                _currentRenderingBitmap?.Dispose();
                _currentRenderingBitmap = newRenderingBitmap;
                _previewBitmap?.Dispose();
                _previewBitmap = null;
            }
            if (pictureBoxPreview.IsHandleCreated)
            {
                pictureBoxPreview.Invalidate();
            }

            var tiles = GenerateTiles(width, height);
            var dispatcher = new TileRenderDispatcher(tiles, Environment.ProcessorCount, RenderPatternSettings.SelectedPattern);
            await dispatcher.RenderAsync(async (tile, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                _renderVisualizer.NotifyTileRenderStart(tile.Bounds);

                byte[] tileBuffer = await GetOrRenderPreviewTileAsync(state, tile, width, height, TILE_SIZE, ct);
                ct.ThrowIfCancellationRequested();

                lock (_bitmapLock)
                {
                    if (ct.IsCancellationRequested || _currentRenderingBitmap != newRenderingBitmap)
                    {
                        return;
                    }

                    BitmapData bmpData = newRenderingBitmap.LockBits(tile.Bounds, ImageLockMode.WriteOnly, newRenderingBitmap.PixelFormat);
                    try
                    {
                        int tileRowWidthInBytes = tile.Bounds.Width * 4;
                        for (int y = 0; y < tile.Bounds.Height; y++)
                        {
                            IntPtr currentDestPtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                            int sourceOffset = y * tileRowWidthInBytes;
                            Marshal.Copy(tileBuffer, sourceOffset, currentDestPtr, tileRowWidthInBytes);
                        }
                    }
                    finally
                    {
                        newRenderingBitmap.UnlockBits(bmpData);
                    }
                }
                _renderVisualizer.NotifyTileRenderComplete(tile.Bounds);
            }, token);

            token.ThrowIfCancellationRequested();
            return newRenderingBitmap;
        }

        /// <summary>
        /// Асинхронно получает плитку для превью из кеша или рендерит её, если в кеше её нет.
        /// Использует статический прогрессивный кеш для ускорения повторного отображения.
        /// </summary>
        /// <param name="state">Состояние фрактала для рендеринга.</param>
        /// <param name="tile">Информация о запрашиваемой плитке.</param>
        /// <param name="totalWidth">Общая ширина изображения превью.</param>
        /// <param name="totalHeight">Общая высота изображения превью.</param>
        /// <param name="tileSize">Размер одной плитки.</param>
        /// <returns>Массив байтов с пиксельными данными для запрошенной плитки (формат 32bpp ARGB).</returns>
        private async Task<byte[]> GetOrRenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize, CancellationToken cancellationToken)
        {
            string currentStateIdentifier = $"{state.FractalType}_{state.SaveName}_{state.Timestamp.Ticks}";
            var tileCoord = new Point(tile.Bounds.X, tile.Bounds.Y);
            long cacheGeneration;

            lock (_previewCacheLock)
            {
                bool isSameSession = _cachedPreviewStateIdentifier == currentStateIdentifier;
                bool hasValidBitmap = _cachedFullPreviewBitmap != null
                                      && _cachedFullPreviewBitmap.Width == totalWidth
                                      && _cachedFullPreviewBitmap.Height == totalHeight;

                if (!isSameSession || !hasValidBitmap || _renderedTilesCache == null)
                {
                    _cachedFullPreviewBitmap?.Dispose();
                    _cachedFullPreviewBitmap = new Bitmap(totalWidth, totalHeight, PixelFormat.Format32bppArgb);
                    _renderedTilesCache = new HashSet<Point>();
                    _cachedPreviewStateIdentifier = currentStateIdentifier;
                    _previewCacheGeneration++;
                }

                cacheGeneration = _previewCacheGeneration;
                if (_renderedTilesCache.Contains(tileCoord))
                {
                    var cachedTileBuffer = new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
                    BitmapData bmpData = _cachedFullPreviewBitmap.LockBits(tile.Bounds, ImageLockMode.ReadOnly, _cachedFullPreviewBitmap.PixelFormat);
                    try
                    {
                        int tileRowBytes = tile.Bounds.Width * 4;
                        for (int y = 0; y < tile.Bounds.Height; y++)
                        {
                            IntPtr sourcePtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                            Marshal.Copy(sourcePtr, cachedTileBuffer, y * tileRowBytes, tileRowBytes);
                        }
                    }
                    finally
                    {
                        _cachedFullPreviewBitmap.UnlockBits(bmpData);
                    }
                    return cachedTileBuffer;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            byte[] renderedTileBuffer = await _ownerFractalForm.RenderPreviewTileAsync(state, tile, totalWidth, totalHeight, tileSize);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_previewCacheLock)
            {
                bool cacheSessionStillValid = _cachedPreviewStateIdentifier == currentStateIdentifier
                                              && _previewCacheGeneration == cacheGeneration
                                              && _cachedFullPreviewBitmap != null
                                              && _cachedFullPreviewBitmap.Width == totalWidth
                                              && _cachedFullPreviewBitmap.Height == totalHeight
                                              && _renderedTilesCache != null;
                if (!cacheSessionStillValid)
                {
                    return renderedTileBuffer;
                }

                if (!_renderedTilesCache.Contains(tileCoord))
                {
                    BitmapData bmpData = _cachedFullPreviewBitmap.LockBits(tile.Bounds, ImageLockMode.WriteOnly, _cachedFullPreviewBitmap.PixelFormat);
                    try
                    {
                        int tileRowBytes = tile.Bounds.Width * 4;
                        for (int y = 0; y < tile.Bounds.Height; y++)
                        {
                            IntPtr destPtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                            Marshal.Copy(renderedTileBuffer, y * tileRowBytes, destPtr, tileRowBytes);
                        }
                    }
                    finally
                    {
                        _cachedFullPreviewBitmap.UnlockBits(bmpData);
                    }
                    _renderedTilesCache.Add(tileCoord);
                }
            }

            return renderedTileBuffer;
        }

        /// <summary>
        /// Обрабатывает событие Paint для PictureBox. Отображает текущее превью и/или процесс рендеринга.
        /// </summary>
        private void PictureBoxPreview_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                e.Graphics.Clear(Color.Black);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Rectangle clientRect = pictureBoxPreview.ClientRectangle;
                RectangleF? renderingBitmapRect = null;

                lock (_bitmapLock)
                {
                    if (_previewBitmap != null)
                    {
                        RectangleF previewRect = CalculateAspectFitRectangle(clientRect, _previewBitmap.Size);
                        e.Graphics.DrawImage(_previewBitmap, previewRect);
                    }
                    if (_currentRenderingBitmap != null)
                    {
                        renderingBitmapRect = CalculateAspectFitRectangle(clientRect, _currentRenderingBitmap.Size);
                        e.Graphics.DrawImage(_currentRenderingBitmap, renderingBitmapRect.Value);
                    }
                }
                if (_renderVisualizer != null && _isRenderingPreview)
                {
                    RectangleF? visualizationRect = renderingBitmapRect;
                    if (!visualizationRect.HasValue)
                    {
                        lock (_bitmapLock)
                        {
                            if (_previewBitmap != null)
                            {
                                visualizationRect = CalculateAspectFitRectangle(clientRect, _previewBitmap.Size);
                            }
                        }
                    }

                    if (visualizationRect.HasValue)
                    {
                        GraphicsState state = e.Graphics.Save();
                        e.Graphics.SetClip(visualizationRect.Value);
                        e.Graphics.TranslateTransform(visualizationRect.Value.X, visualizationRect.Value.Y);
                        float scaleX = visualizationRect.Value.Width / Math.Max(1f, _currentRenderingBitmap?.Width ?? _previewBitmap?.Width ?? 1f);
                        float scaleY = visualizationRect.Value.Height / Math.Max(1f, _currentRenderingBitmap?.Height ?? _previewBitmap?.Height ?? 1f);
                        e.Graphics.ScaleTransform(scaleX, scaleY);
                        _renderVisualizer.DrawVisualization(e.Graphics);
                        e.Graphics.Restore(state);
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Ошибка отрисовки превью (InvalidOperation): {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка отрисовки превью: {ex.Message}");
            }
        }

        /// <summary>
        /// Вычисляет прямоугольник "вписывания" изображения в целевую область с сохранением пропорций и центрированием.
        /// </summary>
        private static RectangleF CalculateAspectFitRectangle(Rectangle targetBounds, Size imageSize)
        {
            if (targetBounds.Width <= 0 || targetBounds.Height <= 0 || imageSize.Width <= 0 || imageSize.Height <= 0)
            {
                return RectangleF.Empty;
            }

            float scale = Math.Min((float)targetBounds.Width / imageSize.Width, (float)targetBounds.Height / imageSize.Height);
            float drawWidth = imageSize.Width * scale;
            float drawHeight = imageSize.Height * scale;
            float drawX = targetBounds.X + (targetBounds.Width - drawWidth) / 2f;
            float drawY = targetBounds.Y + (targetBounds.Height - drawHeight) / 2f;

            return new RectangleF(drawX, drawY, drawWidth, drawHeight);
        }

        /// <summary>
        /// Очищает текущее изображение превью и отменяет активный процесс рендеринга.
        /// </summary>
        private void ClearPreview()
        {
            CancelAndDisposePreviewCts();
            lock (_bitmapLock)
            {
                _previewBitmap?.Dispose();
                _previewBitmap = null;
                _currentRenderingBitmap?.Dispose();
                _currentRenderingBitmap = null;
            }
            if (pictureBoxPreview.IsHandleCreated)
            {
                pictureBoxPreview.Invalidate();
            }
        }

        /// <summary>
        /// Генерирует и сортирует список плиток для мозаичного рендеринга.
        /// Сортировка по удаленности от центра создает более естественный эффект появления изображения.
        /// </summary>
        /// <returns>Список объектов <see cref="TileInfo"/>, отсортированный от центра к краям.</returns>
        private List<TileInfo> GenerateTiles(int width, int height)
        {
            var tiles = new List<TileInfo>();
            Point center = new Point(width / 2, height / 2);
            for (int y = 0; y < height; y += TILE_SIZE)
            {
                for (int x = 0; x < width; x += TILE_SIZE)
                {
                    int tileWidth = Math.Min(TILE_SIZE, width - x);
                    int tileHeight = Math.Min(TILE_SIZE, height - y);
                    tiles.Add(new TileInfo(x, y, tileWidth, tileHeight));
                }
            }
            return tiles.OrderBy(t => Math.Pow(t.Center.X - center.X, 2) + Math.Pow(t.Center.Y - center.Y, 2)).ToList();
        }

        /// <summary>
        /// Обрабатывает событие закрытия формы, освобождая ресурсы.
        /// </summary>
        private void SaveLoadDialogForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            CancelAndDisposePreviewCts();
            ResetStaticPreviewCache();
            ClearPreview();
            _renderVisualizer?.Dispose();
        }

        /// <summary>
        /// Сбрасывает статический кеш превью с инвалидированием поколения.
        /// Метод безопасен при повторных вызовах.
        /// </summary>
        private void ResetStaticPreviewCache()
        {
            lock (_previewCacheLock)
            {
                bool cacheAlreadyReset = _cachedFullPreviewBitmap == null
                                         && _renderedTilesCache == null
                                         && _cachedPreviewStateIdentifier == null;

                if (cacheAlreadyReset)
                {
                    return;
                }

                _cachedFullPreviewBitmap?.Dispose();
                _cachedFullPreviewBitmap = null;
                _renderedTilesCache?.Clear();
                _renderedTilesCache = null;
                _cachedPreviewStateIdentifier = null;
                _previewCacheGeneration++;
            }
        }

        /// <summary>
        /// Отменяет текущий token source рендера превью, освобождает его ресурсы и обнуляет ссылку.
        /// </summary>
        private void CancelAndDisposePreviewCts()
        {
            if (_previewRenderCts == null)
            {
                return;
            }

            try
            {
                _previewRenderCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS уже освобожден в другом участке кода.
            }

            _previewRenderCts.Dispose();
            _previewRenderCts = null;
        }

        /// <summary>
        /// Обрабатывает нажатие кнопки "Загрузить".
        /// </summary>
        private void btnLoad_Click(object sender, EventArgs e)
        {
            TryLoadSelectedState();
        }

        /// <summary>
        /// Обрабатывает двойной клик по элементу списка сохранений/точек интереса.
        /// </summary>
        private void listBoxSaves_DoubleClick(object sender, EventArgs e)
        {
            TryLoadSelectedState();
        }

        /// <summary>
        /// Загружает выбранное состояние (пользовательское сохранение или точку интереса) и закрывает диалог.
        /// </summary>
        private void TryLoadSelectedState()
        {
            if (listBoxSaves.SelectedIndex < 0 || _displayedItems == null || listBoxSaves.SelectedIndex >= _displayedItems.Count)
            {
                return;
            }

            _ownerFractalForm.LoadState(_displayedItems[listBoxSaves.SelectedIndex]);
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// Обрабатывает нажатие кнопки "Сохранить как новую".
        /// </summary>
        private void btnSaveAsNew_Click(object sender, EventArgs e)
        {
            var presetsCheckBox = this.Controls.Find("cbPresets", true).FirstOrDefault() as CheckBox ?? this.Controls.Find("checkBoxShowPresets", true).FirstOrDefault() as CheckBox;
            if (presetsCheckBox.Checked) return;

            string saveName = textBoxSaveName.Text.Trim();
            if (string.IsNullOrWhiteSpace(saveName))
            {
                MessageBox.Show("Пожалуйста, введите имя для сохранения.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var userSaves = _ownerFractalForm.LoadAllSavesForThisType();
            var existingSave = userSaves.FirstOrDefault(s => s.SaveName.Equals(saveName, StringComparison.OrdinalIgnoreCase));

            if (existingSave != null)
            {
                if (MessageBox.Show($"Сохранение с именем '{saveName}' уже существует. Перезаписать?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    DeleteScreenshotForState(existingSave);
                    userSaves.Remove(existingSave);
                }
                else
                {
                    return;
                }
            }

            var newState = _ownerFractalForm.GetCurrentStateForSave(saveName);
            userSaves.Add(newState);
            _ownerFractalForm.SaveAllSavesForThisType(userSaves);
            SaveScreenshotForStateAsync(newState);

            PopulateList(false);
            int newIndex = _displayedItems.FindIndex(s => s.SaveName == saveName && s.Timestamp == newState.Timestamp);
            if (newIndex != -1)
            {
                listBoxSaves.SelectedIndex = newIndex;
            }
        }

        /// <summary>
        /// Обрабатывает нажатие кнопки "Удалить".
        /// </summary>
        private void btnDelete_Click(object sender, EventArgs e)
        {
            var presetsCheckBox = this.Controls.Find("cbPresets", true).FirstOrDefault() as CheckBox ?? this.Controls.Find("checkBoxShowPresets", true).FirstOrDefault() as CheckBox;
            if (presetsCheckBox.Checked) return;

            if (listBoxSaves.SelectedIndex >= 0 && _displayedItems != null && listBoxSaves.SelectedIndex < _displayedItems.Count)
            {
                var stateToDelete = _displayedItems[listBoxSaves.SelectedIndex];
                if (MessageBox.Show($"Вы уверены, что хотите удалить сохранение '{stateToDelete.SaveName}'?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    var userSaves = _ownerFractalForm.LoadAllSavesForThisType();
                    var itemToRemove = userSaves.FirstOrDefault(s => s.SaveName == stateToDelete.SaveName && s.Timestamp == stateToDelete.Timestamp);
                    if (itemToRemove != null)
                    {
                        DeleteScreenshotForState(itemToRemove);
                        userSaves.Remove(itemToRemove);
                        _ownerFractalForm.SaveAllSavesForThisType(userSaves);
                        PopulateList(false);
                    }
                }
            }
        }

        /// <summary>
        /// Обновляет состояние доступности кнопок в зависимости от контекста.
        /// </summary>
        private void UpdateButtonsState()
        {
            var presetsCheckBox = this.Controls.Find("cbPresets", true).FirstOrDefault() as CheckBox ?? this.Controls.Find("checkBoxShowPresets", true).FirstOrDefault() as CheckBox;
            bool itemSelected = listBoxSaves.SelectedIndex != -1;
            bool presetsMode = presetsCheckBox.Checked;

            btnLoad.Enabled = itemSelected;
            btnDelete.Enabled = itemSelected && !presetsMode;
            btnSaveAsNew.Enabled = !presetsMode;
            textBoxSaveName.Enabled = !presetsMode;
            btnRenderPreview.Enabled = itemSelected && !presetsMode && !_isManualPreviewRendering;
        }

        /// <summary>
        /// Обрабатывает изменение состояния чекбокса "Показать пресеты".
        /// </summary>
        private void cbPresets_CheckedChanged(object sender, EventArgs e)
        {
            if (sender is CheckBox presetsCheckBox)
            {
                PopulateList(presetsCheckBox.Checked);
                UpdateButtonsState();
            }
        }

        /// <summary>
        /// Обрабатывает нажатие кнопки "Отмена".
        /// </summary>
        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        /// <summary>
        /// Явный рендер превью выбранного сохранения и замена старого файла превью.
        /// </summary>
        private async void btnRenderPreview_Click(object sender, EventArgs e)
        {
            var presetsCheckBox = this.Controls.Find("cbPresets", true).FirstOrDefault() as CheckBox
                                 ?? this.Controls.Find("checkBoxShowPresets", true).FirstOrDefault() as CheckBox;
            bool presetsMode = presetsCheckBox?.Checked == true;
            if (presetsMode)
            {
                return;
            }

            if (listBoxSaves.SelectedIndex < 0 || _displayedItems == null || listBoxSaves.SelectedIndex >= _displayedItems.Count)
            {
                return;
            }

            FractalSaveStateBase selectedState = _displayedItems[listBoxSaves.SelectedIndex];
            _isManualPreviewRendering = true;
            _isRenderingPreview = true;
            UpdateButtonsState();
            SetRenderStatus("Рендер превью...");
            CancelAndDisposePreviewCts();
            _previewRenderCts = new CancellationTokenSource();
            var token = _previewRenderCts.Token;

            try
            {
                using Bitmap renderedPreview = await RenderPreviewBitmapAsync(selectedState, token);

                token.ThrowIfCancellationRequested();
                ReplaceScreenshotForState(selectedState, renderedPreview);

                lock (_bitmapLock)
                {
                    _previewBitmap?.Dispose();
                    _previewBitmap = (Bitmap)renderedPreview.Clone();
                    _currentRenderingBitmap?.Dispose();
                    _currentRenderingBitmap = null;
                }

                if (pictureBoxPreview.IsHandleCreated)
                {
                    pictureBoxPreview.Invalidate();
                }

                SetRenderStatus("Превью обновлено.");
            }
            catch (OperationCanceledException)
            {
                SetRenderStatus("Рендер отменен.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка рендера превью по запросу: {ex.Message}");
                SetRenderStatus("Ошибка рендера превью.");
            }
            finally
            {
                _isRenderingPreview = false;
                _renderVisualizer.NotifyRenderSessionComplete();
                _isManualPreviewRendering = false;
                if (pictureBoxPreview.IsHandleCreated)
                {
                    pictureBoxPreview.Invalidate();
                }
                UpdateButtonsState();
            }
        }

        /// <summary>
        /// Определяет, должен ли диалог использовать рендеринг превью.
        /// Для пользовательских сохранений рендеринг отключен, кроме фрактала Серпинского.
        /// </summary>
        private bool ShouldUseRenderedPreview()
        {
            if (string.Equals(_ownerFractalForm.FractalTypeIdentifier, "Serpinsky", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var presetsCheckBox = this.Controls.Find("cbPresets", true).FirstOrDefault() as CheckBox
                                 ?? this.Controls.Find("checkBoxShowPresets", true).FirstOrDefault() as CheckBox;
            return presetsCheckBox?.Checked == true;
        }

        /// <summary>
        /// Загружает превью из файла скриншота для пользовательского сохранения.
        /// </summary>
        private void LoadScreenshotPreview(FractalSaveStateBase state)
        {
            string screenshotPath = GetScreenshotPathForState(state);
            if (!File.Exists(screenshotPath))
            {
                ClearPreview();
                return;
            }

            try
            {
                using (var fs = new FileStream(screenshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var rawImage = Image.FromStream(fs))
                {
                    var loadedBitmap = new Bitmap(rawImage);
                    lock (_bitmapLock)
                    {
                        _previewBitmap?.Dispose();
                        _previewBitmap = loadedBitmap;
                        _currentRenderingBitmap?.Dispose();
                        _currentRenderingBitmap = null;
                    }
                }

                if (pictureBoxPreview.IsHandleCreated)
                {
                    pictureBoxPreview.Invalidate();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка загрузки скриншота превью: {ex.Message}");
                ClearPreview();
            }
        }

        /// <summary>
        /// Асинхронно сохраняет PNG-скриншот превью в папке SavePrevData для конкретного состояния.
        /// Использует уже готовый буфер текущего рендера владельца без повторного расчёта фрактала.
        /// </summary>
        private void SaveScreenshotForStateAsync(FractalSaveStateBase state)
        {
            if (state == null || pictureBoxPreview.Width <= 0 || pictureBoxPreview.Height <= 0)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(GetScreenshotDirectoryForFractal());
                string screenshotPath = GetScreenshotPathForState(state);
                using var preview = TryBuildPreviewFromCurrentOwnerRender();
                if (preview == null)
                {
                    return;
                }

                var bitmapToSave = (Bitmap)preview.Clone();
                _ = Task.Run(() =>
                {
                    try
                    {
                        using (bitmapToSave)
                        {
                            bitmapToSave.Save(screenshotPath, ImageFormat.Png);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка сохранения скриншота превью (фоновая задача): {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения скриншота превью: {ex.Message}");
            }
        }

        /// <summary>
        /// Пытается построить превью из уже готового буфера текущего рендера формы-владельца.
        /// </summary>
        private Bitmap TryBuildPreviewFromCurrentOwnerRender()
        {
            if (_ownerFractalForm is not Form ownerForm)
            {
                return null;
            }

            Bitmap sourceBitmap = null;
            try
            {
                // Важно: сначала пытаемся брать полноразмерный буфер рендера, чтобы превью
                // масштабировалось из немного большего размера в меньший (downscale),
                // а не наоборот из маленького в большой (upscale).
                sourceBitmap = TryGetBitmapFromOwnerField(ownerForm, "canvasBitmap")
                               ?? TryCaptureOwnerCanvasBitmap(ownerForm)
                               ?? TryGetBitmapFromOwnerField(ownerForm, "_previewBitmap");

                if (sourceBitmap == null)
                {
                    return null;
                }

                return CreateCenterCroppedPreview(sourceBitmap, pictureBoxPreview.Width, pictureBoxPreview.Height);
            }
            finally
            {
                sourceBitmap?.Dispose();
            }
        }

        private Bitmap TryGetBitmapFromOwnerField(Form ownerForm, string fieldName)
        {
            var field = ownerForm.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(ownerForm) is Bitmap bitmap)
            {
                return (Bitmap)bitmap.Clone();
            }

            return null;
        }

        private Bitmap TryCaptureOwnerCanvasBitmap(Form ownerForm)
        {
            var canvasControl = ownerForm.Controls.Find("canvas", true).FirstOrDefault()
                               ?? ownerForm.Controls.Find("canvasSerpinsky", true).FirstOrDefault();
            if (canvasControl == null || canvasControl.Width <= 0 || canvasControl.Height <= 0)
            {
                return null;
            }

            var capture = new Bitmap(canvasControl.Width, canvasControl.Height, PixelFormat.Format32bppArgb);
            canvasControl.DrawToBitmap(capture, new Rectangle(Point.Empty, canvasControl.Size));
            return capture;
        }

        /// <summary>
        /// Создаёт превью, вырезая центральную область исходного изображения под нужное соотношение сторон
        /// и масштабируя её до целевого размера.
        /// </summary>
        private Bitmap CreateCenterCroppedPreview(Bitmap source, int targetWidth, int targetHeight)
        {
            if (source == null || targetWidth <= 0 || targetHeight <= 0)
            {
                return null;
            }

            float sourceAspect = source.Width / (float)source.Height;
            float targetAspect = targetWidth / (float)targetHeight;
            Rectangle sourceRect;

            if (sourceAspect > targetAspect)
            {
                int cropWidth = Math.Max(1, (int)Math.Round(source.Height * targetAspect));
                int x = Math.Max(0, (source.Width - cropWidth) / 2);
                sourceRect = new Rectangle(x, 0, Math.Min(cropWidth, source.Width), source.Height);
            }
            else
            {
                int cropHeight = Math.Max(1, (int)Math.Round(source.Width / targetAspect));
                int y = Math.Max(0, (source.Height - cropHeight) / 2);
                sourceRect = new Rectangle(0, y, source.Width, Math.Min(cropHeight, source.Height));
            }

            sourceRect = ExpandSourceRect(sourceRect, source.Size, SavedPreviewOverscanFactor);

            int desiredIntermediateWidth = Math.Max(targetWidth + 1, (int)Math.Round(targetWidth * SavedPreviewDownscaleFactor));
            int desiredIntermediateHeight = Math.Max(targetHeight + 1, (int)Math.Round(targetHeight * SavedPreviewDownscaleFactor));

            // Не допускаем промежуточный upscale относительно источника.
            // Если источник недостаточно большой, используем максимально возможный размер без увеличения.
            int intermediateWidth = Math.Min(sourceRect.Width, desiredIntermediateWidth);
            int intermediateHeight = Math.Min(sourceRect.Height, desiredIntermediateHeight);

            if (intermediateWidth <= 0) intermediateWidth = targetWidth;
            if (intermediateHeight <= 0) intermediateHeight = targetHeight;

            using var intermediate = new Bitmap(intermediateWidth, intermediateHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(intermediate))
            {
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, intermediateWidth, intermediateHeight), sourceRect, GraphicsUnit.Pixel);
            }

            var result = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(intermediate, new Rectangle(0, 0, targetWidth, targetHeight), new Rectangle(0, 0, intermediate.Width, intermediate.Height), GraphicsUnit.Pixel);
            }

            return result;
        }

        private Rectangle ExpandSourceRect(Rectangle rect, Size bounds, float factor)
        {
            if (factor <= 1f)
            {
                return rect;
            }

            int expandedWidth = Math.Min(bounds.Width, Math.Max(rect.Width, (int)Math.Round(rect.Width * factor)));
            int expandedHeight = Math.Min(bounds.Height, Math.Max(rect.Height, (int)Math.Round(rect.Height * factor)));

            int centerX = rect.X + rect.Width / 2;
            int centerY = rect.Y + rect.Height / 2;

            int x = centerX - expandedWidth / 2;
            int y = centerY - expandedHeight / 2;

            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x + expandedWidth > bounds.Width) x = bounds.Width - expandedWidth;
            if (y + expandedHeight > bounds.Height) y = bounds.Height - expandedHeight;

            return new Rectangle(x, y, expandedWidth, expandedHeight);
        }

        /// <summary>
        /// Перемещает скриншот превью, привязанный к указанному сохранению, в корзину.
        /// </summary>
        private void DeleteScreenshotForState(FractalSaveStateBase state)
        {
            if (state == null)
            {
                return;
            }

            string screenshotPath = GetScreenshotPathForState(state);
            try
            {
                if (File.Exists(screenshotPath))
                {
                    FileSystem.DeleteFile(
                        screenshotPath,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.DoNothing);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка перемещения скриншота превью в корзину: {ex.Message}");
            }
        }

        /// <summary>
        /// Заменяет старый файл превью на новый с сохранением пути и имени.
        /// Старый файл перемещается в корзину.
        /// </summary>
        private void ReplaceScreenshotForState(FractalSaveStateBase state, Bitmap newPreview)
        {
            if (state == null || newPreview == null)
            {
                return;
            }

            Directory.CreateDirectory(GetScreenshotDirectoryForFractal());
            string screenshotPath = GetScreenshotPathForState(state);

            if (File.Exists(screenshotPath))
            {
                TryDeletePreviewFile(screenshotPath);
            }
            SaveBitmapToPathSafely(newPreview, screenshotPath);
        }

        private void SetRenderStatus(string text)
        {
            if (labelRenderStatus != null)
            {
                labelRenderStatus.Text = text;
            }
        }

        private void TryDeletePreviewFile(string screenshotPath)
        {
            try
            {
                FileSystem.DeleteFile(
                    screenshotPath,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin,
                    UICancelOption.DoNothing);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось переместить старое превью в корзину, используем прямое удаление: {ex.Message}");
                try
                {
                    if (File.Exists(screenshotPath))
                    {
                        File.Delete(screenshotPath);
                    }
                }
                catch (Exception deleteEx)
                {
                    Console.WriteLine($"Не удалось удалить старое превью: {deleteEx.Message}");
                    throw;
                }
            }
        }

        private void SaveBitmapToPathSafely(Bitmap bitmap, string path)
        {
            string tempFilePath = $"{path}.tmp_{Guid.NewGuid():N}.png";
            try
            {
                bitmap.Save(tempFilePath, ImageFormat.Png);
                File.Copy(tempFilePath, path, true);
            }
            finally
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        /// <summary>
        /// Возвращает полный путь к директории скриншотов конкретного фрактала.
        /// </summary>
        private string GetScreenshotDirectoryForFractal()
        {
            return Path.Combine(Application.StartupPath, "Saves", PreviewScreenshotsFolderName, _ownerFractalForm.FractalTypeIdentifier);
        }

        /// <summary>
        /// Возвращает полный путь к файлу скриншота для указанного состояния.
        /// </summary>
        private string GetScreenshotPathForState(FractalSaveStateBase state)
        {
            string safeSaveName = MakeSafeFileName(state.SaveName);
            string timestampSuffix = state.Timestamp.ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture);
            string fileName = $"{safeSaveName}_{timestampSuffix}.png";
            return Path.Combine(GetScreenshotDirectoryForFractal(), fileName);
        }

        /// <summary>
        /// Удаляет потенциально недопустимые символы из части имени файла.
        /// </summary>
        private static string MakeSafeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Save";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var safeChars = value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
            string safeValue = new string(safeChars).Trim();
            return string.IsNullOrWhiteSpace(safeValue) ? "Save" : safeValue;
        }
    }
}
