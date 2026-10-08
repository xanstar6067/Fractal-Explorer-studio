using System.IO;

namespace FractalExplorerWPF.Infrastructure.Migrations;

internal sealed class Migration002CatalogPreviewCache : IUserDataMigration
{
    public int Version => 2;
    public string Description => "Постоянный кэш превью каталога";
    public void Apply(UserDataMigrationContext context) =>
        Directory.CreateDirectory(Path.Combine(context.DataRoot, "CatalogPreviews"));
}
