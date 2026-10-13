using System.IO;
using System.Windows.Controls;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Migrations;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Views;

internal static partial class Program
{
    private static void VerifyFractal3DPreferences()
    {
        using var sandbox = DataSandbox.Create("fractal3d-preferences");
        EnsureWindowThemeStyles();
        string path = AppPaths.GetSettingsFile(Fractal3DPreferencesStore.FileName);
        var migration = new Migration003Fractal3DPreferences();
        var context = new UserDataMigrationContext(AppPaths.DataRoot, Path.Combine(sandbox.Root, "Backups"));
        migration.Apply(context);
        Check(File.ReadAllText(path) == "{}", "New 3D preferences must initially leave preset defaults intact.");

        var first = new Fractal3DWindow(Fractal3DKind.Mandelbulb);
        Fractal3DWindow? second = null;
        Fractal3DWindow? reopened = null;
        try
        {
            T Field<T>(Fractal3DWindow window, string name) where T : class => (T)window.FindName(name);
            var original = first.CaptureState("original");
            var defaults = Fractal3DCatalog.GetPresets(Fractal3DKind.Mandelbulb)[0];
            Check(original.MaxSteps == defaults.MaxSteps && original.SoftShadows == defaults.SoftShadows &&
                  original.MotionQuality == defaults.MotionQuality && original.NavigationMode == defaults.NavigationMode,
                "An empty preference file must preserve the starting view.");

            // Настройки меняются через настоящие обработчики XAML, без показа окна или рендера.
            Field<ComboBox>(first, "NavigationModeBox").SelectedIndex = (int)Fractal3DNavigationMode.Game;
            Field<ComboBox>(first, "MotionQualityBox").SelectedIndex = (int)Fractal3DMotionQuality.Full;
            Field<ComboBox>(first, "MotionResolutionBox").SelectedIndex = (int)Fractal3DMotionResolution.Half;
            Field<Slider>(first, "GameSpeedSlider").Value = 2.5;
            Field<Slider>(first, "GameSensitivitySlider").Value = 1.7;
            Field<CheckBox>(first, "RotationInertiaBox").IsChecked = false;
            Field<TextBox>(first, "AutoRotateSpeedBox").Text = "25";
            Field<TextBox>(first, "MaxStepsBox").Text = "333";
            Field<TextBox>(first, "DetailBox").Text = "0.4";
            Field<TextBox>(first, "MaxDistanceBox").Text = "80";
            Field<ComboBox>(first, "SsaaBox").SelectedIndex = 2;
            Field<CheckBox>(first, "SoftShadowsBox").IsChecked = false;
            Field<TextBox>(first, "ShadowSharpnessBox").Text = "42";
            Field<CheckBox>(first, "AmbientOcclusionBox").IsChecked = false;
            Field<TextBox>(first, "AoStrengthBox").Text = "0.3";

            void CheckPreferences(Fractal3DState state)
            {
                Check(state.NavigationMode == Fractal3DNavigationMode.Game &&
                      state.MotionQuality == Fractal3DMotionQuality.Full && state.MotionResolution == Fractal3DMotionResolution.Half &&
                      state.GameMovementSpeed == 2.5 && state.GameMouseSensitivity == 1.7 && !state.RotationInertia &&
                      state.AutoRotateSpeed == 25 && state.MaxSteps == 333 && state.Detail == 0.4 && state.MaxDistance == 80 &&
                      state.Ssaa == 4 && !state.SoftShadows && state.ShadowSharpness == 42 && !state.AmbientOcclusion && state.AoStrength == 0.3,
                    "Quality, shadows and navigation preferences must survive preset selection and reopening in another mode.");
            }

            CheckPreferences(Fractal3DPreferencesStore.Load().ApplyTo(defaults));
            string stored = File.ReadAllText(path);
            migration.Apply(context);
            Check(File.ReadAllText(path) == stored, "Repeating the migration must preserve existing preferences.");

            Field<TextBox>(first, "MaxStepsBox").Text = "";
            Field<TextBox>(first, "ShadowSharpnessBox").Text = "129";
            Check(File.ReadAllText(path) == stored, "Incomplete or out-of-range input must preserve the last valid preference.");
            // При выборе другого готового вида меняется форма, а предпочтения остаются.
            Field<ComboBox>(first, "PresetBox").SelectedIndex = 1;
            CheckPreferences(first.CaptureState("preset"));
            Check(first.CaptureState("preset").Power == Fractal3DCatalog.GetPresets(first.Kind)[1].Power,
                "Selecting a preset must still apply its shape parameters.");
            Check(File.ReadAllText(path) == stored, "Applying a preset must not write its defaults to preferences.");

            second = new Fractal3DWindow(Fractal3DKind.Mandelbox);
            CheckPreferences(second.CaptureState("another mode"));
            Field<TextBox>(second, "MaxStepsBox").Text = "444";
            Field<ComboBox>(first, "MotionQualityBox").SelectedIndex = (int)Fractal3DMotionQuality.Draft;
            Fractal3DPreferences merged = Fractal3DPreferencesStore.Load();
            Check(merged.MaxSteps == 444 && merged.MotionQuality == Fractal3DMotionQuality.Draft,
                "Changes in different open windows must merge without overwriting unrelated preferences.");

            stored = File.ReadAllText(path);
            first.LoadState(original);
            var restored = first.CaptureState("restored");
            Check(restored.MaxSteps == original.MaxSteps && restored.NavigationMode == original.NavigationMode &&
                  restored.SoftShadows == original.SoftShadows && restored.MotionQuality == original.MotionQuality,
                "Loading a saved scene must restore its own settings exactly.");
            Check(File.ReadAllText(path) == stored, "Loading a scene must not overwrite application preferences.");

            first.Close();
            reopened = new Fractal3DWindow(Fractal3DKind.Mandelbulb);
            Check(reopened.CaptureState("reopened").MaxSteps == 444 &&
                  reopened.CaptureState("reopened").MotionQuality == Fractal3DMotionQuality.Draft,
                "Preferences must be read from disk after closing and reopening the window.");

            File.WriteAllText(path, "{broken");
            Check(Fractal3DPreferencesStore.Load().ApplyTo(defaults).MaxSteps == defaults.MaxSteps,
                "A damaged preference file must fall back to the preset.");
            File.WriteAllText(path, "{\"NavigationMode\":999,\"MotionQuality\":999,\"Ssaa\":3,\"MaxSteps\":0,\"Detail\":-1,\"ShadowSharpness\":999,\"SoftShadows\":false}");
            Fractal3DState safe = Fractal3DPreferencesStore.Load().ApplyTo(defaults);
            Check(safe.NavigationMode == defaults.NavigationMode && safe.MotionQuality == defaults.MotionQuality &&
                  safe.Ssaa == defaults.Ssaa && safe.MaxSteps == defaults.MaxSteps && safe.Detail == defaults.Detail &&
                  safe.ShadowSharpness == defaults.ShadowSharpness && !safe.SoftShadows,
                "Invalid fields must fall back independently while valid preferences remain effective.");
            Check(defaults.SoftShadows == original.SoftShadows && defaults.MaxSteps == original.MaxSteps,
                "Applying preferences must not mutate catalog presets.");
        }
        finally
        {
            first.Close();
            second?.Close();
            reopened?.Close();
        }
        Console.WriteLine("PASS (3D preferences): persistence, presets, other modes, multiple windows, saved scenes and invalid input.");
    }
}
