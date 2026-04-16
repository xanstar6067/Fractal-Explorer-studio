namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    /// <summary>
    /// Состояние сохранения для формы диаграммы бифуркации логистического отображения.
    /// </summary>
    public class BifurcationSaveState : FractalSaveStateBase
    {
        public decimal CenterX { get; set; } = 3.4m;
        public decimal CenterY { get; set; } = 0.5m;
        public decimal Zoom { get; set; } = 1.0m;

        public decimal RMin { get; set; } = 2.8m;
        public decimal RMax { get; set; } = 4.0m;
        public decimal XMin { get; set; } = 0.0m;
        public decimal XMax { get; set; } = 1.0m;

        public int TransientIterations { get; set; } = 500;
        public int SamplesPerR { get; set; } = 240;
        public int Iterations { get; set; } = 1200;

        public BifurcationSaveState()
        {
        }

        public BifurcationSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
