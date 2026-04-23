using FractalExplorer.Engines;
using FractalExplorer.Projects;
using FractalExplorer.Utilities;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FractalExplorer.Utilities.RenderUtilities;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalJuliaBurningShipGridForm : Form
    {
        private Bitmap? _canvasBitmap;
        private readonly object _bitmapLock = new();
        private List<GridCellInfo> _cells = new();
        private bool _isRendering;
        private readonly System.Windows.Forms.Timer _autoRenderTimer = new();
        private bool _renderRestartRequested;
        private bool _isColsTrackBarDragging;
        private bool _isRowsTrackBarDragging;
        private bool _isTileSizeTrackBarDragging;
        private bool _suspendAutoRenderScheduling;
        private bool _isClosing;
        private CancellationTokenSource? _renderCts;
        private int _activeRenderVersion;

        public FractalJuliaBurningShipGridForm()
        {
            InitializeComponent();
            ConfigureAutoRender();
            WireAutoRenderHandlers();
            SyncTrackBarsWithNumericControls();
            Shown += (_, _) => ScheduleAutoRender();
            FormClosing += FractalJuliaBurningShipGridForm_FormClosing;
        }

        private async void btnRender_Click(object sender, EventArgs e)
        {
            if (_isRendering)
            {
                return;
            }

            if (nudReMin.Value >= nudReMax.Value || nudImMin.Value >= nudImMax.Value)
            {
                MessageBox.Show(this, "Минимальные границы должны быть меньше максимальных.", "Некорректный диапазон", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await RenderGridAsync();
        }

        private async Task RenderGridAsync()
        {
            _isRendering = true;
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = new CancellationTokenSource();
            var cancellationToken = _renderCts.Token;
            int renderVersion = Interlocked.Increment(ref _activeRenderVersion);

            btnRender.Enabled = false;
            try
            {
                int cols = (int)nudCols.Value;
                int rows = (int)nudRows.Value;
                int tileSize = (int)nudTileSize.Value;

                int width = cols * tileSize;
                int height = rows * tileSize;

                _cells = BuildGridCells(cols, rows, tileSize);
                var tiles = _cells.Select(c => c.Tile).ToList();

                _canvasBitmap?.Dispose();
                _canvasBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                ReplacePreviewImage((Bitmap)_canvasBitmap.Clone());

                progressBar.Minimum = 0;
                progressBar.Maximum = tiles.Count;
                progressBar.Value = 0;
                lblStatus.Text = "Рендер сетки...";

                int renderedCount = 0;
                var dispatcher = new TileRenderDispatcher(tiles, Math.Max(1, Environment.ProcessorCount - 1), RenderPatternSettings.SelectedPattern);

                await dispatcher.RenderAsync(async (tile, ct) =>
                {
                    if (cancellationToken.IsCancellationRequested || _isClosing)
                    {
                        return;
                    }
                    if (renderVersion != _activeRenderVersion)
                    {
                        return;
                    }

                    GridCellInfo? cell = _cells.FirstOrDefault(c => c.Tile.Bounds == tile.Bounds);
                    if (cell == null)
                    {
                        return;
                    }

                    using Bitmap tileBitmap = RenderJuliaThumbnail(tile.Bounds.Width, tile.Bounds.Height, cell.C);
                    WriteTileToCanvas(tile.Bounds, tileBitmap);

                    int current = Interlocked.Increment(ref renderedCount);
                    if (!IsDisposed && IsHandleCreated)
                    {
                        try
                        {
                            BeginInvoke(() =>
                            {
                                if (IsDisposed || _isClosing)
                                {
                                    return;
                                }

                                progressBar.Value = Math.Min(progressBar.Maximum, current);
                                lblStatus.Text = $"Отрисовано {current}/{tiles.Count} клеток";
                                UpdatePreviewImage();
                            });
                        }
                        catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
                        {
                            // Окно уже закрывается/закрыто.
                        }
                    }

                    await Task.CompletedTask;
                }, cancellationToken);

                if (!cancellationToken.IsCancellationRequested && !_isClosing)
                {
                    lblStatus.Text = $"Готово. Размер полотна: {_canvasBitmap.Width}x{_canvasBitmap.Height}px.";
                }
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Рендер отменен.";
            }
            finally
            {
                _isRendering = false;
                if (!IsDisposed)
                {
                    btnRender.Enabled = true;
                }

                if (_renderRestartRequested && !_isClosing)
                {
                    _renderRestartRequested = false;
                    ScheduleAutoRender();
                }
            }
        }

        private void ConfigureAutoRender()
        {
            _autoRenderTimer.Interval = 400;
            _autoRenderTimer.Tick += AutoRenderTimer_Tick;
        }

        private void WireAutoRenderHandlers()
        {
            nudReMin.ValueChanged += ParameterControl_ValueChanged;
            nudReMax.ValueChanged += ParameterControl_ValueChanged;
            nudImMin.ValueChanged += ParameterControl_ValueChanged;
            nudImMax.ValueChanged += ParameterControl_ValueChanged;
            nudCols.ValueChanged += ParameterControl_ValueChanged;
            nudRows.ValueChanged += ParameterControl_ValueChanged;
            nudTileSize.ValueChanged += ParameterControl_ValueChanged;

            trackBarCols.ValueChanged += TrackBarCols_ValueChanged;
            trackBarRows.ValueChanged += TrackBarRows_ValueChanged;
            trackBarTileSize.ValueChanged += TrackBarTileSize_ValueChanged;

            trackBarCols.MouseDown += (_, _) => _isColsTrackBarDragging = true;
            trackBarRows.MouseDown += (_, _) => _isRowsTrackBarDragging = true;
            trackBarTileSize.MouseDown += (_, _) => _isTileSizeTrackBarDragging = true;

            trackBarCols.MouseUp += (_, _) =>
            {
                _isColsTrackBarDragging = false;
                ScheduleAutoRender();
            };
            trackBarRows.MouseUp += (_, _) =>
            {
                _isRowsTrackBarDragging = false;
                ScheduleAutoRender();
            };
            trackBarTileSize.MouseUp += (_, _) =>
            {
                _isTileSizeTrackBarDragging = false;
                ScheduleAutoRender();
            };
        }

        private void ParameterControl_ValueChanged(object? sender, EventArgs e)
        {
            if (_suspendAutoRenderScheduling)
            {
                return;
            }

            SyncTrackBarsWithNumericControls();
            ScheduleAutoRender();
        }

        private void TrackBarCols_ValueChanged(object? sender, EventArgs e)
        {
            if (nudCols.Value != trackBarCols.Value)
            {
                _suspendAutoRenderScheduling = true;
                nudCols.Value = trackBarCols.Value;
                _suspendAutoRenderScheduling = false;
            }

            lblColsValue.Text = trackBarCols.Value.ToString();
            if (!_isColsTrackBarDragging)
            {
                ScheduleAutoRender();
            }
        }

        private void TrackBarRows_ValueChanged(object? sender, EventArgs e)
        {
            if (nudRows.Value != trackBarRows.Value)
            {
                _suspendAutoRenderScheduling = true;
                nudRows.Value = trackBarRows.Value;
                _suspendAutoRenderScheduling = false;
            }

            lblRowsValue.Text = trackBarRows.Value.ToString();
            if (!_isRowsTrackBarDragging)
            {
                ScheduleAutoRender();
            }
        }

        private void TrackBarTileSize_ValueChanged(object? sender, EventArgs e)
        {
            if (nudTileSize.Value != trackBarTileSize.Value)
            {
                _suspendAutoRenderScheduling = true;
                nudTileSize.Value = trackBarTileSize.Value;
                _suspendAutoRenderScheduling = false;
            }

            lblTileSizeValue.Text = $"{trackBarTileSize.Value}px";
            if (!_isTileSizeTrackBarDragging)
            {
                ScheduleAutoRender();
            }
        }

        private void SyncTrackBarsWithNumericControls()
        {
            _suspendAutoRenderScheduling = true;
            if (trackBarCols.Value != (int)nudCols.Value)
            {
                trackBarCols.Value = (int)nudCols.Value;
            }

            if (trackBarRows.Value != (int)nudRows.Value)
            {
                trackBarRows.Value = (int)nudRows.Value;
            }

            if (trackBarTileSize.Value != (int)nudTileSize.Value)
            {
                trackBarTileSize.Value = (int)nudTileSize.Value;
            }
            _suspendAutoRenderScheduling = false;

            lblColsValue.Text = nudCols.Value.ToString();
            lblRowsValue.Text = nudRows.Value.ToString();
            lblTileSizeValue.Text = $"{nudTileSize.Value}px";
        }

        private void ScheduleAutoRender()
        {
            if (_isClosing || IsDisposed)
            {
                return;
            }

            _autoRenderTimer.Stop();
            _autoRenderTimer.Start();
            lblStatus.Text = "Параметры изменены. Ожидание автоперерендера...";
        }

        private async void AutoRenderTimer_Tick(object? sender, EventArgs e)
        {
            _autoRenderTimer.Stop();
            if (_isClosing || IsDisposed)
            {
                return;
            }

            if (_isRendering)
            {
                _renderRestartRequested = true;
                return;
            }

            if (nudReMin.Value >= nudReMax.Value || nudImMin.Value >= nudImMax.Value)
            {
                lblStatus.Text = "Некорректный диапазон: min должно быть меньше max.";
                return;
            }

            await RenderGridAsync();
        }

        private void FractalJuliaBurningShipGridForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            _isClosing = true;
            Interlocked.Increment(ref _activeRenderVersion);
            _autoRenderTimer.Stop();
            _renderCts?.Cancel();
        }

        private List<GridCellInfo> BuildGridCells(int cols, int rows, int tileSize)
        {
            decimal reMin = nudReMin.Value;
            decimal reMax = nudReMax.Value;
            decimal imMin = nudImMin.Value;
            decimal imMax = nudImMax.Value;

            decimal reStep = (reMax - reMin) / cols;
            decimal imStep = (imMax - imMin) / rows;

            var cells = new List<GridCellInfo>(cols * rows);

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    decimal re = reMin + (col + 0.5m) * reStep;
                    decimal im = imMax - (row + 0.5m) * imStep;

                    var tile = new TileInfo(col * tileSize, row * tileSize, tileSize, tileSize);
                    cells.Add(new GridCellInfo(tile, new ComplexDecimal(re, im), row, col));
                }
            }

            return cells;
        }

        private static Bitmap RenderJuliaThumbnail(int width, int height, ComplexDecimal juliaC)
        {
            var engine = new JuliaBurningShipEngine
            {
                MaxIterations = 180,
                MaxColorIterations = 180,
                ThresholdSquared = 4m,
                C = juliaC,
                Scale = 4m,
                CenterX = 0m,
                CenterY = 0m,
                Palette = BuildPalette,
                SmoothPalette = BuildSmoothPalette,
                UseSmoothColoring = true
            };

            return engine.RenderToBitmap(width, height, 1, _ => { });
        }

        private void WriteTileToCanvas(Rectangle tileRect, Bitmap tileBitmap)
        {
            if (_canvasBitmap == null)
            {
                return;
            }

            lock (_bitmapLock)
            {
                BitmapData? tileData = null;
                BitmapData? canvasData = null;
                try
                {
                    tileData = tileBitmap.LockBits(new Rectangle(0, 0, tileBitmap.Width, tileBitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    canvasData = _canvasBitmap.LockBits(tileRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                    int rowBytes = tileRect.Width * 4;
                    byte[] rowBuffer = new byte[rowBytes];

                    for (int y = 0; y < tileRect.Height; y++)
                    {
                        IntPtr src = tileData.Scan0 + (y * tileData.Stride);
                        IntPtr dst = canvasData.Scan0 + (y * canvasData.Stride);
                        Marshal.Copy(src, rowBuffer, 0, rowBytes);
                        Marshal.Copy(rowBuffer, 0, dst, rowBytes);
                    }
                }
                catch (ArgumentException) when (_isClosing || IsDisposed)
                {
                    return;
                }
                finally
                {
                    if (canvasData != null)
                    {
                        _canvasBitmap.UnlockBits(canvasData);
                    }

                    if (tileData != null)
                    {
                        tileBitmap.UnlockBits(tileData);
                    }
                }
            }
        }

        private static Color BuildPalette(int iteration, int maxIterations, int colorIterations)
        {
            if (iteration >= maxIterations)
            {
                return Color.Black;
            }

            double t = (double)iteration / Math.Max(1, colorIterations);
            int r = (int)(9 * (1 - t) * t * t * t * 255);
            int g = (int)(15 * (1 - t) * (1 - t) * t * t * 255);
            int b = (int)(8.5 * (1 - t) * (1 - t) * (1 - t) * t * 255);
            return Color.FromArgb(255, ClampByte(r), ClampByte(g), ClampByte(b));
        }

        private static Color BuildSmoothPalette(double smoothIteration)
        {
            double t = (smoothIteration % 256.0) / 255.0;
            int r = (int)(255 * Math.Pow(t, 0.8));
            int g = (int)(255 * Math.Pow(t, 0.5));
            int b = (int)(255 * (1.0 - t));
            return Color.FromArgb(255, ClampByte(r), ClampByte(g), ClampByte(b));
        }

        private static int ClampByte(int value) => Math.Max(0, Math.Min(255, value));

        private void UpdatePreviewImage()
        {
            if (_canvasBitmap == null)
            {
                return;
            }

            Bitmap preview;
            lock (_bitmapLock)
            {
                try
                {
                    preview = (Bitmap)_canvasBitmap.Clone();
                }
                catch (ArgumentException) when (_isClosing || IsDisposed)
                {
                    return;
                }
            }

            ReplacePreviewImage(preview);
        }

        private void ReplacePreviewImage(Bitmap newImage)
        {
            if (_isClosing || IsDisposed)
            {
                newImage.Dispose();
                return;
            }

            try
            {
                Image? oldImage = pictureBoxGrid.Image;
                pictureBoxGrid.Image = newImage;
                oldImage?.Dispose();
            }
            catch (ArgumentException) when (_isClosing || IsDisposed)
            {
                newImage.Dispose();
            }
        }

        private void btnExportCanvas_Click(object sender, EventArgs e)
        {
            if (_canvasBitmap == null)
            {
                MessageBox.Show(this, "Сначала постройте сетку.", "Нет данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using SaveFileDialog sfd = new()
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg",
                FileName = "julia_burningship_grid_canvas.png"
            };

            if (sfd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            ImageFormat format = Path.GetExtension(sfd.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Jpeg
                : ImageFormat.Png;

            using Bitmap exportBitmap = CloneCanvasBitmap();
            exportBitmap.Save(sfd.FileName, format);
            lblStatus.Text = $"Экспортировано: {sfd.FileName}";
        }

        private Bitmap CloneCanvasBitmap()
        {
            if (_canvasBitmap == null)
            {
                throw new InvalidOperationException("Холст не инициализирован.");
            }

            lock (_bitmapLock)
            {
                return (Bitmap)_canvasBitmap.Clone();
            }
        }

        private void pictureBoxGrid_MouseClick(object sender, MouseEventArgs e)
        {
            if (!chkOpenOnClick.Checked || _canvasBitmap == null || _cells.Count == 0)
            {
                return;
            }

            if (!TryGetBitmapPoint(e.Location, out Point bitmapPoint))
            {
                return;
            }

            GridCellInfo? selected = _cells.FirstOrDefault(c => c.Tile.Bounds.Contains(bitmapPoint));
            if (selected == null)
            {
                return;
            }

            var juliaForm = new FractalJuliaBurningShip();
            juliaForm.ApplyJuliaConstant(selected.C.Real, selected.C.Imaginary);
            juliaForm.Show(this);
        }

        private bool TryGetBitmapPoint(Point clickPoint, out Point bitmapPoint)
        {
            bitmapPoint = Point.Empty;
            if (_canvasBitmap == null)
            {
                return false;
            }

            Rectangle imageRect = GetZoomedImageRect(pictureBoxGrid, _canvasBitmap.Size);
            if (!imageRect.Contains(clickPoint))
            {
                return false;
            }

            float xScale = (float)_canvasBitmap.Width / imageRect.Width;
            float yScale = (float)_canvasBitmap.Height / imageRect.Height;

            int x = (int)((clickPoint.X - imageRect.X) * xScale);
            int y = (int)((clickPoint.Y - imageRect.Y) * yScale);
            bitmapPoint = new Point(Math.Clamp(x, 0, _canvasBitmap.Width - 1), Math.Clamp(y, 0, _canvasBitmap.Height - 1));
            return true;
        }

        private static Rectangle GetZoomedImageRect(PictureBox pb, Size imageSize)
        {
            float ratio = Math.Min((float)pb.ClientSize.Width / imageSize.Width, (float)pb.ClientSize.Height / imageSize.Height);
            int drawWidth = (int)(imageSize.Width * ratio);
            int drawHeight = (int)(imageSize.Height * ratio);
            int posX = (pb.ClientSize.Width - drawWidth) / 2;
            int posY = (pb.ClientSize.Height - drawHeight) / 2;
            return new Rectangle(posX, posY, drawWidth, drawHeight);
        }

        private sealed record GridCellInfo(TileInfo Tile, ComplexDecimal C, int Row, int Col);
    }
}
