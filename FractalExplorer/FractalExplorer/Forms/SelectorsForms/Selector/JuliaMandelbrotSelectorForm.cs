using FractalExplorer.Utilities;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;

using FractalExplorer.Utilities.Theme;
namespace FractalExplorer.Forms.SelectorsForms.Selector
{
    /// <summary>
    /// Форма для выбора комплексного параметра 'c' (для фрактала Жюлиа)
    /// путем интерактивного клика по изображению множества Мандельброта.
    /// Также поддерживает панорамирование и масштабирование отображаемого множества Мандельброта.
    /// </summary>
    public partial class JuliaMandelbrotSelectorForm : Form
    {
        #region Fields

        /// <summary>
        /// Допустимая минимальная действительная часть для выбора.
        /// </summary>
        private readonly double _validMinRe;

        /// <summary>
        /// Допустимая максимальная действительная часть для выбора.
        /// </summary>
        private readonly double _validMaxRe;

        /// <summary>
        /// Допустимая минимальная мнимая часть для выбора.
        /// </summary>
        private readonly double _validMinIm;

        /// <summary>
        /// Допустимая максимальная мнимая часть для выбора.
        /// </summary>
        private readonly double _validMaxIm;

        /// <summary>
        /// Ссылка на главную форму, которая является владельцем этого окна и реализует IFractalForm.
        /// </summary>
        private readonly IFractalForm ownerForm;

        private bool isCanvasHovered = false;
        private EventHandler? themeChangedHandler;

        /// <summary>
        /// Bitmap с текущим отрисованным изображением множества Мандельброта.
        /// </summary>
        private Bitmap mandelbrotBitmap;

        /// <summary>
        /// Выбранные координаты на множестве Мандельброта (в системе координат комплексной плоскости).
        /// </summary>
        private PointF selectedMandelbrotCoords = new PointF(float.NaN, float.NaN);

        /// <summary>
        /// Флаг, указывающий, идет ли в данный момент рендеринг множества Мандельброта.
        /// </summary>
        private volatile bool isRendering = false;

        /// <summary>
        /// Текущая минимальная действительная часть отображаемой области множества Мандельброта.
        /// </summary>
        private double currentMinRe = MIN_RE;

        /// <summary>
        /// Текущая максимальная действительная часть отображаемой области множества Мандельброта.
        /// </summary>
        private double currentMaxRe = MAX_RE;

        /// <summary>
        /// Текущая минимальная мнимая часть отображаемой области множества Мандельброта.
        /// </summary>
        private double currentMinIm = MIN_IM;

        /// <summary>
        /// Текущая максимальная мнимая часть отображаемой области множества Мандельброта.
        /// </summary>
        private double currentMaxIm = MAX_IM;

        /// <summary>
        /// Начальная точка курсора мыши при панорамировании.
        /// </summary>
        private Point panStart;

        /// <summary>
        /// Флаг, указывающий, находится ли пользователь в режиме панорамирования.
        /// </summary>
        private bool panning = false;

        /// <summary>
        /// Начальная минимальная действительная часть для отображения множества Мандельброта по умолчанию.
        /// </summary>
        private const double MIN_RE = -2.0;

        /// <summary>
        /// Начальная максимальная действительная часть для отображения множества Мандельброта по умолчанию.
        /// </summary>
        private const double MAX_RE = 1.0;

        /// <summary>
        /// Начальная минимальная мнимая часть для отображения множества Мандельброта по умолчанию.
        /// </summary>
        private const double MIN_IM = -1.2;

        /// <summary>
        /// Начальная максимальная мнимая часть для отображения множества Мандельброта по умолчанию.
        /// </summary>
        private const double MAX_IM = 1.2;

        /// <summary>
        /// Количество итераций для рендеринга множества Мандельброта в селекторе.
        /// </summary>
        private const int ITERATIONS = 200;

        /// <summary>
        /// Минимальная действительная часть, по которой был отрисован текущий <see cref="mandelbrotBitmap"/>.
        /// </summary>
        private double renderedMinRe = MIN_RE;

        /// <summary>
        /// Максимальная действительная часть, по которой был отрисован текущий <see cref="mandelbrotBitmap"/>.
        /// </summary>
        private double renderedMaxRe = MAX_RE;

        /// <summary>
        /// Минимальная мнимая часть, по которой был отрисован текущий <see cref="mandelbrotBitmap"/>.
        /// </summary>
        private double renderedMinIm = MIN_IM;

        /// <summary>
        /// Максимальная мнимая часть, по которой был отрисован текущий <see cref="mandelbrotBitmap"/>.
        /// </summary>
        private double renderedMaxIm = MAX_IM;

        /// <summary>
        /// Таймер для отложенного запуска рендеринга, чтобы избежать частых перерисовок.
        /// </summary>
        private System.Windows.Forms.Timer renderDebounceTimer;

        /// <summary>
        /// Задержка в миллисекундах для таймера отложенного рендеринга.
        /// </summary>
        private const int RENDER_DEBOUNCE_MILLISECONDS = 300;

        #endregion

        #region Events

        /// <summary>
        /// Событие, возникающее при выборе координат (параметра 'c') на множестве Мандельброта.
        /// </summary>
        public event Action<double, double> CoordinatesSelected;

        #endregion

        #region Constructor

        /// <summary>
        /// Конструктор формы выбора параметра 'c'.
        /// </summary>
        /// <param name="owner">Главная форма, являющаяся владельцем этого окна.</param>
        /// <param name="initialRe">Начальное значение действительной части для выбранных координат (по умолчанию NaN).</param>
        /// <param name="initialIm">Начальное значение мнимой части для выбранных координат (по умолчанию NaN).</param>
        public JuliaMandelbrotSelectorForm(IFractalForm owner, double initialRe, double initialIm, double validMinRe, double validMaxRe, double validMinIm, double validMaxIm)
        {
            ownerForm = owner ?? throw new ArgumentNullException(nameof(owner));
            InitializeComponent();

            ThemeManager.RegisterForm(this);

            // Сохраняем допустимые границы
            _validMinRe = validMinRe;
            _validMaxRe = validMaxRe;
            _validMinIm = validMinIm;
            _validMaxIm = validMaxIm;


            // Подписка на события.
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

            // Инициализация таймера
            renderDebounceTimer = new System.Windows.Forms.Timer { Interval = RENDER_DEBOUNCE_MILLISECONDS };
            renderDebounceTimer.Tick += RenderDebounceTimer_Tick;

            // Установка начальных параметров для отрендеренного битмапа
            renderedMinRe = currentMinRe;
            renderedMaxRe = currentMaxRe;
            renderedMinIm = currentMinIm;
            renderedMaxIm = currentMaxIm;

            // Corrected initialization of renderedMaxIm based on currentMaxIm
            renderedMaxIm = currentMaxIm;

            themeChangedHandler = (_, _) => ApplyInteractiveStyles();
            ThemeManager.ThemeChanged += themeChangedHandler;
            ApplyInteractiveStyles();

            SetSelectedCoordinates(initialRe, initialIm, true);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Устанавливает выбранные координаты параметра 'c' (Re, Im) и обновляет отображение (маркер).
        /// </summary>
        /// <param name="re">Действительная часть комплексного числа.</param>
        /// <param name="im">Мнимая часть комплексного числа.</param>
        /// <param name="raiseEvent">Если True, вызывает событие <see cref="CoordinatesSelected"/>.</param>
        public void SetSelectedCoordinates(double re, double im, bool raiseEvent = true)
        {
            // Ограничиваем координаты в пределах текущего отображаемого диапазона.
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
                mandelbrotDisplay.Invalidate(); // Запрашиваем перерисовку маркера
            }

            if (raiseEvent && !float.IsNaN(selectedMandelbrotCoords.X))
            {
                CoordinatesSelected?.Invoke(selectedMandelbrotCoords.X, selectedMandelbrotCoords.Y);
            }
        }

        #endregion

        #region Rendering Logic

        /// <summary>
        /// Рисует красную рамку, ограничивающую допустимую область выбора.
        /// </summary>
        /// <param name="graphics">Графический контекст для рисования.</param>
        private void DrawValidationBorder(Graphics graphics)
        {
            double realRange = currentMaxRe - currentMinRe;
            double imaginaryRange = currentMaxIm - currentMinIm;

            if (realRange <= 0 || imaginaryRange <= 0) return;

            // Преобразуем комплексные координаты углов рамки в пиксельные
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

        /// <summary>
        /// Обработчик события загрузки формы. Запускает асинхронный рендеринг множества.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private async void MandelbrotSelectorForm_Load(object sender, EventArgs e)
        {
            await RenderMandelbrotAsync();
        }

        /// <summary>
        /// Обработчик события тика таймера отложенного рендеринга.
        /// Запускает асинхронный рендеринг множества.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private async void RenderDebounceTimer_Tick(object sender, EventArgs e)
        {
            renderDebounceTimer.Stop();
            if (IsHandleCreated && !IsDisposed && !Disposing)
            {
                await RenderMandelbrotAsync();
            }
        }

        /// <summary>
        /// Планирует отложенный рендеринг множества. Останавливает текущий таймер и запускает его снова.
        /// </summary>
        private void ScheduleDelayedRender()
        {
            if (IsHandleCreated && !IsDisposed && !Disposing)
            {
                renderDebounceTimer.Stop();
                renderDebounceTimer.Start();
            }
        }

        /// <summary>
        /// Обработчик события изменения размера PictureBox.
        /// Планирует отложенный рендеринг для обновления изображения.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void MandelbrotDisplay_Resize(object sender, EventArgs e)
        {
            if (mandelbrotDisplay.Width > 0 && mandelbrotDisplay.Height > 0)
            {
                // При изменении размера контрола всегда нужен полный перерендер,
                // так как размер bitmap должен измениться.
                ScheduleDelayedRender();
            }
        }

        /// <summary>
        /// Асинхронно рендерит множество Мандельброта в фоновом потоке и обновляет изображение на PictureBox.
        /// </summary>
        /// <returns>Задача, представляющая асинхронную операцию рендеринга.</returns>
        private async Task RenderMandelbrotAsync()
        {
            if (isRendering)
            {
                return;
            }
            if (mandelbrotDisplay.Width <= 0 || mandelbrotDisplay.Height <= 0 ||
                !mandelbrotDisplay.IsHandleCreated || mandelbrotDisplay.IsDisposed || Disposing)
            {
                return;
            }

            isRendering = true;

            int currentWidth = mandelbrotDisplay.Width;
            int currentHeight = mandelbrotDisplay.Height;
            // Захватываем текущие целевые параметры для рендера
            double minReCapture = currentMinRe;
            double maxReCapture = currentMaxRe;
            double minImCapture = currentMinIm;
            double maxImCapture = currentMaxIm;

            Bitmap newRenderedBitmap = null;

            try
            {
                newRenderedBitmap = await Task.Run(() =>
                    RenderMandelbrotSetForSelector(currentWidth, currentHeight, ITERATIONS,
                                                   minReCapture, maxReCapture, minImCapture, maxImCapture));

                // Обновляем UI в основном (UI) потоке
                if (mandelbrotDisplay.IsHandleCreated && !mandelbrotDisplay.IsDisposed && !Disposing)
                {
                    mandelbrotDisplay.Invoke(() =>
                    {
                        if (mandelbrotDisplay.IsHandleCreated && !mandelbrotDisplay.IsDisposed && !Disposing)
                        {
                            Bitmap oldOwnedBitmap = mandelbrotBitmap;
                            mandelbrotBitmap = newRenderedBitmap;

                            // Сохраняем параметры, с которыми был сделан этот рендер
                            renderedMinRe = minReCapture;
                            renderedMaxRe = maxReCapture;
                            renderedMinIm = minImCapture;
                            renderedMaxIm = maxImCapture;

                            // Освобождаем старый Bitmap, если он отличается от нового
                            if (oldOwnedBitmap != null && oldOwnedBitmap != mandelbrotBitmap)
                            {
                                oldOwnedBitmap.Dispose();
                            }
                            // Запросить перерисовку (теперь уже без трансформации, т.к. current == rendered,
                            // или с минимальной, если пользователь успел еще что-то изменить)
                            mandelbrotDisplay.Invalidate();
                        }
                        else
                        {
                            newRenderedBitmap?.Dispose(); // Контрол был уничтожен
                        }
                    });
                }
                else
                {
                    newRenderedBitmap?.Dispose(); // Форма или PictureBox были уничтожены
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка рендеринга Мандельброта для селектора: {ex.Message}");
                newRenderedBitmap?.Dispose();
            }
            finally
            {
                isRendering = false;
            }
        }

        /// <summary>
        /// Рендерит множество Мандельброта в Bitmap.
        /// </summary>
        /// <param name="canvasWidth">Ширина канваса.</param>
        /// <param name="canvasHeight">Высота канваса.</param>
        /// <param name="maxIter">Максимальное количество итераций.</param>
        /// <param name="minRe">Минимальная действительная часть диапазона.</param>
        /// <param name="maxRe">Максимальная действительная часть диапазона.</param>
        /// <param name="minIm">Минимальная мнимая часть диапазона.</param>
        /// <param name="maxIm">Максимальная мнимая часть диапазона.</param>
        /// <returns>Отрисованный Bitmap.</returns>
        private Bitmap RenderMandelbrotSetForSelector(int canvasWidth, int canvasHeight, int maxIter,
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

            // Заполняем буфер черным цветом, если диапазон некорректен
            if (realRange <= 0 || imaginaryRange <= 0)
            {
                for (int i = 0; i < pixelBuffer.Length; i++)
                {
                    pixelBuffer[i] = 0;
                }
                Marshal.Copy(pixelBuffer, 0, scan0, bufferSize);
                bitmap.UnlockBits(bitmapData);
                return bitmap;
            }

            Parallel.For(0, canvasHeight, yCoord =>
            {
                int rowOffset = yCoord * stride;
                for (int xCoord = 0; xCoord < canvasWidth; xCoord++)
                {
                    // Преобразуем пиксельные координаты в комплексные
                    double cReal = minRe + xCoord / (double)canvasWidth * realRange;
                    double cImaginary = maxIm - yCoord / (double)canvasHeight * imaginaryRange; // Ось Y инвертирована для стандартного отображения фракталов
                    Complex c0 = new Complex(cReal, cImaginary);
                    Complex z = Complex.Zero;
                    int iteration = 0;

                    // Формула итерации для множества Мандельброта: Z_n+1 = Z_n^2 + C
                    while (iteration < maxIter && z.Magnitude < 2.0) // Порог 2.0 является стандартным для Мандельброт-подобных множеств
                    {
                        z = z * z + c0;
                        iteration++;
                    }

                    byte redValue, greenValue, blueValue;
                    if (iteration == maxIter)
                    {
                        redValue = greenValue = blueValue = 0; // Внутри множества - черный цвет
                    }
                    else
                    {
                        // Классическая палитра Мандельброта
                        double t = (double)iteration / maxIter;
                        if (t < 0.5)
                        {
                            redValue = (byte)(t * 2 * 200);
                            greenValue = (byte)(t * 2 * 50);
                            blueValue = (byte)(t * 2 * 30);
                        }
                        else
                        {
                            t = (t - 0.5) * 2;
                            redValue = (byte)(200 + t * 55);
                            greenValue = (byte)(50 + t * 205);
                            blueValue = (byte)(30 + t * 225);
                        }
                    }
                    int index = rowOffset + xCoord * 3; // Предполагаем 3 байта на пиксель (24bppRgb)
                    pixelBuffer[index] = blueValue;
                    pixelBuffer[index + 1] = greenValue;
                    pixelBuffer[index + 2] = redValue;
                }
            });

            Marshal.Copy(pixelBuffer, 0, scan0, bufferSize);
            bitmap.UnlockBits(bitmapData);
            return bitmap;
        }

        #endregion

        #region Canvas Interaction

        /// <summary>
        /// Обработчик события клика мыши по PictureBox.
        /// Выбирает комплексные координаты 'c' в соответствии с позицией клика.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void MandelbrotDisplay_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || mandelbrotDisplay.Width <= 0 || mandelbrotDisplay.Height <= 0)
            {
                return;
            }

            double realRange = currentMaxRe - currentMinRe;
            double imaginaryRange = currentMaxIm - currentMinIm;

            if (realRange <= 0 || imaginaryRange <= 0)
            {
                return;
            }

            // Преобразуем пиксельные координаты клика в комплексные.
            double selectedReal = currentMinRe + e.X / (double)mandelbrotDisplay.Width * realRange;
            double selectedImaginary = currentMaxIm - e.Y / (double)mandelbrotDisplay.Height * imaginaryRange;

            // Проверяем, находится ли клик внутри допустимой области
            bool isInsideValidArea = selectedReal >= _validMinRe && selectedReal <= _validMaxRe &&
                                     selectedImaginary >= _validMinIm && selectedImaginary <= _validMaxIm;

            if (isInsideValidArea)
            {
                SetSelectedCoordinates(selectedReal, selectedImaginary, true);
            }
        }

        /// <summary>
        /// Обработчик события отрисовки PictureBox.
        /// Рисует отрисованное множество, масштабируя его при необходимости,
        /// а затем рисует маркер выбранной точки 'c'.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события рисования.</param>
        private void MandelbrotDisplay_Paint(object sender, PaintEventArgs e)
        {
            if (mandelbrotBitmap == null || mandelbrotDisplay.Width <= 0 || mandelbrotDisplay.Height <= 0)
            {
                e.Graphics.Clear(Color.Black); // Очищаем фон, если нет битмапа.
                DrawMarker(e.Graphics); // Всегда рисуем маркер, если есть выбранные координаты.
                DrawValidationBorder(e.Graphics); // Рисуем рамку
                return;
            }

            // Параметры, с которыми был создан mandelbrotBitmap
            double renderedMinRealParam = renderedMinRe;
            double renderedMaxRealParam = renderedMaxRe;
            double renderedMinImaginaryParam = renderedMinIm;
            double renderedMaxImaginaryParam = renderedMaxIm;

            // Текущие целевые параметры отображения
            double currentMinRealParam = currentMinRe;
            double currentMaxRealParam = currentMaxRe;
            double currentMinImaginaryParam = currentMinIm;
            double currentMaxImaginaryParam = currentMaxIm;

            double renderedComplexWidth = renderedMaxRealParam - renderedMinRealParam;
            double renderedComplexHeight = renderedMaxImaginaryParam - renderedMinImaginaryParam;

            double currentComplexWidth = currentMaxRealParam - currentMinRealParam;
            double currentComplexHeight = currentMaxImaginaryParam - currentMinImaginaryParam;

            // Если какой-либо диапазон некорректен, очищаем и рисуем только маркер.
            if (renderedComplexWidth <= 0 || renderedComplexHeight <= 0 || currentComplexWidth <= 0 || currentComplexHeight <= 0)
            {
                e.Graphics.Clear(Color.Black);
                if (mandelbrotBitmap != null)
                {
                    e.Graphics.DrawImageUnscaled(mandelbrotBitmap, Point.Empty); // Запасной вариант для отображения, если масштабирование невозможно
                }
                DrawMarker(e.Graphics);
                DrawValidationBorder(e.Graphics); // Рисуем рамку
                return;
            }

            // Вычисляем положение и размер для отрисовки исходного битмапа с учетом нового зума и панорамирования.
            float offsetX = (float)((renderedMinRealParam - currentMinRealParam) / currentComplexWidth * mandelbrotDisplay.Width);
            float offsetY = (float)((currentMaxImaginaryParam - renderedMaxImaginaryParam) / currentComplexHeight * mandelbrotDisplay.Height);

            float destinationWidthPixels = (float)(renderedComplexWidth / currentComplexWidth * mandelbrotDisplay.Width);
            float destinationHeightPixels = (float)(renderedComplexHeight / currentComplexHeight * mandelbrotDisplay.Height);

            PointF destinationPoint1 = new PointF(offsetX, offsetY);
            PointF destinationPoint2 = new PointF(offsetX + destinationWidthPixels, offsetY);
            PointF destinationPoint3 = new PointF(offsetX, offsetY + destinationHeightPixels);

            e.Graphics.Clear(Color.Black);

            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half; // Для лучшего качества при масштабировании

            if (destinationWidthPixels > 0 && destinationHeightPixels > 0)
            {
                try
                {
                    e.Graphics.DrawImage(mandelbrotBitmap, new PointF[] { destinationPoint1, destinationPoint2, destinationPoint3 });
                }
                catch (ArgumentException)
                {
                    if (mandelbrotBitmap != null)
                    {
                        e.Graphics.DrawImageUnscaled(mandelbrotBitmap, Point.Empty);
                    }
                }
            }
            else
            {
                if (mandelbrotBitmap != null)
                {
                    e.Graphics.DrawImageUnscaled(mandelbrotBitmap, Point.Empty);
                }
            }

            DrawMarker(e.Graphics); // Рисуем маркер выбранной точки.
            DrawValidationBorder(e.Graphics); // Рисуем рамку допустимой области.
        }

        /// <summary>
        /// Рисует маркер выбранных координат 'c' на графическом контексте.
        /// </summary>
        /// <param name="graphics">Графический контекст для рисования.</param>
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
                        checked // Включаем проверку на переполнение для целочисленных преобразований.
                        {
                            // Преобразуем комплексные координаты маркера в пиксельные.
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
                    catch (OverflowException ex)
                    {
                        // Если происходит переполнение при вычислении координат маркера,
                        // выводим сообщение об ошибке в заголовок формы.
                        string errorMessage = $"Переполнение при отрисовке маркера: {ex.Message}";
                        if (IsHandleCreated && !IsDisposed && !Disposing)
                        {
                            Invoke(() => { Text = errorMessage; });
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Обработчик события прокрутки колеса мыши.
        /// Изменяет масштаб отображаемого множества.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void MandelbrotDisplay_MouseWheel(object sender, MouseEventArgs e)
        {
            if (mandelbrotDisplay.Width <= 0 || mandelbrotDisplay.Height <= 0)
            {
                return;
            }

            double zoomFactor = e.Delta > 0 ? 1.2 : 1.0 / 1.2; // Коэффициент зума
            double oldReRange = currentMaxRe - currentMinRe;
            double oldImRange = currentMaxIm - currentMinIm;

            if (oldReRange <= 0 || oldImRange <= 0)
            {
                return;
            }

            // Вычисляем мировые координаты точки под курсором до изменения масштаба.
            double mouseReal = currentMinRe + e.X / (double)mandelbrotDisplay.Width * oldReRange;
            double mouseImaginary = currentMaxIm - e.Y / (double)mandelbrotDisplay.Height * oldImRange;

            double newReRange = oldReRange / zoomFactor;
            double newImRange = oldImRange / zoomFactor;

            // Ограничения для диапазона масштабирования, чтобы избежать слишком больших/маленьких значений.
            const double MIN_RANGE_ALLOWED = 1e-13;
            const double MAX_RANGE_ALLOWED = 100.0;
            const double MIN_IMAGE_WIDTH_REAL = 1e-9; // Минимальная ширина по Re
            const double MIN_IMAGE_HEIGHT_IMAGINARY = 1e-9; // Минимальная высота по Im

            if (newReRange < MIN_RANGE_ALLOWED || newImRange < MIN_RANGE_ALLOWED ||
                newReRange > MAX_RANGE_ALLOWED || newImRange > MAX_RANGE_ALLOWED)
            {
                return;
            }

            double reRatio = e.X / (double)mandelbrotDisplay.Width;
            double imRatio = e.Y / (double)mandelbrotDisplay.Height;

            // Вычисляем новые границы, но пока не присваиваем.
            double proposedMinRe = mouseReal - reRatio * newReRange;
            double proposedMaxRe = proposedMinRe + newReRange;

            double proposedMinIm = mouseImaginary - (1.0 - imRatio) * newImRange;
            double proposedMaxIm = proposedMinIm + newImRange;

            // Проверяем, не стали ли размеры слишком маленькими.
            if (proposedMaxRe - proposedMinRe < MIN_IMAGE_WIDTH_REAL || proposedMaxIm - proposedMinIm < MIN_IMAGE_HEIGHT_IMAGINARY)
            {
                return; // Не масштабируем, если размеры слишком малы.
            }

            // Если все проверки пройдены, применяем изменения.
            currentMinRe = proposedMinRe;
            currentMaxRe = proposedMaxRe;
            currentMinIm = proposedMinIm;
            currentMaxIm = proposedMaxIm;

            mandelbrotDisplay.Invalidate(); // Запрашиваем перерисовку для немедленного отображения зума.
            ScheduleDelayedRender(); // Планируем новый рендеринг для высокой четкости.
        }

        /// <summary>
        /// Обработчик события нажатия кнопки мыши.
        /// Инициирует режим панорамирования.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void MandelbrotDisplay_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                panning = true;
                panStart = e.Location;
                mandelbrotDisplay.Cursor = Cursors.Hand;
            }
        }

        /// <summary>
        /// Обработчик события перемещения мыши.
        /// Выполняет панорамирование отображаемого множества.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void MandelbrotDisplay_MouseMove(object sender, MouseEventArgs e)
        {
            if (panning)
            {
                if (mandelbrotDisplay.Width <= 0 || mandelbrotDisplay.Height <= 0)
                {
                    return;
                }

                double realRange = currentMaxRe - currentMinRe;
                double imaginaryRange = currentMaxIm - currentMinIm;

                if (realRange <= 0 || imaginaryRange <= 0)
                {
                    return;
                }

                // Вычисляем смещение в пикселях.
                double deltaXPixels = panStart.X - e.X;
                double deltaYPixels = panStart.Y - e.Y;

                // Преобразуем смещение из пикселей в комплексные координаты.
                double deltaXComplex = deltaXPixels * (realRange / mandelbrotDisplay.Width);
                double deltaYComplex = deltaYPixels * (imaginaryRange / mandelbrotDisplay.Height);

                // Смещаем диапазон отображения.
                currentMinRe += deltaXComplex;
                currentMaxRe += deltaXComplex;
                currentMinIm -= deltaYComplex; // Ось Y на экране инвертирована относительно мнимой оси.
                currentMaxIm -= deltaYComplex;

                panStart = e.Location; // Обновляем начальную точку панорамирования.

                mandelbrotDisplay.Invalidate(); // Запрашиваем перерисовку для плавного панорамирования.
                ScheduleDelayedRender(); // Планируем новый рендеринг для высокой четкости.
            }
        }

        /// <summary>
        /// Обработчик события отпускания кнопки мыши.
        /// Завершает режим панорамирования.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события мыши.</param>
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

        #endregion

        #region Form Lifecycle

        /// <summary>
        /// Обработчик события закрытия формы.
        /// Освобождает ресурсы и отключает таймер.
        /// </summary>
        /// <param name="e">Аргументы события.</param>
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

        #endregion
    }
}
