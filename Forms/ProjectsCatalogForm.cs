using System.Data;
using FlowStudio.Services;

namespace FlowStudio.Forms;

public sealed class ProjectsCatalogForm : Form
{
    private readonly DatabaseService _database;
    private readonly Func<int, Task> _openProject;
    private readonly DataGridView _grid = new();
    private readonly TextBox _authorFilter = new();
    private readonly DateTimePicker _dateFrom = new();
    private readonly DateTimePicker _dateTo = new();
    private readonly CheckBox _useDates = new() { Text = "Фильтр по дате", AutoSize = true };

    public ProjectsCatalogForm(DatabaseService database, Func<int, Task> openProject)
    {
        _database = database;
        _openProject = openProject;
        Text = "Каталог проектов";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 600);
        MinimumSize = new Size(760, 420);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8), WrapContents = true };
        filters.Controls.Add(new Label { Text = "Автор:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _authorFilter.Width = 150;
        filters.Controls.Add(_authorFilter);
        filters.Controls.Add(_useDates);
        filters.Controls.Add(new Label { Text = "С:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        ConfigureDate(_dateFrom);
        filters.Controls.Add(_dateFrom);
        filters.Controls.Add(new Label { Text = "По:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        ConfigureDate(_dateTo);
        filters.Controls.Add(_dateTo);
        var search = new Button { Text = "Найти", AutoSize = true };
        search.Click += async (_, _) => await RefreshProjectsAsync();
        filters.Controls.Add(search);
        var reset = new Button { Text = "Сбросить", AutoSize = true };
        reset.Click += async (_, _) =>
        {
            _authorFilter.Clear();
            _useDates.Checked = false;
            await RefreshProjectsAsync();
        };
        filters.Controls.Add(reset);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.DoubleClick += async (_, _) => await OpenSelectedAsync();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var open = new Button { Text = "Открыть схему", AutoSize = true };
        open.Click += async (_, _) => await OpenSelectedAsync();
        var delete = new Button { Text = "Удалить", AutoSize = true };
        delete.Click += async (_, _) => await DeleteSelectedAsync();
        var refresh = new Button { Text = "Обновить", AutoSize = true };
        refresh.Click += async (_, _) => await RefreshProjectsAsync();
        buttons.Controls.Add(open);
        buttons.Controls.Add(delete);
        buttons.Controls.Add(refresh);

        Controls.Add(_grid);
        Controls.Add(buttons);
        Controls.Add(filters);
        Shown += async (_, _) => await RefreshProjectsAsync();
    }

    private static void ConfigureDate(DateTimePicker picker)
    {
        picker.Format = DateTimePickerFormat.Short;
        picker.Width = 110;
        picker.Enabled = false;
    }

    private async Task RefreshProjectsAsync()
    {
        try
        {
            _dateFrom.Enabled = _useDates.Checked;
            _dateTo.Enabled = _useDates.Checked;
            DateTime? from = _useDates.Checked ? _dateFrom.Value.Date : null;
            DateTime? to = _useDates.Checked ? _dateTo.Value.Date : null;
            if (from > to)
            {
                MessageBox.Show(this, "Начальная дата не может быть позже конечной.", "Фильтр", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _grid.DataSource = await _database.GetProjectsSummaryAsync(_authorFilter.Text, from, to);
            if (_grid.Columns["ID"] is not null) _grid.Columns["ID"].FillWeight = 35;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось загрузить каталог.\n" + ex.Message, "Ошибка БД", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private int? SelectedProjectId()
    {
        if (_grid.CurrentRow?.Cells["ID"].Value is null) return null;
        return Convert.ToInt32(_grid.CurrentRow.Cells["ID"].Value);
    }

    private async Task OpenSelectedAsync()
    {
        int? id = SelectedProjectId();
        if (id is null)
        {
            MessageBox.Show(this, "Сначала выберите проект.", "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            await _openProject(id.Value);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка загрузки", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        int? id = SelectedProjectId();
        if (id is null)
        {
            MessageBox.Show(this, "Сначала выберите проект.", "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, "Удалить выбранный проект и все его фигуры?", "Подтверждение",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            await _database.DeleteProjectAsync(id.Value);
            await RefreshProjectsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка удаления", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
