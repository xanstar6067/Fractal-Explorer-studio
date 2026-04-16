namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    /// <summary>
    /// Состояние сохранения для формы орбит логистического отображения.
    /// </summary>
    public class LogisticMapSaveState : FractalSaveStateBase
    {
        public decimal CenterX { get; set; }
        public decimal CenterY { get; set; }
        public decimal Zoom { get; set; }
        public decimal R { get; set; }
        public decimal X0 { get; set; }
        public int Iterations { get; set; }
        public int TransientIterations { get; set; }
        public string VisualizationMode { get; set; } = "Orbit";
        public decimal BifurcationRMin { get; set; } = 2.8m;
        public decimal BifurcationRMax { get; set; } = 4.0m;
        public int BifurcationSamples { get; set; } = 1600;
        public int BifurcationTransient { get; set; } = 500;
        public int BifurcationPlottedPoints { get; set; } = 240;
        public int CobwebSteps { get; set; } = 40;
        public string PaletteName { get; set; } = string.Empty;

        public LogisticMapSaveState()
        {
        }

        public LogisticMapSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
