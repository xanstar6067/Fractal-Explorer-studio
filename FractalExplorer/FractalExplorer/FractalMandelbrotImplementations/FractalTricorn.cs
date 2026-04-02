using FractalDraving;
using FractalExplorer.Engines;
using FractalExplorer.Utilities.SaveIO;
using FractalExplorer.Utilities.SaveIO.SaveStateImplementations;

namespace FractalExplorer.Projects
{
    /// <summary>
    /// Форма для отображения и взаимодействия с фракталом Трикорн (Mandelbar).
    /// </summary>
    public partial class FractalTricorn : FractalMandelbrotFamilyForm
    {
        public FractalTricorn()
        {
            Text = "Фрактал Трикорн";
        }

        protected override FractalMandelbrotFamilyEngine CreateEngine()
        {
            return new TricornEngine();
        }

        protected override void OnPostInitialize()
        {
            lblRe.Visible = false;
            nudRe.Visible = false;
            lblIm.Visible = false;
            nudIm.Visible = false;
            mandelbrotPreviewPanel.Visible = false;
        }

        protected override string GetSaveFileNameDetails()
        {
            return "tricorn";
        }

        public override string FractalTypeIdentifier => "Tricorn";

        public override Type ConcreteSaveStateType => typeof(MandelbrotFamilySaveState);

        public override List<FractalSaveStateBase> LoadAllSavesForThisType()
        {
            var specificSaves = SaveFileManager.LoadSaves<MandelbrotFamilySaveState>(this.FractalTypeIdentifier);
            return specificSaves.Cast<FractalSaveStateBase>().ToList();
        }

        public override void SaveAllSavesForThisType(List<FractalSaveStateBase> saves)
        {
            var specificSaves = saves.Cast<MandelbrotFamilySaveState>().ToList();
            SaveFileManager.SaveSaves(this.FractalTypeIdentifier, specificSaves);
        }
    }
}
