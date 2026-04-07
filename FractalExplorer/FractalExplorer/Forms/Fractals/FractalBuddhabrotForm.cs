using FractalExplorer.Engines;
using FractalExplorer.Resources;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.ColorPalettes;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;
using FractalExplorer.SelectorsForms;
using FractalExplorer.Utilities.RenderUtilities;
using FractalExplorer.Utilities.Theme;
using FractalExplorer.Utilities.UI;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FractalExplorer.Forms.Other;

namespace FractalExplorer.Forms.Fractals
{
    public sealed partial class FractalBuddhabrotForm : Form, ISaveLoadCapableFractal, IFullPreviewRenderCapableFractal, IHighResRenderable
    {
        private const decimal BaseScale = 4.0m;
        private const int ToggleButtonMargin = 12;
        private const int ProgressiveUiUpdateIntervalMs = 200;
        private const int AdaptiveBatchMinSize = 5_000;
        private const int AdaptiveBatchMaxSize = 400_000;
        private const int AdaptiveBatchTargetMinMs = 45;
        private const int AdaptiveBatchTargetMaxMs = 140;
        private const int ProgressiveUiTargetFrames = 24;
        private const string AutoThreadOptionText = "Авто";

        private readonly FractalBuddhabrotEngine _engine = new();
        private readonly BuddhabrotPaletteManager _paletteManager = new();
        private ColorConfigurationBuddhabrotForm? _buddhabrotColorConfigForm;
        private CancellationTokenSource? _renderCts;
        private readonly System.Windows.Forms.Timer _renderRestartTimer = new() { Interval = 350 };
        private readonly FullscreenToggleController _fullscreenController = new();

        private decimal _centerX = 0;
        private decimal _centerY = 0;
        private bool _controlsPanelVisible = true;
        private bool _isPanning;
        private bool _suppressAutoRender;
        private bool _isQueuingRenderRestart;
        private bool _isProcessingRenderQueue;
        private readonly SemaphoreSlim _renderExecutionLock = new(1, 1);
        private Point _panStartPoint;
        private Bitmap? _interactionSourceBitmap;
        private decimal _interactionSourceCenterX;
        private decimal _interactionSourceCenterY;
        private decimal _interactionSourceZoom = 1.0m;
        private decimal _renderedCenterX;
        private decimal _renderedCenterY;
        private decimal _renderedZoom = 1.0m;
        private int _renderSessionVersion;
        private long _lastCancellationRequestTimestamp;
        private readonly string _baseTitle;

        public FractalBuddhabrotForm()
        {
            InitializeComponent();
            _baseTitle = Text;
            ThemeManager.RegisterForm(this);
            InitializeUiState();
        }

        private void InitializeUiState()
        {
            _modeCombo.SelectedIndex = 0;
            int cores = Environment.ProcessorCount;
            _threadsCombo.Items.Clear();
            _threadsCombo.Items.Add(AutoThreadOptionText);
            for (int i = 1; i <= cores; i++)
            {
                _threadsCombo.Items.Add(i);
            }
            _threadsCombo.SelectedItem = AutoThreadOptionText;

            EnsureActivePalette();

            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseLeave += Canvas_MouseLeave;
            _canvas.MouseEnter += (_, _) => _canvas.Focus();
            _canvas.SizeChanged += Canvas_SizeChanged;
            _renderRestartTimer.Tick += RenderRestartTimer_Tick;
            _btnSaveImage.Click += BtnSaveImage_Click;
            KeyDown += Form_KeyDown;
            AttachAutoRenderControlTriggers();
        }

        private void FractalBuddhabrotForm_Load(object? sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
            ApplyUiToEngine();
            QueueRenderRestart(immediate: true);
        }

        private void FractalBuddhabrotForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            ExitFullscreenSafely();
            _renderCts?.Cancel();
            _renderRestartTimer.Stop();
            _renderRestartTimer.Tick -= RenderRestartTimer_Tick;
            _renderRestartTimer.Dispose();
            _buddhabrotColorConfigForm?.Dispose();
            _buddhabrotColorConfigForm = null;
            _interactionSourceBitmap?.Dispose();
            _interactionSourceBitmap = null;
        }

        private void BtnRender_Click(object? sender, EventArgs e)
        {
            QueueRenderRestart(immediate: true);
        }

        private void BtnSaveLoad_Click(object? sender, EventArgs e)
        {
            using var dialog = new SaveLoadDialogForm(this);
            dialog.ShowDialog(this);
        }

        private void BtnSaveImage_Click(object? sender, EventArgs e)
        {
            if (_btnRender.Enabled == false)
            {
                MessageBox.Show("Процесс рендеринга уже запущен.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var saveManager = new SaveImageManagerForm(this);
            saveManager.ShowDialog(this);
        }

        private void BtnPalette_Click(object? sender, EventArgs e)
        {
            if (_buddhabrotColorConfigForm == null || _buddhabrotColorConfigForm.IsDisposed)
            {
                _buddhabrotColorConfigForm = new ColorConfigurationBuddhabrotForm(_paletteManager, (int)_iterations.Value);
                _buddhabrotColorConfigForm.PaletteApplied += (_, _) =>
                {
                    EnsureActivePalette();
                    QueueRenderRestart(immediate: true);
                };
            }

            _buddhabrotColorConfigForm.ShowDialog(this);
            EnsureActivePalette();
            QueueRenderRestart(immediate: true);
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (_canvas.Width <= 0 || _canvas.Height <= 0)
            {
                return;
            }

            BeginInteractivePreview();

            decimal scaleBeforeZoom = BaseScale / Math.Max(0.0000001m, _zoom.Value);
            decimal mouseReal = _centerX + (e.X - _canvas.Width / 2.0m) * scaleBeforeZoom / _canvas.Width;
            decimal mouseImaginary = _centerY - (e.Y - _canvas.Height / 2.0m) * scaleBeforeZoom / _canvas.Height;

            decimal nextZoom = Math.Clamp(_zoom.Value * (e.Delta > 0 ? 1.25m : 0.8m), _zoom.Minimum, _zoom.Maximum);
            if (nextZoom == _zoom.Value)
            {
                return;
            }

            decimal scaleAfterZoom = BaseScale / Math.Max(0.0000001m, nextZoom);
            _centerX = mouseReal - (e.X - _canvas.Width / 2.0m) * scaleAfterZoom / _canvas.Width;
            _centerY = mouseImaginary + (e.Y - _canvas.Height / 2.0m) * scaleAfterZoom / _canvas.Height;

            _suppressAutoRender = true;
            _zoom.Value = nextZoom;
            _suppressAutoRender = false;

            ApplyInstantViewportPreview();
            QueueRenderRestart();
        }

        private void Canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _canvas.Width <= 0 || _canvas.Height <= 0)
            {
                return;
            }

            BeginInteractivePreview();

            _isPanning = true;
            _panStartPoint = e.Location;
            ApplyInstantViewportPreview();
            _canvas.Cursor = Cursors.SizeAll;
        }

        private void Canvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isPanning || _canvas.Width <= 0 || _canvas.Height <= 0)
            {
                return;
            }

            decimal unitsPerPixel = BaseScale / Math.Max(0.0000001m, _zoom.Value) / _canvas.Width;
            _centerX -= (e.X - _panStartPoint.X) * unitsPerPixel;
            _centerY += (e.Y - _panStartPoint.Y) * unitsPerPixel;
            _panStartPoint = e.Location;
            ApplyInstantViewportPreview();
        }

        private void Canvas_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            bool wasPanning = _isPanning;
            _isPanning = false;
            _canvas.Cursor = Cursors.Default;
            if (wasPanning)
            {
                EndInteractivePreview();
                QueueRenderRestart(immediate: true);
            }
        }

        private void Canvas_MouseLeave(object? sender, EventArgs e)
        {
            bool wasPanning = _isPanning;
            _isPanning = false;
            _canvas.Cursor = Cursors.Default;
            if (wasPanning)
            {
                EndInteractivePreview();
                QueueRenderRestart(immediate: true);
            }
        }

        private void Canvas_SizeChanged(object? sender, EventArgs e)
        {
            if (_canvas.Width <= 1 || _canvas.Height <= 1)
            {
                return;
            }

            QueueRenderRestart();
        }


        private void BeginInteractivePreview()
        {
            InvalidateActiveRenderSession();

            if (_interactionSourceBitmap != null)
            {
                return;
            }

            if (_canvas.Image is not Bitmap currentFrame)
            {
                return;
            }

            _interactionSourceBitmap = (Bitmap)currentFrame.Clone();
            _interactionSourceCenterX = _renderedCenterX;
            _interactionSourceCenterY = _renderedCenterY;
            _interactionSourceZoom = Math.Max(0.0000001m, _renderedZoom);
        }

        private void InvalidateActiveRenderSession()
        {
            long now = Environment.TickCount64;
            Interlocked.Exchange(ref _lastCancellationRequestTimestamp, now);
            Interlocked.Increment(ref _renderSessionVersion);
            _renderCts?.Cancel();

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[Buddhabrot] Cancel requested for active render session at t={now}ms.");
#endif
        }

        private void EndInteractivePreview()
        {
            _interactionSourceBitmap?.Dispose();
            _interactionSourceBitmap = null;
        }

        private void ApplyInstantViewportPreview()
        {
            if (_canvas.Width <= 0 || _canvas.Height <= 0 || _interactionSourceBitmap == null)
            {
                return;
            }

            using var preview = new Bitmap(_canvas.Width, _canvas.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(preview))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.Bilinear;

                decimal sourceScale = BaseScale / Math.Max(0.0000001m, _interactionSourceZoom);
                decimal targetScale = BaseScale / Math.Max(0.0000001m, _zoom.Value);
                float scaleRatio = (float)(sourceScale / targetScale);

                float newWidth = _canvas.Width * scaleRatio;
                float newHeight = _canvas.Height * scaleRatio;
                float offsetX = (float)((_interactionSourceCenterX - _centerX) / targetScale * _canvas.Width);
                float offsetY = (float)(-(_interactionSourceCenterY - _centerY) / targetScale * _canvas.Height);

                float drawX = (_canvas.Width - newWidth) * 0.5f + offsetX;
                float drawY = (_canvas.Height - newHeight) * 0.5f + offsetY;

                g.DrawImage(_interactionSourceBitmap, drawX, drawY, newWidth, newHeight);
            }

            Bitmap? old = _canvas.Image as Bitmap;
            _canvas.Image = (Bitmap)preview.Clone();
            if (!ReferenceEquals(old, _interactionSourceBitmap))
            {
                old?.Dispose();
            }
        }

        private void EnsureActivePalette()
        {
            if (_paletteManager.ActivePalette != null
                && _paletteManager.Palettes.Any(p => string.Equals(p.Name, _paletteManager.ActivePalette.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _paletteManager.ActivePalette = _paletteManager.Palettes.FirstOrDefault()
                ?? BuddhabrotPaletteManager.CreateDefaultBuiltInPalette();
        }

        private BuddhabrotColorPalette ResolvePaletteFromState(BuddhabrotSaveState state)
        {
            if (state.PaletteId != Guid.Empty)
            {
                BuddhabrotColorPalette? byId = _paletteManager.Palettes.FirstOrDefault(p => p.Id == state.PaletteId);
                if (byId is not null)
                {
                    return byId;
                }
            }

            if (!string.IsNullOrWhiteSpace(state.PaletteName))
            {
                BuddhabrotColorPalette? byName = _paletteManager.Palettes.FirstOrDefault(p =>
                    string.Equals(p.Name, state.PaletteName, StringComparison.OrdinalIgnoreCase));
                if (byName is not null)
                {
                    return byName;
                }
            }

            if (state.Palette is not null)
            {
                BuddhabrotColorPalette? byContent = _paletteManager.Palettes.FirstOrDefault(p => ArePalettesEquivalentByContent(p, state.Palette));
                if (byContent is not null)
                {
                    return byContent;
                }

                BuddhabrotColorPalette imported = state.Palette.CloneAsCustom(state.Palette.Name);
                if (state.PaletteId != Guid.Empty)
                {
                    imported.Id = state.PaletteId;
                }

                if (!string.IsNullOrWhiteSpace(state.PaletteName))
                {
                    imported.Name = state.PaletteName;
                }

                string uniqueName = imported.Name;
                int suffix = 1;
                while (_paletteManager.Palettes.Any(p => p.Name.Equals(uniqueName, StringComparison.OrdinalIgnoreCase)))
                {
                    uniqueName = $"{imported.Name} (из сохранения {suffix++})";
                }

                imported.Name = uniqueName;
                _paletteManager.Palettes.Add(imported);
                return imported;
            }

            return _paletteManager.Palettes.FirstOrDefault() ?? BuddhabrotPaletteManager.CreateDefaultBuiltInPalette();
        }

        private static bool ArePalettesEquivalentByContent(BuddhabrotColorPalette a, BuddhabrotColorPalette b)
        {
            if (a.ColoringMode != b.ColoringMode
                || Math.Abs(a.Gamma - b.Gamma) > 0.0000001
                || a.IsGradient != b.IsGradient
                || a.AlignWithRenderIterations != b.AlignWithRenderIterations
                || a.MaxColorIterations != b.MaxColorIterations
                || a.Colors.Count != b.Colors.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Colors.Count; i++)
            {
                if (a.Colors[i].ToArgb() != b.Colors[i].ToArgb())
                {
                    return false;
                }
            }

            return true;
        }

        private void AttachAutoRenderControlTriggers()
        {
            _modeCombo.SelectedIndexChanged += (_, _) => QueueRenderRestart();
            _threadsCombo.SelectedIndexChanged += (_, _) => QueueRenderRestart();
            _iterations.ValueChanged += (_, _) => QueueRenderRestart();
            _samples.ValueChanged += (_, _) => QueueRenderRestart();
            _zoom.ValueChanged += (_, _) =>
            {
                if (_suppressAutoRender)
                {
                    return;
                }

                QueueRenderRestart();
            };
            _sampleMinRe.ValueChanged += (_, _) => QueueRenderRestart();
            _sampleMaxRe.ValueChanged += (_, _) => QueueRenderRestart();
            _sampleMinIm.ValueChanged += (_, _) => QueueRenderRestart();
            _sampleMaxIm.ValueChanged += (_, _) => QueueRenderRestart();
        }

        private void QueueRenderRestart(bool immediate = false)
        {
            if (IsDisposed)
            {
                return;
            }

            _isQueuingRenderRestart = true;
            if (immediate)
            {
                _renderRestartTimer.Stop();
                _ = TriggerQueuedRenderRestartAsync();
                return;
            }

            _renderRestartTimer.Stop();
            _renderRestartTimer.Start();
        }

        private async void RenderRestartTimer_Tick(object? sender, EventArgs e)
        {
            _renderRestartTimer.Stop();
            await TriggerQueuedRenderRestartAsync();
        }

        private async Task TriggerQueuedRenderRestartAsync()
        {
            if (_isProcessingRenderQueue || !_isQueuingRenderRestart)
            {
                return;
            }

            _isProcessingRenderQueue = true;
            try
            {
                while (_isQueuingRenderRestart && !IsDisposed)
                {
                    if (_isPanning)
                    {
                        break;
                    }

                    _isQueuingRenderRestart = false;
                    await RenderAsync();
                }
            }
            finally
            {
                _isProcessingRenderQueue = false;
                if (_isQueuingRenderRestart && !_isPanning && !IsDisposed)
                {
                    _ = TriggerQueuedRenderRestartAsync();
                }
            }
        }

        private void ToggleFullscreenSafely()
        {
            _fullscreenController.Toggle(this);
            UpdateToggleControlsPosition();
            QueueRenderRestart(immediate: true);
        }

        private void ExitFullscreenSafely()
        {
            if (!_fullscreenController.IsFullscreen(this))
            {
                return;
            }

            _fullscreenController.ExitFullscreen(this);
            UpdateToggleControlsPosition();
            QueueRenderRestart(immediate: true);
        }

        private void Form_KeyDown(object? sender, KeyEventArgs e)
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

        private static BuddhabrotRenderMode ParseRenderMode(int modeValue)
        {
            return Enum.IsDefined(typeof(BuddhabrotRenderMode), modeValue)
                ? (BuddhabrotRenderMode)modeValue
                : BuddhabrotRenderMode.Buddhabrot;
        }

        private void ApplyUiToEngine()
        {
            _engine.CenterX = _centerX;
            _engine.CenterY = _centerY;
            _engine.Scale = BaseScale / Math.Max(0.0000001m, _zoom.Value);
            _engine.MaxIterations = (int)_iterations.Value;
            _engine.SampleCount = (int)_samples.Value;
            _engine.ThreadCount = _threadsCombo.SelectedItem?.ToString() == AutoThreadOptionText
                ? 0
                : Convert.ToInt32(_threadsCombo.SelectedItem);
            _engine.RenderMode = ParseRenderMode(_modeCombo.SelectedIndex);

            _engine.SampleMinRe = _sampleMinRe.Value;
            _engine.SampleMaxRe = _sampleMaxRe.Value;
            _engine.SampleMinIm = _sampleMinIm.Value;
            _engine.SampleMaxIm = _sampleMaxIm.Value;

            EnsureActivePalette();
            _engine.DensityPalette = CreateDensityPalette(_paletteManager.ActivePalette, _engine.MaxIterations);
        }

        private static Func<double, Color> CreateDensityPalette(BuddhabrotColorPalette palette, int maxIterations)
        {
            var colors = palette.Colors;
            if (colors == null || colors.Count == 0)
            {
                return t =>
                {
                    int g = (int)(Math.Clamp(t, 0, 1) * 255);
                    return Color.FromArgb(255, g, g, g);
                };
            }

            if (colors.Count == 1)
            {
                Color only = ApplyGamma(colors[0], palette.Gamma);
                return _ => only;
            }

            int cycleLength = palette.AlignWithRenderIterations
                ? Math.Max(2, maxIterations)
                : Math.Max(2, palette.MaxColorIterations);

            return normalized =>
            {
                double mapped = MapNormalizedByMode(normalized, palette.ColoringMode);

                if (palette.IsGradient)
                {
                    double t = Math.Clamp(mapped, 0.0, 1.0);
                    double scaled = t * (colors.Count - 1);
                    int i0 = (int)Math.Floor(scaled);
                    int i1 = Math.Min(i0 + 1, colors.Count - 1);
                    double f = scaled - i0;

                    Color c0 = ApplyGamma(colors[i0], palette.Gamma);
                    Color c1 = ApplyGamma(colors[i1], palette.Gamma);
                    return Color.FromArgb(
                        255,
                        (int)(c0.R + (c1.R - c0.R) * f),
                        (int)(c0.G + (c1.G - c0.G) * f),
                        (int)(c0.B + (c1.B - c0.B) * f));
                }

                // Quantization by cycleLength with explicit edge handling for mapped == 1.0.
                double mappedClamped = Math.Clamp(mapped, 0.0, 1.0);
                double quant = mappedClamped >= 1.0
                    ? 1.0
                    : Math.Floor(mappedClamped * cycleLength) / (cycleLength - 1.0);
                double quantT = Math.Clamp(quant, 0.0, 1.0);
                double quantScaled = quantT * (colors.Count - 1);
                int quantI0 = Math.Min((int)Math.Floor(quantScaled), colors.Count - 1);
                int quantI1 = Math.Min(quantI0 + 1, colors.Count - 1);
                double quantF = quantScaled - quantI0;

                Color quantC0 = ApplyGamma(colors[quantI0], palette.Gamma);
                Color quantC1 = ApplyGamma(colors[quantI1], palette.Gamma);
                return Color.FromArgb(
                    255,
                    (int)(quantC0.R + (quantC1.R - quantC0.R) * quantF),
                    (int)(quantC0.G + (quantC1.G - quantC0.G) * quantF),
                    (int)(quantC0.B + (quantC1.B - quantC0.B) * quantF));
            };
        }

        private static double MapNormalizedByMode(double normalized, BuddhabrotColoringMode mode)
        {
            normalized = Math.Clamp(normalized, 0.0, 1.0);
            return mode switch
            {
                BuddhabrotColoringMode.Linear => normalized,
                BuddhabrotColoringMode.Sqrt => Math.Sqrt(normalized),
                _ => Math.Log(1 + normalized * 15.0) / Math.Log(16.0)
            };
        }

        private static Color ApplyGamma(Color color, double gamma)
        {
            gamma = Math.Clamp(gamma, 0.1, 5.0);
            static int Convert(int v, double g)
            {
                double normalized = v / 255.0;
                double corrected = Math.Pow(normalized, 1.0 / g);
                return (int)Math.Round(Math.Clamp(corrected, 0, 1) * 255.0);
            }

            return Color.FromArgb(color.A, Convert(color.R, gamma), Convert(color.G, gamma), Convert(color.B, gamma));
        }

        private async Task RenderAsync()
        {
            await _renderExecutionLock.WaitAsync();
            try
            {
                if (_canvas.Width <= 0 || _canvas.Height <= 0) return;

                long restartCancellationAt = Environment.TickCount64;
                Interlocked.Exchange(ref _lastCancellationRequestTimestamp, restartCancellationAt);
                _renderCts?.Cancel();
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Buddhabrot] Render restart requested. Previous session cancellation at t={restartCancellationAt}ms.");
#endif
                _renderCts = new CancellationTokenSource();
                var token = _renderCts.Token;
                int sessionVersion = Volatile.Read(ref _renderSessionVersion);

                ApplyUiToEngine();
                decimal renderCenterX = _centerX;
                decimal renderCenterY = _centerY;
                decimal renderZoom = _zoom.Value;
                _renderedCenterX = renderCenterX;
                _renderedCenterY = renderCenterY;
                _renderedZoom = renderZoom;

                int width = _canvas.Width;
                int height = _canvas.Height;
                int totalSamples = _engine.SampleCount;
                var renderStopwatch = System.Diagnostics.Stopwatch.StartNew();

                byte[] pixels = new byte[width * height * 4];
                try
                {
                    SetRenderingState(isRendering: true);
                    _engine.InitializeOrResetDensityBuffer(width, height);
                    var uiUpdateStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    int currentBatchSize = Math.Clamp(
                        Math.Max(AdaptiveBatchMinSize, totalSamples / 120),
                        AdaptiveBatchMinSize,
                        Math.Min(AdaptiveBatchMaxSize, Math.Max(AdaptiveBatchMinSize, totalSamples)));
                    int minSamplesPerPresentedFrame = Math.Max(AdaptiveBatchMinSize, totalSamples / ProgressiveUiTargetFrames);
                    int lastPresentedSamples = 0;

                    while (_engine.ProcessedSamples < totalSamples)
                    {
                        token.ThrowIfCancellationRequested();
                        var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        await Task.Run(() => _engine.AccumulateSamplesBatch(token, currentBatchSize), token);
                        batchStopwatch.Stop();
                        token.ThrowIfCancellationRequested();

                        int processedSamples = _engine.ProcessedSamples;
                        int progress = (int)Math.Round(processedSamples * 100.0 / Math.Max(1, totalSamples));
                        UpdateRenderProgressSafe(Math.Clamp(progress, 0, 100));
                        currentBatchSize = CalculateAdaptiveBatchSize(currentBatchSize, totalSamples, processedSamples, batchStopwatch.ElapsedMilliseconds);

                        bool shouldPresentIntermediate = processedSamples >= totalSamples
                            || uiUpdateStopwatch.ElapsedMilliseconds >= ProgressiveUiUpdateIntervalMs
                            || processedSamples - lastPresentedSamples >= minSamplesPerPresentedFrame;
                        if (!shouldPresentIntermediate)
                        {
                            continue;
                        }

                        token.ThrowIfCancellationRequested();
                        if (sessionVersion != Volatile.Read(ref _renderSessionVersion))
                        {
                            throw new OperationCanceledException(token);
                        }
                        await Task.Run(() => _engine.ConvertCurrentDensityToColor(pixels, width, height, width * 4, 4), token);
                        token.ThrowIfCancellationRequested();
                        if (sessionVersion != Volatile.Read(ref _renderSessionVersion))
                        {
                            throw new OperationCanceledException(token);
                        }
                        PresentRenderedBitmap(pixels, width, height);
                        uiUpdateStopwatch.Restart();
                        lastPresentedSamples = processedSamples;
                    }

                    UpdateRenderProgressSafe(100);
                    EndInteractivePreview();
                    _renderedCenterX = renderCenterX;
                    _renderedCenterY = renderCenterY;
                    _renderedZoom = renderZoom;
                    Text = $"{_baseTitle} - Время последнего рендера: {renderStopwatch.Elapsed.TotalSeconds:F3} сек.";
                }
                catch (OperationCanceledException)
                {
#if DEBUG
                    long cancelAt = Interlocked.Read(ref _lastCancellationRequestTimestamp);
                    if (cancelAt > 0)
                    {
                        long elapsed = Math.Max(0, Environment.TickCount64 - cancelAt);
                        const long targetMs = 150;
                        string marker = elapsed <= targetMs ? "OK" : "SLOW";
                        System.Diagnostics.Debug.WriteLine($"[Buddhabrot] Cancellation reaction: {elapsed} ms (target <={targetMs} ms) [{marker}].");
                    }
#endif
                }
                finally
                {
                    _engine.SampleCount = totalSamples;
                    SetRenderingState(isRendering: false);
                }
            }
            finally
            {
                _renderExecutionLock.Release();
            }
        }

        private static int CalculateAdaptiveBatchSize(int currentBatchSize, int totalSamples, int processedSamples, long elapsedMs)
        {
            int maxBatchByTotal = Math.Max(AdaptiveBatchMinSize, Math.Min(AdaptiveBatchMaxSize, Math.Max(AdaptiveBatchMinSize, totalSamples / 6)));
            int nextBatch = currentBatchSize;

            if (elapsedMs < AdaptiveBatchTargetMinMs)
            {
                nextBatch = (int)Math.Round(currentBatchSize * 1.5);
            }
            else if (elapsedMs > AdaptiveBatchTargetMaxMs)
            {
                nextBatch = (int)Math.Round(currentBatchSize * 0.7);
            }

            // На ранней стадии оставляем батчи меньше, чтобы чаще показывать "оживление" картинки.
            double processedRatio = processedSamples / (double)Math.Max(1, totalSamples);
            if (processedRatio < 0.12)
            {
                int earlyCap = Math.Max(AdaptiveBatchMinSize, totalSamples / 40);
                nextBatch = Math.Min(nextBatch, earlyCap);
            }

            return Math.Clamp(nextBatch, AdaptiveBatchMinSize, maxBatchByTotal);
        }

        private void PresentRenderedBitmap(byte[] pixels, int width, int height)
        {
            using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            Bitmap? old = _canvas.Image as Bitmap;
            _canvas.Image = (Bitmap)bmp.Clone();
            if (!ReferenceEquals(old, _interactionSourceBitmap))
            {
                old?.Dispose();
            }
        }

        private void UpdateRenderProgressSafe(int value)
        {
            if (IsDisposed) return;

            void SetProgress()
            {
                int clamped = Math.Clamp(value, _renderProgress.Minimum, _renderProgress.Maximum);
                _renderProgress.Value = clamped;
                _progressLabel.Text = $"Обработка: {clamped}%";
            }

            if (InvokeRequired)
            {
                BeginInvoke((Action)SetProgress);
            }
            else
            {
                SetProgress();
            }
        }

        private void SetRenderingState(bool isRendering)
        {
            if (IsDisposed) return;

            void UpdateUi()
            {
                _btnRender.Enabled = !isRendering;
                if (!isRendering && _renderProgress.Value >= _renderProgress.Maximum)
                {
                    _progressLabel.Text = "Обработка: завершено";
                }
                else if (!isRendering)
                {
                    _progressLabel.Text = "Обработка: 0%";
                    _renderProgress.Value = 0;
                }
                else
                {
                    _progressLabel.Text = "Обработка: 0%";
                    _renderProgress.Value = 0;
                }
            }

            if (InvokeRequired)
            {
                BeginInvoke((Action)UpdateUi);
            }
            else
            {
                UpdateUi();
            }
        }

        private void BtnToggleControls_Click(object? sender, EventArgs e)
        {
            _controlsPanelVisible = !_controlsPanelVisible;
            _controlsHost.Visible = _controlsPanelVisible;
            _btnToggleControls.Text = _controlsPanelVisible ? "✕" : "☰";
            UpdateToggleControlsPosition();
        }

        private void CanvasHost_Resize(object? sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
        }

        private void ControlsHost_SizeChanged(object? sender, EventArgs e)
        {
            UpdateToggleControlsPosition();
        }

        private void UpdateToggleControlsPosition()
        {
            int targetX = ToggleButtonMargin;
            if (_controlsPanelVisible)
            {
                targetX = _controlsHost.Right + ToggleButtonMargin;
            }

            int maxX = Math.Max(ToggleButtonMargin, _canvasHost.ClientSize.Width - _btnToggleControls.Width - ToggleButtonMargin);
            _btnToggleControls.Location = new Point(Math.Min(targetX, maxX), ToggleButtonMargin);
            _btnToggleControls.BringToFront();
        }

        public string FractalTypeIdentifier => "Buddhabrot";
        public Type ConcreteSaveStateType => typeof(BuddhabrotSaveState);

        public FractalSaveStateBase GetCurrentStateForSave(string saveName)
        {
            ApplyUiToEngine();
            return new BuddhabrotSaveState(FractalTypeIdentifier)
            {
                SaveName = saveName,
                Timestamp = DateTime.Now,
                CenterX = _engine.CenterX,
                CenterY = _engine.CenterY,
                Zoom = _zoom.Value,
                MaxIterations = _engine.MaxIterations,
                SampleCount = _engine.SampleCount,
                PaletteId = _paletteManager.ActivePalette?.Id ?? Guid.Empty,
                PaletteName = _paletteManager.ActivePalette?.Name ?? string.Empty,
                Palette = _paletteManager.ActivePalette?.CloneAsCustom(_paletteManager.ActivePalette.Name),
                RenderMode = (int)_engine.RenderMode,
                SampleMinRe = _engine.SampleMinRe,
                SampleMaxRe = _engine.SampleMaxRe,
                SampleMinIm = _engine.SampleMinIm,
                SampleMaxIm = _engine.SampleMaxIm
            };
        }

        public void LoadState(FractalSaveStateBase state)
        {
            if (state is not BuddhabrotSaveState s) return;

            _centerX = s.CenterX;
            _centerY = s.CenterY;
            _zoom.Value = Math.Clamp(s.Zoom, _zoom.Minimum, _zoom.Maximum);
            _iterations.Value = Math.Clamp(s.MaxIterations, _iterations.Minimum, _iterations.Maximum);
            _samples.Value = Math.Clamp(s.SampleCount, _samples.Minimum, _samples.Maximum);
            _modeCombo.SelectedIndex = Math.Clamp((int)ParseRenderMode(s.RenderMode), 0, _modeCombo.Items.Count - 1);
            _sampleMinRe.Value = Math.Clamp(s.SampleMinRe, _sampleMinRe.Minimum, _sampleMinRe.Maximum);
            _sampleMaxRe.Value = Math.Clamp(s.SampleMaxRe, _sampleMaxRe.Minimum, _sampleMaxRe.Maximum);
            _sampleMinIm.Value = Math.Clamp(s.SampleMinIm, _sampleMinIm.Minimum, _sampleMinIm.Maximum);
            _sampleMaxIm.Value = Math.Clamp(s.SampleMaxIm, _sampleMaxIm.Minimum, _sampleMaxIm.Maximum);
            _paletteManager.ActivePalette = ResolvePaletteFromState(s);

            QueueRenderRestart(immediate: true);
        }

        public Bitmap RenderPreview(FractalSaveStateBase state, int previewWidth, int previewHeight)
        {
            if (state is not BuddhabrotSaveState s)
                throw new InvalidOperationException("Ожидался BuddhabrotSaveState");

            var engine = BuildEngineFromState(s);
            byte[] pixels = new byte[previewWidth * previewHeight * 4];
            engine.RenderToBuffer(pixels, previewWidth, previewHeight, previewWidth * 4, 4, CancellationToken.None);

            var bmp = new Bitmap(previewWidth, previewHeight, PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, previewWidth, previewHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bmp.UnlockBits(data);
            return bmp;
        }

        public async Task<byte[]> RenderPreviewTileAsync(FractalSaveStateBase state, TileInfo tile, int totalWidth, int totalHeight, int tileSize)
        {
            if (state is not BuddhabrotSaveState s)
                return Array.Empty<byte>();

            int w = Math.Max(1, tile.Bounds.Width);
            int h = Math.Max(1, tile.Bounds.Height);
            byte[] pixels = new byte[w * h * 4];

            await Task.Run(() =>
            {
                var engine = BuildEngineFromState(s);
                int fullWidth = Math.Max(1, totalWidth);
                int fullHeight = Math.Max(1, totalHeight);
                byte[] fullPixels = new byte[fullWidth * fullHeight * 4];
                engine.RenderToBuffer(fullPixels, fullWidth, fullHeight, fullWidth * 4, 4, CancellationToken.None);

                for (int y = 0; y < h; y++)
                {
                    int srcY = tile.Bounds.Y + y;
                    if ((uint)srcY >= (uint)fullHeight)
                    {
                        continue;
                    }

                    int srcX = Math.Max(0, tile.Bounds.X);
                    int copyWidth = Math.Min(w, fullWidth - srcX);
                    if (copyWidth <= 0)
                    {
                        continue;
                    }

                    Buffer.BlockCopy(
                        fullPixels,
                        (srcY * fullWidth + srcX) * 4,
                        pixels,
                        y * w * 4,
                        copyWidth * 4);
                }
            });

            return pixels;
        }

        public async Task<byte[]> RenderPreviewAsync(
            FractalSaveStateBase state,
            int previewWidth,
            int previewHeight,
            CancellationToken cancellationToken,
            IProgress<int>? progress = null)
        {
            if (state is not BuddhabrotSaveState s)
            {
                return Array.Empty<byte>();
            }

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            byte[] pixels = new byte[width * height * 4];
            var engine = BuildEngineFromState(s);

            await Task.Run(() =>
            {
                engine.RenderToBuffer(
                    pixels,
                    width,
                    height,
                    width * 4,
                    4,
                    cancellationToken,
                    p => progress?.Report(Math.Clamp(p, 0, 100)));
            }, cancellationToken);

            return pixels;
        }

        public HighResRenderState GetRenderState()
        {
            ApplyUiToEngine();

            return new HighResRenderState
            {
                EngineType = FractalTypeIdentifier,
                CenterX = _engine.CenterX,
                CenterY = _engine.CenterY,
                Zoom = _zoom.Value,
                BaseScale = BaseScale,
                Iterations = _engine.MaxIterations,
                ActivePaletteName = _paletteManager.ActivePalette?.Name ?? string.Empty,
                FileNameDetails = "buddhabrot_fractal",
                BuddhabrotSampleCount = _engine.SampleCount,
                BuddhabrotRenderMode = (int)_engine.RenderMode,
                BuddhabrotSampleMinRe = _engine.SampleMinRe,
                BuddhabrotSampleMaxRe = _engine.SampleMaxRe,
                BuddhabrotSampleMinIm = _engine.SampleMinIm,
                BuddhabrotSampleMaxIm = _engine.SampleMaxIm
            };
        }

        public async Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken)
        {
            BuddhabrotColorPalette palette = _paletteManager.Palettes
                .FirstOrDefault(p => string.Equals(p.Name, state.ActivePaletteName, StringComparison.OrdinalIgnoreCase))
                ?? _paletteManager.ActivePalette;

            var engine = new FractalBuddhabrotEngine
            {
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Scale = state.BaseScale / Math.Max(0.0000001m, state.Zoom),
                MaxIterations = state.Iterations,
                SampleCount = state.BuddhabrotSampleCount ?? _engine.SampleCount,
                ThreadCount = _threadsCombo.SelectedItem?.ToString() == AutoThreadOptionText ? 0 : Convert.ToInt32(_threadsCombo.SelectedItem),
                RenderMode = ParseRenderMode(state.BuddhabrotRenderMode ?? 0),
                SampleMinRe = state.BuddhabrotSampleMinRe ?? _engine.SampleMinRe,
                SampleMaxRe = state.BuddhabrotSampleMaxRe ?? _engine.SampleMaxRe,
                SampleMinIm = state.BuddhabrotSampleMinIm ?? _engine.SampleMinIm,
                SampleMaxIm = state.BuddhabrotSampleMaxIm ?? _engine.SampleMaxIm,
                DensityPalette = CreateDensityPalette(palette, state.Iterations)
            };

            int renderWidth = Math.Max(1, width * Math.Max(1, ssaaFactor));
            int renderHeight = Math.Max(1, height * Math.Max(1, ssaaFactor));
            byte[] pixels = new byte[renderWidth * renderHeight * 4];

            await Task.Run(() =>
            {
                engine.RenderToBuffer(
                    pixels,
                    renderWidth,
                    renderHeight,
                    renderWidth * 4,
                    4,
                    cancellationToken,
                    p => progress.Report(new RenderProgress { Percentage = Math.Clamp(p, 0, 100), Status = "Рендеринг..." }));
            }, cancellationToken);

            var fullBitmap = new Bitmap(renderWidth, renderHeight, PixelFormat.Format32bppArgb);
            BitmapData fullData = fullBitmap.LockBits(new Rectangle(0, 0, renderWidth, renderHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(pixels, 0, fullData.Scan0, pixels.Length);
            }
            finally
            {
                fullBitmap.UnlockBits(fullData);
            }

            if (ssaaFactor <= 1)
            {
                return fullBitmap;
            }

            var downscaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(downscaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(fullBitmap, new Rectangle(0, 0, width, height));
            }

            fullBitmap.Dispose();
            return downscaled;
        }

        public Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight)
        {
            var previewEngine = new FractalBuddhabrotEngine
            {
                CenterX = state.CenterX,
                CenterY = state.CenterY,
                Scale = state.BaseScale / Math.Max(0.0000001m, state.Zoom),
                MaxIterations = Math.Max(50, Math.Min(state.Iterations, 300)),
                SampleCount = Math.Max(25_000, (state.BuddhabrotSampleCount ?? _engine.SampleCount) / 6),
                ThreadCount = 1,
                RenderMode = ParseRenderMode(state.BuddhabrotRenderMode ?? 0),
                SampleMinRe = state.BuddhabrotSampleMinRe ?? _engine.SampleMinRe,
                SampleMaxRe = state.BuddhabrotSampleMaxRe ?? _engine.SampleMaxRe,
                SampleMinIm = state.BuddhabrotSampleMinIm ?? _engine.SampleMinIm,
                SampleMaxIm = state.BuddhabrotSampleMaxIm ?? _engine.SampleMaxIm,
                DensityPalette = CreateDensityPalette(_paletteManager.ActivePalette, state.Iterations)
            };

            int width = Math.Max(1, previewWidth);
            int height = Math.Max(1, previewHeight);
            byte[] pixels = new byte[width * height * 4];
            previewEngine.RenderToBuffer(pixels, width, height, width * 4, 4, CancellationToken.None);

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        public List<FractalSaveStateBase> LoadAllSavesForThisType() =>
            SaveFileManager.LoadSaves<BuddhabrotSaveState>(FractalTypeIdentifier).Cast<FractalSaveStateBase>().ToList();

        public void SaveAllSavesForThisType(List<FractalSaveStateBase> saves) =>
            SaveFileManager.SaveSaves(FractalTypeIdentifier, saves.Cast<BuddhabrotSaveState>().ToList());

        private FractalBuddhabrotEngine BuildEngineFromState(BuddhabrotSaveState s)
        {
            BuddhabrotColorPalette palette = ResolvePaletteFromState(s);
            return new FractalBuddhabrotEngine
            {
                CenterX = s.CenterX,
                CenterY = s.CenterY,
                Scale = BaseScale / Math.Max(0.0000001m, s.Zoom),
                MaxIterations = s.MaxIterations,
                SampleCount = s.SampleCount,
                ThreadCount = _threadsCombo.SelectedItem?.ToString() == AutoThreadOptionText ? 0 : Convert.ToInt32(_threadsCombo.SelectedItem),
                RenderMode = ParseRenderMode(s.RenderMode),
                SampleMinRe = s.SampleMinRe,
                SampleMaxRe = s.SampleMaxRe,
                SampleMinIm = s.SampleMinIm,
                SampleMaxIm = s.SampleMaxIm,
                DensityPalette = CreateDensityPalette(palette, s.MaxIterations)
            };
        }
    }
}
