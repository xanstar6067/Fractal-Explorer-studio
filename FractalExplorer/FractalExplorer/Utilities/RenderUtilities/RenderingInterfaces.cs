using FractalExplorer.Engines;
using FractalExplorer.Resources;

namespace FractalExplorer.Utilities.RenderUtilities
{
    /// <summary>
    /// Представляет состояние фрактала для рендеринга в высоком разрешении.
    /// </summary>
    public class HighResRenderState
    {
        public string EngineType { get; set; }
        public decimal CenterX { get; set; }
        public decimal CenterY { get; set; }
        public decimal Zoom { get; set; }
        public decimal BaseScale { get; set; }
        public int Iterations { get; set; }
        public decimal Threshold { get; set; }
        public string ActivePaletteName { get; set; }
        public ComplexDecimal? JuliaC { get; set; }
        public string FileNameDetails { get; set; }
        public bool UseSmoothColoring { get; set; }
        public int ColoringMode { get; set; }
        public bool HistogramEnabledEqualization { get; set; }
        public double HistogramContrast { get; set; }
        public bool HistogramInputUseSmooth { get; set; }
        public double OrbitTrapStrength { get; set; }
        public double OrbitTrapBias { get; set; }
        public double StripeFrequency { get; set; }
        public double StripeStrength { get; set; }
        public double StripeBias { get; set; }
        public decimal? Power { get; set; }
        public decimal Scale { get; set; }
        public bool UseInversion { get; set; }

        #region Collatz Specific Parameters
        public CollatzVariation? Variation { get; set; }
        public decimal? P_Parameter { get; set; }
        #endregion

        // --- NEW: Nova Specific Parameters ---
        #region Nova Specific Parameters
        /// <summary>
        /// Комплексная степень 'p' для фрактала Nova.
        /// </summary>
        public ComplexDecimal? NovaP { get; set; }
        /// <summary>
        /// Начальное значение Z₀ для фрактала Nova.
        /// </summary>
        public ComplexDecimal? NovaZ0 { get; set; }
        /// <summary>
        /// Параметр релаксации 'm' для фрактала Nova.
        /// </summary>
        public decimal? NovaM { get; set; }
        #endregion
        // --- End of new code ---

        #region Lyapunov Specific Parameters
        public decimal? LyapunovAMin { get; set; }
        public decimal? LyapunovAMax { get; set; }
        public decimal? LyapunovBMin { get; set; }
        public decimal? LyapunovBMax { get; set; }
        public int? LyapunovTransientIterations { get; set; }
        public string? LyapunovPattern { get; set; }
        #endregion

        public HighResRenderState Clone()
        {
            return (HighResRenderState)this.MemberwiseClone();
        }
    }

    /// <summary>
    /// Описывает прогресс выполнения операции рендеринга.
    /// </summary>
    public class RenderProgress
    {
        public int Percentage { get; set; }
        public string Status { get; set; }
    }

    /// <summary>
    /// Интерфейс для объектов, которые могут быть отрендерены в высоком разрешении.
    /// </summary>
    public interface IHighResRenderable
    {
        HighResRenderState GetRenderState();
        Task<Bitmap> RenderHighResolutionAsync(HighResRenderState state, int width, int height, int ssaaFactor, IProgress<RenderProgress> progress, CancellationToken cancellationToken);
        Bitmap RenderPreview(HighResRenderState state, int previewWidth, int previewHeight);
    }
}
