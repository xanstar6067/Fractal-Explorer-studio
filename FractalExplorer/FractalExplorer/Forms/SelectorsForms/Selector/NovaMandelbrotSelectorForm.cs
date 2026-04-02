using FractalExplorer.Resources;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;

using FractalExplorer.Utilities.Theme;
namespace FractalExplorer.Forms.SelectorsForms.Selector
{
    public partial class NovaMandelbrotSelectorForm : Form
    {
        private readonly double _validMinRe;
        private readonly double _validMaxRe;
        private readonly double _validMinIm;
        private readonly double _validMaxIm;

        private readonly IFractalForm ownerForm;
        private bool isCanvasHovered = false;
        private EventHandler? themeChangedHandler;
        private Bitmap mandelbrotBitmap;
        private PointF selectedMandelbrotCoords = new PointF(float.NaN, float.NaN);

        private volatile bool isRendering = false;

        private double currentMinRe = -2.0;
        private double currentMaxRe = 2.0;
        private double currentMinIm = -2.0;
        private double currentMaxIm = 2.0;

        private Point panStart;
        private bool panning = false;

        private double renderedMinRe;
        private double renderedMaxRe;
        private double renderedMinIm;
        private double renderedMaxIm;

        private System.Windows.Forms.Timer renderDebounceTimer;
        private const int RENDER_DEBOUNCE_MILLISECONDS = 300;
        private const int ITERATIONS = 100;

        // Nova specific parameters for rendering the map
        private readonly Complex _p;
        private readonly double _m;
        private readonly Complex _z0;

        public event Action<double, double> CoordinatesSelected;

        public NovaMandelbrotSelectorForm(
            IFractalForm owner,
            double initialRe, double initialIm,
            double validMinRe, double validMaxRe,
            double validMinIm, double validMaxIm,
            Complex p, double m, Complex z0)
        {
            ownerForm = owner ?? throw new ArgumentNullException(nameof(owner));
            _p = p;
            _m = m;
            _z0 = z0;

            _validMinRe = validMinRe;
            _validMaxRe = validMaxRe;
            _validMinIm = validMinIm;
            _validMaxIm = validMaxIm;

            InitializeComponent();

            ThemeManager.RegisterForm(this);

            Load += MandelbrotSelectorForm_Load;
            mandelbrotDisplay.Paint += MandelbrotDisplay_Paint;
            mandelbrotDisplay.MouseClick += MandelbrotDisplay_MouseClick;
            mandelbrotDisplay.MouseWheel += MandelbrotDisplay_MouseWheel;
            mandelbrotDisplay.MouseDown += MandelbrotDisplay_MouseDown;
            mandelbrotDisplay.MouseMove += MandelbrotDisplay_MouseMove;
            mandelbrotDisplay.MouseUp += MandelbrotDisplay_MouseUp;
            mandelbrotDisplay.Resize += MandelbrotDisplay_Resize;
            mandelbrotDisplay.MouseEnter += MandelbrotDisplay_MouseEnter;
            mandelbrotDisplay.MouseLeave += MandelbrotDisplay_MouseLeave;

            renderDebounceTimer = new System.Windows.Forms.Timer { Interval = RENDER_DEBOUNCE_MILLISECONDS };
            renderDebounceTimer.Tick += RenderDebounceTimer_Tick;

            renderedMinRe = currentMinRe;
            renderedMaxRe = currentMaxRe;
            renderedMinIm = currentMinIm;
            renderedMaxIm = currentMaxIm;

            themeChangedHandler = (_, _) => ApplyInteractiveStyles();
            ThemeManager.ThemeChanged += themeChangedHandler;
            ApplyInteractiveStyles();

            SetSelectedCoordinates(initialRe, initialIm, true);
        }

        public void SetSelectedCoordinates(double re, double im, bool raiseEvent = true)
        {
            re = Math.Max(currentMinRe, Math.Min(currentMaxRe, re));
            im = Math.Max(currentMinIm, Math.Min(currentMaxIm, im));

            bool changed = false;

            if (double.IsNaN(re) || double.IsNaN(im))
            {
                if (!float.IsNaN(selectedMandelbrotCoords.X))
                {
                    changed = true;
                }
                selectedMandelbrotCoords = new PointF(float.NaN, float.NaN);
            }
            else if (selectedMandelbrotCoords.X != (float)re || selectedMandelbrotCoords.Y != (float)im)
            {
                selectedMandelbrotCoords = new PointF((float)re, (float)im);
                changed = true;
            }

            if (changed && mandelbrotDisplay.IsHandleCreated && !mandelbrotDisplay.IsDisposed)
            {
                mandelbrotDisplay.Invalidate();
            }

            if (raiseEvent && !float.IsNaN(selectedMandelbrotCoords.X))
            {
                CoordinatesSelected?.Invoke(selectedMandelbrotCoords.X, selectedMandelbrotCoords.Y);
            }
        }

        private void DrawValidationBorder(Graphics graphics)
        {
            double realRange = currentMaxRe - currentMinRe;
            double imaginaryRange = currentMaxIm - currentMinIm;

            if (realRange <= 0 || imaginaryRange <= 0) return;

            float x1 = (float)((_validMinRe - currentMinRe) / realRange * mandelbrotDisplay.Width);
            float y1 = (float)((currentMaxIm - _validMaxIm) / imaginaryRange * mandelbrotDisplay.Height);

            float x2 = (float)((_validMaxRe - currentMinRe) / realRange * mandelbrotDisplay.Width);
            float y2 = (float)((currentMaxIm - _validMinIm) / imaginaryRange * mandelbrotDisplay.Height);

            float width = x2 - x1;
            float height = y2 - y1;

            using (Pen borderPen = new Pen(Color.Red, 1f))
            {
                graphics.DrawRectangle(borderPen, x1, y1, width, height);
            }
        }

        private async void MandelbrotSelectorForm_Load(object sender, EventArgs e)
        {
            await RenderMandelbrotAsync();
        }

        private async void RenderDebounceTimer_Tick(object sender, EventArgs e)
        {
            renderDebounceTimer.Stop();
            if (IsHandleCreated && !IsDisposed && !Disposing)
            {
                await RenderMandelbrotAsync();
            }
        }

        private void ScheduleDelayedRender()
        {
            if (IsHandleCreated && !IsDisposed && !Disposing)
            {
                renderDebounceTimer.Stop();
                renderDebounceTimer.Start();
            }
        }

        private void MandelbrotDisplay_Resize(object sender, EventArgs e)
        {
            if (mandelbrotDisplay.Width > 0 && mandelbrotDisplay.Height > 0)
            {
                ScheduleDelayedRender();
            }
        }

        private async Task RenderMandelbrotAsync()
        {
            if (isRendering) return;

            if (mandelbrotDisplay.Width <= 0 || mandelbrotDisplay.Height <= 0 ||
                !mandelbrotDisplay.IsHandleCreated || mandelbrotDisplay.IsDisposed || Disposing)
            {
                return;
            }

            isRendering = true;

            int currentWidth = mandelbrotDisplay.Width;
            int currentHeight = mandelbrotDisplay.Height;

            double minReCapture = currentMinRe;
            double maxReCapture = currentMaxRe;
            double minImCapture = currentMinIm;
            double maxImCapture = currentMaxIm;

            Bitmap newRenderedBitmap = null;

            try
            {
                newRenderedBitmap = await Task.Run(() =>
                    RenderNovaMandelbrotSetForSelector(currentWidth, currentHeight, ITERATIONS,
                                                   minReCapture, maxReCapture, minImCapture, maxImCapture));

                if (mandelbrotDisplay.IsHandleCreated && !mandelbrotDisplay.IsDisposed && !Disposing)
                {
                    mandelbrotDisplay.Invoke(() =>
                    {
                        if (mandelbrotDisplay.IsHandleCreated && !mandelbrotDisplay.IsDisposed && !Disposing)
                        {
                            Bitmap oldOwnedBitmap = mandelbrotBitmap;
                            mandelbrotBitmap = newRenderedBitmap;

                            renderedMinRe = minReCapture;
                            renderedMaxRe = maxReCapture;
                            renderedMinIm = minImCapture;
                            renderedMaxIm = maxImCapture;

                            if (oldOwnedBitmap != null && oldOwnedBitmap != mandelbrotBitmap)
                            {
                                oldOwnedBitmap.Dispose();
                            }
                            mandelbrotDisplay.Invalidate();
                        }
                        else
                        {
                            newRenderedBitmap?.Dispose();
                        }
                    });
                }
                else
                {
                    newRenderedBitmap?.Dispose();
                }
            }
            catch (Exception)
            {
                newRenderedBitmap?.Dispose();
            }
            finally
            {
                isRendering = false;
            }
        }

        private Bitmap RenderNovaMandelbrotSetForSelector(int canvasWidth, int canvasHeight, int maxIter,
                                                      double minRe, double maxRe, double minIm, double maxIm)
        {
            Bitmap bitmap = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format24bppRgb);
            BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, canvasWidth, canvasHeight), ImageLockMode.WriteOnly, bitmap.PixelFormat);
            int stride = bitmapData.Stride;
            nint scan0 = bitmapData.Scan0;
            int bufferSize = Math.Abs(stride) * canvasHeight;
            byte[] pixelBuffer = new byte[bufferSize];

            double realRange = maxRe - minRe;
            double imaginaryRange = maxIm - minIm;

            if (realRange <= 0 || imaginaryRange <= 0)
            {
                bitmap.UnlockBits(bitmapData);
                return bitmap;
            }

            Complex p = _p;
            double m = _m;
            Complex z0 = _z0;
            Complex one = Complex.One;

            Parallel.For(0, canvasHeight, yCoord =>
            {
                int rowOffset = yCoord * stride;
                for (int xCoord = 0; xCoord < canvasWidth; xCoord++)
                {
                    double cReal = minRe + xCoord / (double)canvasWidth * realRange;
                    double cImaginary = maxIm - yCoord / (double)canvasHeight * imaginaryRange;

                    Complex c = new Complex(cReal, cImaginary);
                    Complex z = z0;
                    int iter = 0;

                    // Оптимизированный цикл Nova Mandelbrot
                    while (iter < maxIter)
                    {
                        // Проверка на выход за пределы (Bailout)
                        // Для Nova обычно смотрят на сходимость к корням, но для визуализации 
                        // "карты" расходимость тоже дает хорошую структуру.
                        if (z.Magnitude > 20.0) break;

                        // Защита от деления на ноль
                        if (z.Magnitude < 1e-6) break;

                        Complex z_pow_p = Complex.Pow(z, p);
                        Complex z_pow_p_minus_1 = Complex.Pow(z, p - one);

                        Complex numerator = m * (z_pow_p - one);
                        Complex denominator = p * z_pow_p_minus_1;

                        if (denominator.Magnitude < 1e-12) break;

                        z = z - numerator / denominator + c;
                        iter++;
                    }

                    byte r, g, b;
                    if (iter == maxIter)
                    {
                        // Внутри множества - черный
                        r = g = b = 0;
                    }
                    else
                    {
                        // --- ПАЛИТРА "ОГОНЬ" (FIRE) ---
                        // Ограничиваем цикл окраски 20 итерациями для высокой контрастности
                        int cycle = 20;

                        // t изменяется от 0.0 до 1.0 внутри каждых 20 итераций
                        double t = (double)(iter % cycle) / cycle;

                        // Формируем градиент: Черный -> Красный -> Желтый -> Белый

                        // Red канал растет быстро
                        r = (byte)(Math.Min(255, t * 3 * 255));

                        // Green канал подключается позже (создает желтый)
                        g = (byte)(Math.Min(255, Math.Max(0, (t - 0.33) * 3 * 255)));

                        // Blue канал подключается в конце (создает белый)
                        b = (byte)(Math.Min(255, Math.Max(0, (t - 0.66) * 3 * 255)));
                    }

                    int index = rowOffset + xCoord * 3;
                    pixelBuffer[index] = b;     // Blue
                    pixelBuffer[index + 1] = g; // Green
                    pixelBuffer[index + 2] = r; // Red
                }
            });

            Marshal.Copy(pixelBuffer, 0, scan0, bufferSize);
            bitmap.UnlockBits(bitmapData);
            return bitmap;
        }

        private void MandelbrotDisplay_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || mandelbrotDisplay.Width <= 0 || mandelbrotDisplay.Height <= 0)
                return;

            double realRange = currentMaxRe - currentMinRe;
            double imaginaryRange = currentMaxIm - currentMinIm;

            if (realRange <= 0 || imaginaryRange <= 0) return;

            double selectedReal = currentMinRe + e.X / (double)mandelbrotDisplay.Width * realRange;
            double selectedImaginary = currentMaxIm - e.Y / (double)mandelbrotDisplay.Height * imaginaryRange;

            bool isInsideValidArea = selectedReal >= _validMinRe && selectedReal <= _validMaxRe &&
                                     selectedImaginary >= _validMinIm && selectedImaginary <= _validMaxIm;

            if (isInsideValidArea)
            {
                SetSelectedCoordinates(selectedReal, selectedImaginary, true);
            }
        }

        private void MandelbrotDisplay_Paint(object sender, PaintEventArgs e)
        {
            if (mandelbrotBitmap == null || mandelbrotDisplay.Width <= 0 || mandelbrotDisplay.Height <= 0)
            {
                e.Graphics.Clear(Color.Black);
                DrawMarker(e.Graphics);
                DrawValidationBorder(e.Graphics);
                return;
            }

            double renderedComplexWidth = renderedMaxRe - renderedMinRe;
            double renderedComplexHeight = renderedMaxIm - renderedMinIm;

            double currentComplexWidth = currentMaxRe - currentMinRe;
            double currentComplexHeight = currentMaxIm - currentMinIm;

            if (renderedComplexWidth <= 0 || renderedComplexHeight <= 0 || currentComplexWidth <= 0 || currentComplexHeight <= 0)
            {
                e.Graphics.Clear(Color.Black);
                if (mandelbrotBitmap != null)
                    e.Graphics.DrawImageUnscaled(mandelbrotBitmap, Point.Empty);
                DrawMarker(e.Graphics);
                DrawValidationBorder(e.Graphics);
                return;
            }

            float offsetX = (float)((renderedMinRe - currentMinRe) / currentComplexWidth * mandelbrotDisplay.Width);
            float offsetY = (float)((currentMaxIm - renderedMaxIm) / currentComplexHeight * mandelbrotDisplay.Height);

            float destinationWidthPixels = (float)(renderedComplexWidth / currentComplexWidth * mandelbrotDisplay.Width);
            float destinationHeightPixels = (float)(renderedComplexHeight / currentComplexHeight * mandelbrotDisplay.Height);

            PointF destinationPoint1 = new PointF(offsetX, offsetY);
            PointF destinationPoint2 = new PointF(offsetX + destinationWidthPixels, offsetY);
            PointF destinationPoint3 = new PointF(offsetX, offsetY + destinationHeightPixels);

            e.Graphics.Clear(Color.Black);
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

            if (destinationWidthPixels > 0 && destinationHeightPixels > 0)
            {
                try
                {
                    e.Graphics.DrawImage(mandelbrotBitmap, new PointF[] { destinationPoint1, destinationPoint2, destinationPoint3 });
                }
                catch
                {
                    if (mandelbrotBitmap != null) e.Graphics.DrawImageUnscaled(mandelbrotBitmap, Point.Empty);
                }
            }
            else if (mandelbrotBitmap != null)
            {
                e.Graphics.DrawImageUnscaled(mandelbrotBitmap, Point.Empty);
            }

            DrawMarker(e.Graphics);
            DrawValidationBorder(e.Graphics);
        }

        private void DrawMarker(Graphics graphics)
        {
            if (!float.IsNaN(selectedMandelbrotCoords.X) && mandelbrotDisplay.Width > 0 && mandelbrotDisplay.Height > 0)
            {
                double realRange = currentMaxRe - currentMinRe;
                double imaginaryRange = currentMaxIm - currentMinIm;

                if (realRange > 0 && imaginaryRange > 0)
                {
                    try
                    {
                        checked
                        {
                            int markerX = (int)((selectedMandelbrotCoords.X - currentMinRe) / realRange * mandelbrotDisplay.Width);
                            int markerY = (int)((currentMaxIm - selectedMandelbrotCoords.Y) / imaginaryRange * mandelbrotDisplay.Height);

                            int markerSize = 9;
                            using (Pen markerPen = new Pen(Color.FromArgb(220, Color.Green), 2f))
                            {
                                graphics.DrawLine(markerPen, markerX - markerSize, markerY, markerX + markerSize, markerY);
                                graphics.DrawLine(markerPen, markerX, markerY - markerSize, markerX, markerY + markerSize);
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private void MandelbrotDisplay_MouseWheel(object sender, MouseEventArgs e)
        {
            if (mandelbrotDisplay.Width <= 0 || mandelbrotDisplay.Height <= 0) return;

            double zoomFactor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
            double oldReRange = currentMaxRe - currentMinRe;
            double oldImRange = currentMaxIm - currentMinIm;

            if (oldReRange <= 0 || oldImRange <= 0) return;

            double mouseReal = currentMinRe + e.X / (double)mandelbrotDisplay.Width * oldReRange;
            double mouseImaginary = currentMaxIm - e.Y / (double)mandelbrotDisplay.Height * oldImRange;

            double newReRange = oldReRange / zoomFactor;
            double newImRange = oldImRange / zoomFactor;

            double reRatio = e.X / (double)mandelbrotDisplay.Width;
            double imRatio = e.Y / (double)mandelbrotDisplay.Height;

            currentMinRe = mouseReal - reRatio * newReRange;
            currentMaxRe = currentMinRe + newReRange;

            currentMinIm = mouseImaginary - (1.0 - imRatio) * newImRange;
            currentMaxIm = currentMinIm + newImRange;

            mandelbrotDisplay.Invalidate();
            ScheduleDelayedRender();
        }

        private void MandelbrotDisplay_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                panning = true;
                panStart = e.Location;
                mandelbrotDisplay.Cursor = Cursors.Hand;
            }
        }

        private void MandelbrotDisplay_MouseMove(object sender, MouseEventArgs e)
        {
            if (panning)
            {
                if (mandelbrotDisplay.Width <= 0 || mandelbrotDisplay.Height <= 0) return;

                double realRange = currentMaxRe - currentMinRe;
                double imaginaryRange = currentMaxIm - currentMinIm;

                if (realRange <= 0 || imaginaryRange <= 0) return;

                double deltaXPixels = panStart.X - e.X;
                double deltaYPixels = panStart.Y - e.Y;

                double deltaXComplex = deltaXPixels * (realRange / mandelbrotDisplay.Width);
                double deltaYComplex = deltaYPixels * (imaginaryRange / mandelbrotDisplay.Height);

                currentMinRe += deltaXComplex;
                currentMaxRe += deltaXComplex;
                currentMinIm -= deltaYComplex;
                currentMaxIm -= deltaYComplex;

                panStart = e.Location;
                mandelbrotDisplay.Invalidate();
                ScheduleDelayedRender();
            }
        }

        private void MandelbrotDisplay_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                panning = false;
                mandelbrotDisplay.Cursor = Cursors.Cross;
            }
        }


        private void MandelbrotDisplay_MouseEnter(object? sender, EventArgs e)
        {
            isCanvasHovered = true;
            ApplyInteractiveStyles();
        }

        private void MandelbrotDisplay_MouseLeave(object? sender, EventArgs e)
        {
            isCanvasHovered = false;
            ApplyInteractiveStyles();
        }

        private void ApplyInteractiveStyles()
        {
            ThemeDefinition theme = ThemeManager.CurrentDefinition;
            mandelbrotHintLabel.ForeColor = theme.SecondaryText;
            mandelbrotHintLabel.BackColor = Color.Transparent;
            Color borderBackground = mandelbrotCanvasBorder.Parent?.BackColor ?? theme.PanelBackground;
            Color borderColor = ThemeManager.GetInteractiveStateColor(theme, borderBackground, isCanvasHovered);
            mandelbrotCanvasBorder.BackColor = borderColor;
            mandelbrotCanvasHoverBorder.BackColor = isCanvasHovered ? borderColor : borderBackground;
            mandelbrotDisplay.BackColor = theme.ControlBackground;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            renderDebounceTimer?.Stop();
            renderDebounceTimer?.Dispose();
            renderDebounceTimer = null;
            mandelbrotBitmap?.Dispose();
            mandelbrotBitmap = null;

            if (themeChangedHandler is not null)
            {
                ThemeManager.ThemeChanged -= themeChangedHandler;
                themeChangedHandler = null;
            }

            base.OnFormClosed(e);
        }
    }
}
