using FractalExplorer.Engines;
using System.ComponentModel;

namespace FractalExplorer.Forms.Fractals
{
    public sealed partial class FlameTransformEditorForm : Form
    {
        private readonly BindingSource _binding = new();

        public List<FlameTransform> ResultTransforms { get; private set; }

        public FlameTransformEditorForm(IEnumerable<FlameTransform> transforms)
        {
            ResultTransforms = transforms.Select(t => t.Clone()).ToList();
            InitializeComponent();
            ConfigureGrid();
        }

        private void ConfigureGrid()
        {
            _grid.AutoGenerateColumns = false;
            _grid.AllowUserToAddRows = true;
            _grid.AllowUserToDeleteRows = true;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FlameTransform.Weight), HeaderText = "Weight" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FlameTransform.A), HeaderText = "a" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FlameTransform.B), HeaderText = "b" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FlameTransform.C), HeaderText = "c" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FlameTransform.D), HeaderText = "d" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FlameTransform.E), HeaderText = "e" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FlameTransform.F), HeaderText = "f" });
            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                DataPropertyName = nameof(FlameTransform.Variation),
                HeaderText = "Variation",
                DataSource = Enum.GetValues(typeof(FlameVariation))
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FlameTransform.Color), HeaderText = "Color" });

            _binding.DataSource = new BindingList<FlameTransform>(ResultTransforms);
            _grid.DataSource = _binding;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CellParsing += Grid_CellParsing;
            _btnOk.Click += (_, _) => CommitValues();
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (_grid.Columns[e.ColumnIndex].DataPropertyName != nameof(FlameTransform.Color) || e.Value is not Color c)
            {
                return;
            }

            e.Value = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            e.FormattingApplied = true;
        }

        private void Grid_CellParsing(object? sender, DataGridViewCellParsingEventArgs e)
        {
            if (_grid.Columns[e.ColumnIndex].DataPropertyName != nameof(FlameTransform.Color) || e.Value is not string text)
            {
                return;
            }

            string normalized = text.Trim();
            if (normalized.StartsWith("#"))
            {
                normalized = normalized[1..];
            }

            if (normalized.Length == 6 && int.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            {
                int r = (rgb >> 16) & 0xFF;
                int g = (rgb >> 8) & 0xFF;
                int b = rgb & 0xFF;
                e.Value = Color.FromArgb(255, r, g, b);
                e.ParsingApplied = true;
            }
        }

        private void CommitValues()
        {
            _grid.EndEdit();
            ResultTransforms = ((BindingList<FlameTransform>)_binding.List)
                .Where(t => t != null && t.Weight > 0)
                .Select(t => t.Clone())
                .ToList();
        }
    }
}
