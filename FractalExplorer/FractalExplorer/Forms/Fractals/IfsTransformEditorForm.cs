using FractalExplorer.Engines;

namespace FractalExplorer.Forms.Fractals
{
    public sealed partial class IfsTransformEditorForm : Form
    {
        private readonly BindingSource _binding = new();

        public List<IfsAffineTransform> ResultTransforms { get; private set; }

        public IfsTransformEditorForm(IEnumerable<IfsAffineTransform> source)
        {
            InitializeComponent();
            ResultTransforms = source.Select(t => t.Clone()).ToList();
            ConfigureGridFormatting();
            BindGrid();
            WireEvents();
            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void ConfigureGridFormatting()
        {
            DataGridViewCellStyle numericStyle = new() { Format = "N6" };
            _colA.DefaultCellStyle = numericStyle;
            _colB.DefaultCellStyle = numericStyle;
            _colC.DefaultCellStyle = numericStyle;
            _colD.DefaultCellStyle = numericStyle;
            _colE.DefaultCellStyle = numericStyle;
            _colF.DefaultCellStyle = numericStyle;
            _colProbability.DefaultCellStyle = numericStyle;
        }

        private void BindGrid()
        {
            _binding.DataSource = ResultTransforms;
            _grid.DataSource = _binding;
        }

        private void WireEvents()
        {
            _btnAdd.Click += BtnAdd_Click;
            _btnRemove.Click += BtnRemove_Click;
            _btnNormalize.Click += BtnNormalize_Click;
            _btnOk.Click += BtnOk_Click;
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            ResultTransforms.Add(new IfsAffineTransform
            {
                A = 0.5,
                D = 0.5,
                Probability = 0.5
            });
            _binding.ResetBindings(false);
        }

        private void BtnRemove_Click(object? sender, EventArgs e)
        {
            if (_grid.CurrentRow?.DataBoundItem is not IfsAffineTransform transform)
            {
                return;
            }

            ResultTransforms.Remove(transform);
            _binding.ResetBindings(false);
        }

        private void BtnNormalize_Click(object? sender, EventArgs e)
        {
            double total = ResultTransforms.Sum(t => Math.Max(0, t.Probability));
            if (total <= 0)
            {
                return;
            }

            foreach (IfsAffineTransform transform in ResultTransforms)
            {
                transform.Probability = Math.Max(0, transform.Probability) / total;
            }

            _binding.ResetBindings(false);
        }

        private void BtnOk_Click(object? sender, EventArgs e)
        {
            if (ResultTransforms.Count > 0)
            {
                return;
            }

            MessageBox.Show(this, "Добавьте хотя бы одно преобразование.", "IFS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }
}
