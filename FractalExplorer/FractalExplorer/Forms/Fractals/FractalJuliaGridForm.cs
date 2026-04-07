using FractalExplorer.Engines;
using FractalExplorer.Projects;
using FractalExplorer.Resources;
using FractalExplorer.Utilities;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FractalExplorer.Utilities.RenderUtilities;

namespace FractalExplorer.Forms.Fractals
{
    public partial class FractalJuliaGridForm : Form
    {
        private Bitmap? _canvasBitmap;
        private readonly object _bitmapLock = new();
        private List<GridCellInfo> _cells = new();
        private bool _isRendering;

        public FractalJuliaGridForm()
        {
            InitializeComponent();
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
                pictureBoxGrid.Image = _canvasBitmap;

                progressBar.Minimum = 0;
                progressBar.Maximum = tiles.Count;
                progressBar.Value = 0;
                lblStatus.Text = "Рендер сетки...";

                int renderedCount = 0;
                var dispatcher = new TileRenderDispatcher(tiles, Math.Max(1, Environment.ProcessorCount - 1), RenderPatternSettings.SelectedPattern);

                await dispatcher.RenderAsync(async (tile, ct) =>
                {
                    GridCellInfo? cell = _cells.FirstOrDefault(c => c.Tile.Bounds == tile.Bounds);
                    if (cell == null)
                    {
                        return;
                    }

                    using Bitmap tileBitmap = RenderJuliaThumbnail(tile.Bounds.Width, tile.Bounds.Height, cell.C);
                    WriteTileToCanvas(tile.Bounds, tileBitmap);

                    int current = Interlocked.Increment(ref renderedCount);
                    BeginInvoke(() =>
                    {
                        progressBar.Value = Math.Min(progressBar.Maximum, current);
                        lblStatus.Text = $"Отрисовано {current}/{tiles.Count} клеток";
                        pictureBoxGrid.Invalidate();
                    });

                    await Task.CompletedTask;
                }, CancellationToken.None);

                lblStatus.Text = $"Готово. Размер полотна: {_canvasBitmap.Width}x{_canvasBitmap.Height}px.";
            }
            finally
            {
                _isRendering = false;
                btnRender.Enabled = true;
            }
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
            var engine = new JuliaEngine
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

            BitmapData? tileData = null;
            BitmapData? canvasData = null;
            try
            {
                tileData = tileBitmap.LockBits(new Rectangle(0, 0, tileBitmap.Width, tileBitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                canvasData = _canvasBitmap.LockBits(tileRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                int rowBytes = tileRect.Width * 4;
                byte[] rowBuffer = new byte[rowBytes];

                lock (_bitmapLock)
                {
                    for (int y = 0; y < tileRect.Height; y++)
                    {
                        IntPtr src = tileData.Scan0 + (y * tileData.Stride);
                        IntPtr dst = canvasData.Scan0 + (y * canvasData.Stride);
                        Marshal.Copy(src, rowBuffer, 0, rowBytes);
                        Marshal.Copy(rowBuffer, 0, dst, rowBytes);
                    }
                }
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
                FileName = "julia_grid_canvas.png"
            };

            if (sfd.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            ImageFormat format = Path.GetExtension(sfd.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Jpeg
                : ImageFormat.Png;

            _canvasBitmap.Save(sfd.FileName, format);
            lblStatus.Text = $"Экспортировано: {sfd.FileName}";
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

            var juliaForm = new FractalJulia();
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
