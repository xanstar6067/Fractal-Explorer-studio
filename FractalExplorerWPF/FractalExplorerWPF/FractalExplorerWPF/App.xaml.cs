using System.Windows;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Infrastructure.Migrations;
using FractalExplorerWPF.Theming;

namespace FractalExplorerWPF;

public partial class App : Application
{
    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        GlobalExceptionHandler.Install(this);
        // До загрузки тем и окон: они уже читают данные из новой раскладки.
        UserDataMigrationResult migration = UserDataMigrator.Run();
        ThemeManager.Initialize(this);
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (!string.IsNullOrWhiteSpace(ThemeManager.InitializationWarning))
        {
            MessageBox.Show(mainWindow, ThemeManager.InitializationWarning, "Темы оформления",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (migration.HasMessageForUser)
        {
            string text = string.Join(Environment.NewLine + Environment.NewLine,
                migration.Notes.Append(migration.Error).OfType<string>()
                    .Append($"Папка с данными: {AppPaths.DataRoot}. Открыть её можно из окна «О программе»."));
            MessageBox.Show(mainWindow, text, "Данные приложения", MessageBoxButton.OK,
                migration.Error is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }
}
