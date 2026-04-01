using FractalExplorer.Engines;
using FractalExplorer.Forms;
using FractalExplorer.Forms.Other;
using FractalExplorer.Projects;
using FractalExplorer.Resources;
using FractalExplorer.Utilities;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

using FractalExplorer.Utilities.Theme;
using FractalExplorer.Utilities.UI;
namespace FractalDraving
{
    /// <summary>
    /// Базовый абстрактный класс для форм, отображающих фракталы семейства Мандельброта.
    /// Предоставляет общую логику для управления движком рендеринга, палитрой,
    /// масштабированием, панорамированием, сохранением изображений и состояний.
    /// Включает логику для корректной обработки изменения размеров и сворачивания окна.
    /// </summary>
    public abstract partial class FractalMandelbrotFamilyForm : Form, IFractalForm, ISaveLoadCapableFractal, IHighResRenderable
    {
        #region Fields

        /// <summary>
        /// Компонент для визуализации процесса рендеринга плиток.
        /// </summary>
        private RenderVisualizerComponent _renderVisualizer;

        /// <summary>
        /// Менеджер палитр, используемый этой формой для управления цветовыми схемами.
        /// </summary>
        protected PaletteManager _paletteManager;

        /// <summary>
        /// Кэш для цветов палитры с уже примененной гамма-коррекцией (для дискретного режима).
        /// Индекс массива соответствует номеру итерации.
        /// </summary>
        private Color[] _gammaCorrectedPaletteCache;

        /// <summary>
        /// "Подпись" палитры, для которой был сгенерирован кэш. 
        /// Используется для определения необходимости обновления кэша.
        /// </summary>
        private string _paletteCacheSignature;

        /// <summary>
        /// Форма конфигурации палитр, связанная с этой формой, для настройки цветов.
        /// </summary>
        private ColorConfigurationForm _colorConfigForm;
        private ColoringModeSettingsForm _coloringModeSettingsForm;

        /// <summary>
        /// Размер одной плитки (тайла) в пикселях для пошагового рендеринга.
        /// </summary>
        private const int TILE_SIZE = 16;

        /// <summary>
        /// Объект для блокировки доступа к битмапам во время операций рендеринга.
        /// </summary>
        private readonly object _bitmapLock = new object();

        /// <summary>
        /// Битмап, содержащий отрисованное изображение для предпросмотра фрактала.
        /// </summary>
        private Bitmap _previewBitmap;

        /// <summary>
        /// Битмап, в который в текущий момент происходит рендеринг плиток.
        /// </summary>
        private Bitmap _currentRenderingBitmap;

        /// <summary>
        /// Токен отмены для операций рендеринга предпросмотра.
        /// </summary>
        private CancellationTokenSource _previewRenderCts;

        /// <summary>
        /// Флаг, указывающий, выполняется ли сейчас рендеринг в высоком разрешении.
        /// </summary>
        private volatile bool _isHighResRendering = false;

        /// <summary>
        /// Флаг, указывающий, выполняется ли сейчас рендеринг предпросмотра.
        /// </summary>
        private volatile bool _isRenderingPreview = false;

        /// <summary>
        /// Экземпляр движка для рендеринга фрактала.
        /// </summary>
        protected FractalMandelbrotFamilyEngine _fractalEngine;

        /// <summary>
        /// Текущий коэффициент масштабирования фрактала.
        /// </summary>
        protected decimal _zoom = 1.0m;

        /// <summary>
        /// Текущая координата X (реальная часть) центра видимой области фрактала.
        /// </summary>
        protected decimal _centerX = 0.0m;

        /// <summary>
        /// Текущая координата Y (мнимая часть) центра видимой области фрактала.
        /// </summary>
        protected decimal _centerY = 0.0m;

        /// <summary>
        /// Координата X центра, по которой был отрисован текущий <see cref="_previewBitmap"/>.
        /// </summary>
        private decimal _renderedCenterX;

        /// <summary>
        /// Координата Y центра, по которой был отрисован текущий <see cref="_previewBitmap"/>.
        /// </summary>
        private decimal _renderedCenterY;

        /// <summary>
        /// Коэффициент масштабирования, по которому был отрисован текущий <see cref="_previewBitmap"/>.
        /// </summary>
        private decimal _renderedZoom;

        /// <summary>
        /// Начальная позиция курсора мыши при инициировании панорамирования.
        /// </summary>
        private Point _panStart;

        /// <summary>
        /// Флаг, указывающий, находится ли пользователь в режиме панорамирования.
        /// </summary>
        private bool _panning = false;

        /// <summary>
        /// Таймер для отложенного запуска рендеринга.
        /// </summary>
        private System.Windows.Forms.Timer _renderDebounceTimer;

        /// <summary>
        /// Базовый заголовок окна для восстановления после отображения времени рендера.
        /// </summary>
        private string _baseTitle;

        /// <summary>
        /// Подсказка для интерактивного предпросмотра множества.
        /// </summary>
        private Label _previewInteractionHintLabel;

        /// <summary>
        /// Обертка-рамка вокруг канваса предпросмотра для акцентирования интерактивности.
        /// </summary>
        private Panel _previewCanvasBorderPanel;
        private Panel _previewCanvasHoverBorderPanel;

        /// <summary>
        /// Флаг наведения курсора на канвас предпросмотра.
        /// </summary>
        private bool _isPreviewCanvasHovered;

        /// <summary>
        /// Обработчик изменения темы, применяющий стили интерактивного предпросмотра.
        /// </summary>
        private EventHandler? _previewThemeChangedHandler;

        /// <summary>
        /// Размер отступа кнопки сворачивания панели управления.
        /// </summary>
        private const int ToggleButtonMargin = 12;

        /// <summary>
        /// Видимая область внутри полной буферной поверхности для последнего завершенного кадра.
        /// </summary>
        private Rectangle _renderedViewportRect = Rectangle.Empty;

        /// <summary>
        /// Флаг, отключающий запуск полного рендера при программном изменении layout.
        /// </summary>
        private bool _suppressResizeRender = false;
        private readonly FullscreenToggleController _fullscreenController = new();

        /// <summary>
        /// Признак видимости панели управления.
        /// </summary>
        private bool _controlsPanelVisible = true;

        /// <summary>
        /// Runtime-состояние выбранного режима окрашивания и его параметров.
        /// Существует только в памяти текущей формы и не сериализуется.
        /// </summary>
        private readonly ColoringRuntimeState _coloringRuntimeState = ColoringRuntimeState.CreateDefault();
        private bool _suppressQuickModeChange = false;

        #endregion

        #region Constructor

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="FractalMandelbrotFamilyForm"/>.
        /// </summary>
        protected FractalMandelbrotFamilyForm()
        {
            InitializeComponent();
            KeyPreview = true;
            ThemeManager.RegisterForm(this);
            InitializePreviewCanvasInteractionUi();
            _centerX = InitialCenterX;
            _centerY = InitialCenterY;
        }

        /// <summary>
        /// Настраивает UI-элементы интерактивного предпросмотра множества Мандельброта.
        /// </summary>
        private void InitializePreviewCanvasInteractionUi()
        {
            _previewInteractionHintLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 20,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Кликните по изображению для выбора точки",
                Padding = new Padding(0, 0, 0, 4),
                Visible = false
            };

            _previewCanvasBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };

            _previewCanvasHoverBorderPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                Margin = new Padding(0)
            };

            mandelbrotPreviewCanvas.BorderStyle = BorderStyle.None;
            mandelbrotPreviewCanvas.Cursor = Cursors.Default;
            mandelbrotPreviewCanvas.MouseEnter += MandelbrotPreviewCanvas_MouseEnter;
            mandelbrotPreviewCanvas.MouseLeave += MandelbrotPreviewCanvas_MouseLeave;

            mandelbrotPreviewPanel.Controls.Remove(mandelbrotPreviewCanvas);
            _previewCanvasHoverBorderPanel.Controls.Add(mandelbrotPreviewCanvas);
            _previewCanvasBorderPanel.Controls.Add(_previewCanvasHoverBorderPanel);
            mandelbrotPreviewPanel.Controls.Add(_previewCanvasBorderPanel);
            mandelbrotPreviewPanel.Controls.Add(_previewInteractionHintLabel);

            _previewThemeChangedHandler = (_, _) => ApplyPreviewCanvasInteractiveStyles();
            ThemeManager.ThemeChanged += _previewThemeChangedHandler;
            ApplyPreviewCanvasInteractiveStyles();
        }

        /// <summary>
        /// Включает или отключает акцентированное интерактивное оформление канваса предпросмотра.
        /// </summary>
        /// <param name="enabled">True, если предпросмотр в текущем фрактале интерактивен.</param>
        protected void SetMandelbrotPreviewInteractive(bool enabled)
        {
            if (_previewInteractionHintLabel == null || _previewCanvasBorderPanel == null || _previewCanvasHoverBorderPanel == null)
            {
                return;
            }

            _previewInteractionHintLabel.Visible = enabled;
            if (!enabled)
            {
                _isPreviewCanvasHovered = false;
            }

            mandelbrotPreviewCanvas.Cursor = enabled ? Cursors.Cross : Cursors.Default;
            ApplyPreviewCanvasInteractiveStyles();
        }

        private void MandelbrotPreviewCanvas_MouseEnter(object? sender, EventArgs e)
        {
            if (_previewInteractionHintLabel is null || !_previewInteractionHintLabel.Visible)
            {
                return;
            }

            _isPreviewCanvasHovered = true;
            ApplyPreviewCanvasInteractiveStyles();
        }

        private void MandelbrotPreviewCanvas_MouseLeave(object? sender, EventArgs e)
        {
            if (_previewInteractionHintLabel is null || !_previewInteractionHintLabel.Visible)
            {
                return;
            }

            _isPreviewCanvasHovered = false;
            ApplyPreviewCanvasInteractiveStyles();
        }

        private void ApplyPreviewCanvasInteractiveStyles()
        {
            if (_previewInteractionHintLabel == null || _previewCanvasBorderPanel == null || _previewCanvasHoverBorderPanel == null)
            {
                return;
            }

            ThemeDefinition theme = ThemeManager.CurrentDefinition;
            _previewInteractionHintLabel.ForeColor = theme.SecondaryText;
            _previewInteractionHintLabel.BackColor = Color.Transparent;

            bool interactiveEnabled = _previewInteractionHintLabel.Visible;
            Color previewBorderBackground = _previewCanvasBorderPanel.Parent?.BackColor ?? theme.PanelBackground;
            Color borderColor = interactiveEnabled
                ? ThemeManager.GetInteractiveStateColor(theme, previewBorderBackground, _isPreviewCanvasHovered)
                : theme.BorderColor;
            _previewCanvasBorderPanel.BackColor = borderColor;
            _previewCanvasHoverBorderPanel.BackColor = interactiveEnabled && _isPreviewCanvasHovered
                ? borderColor
                : previewBorderBackground;
            mandelbrotPreviewCanvas.BackColor = theme.ControlBackground;
        }

        #endregion

        #region Protected Abstract/Virtual Methods

        /// <summary>
        /// Создает конкретный экземпляр движка фрактала.
        /// Этот метод должен быть переопределен в дочерних классах.
        /// </summary>
        /// <returns>Новый экземпляр движка, унаследованного от <see cref="FractalMandelbrotFamilyEngine"/>.</returns>
        protected abstract FractalMandelbrotFamilyEngine CreateEngine();

        /// <summary>
        /// Получает базовый масштаб для фрактала, определяющий ширину комплексной плоскости при масштабе 1.0.
        /// </summary>
        protected virtual decimal BaseScale => 3.0m;

        /// <summary>
        /// Получает начальную координату X центра.
        /// </summary>
        protected virtual decimal InitialCenterX => -0.5m;

        /// <summary>
        /// Получает начальную координату Y центра.
        /// </summary>
        protected virtual decimal InitialCenterY => 0.0m;

        /// <summary>
        /// Обновляет специфические параметры движка, характерные для конкретной реализации фрактала.
        /// Может быть переопределен в дочерних классах для установки уникальных параметров (например, степени в обобщенном фрактале).
        /// </summary>
        protected virtual void UpdateEngineSpecificParameters() { }

        /// <summary>
        /// Вызывается после завершения основной инициализации формы в методе <see cref="FormBase_Load"/>.
        /// Позволяет дочерним классам выполнять дополнительную настройку.
        /// </summary>
        protected virtual void OnPostInitialize() { }

        /// <summary>
        /// Получает строку с деталями о текущем фрактале для формирования имени файла при сохранении.
        /// </summary>
        /// <returns>Строка с деталями о фрактале.</returns>
        protected virtual string GetSaveFileNameDetails() => "fractal";

        #endregion

        #region UI Initialization

        /// <summary>
        /// Инициализирует элементы управления формы, задавая их начальные значения и диапазоны.
        /// </summary>
        private void InitializeControls()
        {
            int cores = Environment.ProcessorCount;
            cbThreads.Items.Clear();
            for (int i = 1; i <= cores; i++)
            {
                cbThreads.Items.Add(i);
            }
            cbThreads.Items.Add("Auto");
            cbThreads.SelectedItem = "Auto";

            nudIterations.Minimum = 50;
            nudIterations.Maximum = 100000;
            nudIterations.Value = 500;

            nudThreshold.Minimum = 2m;
            nudThreshold.Maximum = 1000m;
            nudThreshold.DecimalPlaces = 1;
            nudThreshold.Increment = 0.1m;
            nudThreshold.Value = 2m;

            nudZoom.DecimalPlaces = 15;
            nudZoom.Increment = 0.1m;
            nudZoom.Minimum = 0.01m;
            nudZoom.Maximum = decimal.MaxValue;
            _zoom = BaseScale / 4.0m;
            nudZoom.Value = _zoom;

            UpdateSmoothSettingsButtonState();
            InitializeQuickColoringModeControls();

            if (nudRe != null && nudIm != null)
            {
                nudRe.Minimum = -2m;
                nudRe.Maximum = 2m;
                nudRe.DecimalPlaces = 15;
                nudRe.Increment = 0.001m;
                nudRe.Value = -0.8m;
                nudIm.Minimum = -2m;
                nudIm.Maximum = 2m;
                nudIm.DecimalPlaces = 15;
                nudIm.Increment = 0.001m;
                nudIm.Value = 0.156m;
            }
        }

        /// <summary>
        /// Инициализирует обработчики событий для элементов управления и самой формы.
        /// </summary>
        private void InitializeEventHandlers()
        {
            nudIterations.ValueChanged += ParamControl_Changed;
            nudThreshold.ValueChanged += ParamControl_Changed;
            cbThreads.SelectedIndexChanged += ParamControl_Changed;
            nudZoom.ValueChanged += ParamControl_Changed;
            if (nudRe != null) nudRe.ValueChanged += ParamControl_Changed;
            if (nudIm != null) nudIm.ValueChanged += ParamControl_Changed;
            btnSmoothSettings.Click += btnSmoothSettings_Click;
            cbQuickColoringMode.SelectedIndexChanged += CbQuickColoringMode_SelectedIndexChanged;

            btnRender.Click += (s, e) => ScheduleRender();

            var configButton = Controls.Find("color_configurations", true).FirstOrDefault();
            if (configButton != null) configButton.Click += color_configurations_Click;

            canvas.MouseWheel += Canvas_MouseWheel;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.Paint += Canvas_Paint;
            canvas.Resize += Canvas_Resize;
            canvasHost.Resize += CanvasHost_Resize;
            controlsHost.SizeChanged += ControlsHost_SizeChanged;
            KeyDown += Form_KeyDown;
            FormClosing += Form_FormClosing;
            FormClosed += FractalMandelbrotFamilyForm_FormClosed;
        }

        private void ToggleFullscreenSafely()
        {
            _suppressResizeRender = true;
            try
            {
                _fullscreenController.Toggle(this);
                UpdateToggleControlsPosition();
                ScheduleRender();
            }
            finally
            {
                _suppressResizeRender = false;
            }
        }

        private void ExitFullscreenSafely()
        {
            if (!_fullscreenController.IsFullscreen(this))
            {
                return;
            }

            _suppressResizeRender = true;
            try
            {
                _fullscreenController.ExitFullscreen(this);
                UpdateToggleControlsPosition();
                ScheduleRender();
            }
            finally
            {
                _suppressResizeRender = false;
            }
        }

        private void Form_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11)
            {
                ToggleFullscreenSafely();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape && _fullscreenController.IsFullscreen(this))
            {
                ExitFullscreenSafely();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            ExitFullscreenSafely();
        }

        #endregion

        #region UI Event Handlers

        /// <summary>
        /// Обрабатывает нажатие на кнопку конфигурации цветов, открывая соответствующую форму.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void color_configurations_Click(object sender, EventArgs e)
        {
            if (_colorConfigForm == null || _colorConfigForm.IsDisposed)
            {
                _colorConfigForm = new ColorConfigurationForm(_paletteManager);
                _colorConfigForm.PaletteApplied += OnPaletteApplied;
                _colorConfigForm.FormClosed += (s, args) => _colorConfigForm = null;
                _colorConfigForm.Show(this);
            }
            else
            {
                _colorConfigForm.Activate();
            }
        }

        /// <summary>
        /// Обрабатывает событие применения новой палитры из формы конфигурации.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void OnPaletteApplied(object sender, EventArgs e)
        {
            UpdateEngineParameters();
            ScheduleRender();
        }

        private void btnSmoothSettings_Click(object sender, EventArgs e)
        {
            if (_coloringModeSettingsForm == null || _coloringModeSettingsForm.IsDisposed)
            {
                _coloringModeSettingsForm = new ColoringModeSettingsForm(
                    _paletteManager,
                    _coloringRuntimeState,
                    _paletteManager.ActivePalette?.Name,
                    this.Icon);

                _coloringModeSettingsForm.SettingsApplied += ColoringModeSettingsForm_SettingsApplied;
                _coloringModeSettingsForm.FormClosed += (s, args) => _coloringModeSettingsForm = null;
                _coloringModeSettingsForm.Show(this);
            }
            else
            {
                _coloringModeSettingsForm.Activate();
            }
        }

        private void ColoringModeSettingsForm_SettingsApplied(object? sender, ColoringModeSettingsAppliedEventArgs e)
        {
            _coloringRuntimeState.ActiveMode = e.RuntimeState.ActiveMode;
            _coloringRuntimeState.SmoothBlendPower = e.RuntimeState.SmoothBlendPower;
            _coloringRuntimeState.SmoothIterationOffset = e.RuntimeState.SmoothIterationOffset;
            _coloringRuntimeState.HistogramSettings = e.RuntimeState.HistogramSettings.Clone();
            _coloringRuntimeState.OrbitTrapSettings = e.RuntimeState.OrbitTrapSettings.Clone();
            _coloringRuntimeState.StripeAverageSettings = e.RuntimeState.StripeAverageSettings.Clone();
            _coloringRuntimeState.PaletteTransform = e.RuntimeState.PaletteTransform.Clone();
            _coloringRuntimeState.InteriorMode = e.RuntimeState.InteriorMode;
            _coloringRuntimeState.InteriorColor = e.RuntimeState.InteriorColor;

            UpdateSmoothSettingsButtonState();
            SyncQuickColoringModeSelection();

            if (!string.IsNullOrWhiteSpace(e.SelectedPaletteName))
            {
                var paletteToApply = _paletteManager.Palettes.FirstOrDefault(p => p.Name == e.SelectedPaletteName);
                if (paletteToApply != null)
                {
                    _paletteManager.ActivePalette = paletteToApply;
                }
            }

            UpdateEngineParameters();
            ScheduleRender();
        }

        private void CbQuickColoringMode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_suppressQuickModeChange)
            {
                return;
            }

            if (cbQuickColoringMode.SelectedItem is not ColoringModeOption option)
            {
                return;
            }

            _coloringRuntimeState.ActiveMode = ColoringModeRuntime.FromType(option.ModeType);
            UpdateEngineParameters();
            ScheduleRender();
        }

        private void UpdateSmoothSettingsButtonState()
        {
            btnSmoothSettings.Enabled = true;
        }

        private void InitializeQuickColoringModeControls()
        {
            cbQuickColoringMode.Items.Clear();
            cbQuickColoringMode.Items.Add(new ColoringModeOption(ColoringModeType.Discrete, "Дискретная"));
            cbQuickColoringMode.Items.Add(new ColoringModeOption(ColoringModeType.Smooth, "Плавная"));
            cbQuickColoringMode.Items.Add(new ColoringModeOption(ColoringModeType.Histogram, "Гистограмма"));
            cbQuickColoringMode.Items.Add(new ColoringModeOption(ColoringModeType.OrbitTrap, "Орбитальная ловушка"));
            cbQuickColoringMode.Items.Add(new ColoringModeOption(ColoringModeType.StripeAverage, "Среднее полосы"));
            SyncQuickColoringModeSelection();
        }

        private void SyncQuickColoringModeSelection()
        {
            _suppressQuickModeChange = true;
            try
            {
                for (int i = 0; i < cbQuickColoringMode.Items.Count; i++)
                {
                    if (cbQuickColoringMode.Items[i] is ColoringModeOption option
                        && option.ModeType == _coloringRuntimeState.ActiveMode.ModeType)
                    {
                        cbQuickColoringMode.SelectedIndex = i;
                        return;
                    }
                }

                cbQuickColoringMode.SelectedIndex = -1;
            }
            finally
            {
                _suppressQuickModeChange = false;
            }
        }

        /// <summary>
        /// Снимает стартовый фокус с комбобоксов, чтобы они не выглядели выделенными сразу после открытия формы.
        /// </summary>
        private void ClearInitialControlSelection()
        {
            BeginInvoke(new Action(() => ActiveControl = null));
        }

        /// <summary>
        /// Обрабатывает изменение значения в одном из контролов параметров фрактала.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        protected void ParamControl_Changed(object sender, EventArgs e)
        {
            if (_isHighResRendering) return;
            if (sender == nudZoom && nudZoom.Value != _zoom)
            {
                _zoom = nudZoom.Value;
            }
            ScheduleRender();
        }

        #endregion

        #region Canvas Interaction

        /// <summary>
        /// Обрабатывает событие прокрутки колеса мыши над холстом для масштабирования.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering || canvas.Width <= 0 || canvas.Height <= 0) return;
            CommitAndBakePreview();
            decimal zoomFactor = e.Delta > 0 ? 1.5m : 1.0m / 1.5m;
            decimal scaleBeforeZoom = BaseScale / _zoom;
            decimal mouseReal = _centerX + (e.X - canvas.Width / 2.0m) * scaleBeforeZoom / canvas.Width;
            decimal mouseImaginary = _centerY - (e.Y - canvas.Height / 2.0m) * scaleBeforeZoom / canvas.Height;

            decimal newZoom;
            if (zoomFactor > 1.0m) // Приближаем (умножаем)
            {
                if (_zoom > nudZoom.Maximum / zoomFactor)
                {
                    newZoom = nudZoom.Maximum;
                }
                else
                {
                    newZoom = _zoom * zoomFactor;
                }
            }
            else // Отдаляем (делим)
            {
                if (_zoom < nudZoom.Minimum / zoomFactor)
                {
                    newZoom = nudZoom.Minimum;
                }
                else
                {
                    newZoom = _zoom * zoomFactor;
                }
            }

            _zoom = Math.Max(nudZoom.Minimum, Math.Min(nudZoom.Maximum, newZoom));
            decimal scaleAfterZoom = BaseScale / _zoom;
            _centerX = mouseReal - (e.X - canvas.Width / 2.0m) * scaleAfterZoom / canvas.Width;
            _centerY = mouseImaginary + (e.Y - canvas.Height / 2.0m) * scaleAfterZoom / canvas.Height;
            canvas.Invalidate();
            if (nudZoom.Value != _zoom) nudZoom.Value = _zoom;
            else ScheduleRender();
        }

        /// <summary>
        /// Обрабатывает нажатие кнопки мыши на холсте, инициируя режим панорамирования.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering) return;
            if (e.Button == MouseButtons.Left)
            {
                _panning = true;
                _panStart = e.Location;
                canvas.Cursor = Cursors.Hand;
            }
        }

        /// <summary>
        /// Обрабатывает перемещение мыши по холсту в режиме панорамирования.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering || !_panning || canvas.Width <= 0) return;
            CommitAndBakePreview();
            decimal unitsPerPixel = BaseScale / _zoom / canvas.Width;
            _centerX -= (e.X - _panStart.X) * unitsPerPixel;
            _centerY += (e.Y - _panStart.Y) * unitsPerPixel;
            _panStart = e.Location;
            canvas.Invalidate();
            ScheduleRender();
        }

        /// <summary>
        /// Обрабатывает отпускание кнопки мыши, завершая режим панорамирования.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (_isHighResRendering) return;
            if (e.Button == MouseButtons.Left)
            {
                _panning = false;
                canvas.Cursor = Cursors.Default;
            }
        }

        /// <summary>
        /// Обрабатывает перерисовку холста, отображая текущее состояние фрактала.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события отрисовки.</param>
        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            if (canvas.Width <= 0 || canvas.Height <= 0)
            {
                e.Graphics.Clear(Color.Black);
                return;
            }
            e.Graphics.Clear(Color.Black);
            Rectangle currentViewportRect = GetCurrentViewportRect();
            lock (_bitmapLock)
            {
                if (_previewBitmap != null)
                {
                    if (_renderedCenterX == _centerX && _renderedCenterY == _centerY && _renderedZoom == _zoom)
                    {
                        Rectangle sourceViewport = GetSafeViewportRect(_previewBitmap.Size, currentViewportRect);
                        DrawPreviewBitmap(e.Graphics, _previewBitmap, sourceViewport, canvas.ClientRectangle);
                    }
                    else
                    {
                        DrawTransformedPreview(e.Graphics, _previewBitmap, canvas.ClientRectangle);
                    }
                }
                if (_currentRenderingBitmap != null)
                {
                    DrawVisibleBitmap(e.Graphics, _currentRenderingBitmap, currentViewportRect, canvas.ClientRectangle);
                }
            }
            if (_renderVisualizer != null && _isRenderingPreview)
            {
                _renderVisualizer.DrawVisualization(e.Graphics);
            }
        }
        #endregion

        #region Rendering Logic

        /// <summary>
        /// Асинхронно запускает рендеринг предпросмотра с использованием суперсэмплинга (SSAA).
        /// </summary>
        /// <param name="ssaaFactor">Фактор суперсэмплинга (например, 2 для 2x2 выборки на пиксель).</param>
        private async Task StartPreviewRenderSSAA(int ssaaFactor)
        {
            Size renderSurfaceSize = GetRenderSurfaceSize();
            Rectangle viewportRect = GetCurrentViewportRect();
            if (renderSurfaceSize.Width <= 0 || renderSurfaceSize.Height <= 0 || viewportRect.Width <= 0 || viewportRect.Height <= 0) return;

            int currentWidth = renderSurfaceSize.Width;
            int currentHeight = renderSurfaceSize.Height;

            var stopwatch = Stopwatch.StartNew();
            _isRenderingPreview = true;
            _previewRenderCts?.Cancel();
            _previewRenderCts = new CancellationTokenSource();
            var token = _previewRenderCts.Token;
            _renderVisualizer?.NotifyRenderSessionStart();

            var newRenderingBitmap = new Bitmap(currentWidth, currentHeight, PixelFormat.Format32bppArgb);
            lock (_bitmapLock)
            {
                _currentRenderingBitmap = newRenderingBitmap;
            }
            UpdateEngineParameters();
            var currentRenderedCenterX = _centerX;
            var currentRenderedCenterY = _centerY;
            var currentRenderedZoom = _zoom;
            var renderEngineCopy = CreateEngine();
            // Копируем все параметры в новый экземпляр движка
            renderEngineCopy.MaxIterations = _fractalEngine.MaxIterations;
            renderEngineCopy.ThresholdSquared = _fractalEngine.ThresholdSquared;
            renderEngineCopy.CenterX = GetRenderSurfaceCenterX(renderSurfaceSize, viewportRect);
            renderEngineCopy.CenterY = _fractalEngine.CenterY;
            renderEngineCopy.Scale = GetRenderSurfaceScale(renderSurfaceSize, viewportRect);
            renderEngineCopy.C = _fractalEngine.C;
            renderEngineCopy.UseSmoothColoring = _fractalEngine.UseSmoothColoring;
            renderEngineCopy.Palette = _fractalEngine.Palette;
            renderEngineCopy.SmoothPalette = _fractalEngine.SmoothPalette;
            renderEngineCopy.MaxColorIterations = _fractalEngine.MaxColorIterations;
            renderEngineCopy.ActiveMode = _fractalEngine.ActiveMode;
            renderEngineCopy.HistogramEnabledEqualization = _fractalEngine.HistogramEnabledEqualization;
            renderEngineCopy.HistogramContrast = _fractalEngine.HistogramContrast;
            renderEngineCopy.HistogramInputUseSmooth = _fractalEngine.HistogramInputUseSmooth;
            renderEngineCopy.OrbitTrapStrength = _fractalEngine.OrbitTrapStrength;
            renderEngineCopy.OrbitTrapBias = _fractalEngine.OrbitTrapBias;
            renderEngineCopy.StripeFrequency = _fractalEngine.StripeFrequency;
            renderEngineCopy.StripeStrength = _fractalEngine.StripeStrength;
            renderEngineCopy.StripeBias = _fractalEngine.StripeBias;
            renderEngineCopy.InteriorColor = _fractalEngine.InteriorColor;
            renderEngineCopy.CopySpecificParametersFrom(_fractalEngine);

            var threadCount = GetThreadCount();
            int tileConcurrency = threadCount;
            int inTileConcurrency = 1;
            var tiles = GenerateTiles(currentWidth, currentHeight);
            var dispatcher = new TileRenderDispatcher(tiles, tileConcurrency, RenderPatternSettings.SelectedPattern);

            if (pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed)
            {
                pbRenderProgress.Invoke((Action)(() => { pbRenderProgress.Value = 0; pbRenderProgress.Maximum = tiles.Count; }));
            }
            int progress = 0;
            try
            {
                await dispatcher.RenderAsync(async (tile, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    _renderVisualizer?.NotifyTileRenderStart(tile.Bounds);

                    // Не допускаем вложенный oversubscription: параллелим по плиткам, внутри плитки — один поток.
                    var tileBuffer = renderEngineCopy.RenderSingleTileSSAA(tile, currentWidth, currentHeight, ssaaFactor, inTileConcurrency, out int bytesPerPixel);

                    ct.ThrowIfCancellationRequested();
                    lock (_bitmapLock)
                    {
                        if (ct.IsCancellationRequested || _currentRenderingBitmap != newRenderingBitmap) return;
                        var tileRect = tile.Bounds;
                        var bitmapRect = new Rectangle(0, 0, _currentRenderingBitmap.Width, _currentRenderingBitmap.Height);
                        tileRect.Intersect(bitmapRect);
                        if (tileRect.Width == 0 || tileRect.Height == 0) return;
                        BitmapData bmpData = _currentRenderingBitmap.LockBits(tileRect, ImageLockMode.WriteOnly, _currentRenderingBitmap.PixelFormat);
                        try
                        {
                            for (int y = 0; y < tileRect.Height; y++)
                            {
                                IntPtr destPtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                                int srcOffset = y * tileRect.Width * bytesPerPixel;
                                Marshal.Copy(tileBuffer, srcOffset, destPtr, tileRect.Width * bytesPerPixel);
                            }
                        }
                        finally
                        {
                            _currentRenderingBitmap.UnlockBits(bmpData);
                        }
                    }
                    _renderVisualizer?.NotifyTileRenderComplete(tile.Bounds);
                    if (ct.IsCancellationRequested || !canvas.IsHandleCreated || canvas.IsDisposed) return;
                    canvas.Invoke((Action)(() =>
                    {
                        if (!ct.IsCancellationRequested && pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed)
                        {
                            pbRenderProgress.Value = Math.Min(pbRenderProgress.Maximum, Interlocked.Increment(ref progress));
                        }
                    }));
                    await Task.Yield();
                }, token);

                token.ThrowIfCancellationRequested();
                stopwatch.Stop();
                this.Text = $"{_baseTitle} - Время рендера (SSAA {ssaaFactor}x): {stopwatch.Elapsed.TotalSeconds:F3} сек.";
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _previewBitmap?.Dispose();
                        _previewBitmap = _currentRenderingBitmap;
                        _currentRenderingBitmap = null;
                        _renderedCenterX = currentRenderedCenterX;
                        _renderedCenterY = currentRenderedCenterY;
                        _renderedZoom = currentRenderedZoom;
                        _renderedViewportRect = viewportRect;
                    }
                    else
                    {
                        newRenderingBitmap?.Dispose();
                    }
                }
                if (canvas.IsHandleCreated && !canvas.IsDisposed) canvas.Invalidate();
            }
            catch (OperationCanceledException)
            {
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _currentRenderingBitmap = null;
                    }
                }
                newRenderingBitmap.Dispose();
            }
            catch (Exception ex)
            {
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _currentRenderingBitmap = null;
                    }
                }
                newRenderingBitmap?.Dispose();
                if (IsHandleCreated && !IsDisposed) MessageBox.Show($"Ошибка рендеринга SSAA: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isRenderingPreview = false;
                _renderVisualizer?.NotifyRenderSessionComplete();
                if (pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed) pbRenderProgress.Invoke((Action)(() => pbRenderProgress.Value = 0));
            }
        }

        /// <summary>
        /// Асинхронно запускает стандартный рендеринг предпросмотра (без SSAA).
        /// </summary>
        private async Task StartPreviewRender()
        {
            Size renderSurfaceSize = GetRenderSurfaceSize();
            Rectangle viewportRect = GetCurrentViewportRect();
            if (renderSurfaceSize.Width <= 0 || renderSurfaceSize.Height <= 0 || viewportRect.Width <= 0 || viewportRect.Height <= 0) return;

            int currentWidth = renderSurfaceSize.Width;
            int currentHeight = renderSurfaceSize.Height;

            var stopwatch = Stopwatch.StartNew();
            _isRenderingPreview = true;
            _previewRenderCts?.Cancel();
            _previewRenderCts = new CancellationTokenSource();
            var token = _previewRenderCts.Token;
            _renderVisualizer?.NotifyRenderSessionStart();

            var newRenderingBitmap = new Bitmap(currentWidth, currentHeight, PixelFormat.Format32bppArgb);
            lock (_bitmapLock)
            {
                _currentRenderingBitmap = newRenderingBitmap;
            }
            UpdateEngineParameters();
            var currentRenderedCenterX = _centerX;
            var currentRenderedCenterY = _centerY;
            var currentRenderedZoom = _zoom;
            var renderEngineCopy = CreateEngine();
            // Копируем все параметры в новый экземпляр движка
            renderEngineCopy.MaxIterations = _fractalEngine.MaxIterations;
            renderEngineCopy.ThresholdSquared = _fractalEngine.ThresholdSquared;
            renderEngineCopy.CenterX = GetRenderSurfaceCenterX(renderSurfaceSize, viewportRect);
            renderEngineCopy.CenterY = _fractalEngine.CenterY;
            renderEngineCopy.Scale = GetRenderSurfaceScale(renderSurfaceSize, viewportRect);
            renderEngineCopy.C = _fractalEngine.C;
            renderEngineCopy.UseSmoothColoring = _fractalEngine.UseSmoothColoring;
            renderEngineCopy.Palette = _fractalEngine.Palette;
            renderEngineCopy.SmoothPalette = _fractalEngine.SmoothPalette;
            renderEngineCopy.MaxColorIterations = _fractalEngine.MaxColorIterations;
            renderEngineCopy.ActiveMode = _fractalEngine.ActiveMode;
            renderEngineCopy.HistogramEnabledEqualization = _fractalEngine.HistogramEnabledEqualization;
            renderEngineCopy.HistogramContrast = _fractalEngine.HistogramContrast;
            renderEngineCopy.HistogramInputUseSmooth = _fractalEngine.HistogramInputUseSmooth;
            renderEngineCopy.OrbitTrapStrength = _fractalEngine.OrbitTrapStrength;
            renderEngineCopy.OrbitTrapBias = _fractalEngine.OrbitTrapBias;
            renderEngineCopy.StripeFrequency = _fractalEngine.StripeFrequency;
            renderEngineCopy.StripeStrength = _fractalEngine.StripeStrength;
            renderEngineCopy.StripeBias = _fractalEngine.StripeBias;
            renderEngineCopy.InteriorColor = _fractalEngine.InteriorColor;
            renderEngineCopy.CopySpecificParametersFrom(_fractalEngine);

            var threadCount = GetThreadCount();
            var tiles = GenerateTiles(currentWidth, currentHeight);
            var dispatcher = new TileRenderDispatcher(tiles, threadCount, RenderPatternSettings.SelectedPattern);

            if (pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed)
            {
                pbRenderProgress.Invoke((Action)(() => { pbRenderProgress.Value = 0; pbRenderProgress.Maximum = tiles.Count; }));
            }
            int progress = 0;
            try
            {
                await dispatcher.RenderAsync(async (tile, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    _renderVisualizer?.NotifyTileRenderStart(tile.Bounds);

                    var tileBuffer = renderEngineCopy.RenderSingleTile(tile, currentWidth, currentHeight, out int bytesPerPixel);

                    ct.ThrowIfCancellationRequested();
                    lock (_bitmapLock)
                    {
                        if (ct.IsCancellationRequested || _currentRenderingBitmap != newRenderingBitmap) return;
                        var tileRect = tile.Bounds;
                        var bitmapRect = new Rectangle(0, 0, _currentRenderingBitmap.Width, _currentRenderingBitmap.Height);
                        tileRect.Intersect(bitmapRect);
                        if (tileRect.Width == 0 || tileRect.Height == 0) return;
                        BitmapData bmpData = _currentRenderingBitmap.LockBits(tileRect, ImageLockMode.WriteOnly, _currentRenderingBitmap.PixelFormat);
                        try
                        {
                            int originalTileWidthInBytes = tile.Bounds.Width * bytesPerPixel;
                            for (int y = 0; y < tileRect.Height; y++)
                            {
                                IntPtr destPtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);
                                int srcOffset = ((y + tileRect.Y) - tile.Bounds.Y) * originalTileWidthInBytes + ((tileRect.X - tile.Bounds.X) * bytesPerPixel);
                                Marshal.Copy(tileBuffer, srcOffset, destPtr, tileRect.Width * bytesPerPixel);
                            }
                        }
                        finally
                        {
                            _currentRenderingBitmap.UnlockBits(bmpData);
                        }
                    }
                    _renderVisualizer?.NotifyTileRenderComplete(tile.Bounds);
                    if (ct.IsCancellationRequested || !canvas.IsHandleCreated || canvas.IsDisposed) return;
                    canvas.Invoke((Action)(() =>
                    {
                        if (!ct.IsCancellationRequested && pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed)
                        {
                            pbRenderProgress.Value = Math.Min(pbRenderProgress.Maximum, Interlocked.Increment(ref progress));
                        }
                    }));
                    await Task.Yield();
                }, token);

                token.ThrowIfCancellationRequested();
                stopwatch.Stop();
                this.Text = $"{_baseTitle} - Время последнего рендера: {stopwatch.Elapsed.TotalSeconds:F3} сек.";
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _previewBitmap?.Dispose();
                        _previewBitmap = _currentRenderingBitmap;
                        _currentRenderingBitmap = null;
                        _renderedCenterX = currentRenderedCenterX;
                        _renderedCenterY = currentRenderedCenterY;
                        _renderedZoom = currentRenderedZoom;
                        _renderedViewportRect = viewportRect;
                    }
                    else
                    {
                        newRenderingBitmap?.Dispose();
                    }
                }
                if (canvas.IsHandleCreated && !canvas.IsDisposed) canvas.Invalidate();
            }
            catch (OperationCanceledException)
            {
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _currentRenderingBitmap = null;
                    }
                }
                newRenderingBitmap.Dispose();
            }
            catch (Exception ex)
            {
                lock (_bitmapLock)
                {
                    if (_currentRenderingBitmap == newRenderingBitmap)
                    {
                        _currentRenderingBitmap = null;
                    }
                }
                newRenderingBitmap?.Dispose();
                if (IsHandleCreated && !IsDisposed) MessageBox.Show($"Ошибка рендеринга: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isRenderingPreview = false;
                _renderVisualizer?.NotifyRenderSessionComplete();
                if (pbRenderProgress.IsHandleCreated && !pbRenderProgress.IsDisposed) pbRenderProgress.Invoke((Action)(() => pbRenderProgress.Value = 0));
            }
        }

        /// <summary>
        /// Обрабатывает событие тика таймера отложенного рендеринга.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private async void RenderDebounceTimer_Tick(object sender, EventArgs e)
        {
            _renderDebounceTimer.Stop();
            if (_isHighResRendering || _isRenderingPreview)
            {
                ScheduleRender();
                return;
            }
            int ssaaFactor = GetSelectedSsaaFactor();
            this.Text = $"{_baseTitle} - Качество: {ssaaFactor}x";
            if (ssaaFactor > 1) await StartPreviewRenderSSAA(ssaaFactor);
            else await StartPreviewRender();
        }

        /// <summary>
        /// Обрабатывает запрос на перерисовку от визуализатора рендеринга.
        /// </summary>
        private void OnVisualizerNeedsRedraw()
        {
            if (canvas.IsHandleCreated && !canvas.IsDisposed)
            {
                canvas.BeginInvoke((Action)(() => canvas.Invalidate()));
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Генерирует список плиток (тайлов) для рендеринга изображения в порядке обычного обхода сетки.
        /// Итоговая стратегия распределения задается в <see cref="TileRenderDispatcher"/>.
        /// </summary>
        /// <param name="width">Ширина изображения в пикселях.</param>
        /// <param name="height">Высота изображения в пикселях.</param>
        /// <returns>Список объектов <see cref="TileInfo"/>.</returns>
        private List<TileInfo> GenerateTiles(int width, int height)
        {
            var tiles = new List<TileInfo>();
            for (int y = 0; y < height; y += TILE_SIZE)
            {
                for (int x = 0; x < width; x += TILE_SIZE)
                {
                    tiles.Add(new TileInfo(x, y, TILE_SIZE, TILE_SIZE));
                }
            }
            return tiles;
        }

        /// <summary>
        /// Планирует запуск рендеринга с небольшой задержкой для предотвращения лишних перерисовок.
        /// </summary>
        private void ScheduleRender()
        {
            if (_isHighResRendering || WindowState == FormWindowState.Minimized) return;
            if (_isRenderingPreview) _previewRenderCts?.Cancel();
            _renderDebounceTimer.Stop();
            _renderDebounceTimer.Start();
        }

        /// <summary>
        /// Завершает текущий рендеринг и "запекает" его результат в основной битмап предпросмотра.
        /// </summary>
        private void CommitAndBakePreview()
        {
            lock (_bitmapLock) { if (!_isRenderingPreview || _currentRenderingBitmap == null) return; }
            _previewRenderCts?.Cancel();
            lock (_bitmapLock)
            {
                Size renderSurfaceSize = GetRenderSurfaceSize();
                Rectangle viewportRect = GetCurrentViewportRect();
                if (_currentRenderingBitmap == null || renderSurfaceSize.Width <= 0 || renderSurfaceSize.Height <= 0 || viewportRect.Width <= 0 || viewportRect.Height <= 0) return;
                var bakedBitmap = new Bitmap(renderSurfaceSize.Width, renderSurfaceSize.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bakedBitmap))
                {
                    g.Clear(Color.Black);
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    if (_previewBitmap != null)
                    {
                        try
                        {
                            decimal renderedComplexWidth = BaseScale / _renderedZoom;
                            decimal currentComplexWidth = BaseScale / _zoom;
                            if (!(_renderedZoom <= 0 || _zoom <= 0 || renderedComplexWidth <= 0 || currentComplexWidth <= 0))
                            {
                                Rectangle sourceViewport = GetSafeViewportRect(_previewBitmap.Size, _renderedViewportRect);
                                decimal unitsPerPixelRendered = renderedComplexWidth / sourceViewport.Width;
                                decimal unitsPerPixelCurrent = currentComplexWidth / viewportRect.Width;
                                decimal renderedReMin = _renderedCenterX - (renderedComplexWidth / 2.0m);
                                decimal renderedImMax = _renderedCenterY + (sourceViewport.Height * unitsPerPixelRendered / 2.0m);
                                decimal currentReMin = _centerX - (currentComplexWidth / 2.0m);
                                decimal currentImMax = _centerY + (viewportRect.Height * unitsPerPixelCurrent / 2.0m);
                                decimal offsetXPixels = (renderedReMin - currentReMin) / unitsPerPixelCurrent;
                                decimal offsetYPixels = (currentImMax - renderedImMax) / unitsPerPixelCurrent;
                                decimal newWidthPixels = sourceViewport.Width * (unitsPerPixelRendered / unitsPerPixelCurrent);
                                decimal newHeightPixels = sourceViewport.Height * (unitsPerPixelRendered / unitsPerPixelCurrent);
                                PointF destPoint1 = new PointF(viewportRect.X + (float)offsetXPixels, viewportRect.Y + (float)offsetYPixels);
                                PointF destPoint2 = new PointF(viewportRect.X + (float)(offsetXPixels + newWidthPixels), viewportRect.Y + (float)offsetYPixels);
                                PointF destPoint3 = new PointF(viewportRect.X + (float)offsetXPixels, viewportRect.Y + (float)(offsetYPixels + newHeightPixels));
                                g.DrawImage(_previewBitmap, new PointF[] { destPoint1, destPoint2, destPoint3 }, sourceViewport, GraphicsUnit.Pixel);
                            }
                        }
                        catch (Exception) { }
                    }
                    g.DrawImageUnscaled(_currentRenderingBitmap, Point.Empty);
                }
                _previewBitmap?.Dispose();
                _previewBitmap = bakedBitmap;
                // ВАЖНО: здесь только detach. Фоновые worker'ы могут еще завершаться и
                // писать в старый bitmap под _bitmapLock, поэтому немедленный Dispose опасен.
                // Реальный Dispose выполняется в безопасной точке жизненного цикла формы (cleanup/закрытие).
                _currentRenderingBitmap = null;
                _renderedCenterX = _centerX;
                _renderedCenterY = _centerY;
                _renderedZoom = _zoom;
                _renderedViewportRect = viewportRect;
            }
        }

        private void Canvas_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized) return;
            if (_suppressResizeRender)
            {
                canvas.Invalidate();
                return;
            }

            ScheduleRender();
        }

        private void btnToggleControls_Click(object sender, EventArgs e)
        {
            ToggleControlsPanel();
        }

        private void CanvasHost_Resize(object sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
        }

        private void ControlsHost_SizeChanged(object sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
        }

        private void UpdateToggleControlsPosition()
        {
            int targetX = ToggleButtonMargin;
            if (_controlsPanelVisible)
            {
                targetX = controlsHost.Right + ToggleButtonMargin;
            }

            int maxX = Math.Max(ToggleButtonMargin, canvasHost.ClientSize.Width - btnToggleControls.Width - ToggleButtonMargin);
            btnToggleControls.Location = new Point(Math.Min(targetX, maxX), ToggleButtonMargin);
            btnToggleControls.BringToFront();
        }

        private void ToggleControlsPanel()
        {
            _controlsPanelVisible = !_controlsPanelVisible;
            btnToggleControls.Text = _controlsPanelVisible ? "✕" : "☰";
            _suppressResizeRender = true;
            try
            {
                controlsHost.Visible = _controlsPanelVisible;
                UpdateToggleControlsPosition();
                canvas.Invalidate();
            }
            finally
            {
                _suppressResizeRender = false;
            }
        }

        private Size GetRenderSurfaceSize()
        {
            return canvas.ClientSize;
        }

        private Rectangle GetCurrentViewportRect()
        {
            return new Rectangle(0, 0, Math.Max(0, canvas.Width), Math.Max(0, canvas.Height));
        }

        private decimal GetRenderSurfaceCenterX(Size renderSurfaceSize, Rectangle viewportRect)
        {
            if (renderSurfaceSize.Width <= 0 || viewportRect.Width <= 0) return _centerX;
            decimal unitsPerPixel = (BaseScale / _zoom) / viewportRect.Width;
            decimal viewportCenterOffset = (viewportRect.X + (viewportRect.Width / 2.0m)) - (renderSurfaceSize.Width / 2.0m);
            return _centerX - (viewportCenterOffset * unitsPerPixel);
        }

        private decimal GetRenderSurfaceScale(Size renderSurfaceSize, Rectangle viewportRect)
        {
            if (renderSurfaceSize.Width <= 0 || viewportRect.Width <= 0) return BaseScale / _zoom;
            return (BaseScale / _zoom) * renderSurfaceSize.Width / viewportRect.Width;
        }

        private Rectangle GetSafeViewportRect(Size bitmapSize, Rectangle viewportRect)
        {
            var safeRect = viewportRect;
            if (safeRect.Width <= 0 || safeRect.Height <= 0)
            {
                safeRect = new Rectangle(Point.Empty, bitmapSize);
            }

            safeRect.Intersect(new Rectangle(Point.Empty, bitmapSize));
            if (safeRect.Width <= 0 || safeRect.Height <= 0)
            {
                safeRect = new Rectangle(Point.Empty, bitmapSize);
            }

            return safeRect;
        }

        private void DrawVisibleBitmap(Graphics graphics, Bitmap bitmap, Rectangle sourceViewport, Rectangle destRect)
        {
            Rectangle safeSourceRect = GetSafeViewportRect(bitmap.Size, sourceViewport);
            DrawPreviewBitmap(graphics, bitmap, safeSourceRect, destRect);
        }

        private void DrawPreviewBitmap(Graphics graphics, Bitmap bitmap, Rectangle sourceViewport, Rectangle destRect)
        {
            if (destRect.Width <= 0 || destRect.Height <= 0 || sourceViewport.Width <= 0 || sourceViewport.Height <= 0) return;
            graphics.DrawImage(bitmap, destRect, sourceViewport, GraphicsUnit.Pixel);
        }

        private void DrawTransformedPreview(Graphics graphics, Bitmap bitmap, Rectangle destRect)
        {
            try
            {
                decimal renderedComplexWidth = BaseScale / _renderedZoom;
                decimal currentComplexWidth = BaseScale / _zoom;
                if (_renderedZoom <= 0 || _zoom <= 0 || renderedComplexWidth <= 0 || currentComplexWidth <= 0 || destRect.Width <= 0 || destRect.Height <= 0) return;

                Rectangle sourceViewport = GetSafeViewportRect(bitmap.Size, _renderedViewportRect);
                decimal unitsPerPixelRendered = renderedComplexWidth / sourceViewport.Width;
                decimal unitsPerPixelCurrent = currentComplexWidth / destRect.Width;
                decimal renderedReMin = _renderedCenterX - (renderedComplexWidth / 2.0m);
                decimal renderedImMax = _renderedCenterY + (sourceViewport.Height * unitsPerPixelRendered / 2.0m);
                decimal currentReMin = _centerX - (currentComplexWidth / 2.0m);
                decimal currentImMax = _centerY + (destRect.Height * unitsPerPixelCurrent / 2.0m);
                decimal offsetXPixels = (renderedReMin - currentReMin) / unitsPerPixelCurrent;
                decimal offsetYPixels = (currentImMax - renderedImMax) / unitsPerPixelCurrent;
                decimal newWidthPixels = sourceViewport.Width * (unitsPerPixelRendered / unitsPerPixelCurrent);
                decimal newHeightPixels = sourceViewport.Height * (unitsPerPixelRendered / unitsPerPixelCurrent);

                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                PointF destPoint1 = new PointF(destRect.X + (float)offsetXPixels, destRect.Y + (float)offsetYPixels);
                PointF destPoint2 = new PointF(destRect.X + (float)(offsetXPixels + newWidthPixels), destRect.Y + (float)offsetYPixels);
                PointF destPoint3 = new PointF(destRect.X + (float)offsetXPixels, destRect.Y + (float)(offsetYPixels + newHeightPixels));
                graphics.DrawImage(bitmap, new PointF[] { destPoint1, destPoint2, destPoint3 }, sourceViewport, GraphicsUnit.Pixel);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Открывает менеджер сохранения изображений.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnOpenSaveManager_Click(object sender, EventArgs e)
        {
            if (_isHighResRendering)
            {
                MessageBox.Show("Процесс рендеринга уже запущен.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var saveManager = new SaveImageManagerForm(this))
            {
                saveManager.ShowDialog(this);
            }
        }

        /// <summary>
        /// Обновляет параметры движка рендеринга на основе значений из элементов управления UI.
        /// </summary>
        private void UpdateEngineParameters()
        {
            _fractalEngine.MaxIterations = (int)nudIterations.Value;
            _fractalEngine.ThresholdSquared = nudThreshold.Value * nudThreshold.Value;
            _fractalEngine.CenterX = _centerX;
            _fractalEngine.CenterY = _centerY;
            _fractalEngine.Scale = BaseScale / _zoom;

            _fractalEngine.UseSmoothColoring = IsSmoothColoringEnabled();
            ApplyColoringRuntimeToEngine(_fractalEngine);

            UpdateEngineSpecificParameters();
            ApplyActivePalette();
        }

        private void ApplyColoringRuntimeToEngine(FractalMandelbrotFamilyEngine engine)
        {
            engine.ActiveMode = (FractalExplorer.Engines.ColoringModeType)_coloringRuntimeState.ActiveMode.ModeType;
            engine.HistogramEnabledEqualization = _coloringRuntimeState.HistogramSettings.EnabledEqualization;
            engine.HistogramContrast = _coloringRuntimeState.HistogramSettings.Contrast;
            engine.HistogramInputUseSmooth = _coloringRuntimeState.HistogramSettings.InputUseSmooth;
            engine.OrbitTrapStrength = _coloringRuntimeState.OrbitTrapSettings.Strength;
            engine.OrbitTrapBias = _coloringRuntimeState.OrbitTrapSettings.Bias;
            engine.StripeFrequency = _coloringRuntimeState.StripeAverageSettings.Frequency;
            engine.StripeStrength = _coloringRuntimeState.StripeAverageSettings.Strength;
            engine.StripeBias = _coloringRuntimeState.StripeAverageSettings.Bias;
            engine.InteriorColor = ResolveInteriorColor();
        }

        private Color ResolveInteriorColor()
        {
            return _coloringRuntimeState.InteriorMode == InteriorMode.Custom
                ? _coloringRuntimeState.InteriorColor
                : Color.Black;
        }

        /// <summary>
        /// Получает выбранный пользователем фактор суперсэмплинга (SSAA) из выпадающего списка.
        /// </summary>
        /// <returns>Целочисленный фактор SSAA (1, 2 или 4).</returns>
        private int GetSelectedSsaaFactor()
        {
            var cbSSAA = this.Controls.Find("cbSSAA", true).FirstOrDefault() as ComboBox;
            if (cbSSAA == null) return 1;
            Func<int> getFactor = () =>
            {
                if (cbSSAA.SelectedItem == null) return 1;
                switch (cbSSAA.SelectedItem.ToString())
                {
                    case "Низкое (2x)": return 2;
                    case "Высокое (4x)": return 4;
                    default: return 1;
                }
            };
            return cbSSAA.InvokeRequired ? (int)cbSSAA.Invoke(getFactor) : getFactor();
        }

        /// <summary>
        /// Получает выбранное пользователем количество потоков для рендеринга.
        /// </summary>
        /// <returns>Количество потоков.</returns>
        private int GetThreadCount()
        {
            return cbThreads.SelectedItem?.ToString() == "Auto" ? Environment.ProcessorCount : Convert.ToInt32(cbThreads.SelectedItem);
        }

        /// <summary>
        /// Определяет, включен ли плавный режим окрашивания.
        /// </summary>
        /// <returns>True, если выбран режим "Плавно".</returns>
        private bool IsSmoothColoringEnabled()
        {
            return _coloringRuntimeState.UseSmoothColoring;
        }

        /// <summary>
        /// Ограничивает значение типа `decimal` заданным диапазоном.
        /// </summary>
        /// <param name="value">Исходное значение.</param>
        /// <param name="min">Минимально допустимое значение.</param>
        /// <param name="max">Максимально допустимое значение.</param>
        /// <returns>Ограниченное значение.</returns>
        private decimal ClampDecimal(decimal value, decimal min, decimal max) => Math.Max(min, Math.Min(max, value));

        /// <summary>
        /// Ограничивает значение типа `int` заданным диапазоном.
        /// </summary>
        /// <param name="value">Исходное значение.</param>
        /// <param name="min">Минимально допустимое значение.</param>
        /// <param name="max">Максимально допустимое значение.</param>
        /// <returns>Ограниченное значение.</returns>
        private int ClampInt(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        /// <inheritdoc/>
        public double LoupeZoom => nudBaseScale != null ? (double)nudBaseScale.Value : 4.0;

        /// <inheritdoc/>
        public event EventHandler LoupeZoomChanged;

        #endregion

        #region Palette Management

        /// <summary>
        /// Генерирует функцию для плавной окраски на основе заданной палитры.
        /// </summary>
        /// <param name="palette">Палитра цветов.</param>
        /// <param name="effectiveMaxColorIterations">Эффективное максимальное количество итераций для цикла палитры.</param>
        /// <returns>Функция, принимающая значение итерации и возвращающая цвет.</returns>
        protected Func<double, Color> GenerateSmoothPaletteFunction(Palette palette, int effectiveMaxColorIterations)
        {
            // Получаем общие свойства палитры
            double gamma = palette.Gamma;
            var colors = new List<Color>(palette.Colors);
            int colorCount = colors.Count;
            Color interiorColor = ResolveInteriorColor();

            // Эта палитра должна быть плавной по всему диапазону итераций,
            // поэтому она намеренно ИГНОРИРУЕТ effectiveMaxColorIterations, чтобы избежать "ломаных" переходов.
            if (palette.Name == "Стандартный серый")
            {
                return (smoothIter) =>
                {
                    smoothIter += _coloringRuntimeState.SmoothIterationOffset;
                    if (smoothIter >= _fractalEngine.MaxIterations) return interiorColor;
                    if (smoothIter < 0) smoothIter = 0;

                    // 1. Используем ОБЩЕЕ количество итераций рендера для масштабирования.
                    //    Это ключ к плавному градиенту без разрывов.
                    double logMax = Math.Log(_fractalEngine.MaxIterations + 1);
                    if (logMax <= 0) return interiorColor; // Защита от деления на ноль

                    // 2. Берем логарифм от ПРЯМОГО значения итерации (без цикличности).
                    double tLog = Math.Log(smoothIter + 1) / logMax;
                    tLog = Math.Pow(tLog, _coloringRuntimeState.SmoothBlendPower);
                    tLog = TransformPaletteIndex(tLog);

                    // 3. Инвертируем значение для градиента от почти белого к черному.
                    int gray_level = (int)(255.0 * (1.0 - tLog));
                    gray_level = Math.Max(0, Math.Min(255, gray_level));

                    // 4. Создаем цвет и применяем гамма-коррекцию.
                    Color baseColor = Color.FromArgb(gray_level, gray_level, gray_level);
                    return ColorCorrection.ApplyGamma(baseColor, gamma);
                };
            }

            // Эта логика корректно использует effectiveMaxColorIterations для повторения палитры.

            // Защита от деления на ноль, если период не задан.
            if (effectiveMaxColorIterations <= 0)
            {
                return (smoothIter) => interiorColor;
            }

            // Обработка крайних случаев.
            if (colorCount == 0) return (smoothIter) => interiorColor;
            if (colorCount == 1) return (smoothIter) => (smoothIter >= _fractalEngine.MaxIterations) ? interiorColor : ColorCorrection.ApplyGamma(colors[0], gamma);

            // Общая логика для всех цветных градиентных палитр.
            return (smoothIter) =>
            {
                smoothIter += _coloringRuntimeState.SmoothIterationOffset;
                if (smoothIter >= _fractalEngine.MaxIterations) return interiorColor;
                if (smoothIter < 0) smoothIter = 0;

                // 1. Берем остаток от деления, чтобы получить значение внутри одного цикла/периода.
                double cyclicIter = smoothIter % effectiveMaxColorIterations;

                // 2. Нормализуем его по отношению к длине этого периода.
                //    (double) необходимо для корректного деления.
                double t = cyclicIter / (double)effectiveMaxColorIterations;
                t = Math.Pow(t, _coloringRuntimeState.SmoothBlendPower);
                t = TransformPaletteIndex(t);

                // 3. Остальная логика интерполяции цвета остается без изменений.
                t = Math.Max(0.0, Math.Min(1.0, t));

                double scaledT = t * (colorCount - 1);
                int index1 = (int)Math.Floor(scaledT);
                int index2 = Math.Min(index1 + 1, colorCount - 1);
                double localT = scaledT - index1;

                Color baseColor = LerpColor(colors[index1], colors[index2], localT);
                return ColorCorrection.ApplyGamma(baseColor, gamma);
            };
        }

        /// <summary>
        /// Генерирует уникальную "подпись" для палитры на основе ее параметров.
        /// </summary>
        /// <param name="palette">Менеджер палитры.</param>
        /// <param name="maxIterationsForAlignment">Максимальное количество итераций рендера для выравнивания.</param>
        /// <returns>Строковая подпись палитры.</returns>
        private string GeneratePaletteSignature(Palette palette, int maxIterationsForAlignment)
        {
            var sb = new StringBuilder();
            sb.Append(palette.Name);
            sb.Append(':');
            foreach (var color in palette.Colors) sb.Append(color.ToArgb().ToString("X8"));
            sb.Append(':');
            sb.Append(palette.IsGradient);
            sb.Append(':');
            sb.Append(palette.Gamma.ToString("F2"));
            sb.Append(':');
            int effectiveMaxColorIterations = palette.AlignWithRenderIterations ? maxIterationsForAlignment : palette.MaxColorIterations;
            sb.Append(effectiveMaxColorIterations);
            sb.Append(':');
            sb.Append(_coloringRuntimeState.PaletteTransform.PhaseOffset.ToString("F4"));
            sb.Append(':');
            sb.Append(_coloringRuntimeState.PaletteTransform.Scale.ToString("F4"));
            sb.Append(':');
            sb.Append((int)_coloringRuntimeState.PaletteTransform.WrapMode);
            return sb.ToString();
        }

        private double TransformPaletteIndex(double t)
        {
            double scale = _coloringRuntimeState.PaletteTransform.Scale;
            if (Math.Abs(scale) < 1e-9) scale = 1.0;

            double transformed = t * scale + _coloringRuntimeState.PaletteTransform.PhaseOffset;
            return _coloringRuntimeState.PaletteTransform.WrapMode switch
            {
                PaletteTransformWrapMode.Clamp => Math.Max(0.0, Math.Min(1.0, transformed)),
                PaletteTransformWrapMode.Mirror => Mirror01(transformed),
                _ => Repeat01(transformed)
            };
        }

        private static double Repeat01(double t)
        {
            t -= Math.Floor(t);
            return t;
        }

        private static double Mirror01(double t)
        {
            double period = t % 2.0;
            if (period < 0.0) period += 2.0;
            return period <= 1.0 ? period : 2.0 - period;
        }

        /// <summary>
        /// Выполняет линейную интерполяцию между двумя цветами.
        /// </summary>
        /// <param name="a">Первый цвет.</param>
        /// <param name="b">Второй цвет.</param>
        /// <param name="t">Коэффициент интерполяции (от 0 до 1).</param>
        /// <returns>Интерполированный цвет.</returns>
        private Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// Генерирует функцию для дискретной окраски на основе заданной палитры.
        /// </summary>
        /// <param name="palette">Палитра цветов.</param>
        /// <returns>Функция, принимающая номер итерации, макс. итераций и макс. итераций цвета и возвращающая цвет.</returns>
        protected Func<int, int, int, Color> GenerateDiscretePaletteFunction(Palette palette)
        {
            double gamma = palette.Gamma;
            var colors = new List<Color>(palette.Colors);
            bool isGradient = palette.IsGradient;
            int colorCount = colors.Count;
            Color interiorColor = ResolveInteriorColor();

            if (palette.Name == "Стандартный серый")
            {
                return (iter, maxIter, maxColorIter) =>
                {
                    if (iter == maxIter) return interiorColor;
                    double logMax = Math.Log(maxColorIter + 1);
                    if (logMax <= 0) return interiorColor;
                    double tLog = Math.Log(Math.Min(iter, maxColorIter) + 1) / logMax;
                    tLog = TransformPaletteIndex(tLog);
                    int cVal = (int)(255.0 * (1 - tLog));
                    return ColorCorrection.ApplyGamma(Color.FromArgb(cVal, cVal, cVal), gamma);
                };
            }

            if (colorCount == 0) return (i, m, mc) => interiorColor;
            if (colorCount == 1) return (iter, max, clrMax) => ColorCorrection.ApplyGamma((iter == max) ? interiorColor : colors[0], gamma);

            return (iter, maxIter, maxColorIter) =>
            {
                if (iter == maxIter) return interiorColor;
                double normalizedIter = maxColorIter > 0 ? (double)Math.Min(iter, maxColorIter) / maxColorIter : 0;
                normalizedIter = TransformPaletteIndex(normalizedIter);
                Color baseColor;
                if (isGradient)
                {
                    double scaledT = normalizedIter * (colorCount - 1);
                    int index1 = (int)Math.Floor(scaledT);
                    int index2 = Math.Min(index1 + 1, colorCount - 1);
                    double localT = scaledT - index1;
                    baseColor = LerpColor(colors[index1], colors[index2], localT);
                }
                else
                {
                    int colorIndex = (int)(normalizedIter * colorCount);
                    if (colorIndex >= colorCount) colorIndex = colorCount - 1;
                    baseColor = colors[colorIndex];
                }
                return ColorCorrection.ApplyGamma(baseColor, gamma);
            };
        }

        /// <summary>
        /// Применяет активную палитру к движку рендеринга, используя кэш для оптимизации.
        /// </summary>
        private void ApplyActivePalette()
        {
            if (_fractalEngine == null || _paletteManager.ActivePalette == null) return;

            var activePalette = _paletteManager.ActivePalette;
            int effectiveMaxColorIterations = activePalette.AlignWithRenderIterations ? _fractalEngine.MaxIterations : activePalette.MaxColorIterations;
            string newSignature = GeneratePaletteSignature(activePalette, _fractalEngine.MaxIterations);

            _fractalEngine.SmoothPalette = GenerateSmoothPaletteFunction(activePalette, effectiveMaxColorIterations);

            if (_gammaCorrectedPaletteCache == null || newSignature != _paletteCacheSignature)
            {
                _paletteCacheSignature = newSignature;
                var paletteGeneratorFunc = GenerateDiscretePaletteFunction(activePalette);
                _gammaCorrectedPaletteCache = new Color[effectiveMaxColorIterations + 1];
                for (int i = 0; i <= effectiveMaxColorIterations; i++)
                {
                    _gammaCorrectedPaletteCache[i] = paletteGeneratorFunc(i, _fractalEngine.MaxIterations, effectiveMaxColorIterations);
                }
            }

            _fractalEngine.MaxColorIterations = effectiveMaxColorIterations;
            _fractalEngine.Palette = (iter, maxIter, maxColorIter) =>
            {
                if (iter == maxIter) return ResolveInteriorColor();
                int index = Math.Min(iter, _gammaCorrectedPaletteCache.Length - 1);
                return _gammaCorrectedPaletteCache[index];
            };
        }

        #endregion

        #region Form Lifecycle

        /// <summary>
        /// Обрабатывает событие загрузки формы, инициализируя все компоненты.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void FormBase_Load(object sender, EventArgs e)
        {
            _baseTitle = this.Text;
            _paletteManager = new PaletteManager();
            _fractalEngine = CreateEngine();
            _renderDebounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _renderDebounceTimer.Tick += RenderDebounceTimer_Tick;
            _renderVisualizer = new RenderVisualizerComponent(TILE_SIZE);
            _renderVisualizer.NeedsRedraw += OnVisualizerNeedsRedraw;

            InitializeControls();
            InitializeEventHandlers();

            var cbSSAA = this.Controls.Find("cbSSAA", true).FirstOrDefault() as ComboBox;
            if (cbSSAA != null)
            {
                cbSSAA.Items.Add("Выкл (1x)");
                cbSSAA.Items.Add("Низкое (2x)");
                cbSSAA.Items.Add("Высокое (4x)");
                cbSSAA.SelectedItem = "Выкл (1x)";
                cbSSAA.SelectedIndexChanged += (s, ev) => ScheduleRender();
            }

            _renderedCenterX = _centerX;
            _renderedCenterY = _centerY;
            _renderedZoom = _zoom;
            _renderedViewportRect = GetCurrentViewportRect();
            btnToggleControls.Text = "✕";
            UpdateToggleControlsPosition();
            OnPostInitialize();
            UpdateEngineParameters();
            ScheduleRender();
            ClearInitialControlSelection();
        }

        /// <summary>
        /// Обрабатывает событие закрытия формы, освобождая все используемые ресурсы.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void FractalMandelbrotFamilyForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _renderDebounceTimer?.Stop();
            _renderDebounceTimer?.Dispose();
            if (_previewRenderCts != null)
            {
                _previewRenderCts.Cancel();
                _previewRenderCts.Dispose();
            }
            lock (_bitmapLock)
            {
                _previewBitmap?.Dispose();
                _previewBitmap = null;
                _currentRenderingBitmap?.Dispose();
                _currentRenderingBitmap = null;
            }
            if (_renderVisualizer != null)
            {
                _renderVisualizer.NeedsRedraw -= OnVisualizerNeedsRedraw;
                _renderVisualizer.Dispose();
            }

            if (_previewThemeChangedHandler is not null)
            {
                ThemeManager.ThemeChanged -= _previewThemeChangedHandler;
                _previewThemeChangedHandler = null;
            }
        }

        #endregion

        #region ISaveLoadCapableFractal Implementation

        /// <summary>
        /// Получает строковый идентификатор типа фрактала (например, "Mandelbrot", "Julia").
        /// </summary>
        public abstract string FractalTypeIdentifier { get; }

        /// <summary>
        /// Получает тип конкретного класса состояния сохранения, используемого этим фракталом.
        /// </summary>
        public abstract Type ConcreteSaveStateType { get; }

        /// <summary>
        /// Хранит параметры, необходимые для рендеринга предпросмотра сохранения.
        /// </summary>
        public class PreviewParams
        {
            /// <summary>
            /// Координата X центра области.
            /// </summary>
            public decimal CenterX { get; set; }
            /// <summary>
            /// Координата Y центра области.
            /// </summary>
            public decimal CenterY { get; set; }
            /// <summary>
            /// Коэффициент масштабирования.
            /// </summary>
            public decimal Zoom { get; set; }
            /// <summary>
            /// Количество итераций.
            /// </summary>
            public int Iterations { get; set; }
            /// <summary>
            /// Имя используемой палитры.
            /// </summary>
            public string PaletteName { get; set; }
            /// <summary>
            /// Пороговое значение (Bail-out).
            /// </summary>
            public decimal Threshold { get; set; }
            /// <summary>
            /// Реальная часть константы C для фракталов Жюлиа.
            /// </summary>
            public decimal CRe { get; set; }
            /// <summary>
            /// Мнимая часть константы C для фракталов Жюлиа.
            /// </summary>
            public decimal CIm { get; set; }
            /// <summary>
            /// Использовать ли плавную раскраску.
            /// </summary>
            public bool UseSmoothColoring { get; set; }
            /// <summary>
            /// Режим окрашивания для превью.
            /// null означает старое сохранение — режим определяется через <see cref="UseSmoothColoring"/>.
            /// </summary>
            public int? ColoringMode { get; set; }
            /// <summary>
            /// Использовать ли сглаженное значение итераций во входе histogram-режима.
            /// </summary>
            public bool? HistogramInputUseSmooth { get; set; }
            /// <summary>
            /// Сила влияния Orbit Trap режима.
            /// </summary>
            public double? OrbitTrapStrength { get; set; }
            /// <summary>
            /// Смещение Orbit Trap режима.
            /// </summary>
            public double? OrbitTrapBias { get; set; }
            /// <summary>
            /// Частота полос для Stripe Average режима.
            /// </summary>
            public double? StripeFrequency { get; set; }
            /// <summary>
            /// Сила полос для Stripe Average режима.
            /// </summary>
            public double? StripeStrength { get; set; }
            /// <summary>
            /// Смещение полос для Stripe Average режима.
            /// </summary>
            public double? StripeBias { get; set; }
            /// <summary>
            /// Тип движка для рендеринга превью.
            /// </summary>
            public string PreviewEngineType { get; set; }
        }

        /// <summary>
        /// In-memory состояние режима окрашивания и его параметров.
        /// Не сохраняется в SaveState.
        /// </summary>
        public sealed class ColoringRuntimeState
        {
            public sealed class HistogramSettingsState
            {
                public bool EnabledEqualization { get; set; } = true;
                public double Contrast { get; set; } = 1.0;
                public bool InputUseSmooth { get; set; } = true;

                public HistogramSettingsState Clone()
                {
                    return new HistogramSettingsState
                    {
                        EnabledEqualization = EnabledEqualization,
                        Contrast = Contrast,
                        InputUseSmooth = InputUseSmooth
                    };
                }
            }

            public sealed class OrbitTrapSettingsState
            {
                public double Strength { get; set; } = 1.0;
                public double Bias { get; set; } = 0.0;

                public OrbitTrapSettingsState Clone()
                {
                    return new OrbitTrapSettingsState
                    {
                        Strength = Strength,
                        Bias = Bias
                    };
                }
            }

            public sealed class StripeAverageSettingsState
            {
                public double Frequency { get; set; } = 3.0;
                public double Strength { get; set; } = 0.5;
                public double Bias { get; set; } = 0.0;

                public StripeAverageSettingsState Clone()
                {
                    return new StripeAverageSettingsState
                    {
                        Frequency = Frequency,
                        Strength = Strength,
                        Bias = Bias
                    };
                }
            }

            public sealed class PaletteTransformState
            {
                public double PhaseOffset { get; set; } = 0.0;
                public double Scale { get; set; } = 1.0;
                public PaletteTransformWrapMode WrapMode { get; set; } = PaletteTransformWrapMode.Repeat;

                public PaletteTransformState Clone()
                {
                    return new PaletteTransformState
                    {
                        PhaseOffset = PhaseOffset,
                        Scale = Scale,
                        WrapMode = WrapMode
                    };
                }
            }

            public ColoringModeRuntime ActiveMode { get; set; } = ColoringModeRuntime.Smooth;
            public double SmoothBlendPower { get; set; } = 1.0;
            public double SmoothIterationOffset { get; set; } = 0.0;
            public HistogramSettingsState HistogramSettings { get; set; } = new();
            public OrbitTrapSettingsState OrbitTrapSettings { get; set; } = new();
            public StripeAverageSettingsState StripeAverageSettings { get; set; } = new();
            public PaletteTransformState PaletteTransform { get; set; } = new();
            public InteriorMode InteriorMode { get; set; } = InteriorMode.Black;
            public Color InteriorColor { get; set; } = Color.Black;
            public bool UseSmoothColoring => ActiveMode.ModeType == ColoringModeType.Smooth;

            public static ColoringRuntimeState CreateDefault()
            {
                return new ColoringRuntimeState
                {
                    ActiveMode = ColoringModeRuntime.Smooth,
                    InteriorMode = InteriorMode.Black,
                    InteriorColor = Color.Black
                };
            }

            public ColoringRuntimeState Clone()
            {
                return new ColoringRuntimeState
                {
                    ActiveMode = ActiveMode,
                    SmoothBlendPower = SmoothBlendPower,
                    SmoothIterationOffset = SmoothIterationOffset,
                    HistogramSettings = HistogramSettings.Clone(),
                    OrbitTrapSettings = OrbitTrapSettings.Clone(),
                    StripeAverageSettings = StripeAverageSettings.Clone(),
                    PaletteTransform = PaletteTransform.Clone(),
                    InteriorMode = InteriorMode,
                    InteriorColor = InteriorColor
                };
            }
        }

        public enum PaletteTransformWrapMode
        {
            Repeat = 0,
            Clamp = 1,
            Mirror = 2
        }

        public enum InteriorMode
        {
            Black = 0,
            Custom = 1
        }

        private sealed class ColoringModeOption
        {
            public ColoringModeType ModeType { get; }
            public string DisplayName { get; }

            public ColoringModeOption(ColoringModeType modeType, string displayName)
            {
                ModeType = modeType;
                DisplayName = displayName;
            }

            public override string ToString() => DisplayName;
        }

        public enum ColoringModeType
        {
            Discrete = 0,
            Smooth = 1,
            Histogram = 2,
            OrbitTrap = 3,
            StripeAverage = 4
        }

        public sealed class ColoringModeRuntime
        {
            public static readonly ColoringModeRuntime Discrete = new(ColoringModeType.Discrete);
            public static readonly ColoringModeRuntime Smooth = new(ColoringModeType.Smooth);
            public static readonly ColoringModeRuntime Histogram = new(ColoringModeType.Histogram);
            public static readonly ColoringModeRuntime OrbitTrap = new(ColoringModeType.OrbitTrap);
            public static readonly ColoringModeRuntime StripeAverage = new(ColoringModeType.StripeAverage);

            public ColoringModeType ModeType { get; }
            public bool HasCustomParameters => ModeType != ColoringModeType.Discrete;

            private ColoringModeRuntime(ColoringModeType modeType)
            {
                ModeType = modeType;
            }

            public static ColoringModeRuntime FromType(ColoringModeType modeType)
            {
                return modeType switch
                {
                    ColoringModeType.Smooth => Smooth,
                    ColoringModeType.Histogram => Histogram,
                    ColoringModeType.OrbitTrap => OrbitTrap,
                    ColoringModeType.StripeAverage => StripeAverage,
                    _ => Discrete
                };
            }
        }

        /// <summary>
        /// Открывает диалог менеджера состояний (сохранений).
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void btnStateManager_Click(object sender, EventArgs e)
        {
            using (var dialog = new SaveLoadDialogForm(this))
            {
                dialog.ShowDialog(this);
            }
        }

        /// <inheritdoc/>
        public virtual FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            MandelbrotFamilySaveState state;
            if (this is FractalJulia || this is FractalJuliaBurningShip) state = new JuliaFamilySaveState(this.FractalTypeIdentifier);
            else state = new MandelbrotFamilySaveState(this.FractalTypeIdentifier);

            state.SaveName = saveName;
            state.Timestamp = DateTime.Now;
            state.CenterX = _centerX;
            state.CenterY = _centerY;
            state.Zoom = _zoom;
            state.Threshold = nudThreshold.Value;
            state.Iterations = (int)nudIterations.Value;
            state.PaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый";
            state.PreviewEngineType = this.FractalTypeIdentifier;
            state.ColoringMode = (int)_coloringRuntimeState.ActiveMode.ModeType;

            var previewParams = new PreviewParams
            {
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                Iterations = (int)nudIterations.Value,
                PaletteName = state.PaletteName,
                Threshold = state.Threshold,
                UseSmoothColoring = IsSmoothColoringEnabled(),
                ColoringMode = (int)_coloringRuntimeState.ActiveMode.ModeType,
                HistogramInputUseSmooth = _coloringRuntimeState.HistogramSettings.InputUseSmooth,
                OrbitTrapStrength = _coloringRuntimeState.OrbitTrapSettings.Strength,
                OrbitTrapBias = _coloringRuntimeState.OrbitTrapSettings.Bias,
                StripeFrequency = _coloringRuntimeState.StripeAverageSettings.Frequency,
                StripeStrength = _coloringRuntimeState.StripeAverageSettings.Strength,
                StripeBias = _coloringRuntimeState.StripeAverageSettings.Bias,
                PreviewEngineType = state.PreviewEngineType
            };

            if (state is JuliaFamilySaveState juliaState)
            {
                if (nudRe != null && nudIm != null && nudRe.Visible)
                {
                    juliaState.CRe = nudRe.Value;
                    juliaState.CIm = nudIm.Value;
                    previewParams.CRe = juliaState.CRe;
                    previewParams.CIm = juliaState.CIm;
                }
            }
            state.PreviewParametersJson = JsonSerializer.Serialize(previewParams, new JsonSerializerOptions());
            return state;
        }

        /// <inheritdoc/>
        public virtual void LoadState(FractalSaveStateBase stateBase)
        {
            if (!(stateBase is MandelbrotFamilySaveState state))
            {
                MessageBox.Show("Несовместимый тип состояния для загрузки.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _isRenderingPreview = false;
            _previewRenderCts?.Cancel();
            _renderDebounceTimer.Stop();

            _centerX = state.CenterX;
            _centerY = state.CenterY;
            _zoom = state.Zoom;

            nudZoom.Value = ClampDecimal(_zoom, nudZoom.Minimum, nudZoom.Maximum);
            nudThreshold.Value = ClampDecimal(state.Threshold, nudThreshold.Minimum, nudThreshold.Maximum);
            nudIterations.Value = ClampInt(state.Iterations, (int)nudIterations.Minimum, (int)nudIterations.Maximum);

            var paletteToLoad = _paletteManager.Palettes.FirstOrDefault(p => p.Name == state.PaletteName);
            if (paletteToLoad != null) _paletteManager.ActivePalette = paletteToLoad;

            if (state is JuliaFamilySaveState juliaState)
            {
                if (nudRe != null && nudIm != null && nudRe.Visible)
                {
                    nudRe.Value = ClampDecimal(juliaState.CRe, nudRe.Minimum, nudRe.Maximum);
                    nudIm.Value = ClampDecimal(juliaState.CIm, nudIm.Minimum, nudIm.Maximum);
                }
            }

            ApplyColoringStateOnLoad(state);
            lock (_bitmapLock)
            {
                _previewBitmap?.Dispose(); _previewBitmap = null;
                _currentRenderingBitmap?.Dispose(); _currentRenderingBitmap = null;
            }
            _renderedCenterX = _centerX;
            _renderedCenterY = _centerY;
            _renderedZoom = _zoom;
            UpdateEngineParameters();
            ScheduleRender();
        }

        private void ApplyColoringStateOnLoad(MandelbrotFamilySaveState state)
        {
            var mode = ResolveLoadedColoringMode(state);
            _coloringRuntimeState.ActiveMode = ColoringModeRuntime.FromType(mode);

            if (!string.IsNullOrWhiteSpace(state.PreviewParametersJson))
            {
                try
                {
                    var previewParams = JsonSerializer.Deserialize<PreviewParams>(state.PreviewParametersJson);
                    if (previewParams is not null)
                    {
                        if (previewParams.HistogramInputUseSmooth.HasValue)
                            _coloringRuntimeState.HistogramSettings.InputUseSmooth = previewParams.HistogramInputUseSmooth.Value;
                        if (previewParams.OrbitTrapStrength.HasValue)
                            _coloringRuntimeState.OrbitTrapSettings.Strength = previewParams.OrbitTrapStrength.Value;
                        if (previewParams.OrbitTrapBias.HasValue)
                            _coloringRuntimeState.OrbitTrapSettings.Bias = previewParams.OrbitTrapBias.Value;
                        if (previewParams.StripeFrequency.HasValue)
                            _coloringRuntimeState.StripeAverageSettings.Frequency = previewParams.StripeFrequency.Value;
                        if (previewParams.StripeStrength.HasValue)
                            _coloringRuntimeState.StripeAverageSettings.Strength = previewParams.StripeStrength.Value;
                        if (previewParams.StripeBias.HasValue)
                            _coloringRuntimeState.StripeAverageSettings.Bias = previewParams.StripeBias.Value;
                    }
                }
                catch
                {
                    // Игнорируем ошибки превью-параметров: базовый режим уже определён выше.
                }
            }

            SyncQuickColoringModeSelection();
        }

        private ColoringModeType ResolveLoadedColoringMode(MandelbrotFamilySaveState state)
        {
            if (state.ColoringMode.HasValue)
            {
                int rawMode = state.ColoringMode.Value;
                if (Enum.IsDefined(typeof(ColoringModeType), rawMode))
                {
                    return (ColoringModeType)rawMode;
                }
            }

            if (!string.IsNullOrWhiteSpace(state.PreviewParametersJson))
            {
                try
                {
                    var previewParams = JsonSerializer.Deserialize<PreviewParams>(state.PreviewParametersJson);
                    if (previewParams is not null)
                    {
                        return ResolvePreviewColoringMode(previewParams);
                    }
                }
                catch
                {
                    // Старые/битые параметры превью: используем fallback ниже.
                }
            }

            // Исторически отсутствие параметра означало классические режимы (Smooth/Discrete).
            // При полном отсутствии данных сохраняем предыдущее поведение по умолчанию — Smooth.
            return ColoringModeType.Smooth;
        }

        /// <inheritdoc/>
        public virtual async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase stateBase, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(stateBase.PreviewParametersJson)) return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
                PreviewParams previewParams;
                try { previewParams = JsonSerializer.Deserialize<PreviewParams>(stateBase.PreviewParametersJson); }
                catch { return new byte[tile.Bounds.Width * tile.Bounds.Height * 4]; }

                FractalMandelbrotFamilyEngine previewEngine;
                switch (previewParams.PreviewEngineType)
                {
                    case "Mandelbrot": previewEngine = new MandelbrotEngine(); break;
                    case "Julia": previewEngine = new JuliaEngine { C = new ComplexDecimal(previewParams.CRe, previewParams.CIm) }; break;
                    case "MandelbrotBurningShip": previewEngine = new MandelbrotBurningShipEngine(); break;
                    case "JuliaBurningShip": previewEngine = new JuliaBurningShipEngine { C = new ComplexDecimal(previewParams.CRe, previewParams.CIm) }; break;
                    case "GeneralizedMandelbrot":
                        var genMandelbrotParams = JsonSerializer.Deserialize<FractalGeneralizedMandelbrot.GeneralizedMandelbrotPreviewParams>(stateBase.PreviewParametersJson);
                        previewEngine = new GeneralizedMandelbrotEngine
                        {
                            Power = genMandelbrotParams.Power
                        };
                        break;
                    default: return new byte[tile.Bounds.Width * tile.Bounds.Height * 4];
                }

                previewEngine.MaxIterations = Math.Max(1, previewParams.Iterations);
                previewEngine.CenterX = previewParams.CenterX;
                previewEngine.CenterY = previewParams.CenterY;
                if (previewParams.Zoom == 0) previewParams.Zoom = 0.001m;
                previewEngine.Scale = this.BaseScale / previewParams.Zoom;
                previewEngine.ThresholdSquared = previewParams.Threshold * previewParams.Threshold;
                var paletteForPreview = _paletteManager.Palettes.FirstOrDefault(p => p.Name == previewParams.PaletteName) ?? _paletteManager.Palettes.First();

                int effectiveMaxColorIterations = paletteForPreview.AlignWithRenderIterations
                        ? previewEngine.MaxIterations
                        : paletteForPreview.MaxColorIterations;

                ApplyPreviewColoring(previewEngine, previewParams, paletteForPreview, effectiveMaxColorIterations);

                return previewEngine.RenderSingleTile(tile, totalWidth, totalHeight, out _);
            });
        }

        /// <inheritdoc/>
        public virtual Bitmap RenderPreview(FractalSaveStateBase stateBase, int previewWidth, int previewHeight)
        {
            if (string.IsNullOrEmpty(stateBase.PreviewParametersJson))
            {
                var bmpError = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkGray); TextRenderer.DrawText(g, "Нет данных", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                return bmpError;
            }
            PreviewParams previewParams;
            try { previewParams = JsonSerializer.Deserialize<PreviewParams>(stateBase.PreviewParametersJson, new JsonSerializerOptions()); }
            catch (Exception)
            {
                var bmpError = new Bitmap(previewWidth, previewHeight);
                using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkRed); TextRenderer.DrawText(g, "Ошибка параметров", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                return bmpError;
            }

            FractalMandelbrotFamilyEngine previewEngine;
            switch (previewParams.PreviewEngineType)
            {
                case "Mandelbrot": previewEngine = new MandelbrotEngine(); break;
                case "Julia": previewEngine = new JuliaEngine { C = new ComplexDecimal(previewParams.CRe, previewParams.CIm) }; break;
                case "MandelbrotBurningShip": previewEngine = new MandelbrotBurningShipEngine(); break;
                case "JuliaBurningShip": previewEngine = new JuliaBurningShipEngine { C = new ComplexDecimal(previewParams.CRe, previewParams.CIm) }; break;
                default:
                    var bmpError = new Bitmap(previewWidth, previewHeight);
                    using (var g = Graphics.FromImage(bmpError)) { g.Clear(Color.DarkOrange); TextRenderer.DrawText(g, "Неизв. тип движка", Font, new Rectangle(0, 0, previewWidth, previewHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
                    return bmpError;
            }
            previewEngine.CenterX = previewParams.CenterX;
            previewEngine.CenterY = previewParams.CenterY;
            if (previewParams.Zoom == 0) previewParams.Zoom = 0.001m;
            previewEngine.Scale = this.BaseScale / previewParams.Zoom;
            previewEngine.MaxIterations = Math.Max(1, previewParams.Iterations);
            previewEngine.ThresholdSquared = previewParams.Threshold * previewParams.Threshold;
            var paletteForPreview = _paletteManager.Palettes.FirstOrDefault(p => p.Name == previewParams.PaletteName) ?? _paletteManager.Palettes.First();

            int effectiveMaxColorIterations = paletteForPreview.AlignWithRenderIterations
                    ? previewEngine.MaxIterations
                    : paletteForPreview.MaxColorIterations;

            ApplyPreviewColoring(previewEngine, previewParams, paletteForPreview, effectiveMaxColorIterations);

            return previewEngine.RenderToBitmap(previewWidth, previewHeight, 1, progress => { });
        }

        private void ApplyPreviewColoring(
            FractalMandelbrotFamilyEngine previewEngine,
            PreviewParams previewParams,
            Palette paletteForPreview,
            int effectiveMaxColorIterations)
        {
            var resolvedMode = ResolvePreviewColoringMode(previewParams);
            previewEngine.ActiveMode = (FractalExplorer.Engines.ColoringModeType)resolvedMode;
            previewEngine.UseSmoothColoring = resolvedMode == ColoringModeType.Smooth;

            previewEngine.HistogramInputUseSmooth = previewParams.HistogramInputUseSmooth ?? true;
            previewEngine.OrbitTrapStrength = previewParams.OrbitTrapStrength ?? 1.0;
            previewEngine.OrbitTrapBias = previewParams.OrbitTrapBias ?? 0.0;
            previewEngine.StripeFrequency = previewParams.StripeFrequency ?? 6.0;
            previewEngine.StripeStrength = previewParams.StripeStrength ?? 0.5;
            previewEngine.StripeBias = previewParams.StripeBias ?? 0.0;

            int safeMaxColorIterations = Math.Max(2, effectiveMaxColorIterations);
            previewEngine.MaxColorIterations = safeMaxColorIterations;
            previewEngine.SmoothPalette = GenerateSmoothPaletteFunctionForPreview(paletteForPreview, previewEngine.MaxIterations, safeMaxColorIterations);
            previewEngine.Palette = GenerateDiscretePaletteFunctionForPreview(paletteForPreview);
        }

        /// <summary>
        /// Вариант генератора плавной палитры для превью, не зависящий от текущего runtime-состояния формы.
        /// Это предотвращает артефакты/черные кадры, когда рендер превью выполняется для сохраненного состояния
        /// с параметрами, отличными от текущих параметров открытой формы.
        /// </summary>
        private Func<double, Color> GenerateSmoothPaletteFunctionForPreview(Palette palette, int previewMaxIterations, int effectiveMaxColorIterations)
        {
            double gamma = palette.Gamma;
            var colors = new List<Color>(palette.Colors);
            int colorCount = colors.Count;
            Color interiorColor = ResolveInteriorColor();

            if (palette.Name == "Стандартный серый")
            {
                return (smoothIter) =>
                {
                    if (smoothIter >= previewMaxIterations) return interiorColor;
                    if (smoothIter < 0) smoothIter = 0;

                    double logMax = Math.Log(previewMaxIterations + 1);
                    if (logMax <= 0) return interiorColor;
                    double tLog = Math.Log(smoothIter + 1) / logMax;
                    tLog = Math.Max(0.0, Math.Min(1.0, tLog));

                    int grayLevel = (int)(255.0 * (1.0 - tLog));
                    grayLevel = Math.Max(0, Math.Min(255, grayLevel));
                    return ColorCorrection.ApplyGamma(Color.FromArgb(grayLevel, grayLevel, grayLevel), gamma);
                };
            }

            if (effectiveMaxColorIterations <= 0)
            {
                return _ => interiorColor;
            }

            if (colorCount == 0) return _ => interiorColor;
            if (colorCount == 1) return smoothIter => smoothIter >= previewMaxIterations ? interiorColor : ColorCorrection.ApplyGamma(colors[0], gamma);

            return (smoothIter) =>
            {
                if (smoothIter >= previewMaxIterations) return interiorColor;
                if (smoothIter < 0) smoothIter = 0;

                double cyclicIter = smoothIter % effectiveMaxColorIterations;
                double t = cyclicIter / effectiveMaxColorIterations;
                t = Math.Max(0.0, Math.Min(1.0, t));

                double scaledT = t * (colorCount - 1);
                int index1 = (int)Math.Floor(scaledT);
                int index2 = Math.Min(index1 + 1, colorCount - 1);
                double localT = scaledT - index1;
                Color baseColor = LerpColor(colors[index1], colors[index2], localT);
                return ColorCorrection.ApplyGamma(baseColor, gamma);
            };
        }

        /// <summary>
        /// Вариант генератора дискретной палитры для превью, не зависящий от transform-параметров активной формы.
        /// </summary>
        private Func<int, int, int, Color> GenerateDiscretePaletteFunctionForPreview(Palette palette)
        {
            double gamma = palette.Gamma;
            var colors = new List<Color>(palette.Colors);
            bool isGradient = palette.IsGradient;
            int colorCount = colors.Count;
            Color interiorColor = ResolveInteriorColor();

            if (palette.Name == "Стандартный серый")
            {
                return (iter, maxIter, maxColorIter) =>
                {
                    if (iter >= maxIter) return interiorColor;
                    int safeMaxColorIter = Math.Max(2, maxColorIter);
                    double logMax = Math.Log(safeMaxColorIter + 1);
                    if (logMax <= 0) return interiorColor;
                    double tLog = Math.Log(Math.Min(iter, safeMaxColorIter) + 1) / logMax;
                    int cVal = (int)(255.0 * (1 - tLog));
                    cVal = Math.Max(0, Math.Min(255, cVal));
                    return ColorCorrection.ApplyGamma(Color.FromArgb(cVal, cVal, cVal), gamma);
                };
            }

            if (colorCount == 0) return (_, _, _) => interiorColor;
            if (colorCount == 1) return (iter, maxIter, _) => ColorCorrection.ApplyGamma((iter >= maxIter) ? interiorColor : colors[0], gamma);

            return (iter, maxIter, maxColorIter) =>
            {
                if (iter >= maxIter) return interiorColor;
                int safeMaxColorIter = Math.Max(2, maxColorIter);
                double normalizedIter = (double)Math.Min(iter, safeMaxColorIter) / safeMaxColorIter;
                Color baseColor;
                if (isGradient)
                {
                    double scaledT = normalizedIter * (colorCount - 1);
                    int index1 = (int)Math.Floor(scaledT);
                    int index2 = Math.Min(index1 + 1, colorCount - 1);
                    double localT = scaledT - index1;
                    baseColor = LerpColor(colors[index1], colors[index2], localT);
                }
                else
                {
                    int colorIndex = (int)(normalizedIter * colorCount);
                    if (colorIndex >= colorCount) colorIndex = colorCount - 1;
                    baseColor = colors[colorIndex];
                }

                return ColorCorrection.ApplyGamma(baseColor, gamma);
            };
        }

        private static ColoringModeType ResolvePreviewColoringMode(PreviewParams previewParams)
        {
            if (previewParams.ColoringMode.HasValue)
            {
                int modeValue = previewParams.ColoringMode.Value;
                if (Enum.IsDefined(typeof(ColoringModeType), modeValue))
                {
                    return (ColoringModeType)modeValue;
                }
            }

            // Обратная совместимость со старыми сохранениями:
            // если нового параметра нет, используем прежний флаг Smooth/Discrete.
            return previewParams.UseSmoothColoring ? ColoringModeType.Smooth : ColoringModeType.Discrete;
        }

        /// <inheritdoc/>
        public virtual List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            throw new NotImplementedException($"Метод LoadAllSavesForThisType должен быть переопределен в классе {this.GetType().Name}, чтобы загружать состояния типа {this.ConcreteSaveStateType.Name}.");
        }

        /// <inheritdoc/>
        public virtual void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            throw new NotImplementedException($"Метод SaveAllSavesForThisType должен быть переопределен в классе {this.GetType().Name}, чтобы сохранять состояния типа {this.ConcreteSaveStateType.Name}.");
        }

        #endregion

        #region IHighResRenderable Implementation

        /// <inheritdoc/>
        public virtual HighResRenderState GetRenderState()
        {
            var state = new HighResRenderState
            {
                EngineType = this.FractalTypeIdentifier,
                CenterX = _centerX,
                CenterY = _centerY,
                Zoom = _zoom,
                BaseScale = this.BaseScale,
                Iterations = (int)nudIterations.Value,
                Threshold = nudThreshold.Value,
                ActivePaletteName = _paletteManager.ActivePalette?.Name ?? "Стандартный серый",
                FileNameDetails = this.GetSaveFileNameDetails(),
                UseSmoothColoring = IsSmoothColoringEnabled(),
                ColoringMode = (int)_coloringRuntimeState.ActiveMode.ModeType,
                HistogramEnabledEqualization = _coloringRuntimeState.HistogramSettings.EnabledEqualization,
                HistogramContrast = _coloringRuntimeState.HistogramSettings.Contrast,
                HistogramInputUseSmooth = _coloringRuntimeState.HistogramSettings.InputUseSmooth,
                OrbitTrapStrength = _coloringRuntimeState.OrbitTrapSettings.Strength,
                OrbitTrapBias = _coloringRuntimeState.OrbitTrapSettings.Bias,
                StripeFrequency = _coloringRuntimeState.StripeAverageSettings.Frequency,
                StripeStrength = _coloringRuntimeState.StripeAverageSettings.Strength,
                StripeBias = _coloringRuntimeState.StripeAverageSettings.Bias
            };

            if (this is FractalJulia || this is FractalJuliaBurningShip)
            {
                state.JuliaC = new ComplexDecimal(nudRe.Value, nudIm.Value);
            }

            return state;
        }

        /// <summary>
        /// Создает и настраивает экземпляр движка фрактала на основе состояния рендеринга.
        /// </summary>
        /// <param name="state">Состояние, описывающее параметры рендеринга.</param>
        /// <param name="forPreview">Если true, используются облегченные параметры (например, меньше итераций).</param>
        /// <returns>Настроенный экземпляр движка фрактала.</returns>
        private FractalMandelbrotFamilyEngine CreateEngineFromState(HighResRenderState state, bool forPreview)
        {
            FractalMandelbrotFamilyEngine engine;
            switch (state.EngineType)
            {
                case "Mandelbrot": engine = new MandelbrotEngine(); break;
                case "Julia": engine = new JuliaEngine { C = state.JuliaC.Value }; break;
                case "MandelbrotBurningShip": engine = new MandelbrotBurningShipEngine(); break;
                case "JuliaBurningShip": engine = new JuliaBurningShipEngine { C = state.JuliaC.Value }; break;
                case "GeneralizedMandelbrot":
                    engine = new GeneralizedMandelbrotEngine();
                    if (state.Power.HasValue && engine is GeneralizedMandelbrotEngine genEngine)
                    {
                        genEngine.Power = state.Power.Value;
                    }
                    break;
                case "Buffalo":
                    engine = new BuffaloEngine();
                    break;
                case "Simonobrot":
                    engine = new SimonobrotEngine();
                    if (engine is SimonobrotEngine simonEngine)
                    {
                        if (state.Power.HasValue) simonEngine.Power = state.Power.Value;
                        simonEngine.UseInversion = state.UseInversion;
                    }
                    break;
                default: throw new NotSupportedException($"Тип движка '{state.EngineType}' не поддерживается.");
            }

            if (forPreview)
            {
                engine.MaxIterations = Math.Min(state.Iterations, 150);
            }
            else
            {
                engine.MaxIterations = state.Iterations;
            }

            engine.ThresholdSquared = state.Threshold * state.Threshold;
            engine.CenterX = state.CenterX;
            engine.CenterY = state.CenterY;
            engine.Scale = state.BaseScale / state.Zoom;
            var paletteForRender = _paletteManager.Palettes.FirstOrDefault(p => p.Name == state.ActivePaletteName) ?? _paletteManager.Palettes.First();

            engine.UseSmoothColoring = state.UseSmoothColoring;
            engine.ActiveMode = (FractalExplorer.Engines.ColoringModeType)state.ColoringMode;
            engine.HistogramEnabledEqualization = state.HistogramEnabledEqualization;
            engine.HistogramContrast = state.HistogramContrast;
            engine.HistogramInputUseSmooth = state.HistogramInputUseSmooth;
            engine.OrbitTrapStrength = state.OrbitTrapStrength;
            engine.OrbitTrapBias = state.OrbitTrapBias;
            engine.StripeFrequency = state.StripeFrequency;
            engine.StripeStrength = state.StripeStrength;
            engine.StripeBias = state.StripeBias;
            int effectiveMaxColorIterations = paletteForRender.AlignWithRenderIterations ? engine.MaxIterations : paletteForRender.MaxColorIterations;
            engine.MaxColorIterations = effectiveMaxColorIterations;

            engine.SmoothPalette = GenerateSmoothPaletteFunction(paletteForRender, effectiveMaxColorIterations);
            engine.Palette = GenerateDiscretePaletteFunction(paletteForRender);

            return engine;
        }

        /// <inheritdoc/>
        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            _isHighResRendering = true;
            try
            {
                FractalMandelbrotFamilyEngine renderEngine = CreateEngineFromState(state, forPreview: false);
                int threadCount = GetThreadCount();
                Action<int> progressCallback = p => progress.Report(new RenderProgress { Percentage = p, Status = "Рендеринг..." });

                Bitmap finalBitmap = await renderEngine.RenderToBitmapSSAA(
                    width,
                    height,
                    threadCount,
                    progressCallback,
                    ssaaFactor,
                    cancellationToken
                );

                return finalBitmap;
            }
            finally
            {
                _isHighResRendering = false;
            }
        }

        /// <inheritdoc/>
        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            var engine = CreateEngineFromState(state, forPreview: true);
            return engine.RenderToBitmap(previewWidth, previewHeight, 1, _ => { }, CancellationToken.None);
        }

        #endregion
    }
}
