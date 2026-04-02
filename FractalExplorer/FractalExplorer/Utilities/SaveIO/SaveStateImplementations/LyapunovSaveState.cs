namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    /// <summary>
    /// Состояние сохранения для фрактала экспоненты Ляпунова.
    /// </summary>
    public class LyapunovSaveState : FractalSaveStateBase
    {
        public decimal AMin { get; set; }
        public decimal AMax { get; set; }
        public decimal BMin { get; set; }
        public decimal BMax { get; set; }
        public string Pattern { get; set; } = "AB";
        public int Iterations { get; set; }
        public int TransientIterations { get; set; }

        public LyapunovSaveState()
        {
        }

        public LyapunovSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
