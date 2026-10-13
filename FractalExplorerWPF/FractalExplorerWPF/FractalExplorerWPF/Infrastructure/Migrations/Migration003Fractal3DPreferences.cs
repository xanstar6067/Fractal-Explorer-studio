namespace FractalExplorerWPF.Infrastructure.Migrations;

internal sealed class Migration003Fractal3DPreferences : IUserDataMigration
{
    public int Version => 3;
    public string Description => "Общие настройки качества, теней и навигации 3D";

    public void Apply(UserDataMigrationContext context)
    {
        string path = AppPaths.GetSettingsFile(Fractal3DPreferencesStore.FileName);
        if (!System.IO.File.Exists(path)) context.RewriteFile(path, "{}");
    }
}
