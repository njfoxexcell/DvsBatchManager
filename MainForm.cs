using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Data.SqlClient;

namespace ExcellDvsBatchManager;

public sealed class MainForm : Form
{
    private const string Server            = @"ExcellSQL\ERP";
    private const string Database          = "EXCEL";
    private const string SourcePrefix      = "DVS7";
    private const string ChunkPrefixRoot   = "DVS8";
    private const int    ChunkSize         = 100;

    private readonly BatchRepository _repo;

    private readonly DataGridView _grid = new();
    private readonly Button _refreshBtn = new();
    private readonly Button _markBtn    = new();
    private readonly Button _selectAllBtn = new();
    private readonly Button _clearBtn   = new();
    private readonly Label  _countLabel = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _serverLabel = new();

    private BindingSource _bindingSource = new();
    private List<BatchRow> _rows = new();

    public MainForm()
    {
        _repo = new BatchRepository(BuildConnectionString());
        BuildUi();
        Load += async (_, _) => await LoadBatchesAsync();
    }

    private static string BuildConnectionString()
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = Database,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ApplicationName = "ExcellDvsBatchManager",
            ConnectTimeout = 10
        };
        return b.ConnectionString;
    }

    private void BuildUi()
    {
        Text = "Excell DVS Batch Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 480);
        Size = new Size(1000, 620);
        Icon = System.Drawing.SystemIcons.Application;
        Font = new Font("Segoe UI", 9f);

        // Top toolbar panel
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(8),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _refreshBtn.Text = "Refresh";
        _refreshBtn.AutoSize = true;
        _refreshBtn.Padding = new Padding(8, 4, 8, 4);
        _refreshBtn.Click += async (_, _) => await LoadBatchesAsync();

        _selectAllBtn.Text = "Select All";
        _selectAllBtn.AutoSize = true;
        _selectAllBtn.Padding = new Padding(8, 4, 8, 4);
        _selectAllBtn.Click += (_, _) => SetAllSelection(true);

        _clearBtn.Text = "Clear";
        _clearBtn.AutoSize = true;
        _clearBtn.Padding = new Padding(8, 4, 8, 4);
        _clearBtn.Click += (_, _) => SetAllSelection(false);

        _countLabel.Text = "";
        _countLabel.AutoSize = true;
        _countLabel.Anchor = AnchorStyles.Left;
        _countLabel.Margin = new Padding(12, 8, 0, 0);

        _markBtn.Text = "Mark Completed";
        _markBtn.Enabled = false;
        _markBtn.AutoSize = true;
        _markBtn.Padding = new Padding(10, 4, 10, 4);
        _markBtn.BackColor = Color.FromArgb(33, 136, 56);
        _markBtn.ForeColor = Color.White;
        _markBtn.FlatStyle = FlatStyle.Flat;
        _markBtn.FlatAppearance.BorderSize = 0;
        _markBtn.Click += async (_, _) => await MarkSelectedCompletedAsync();

        top.Controls.Add(_refreshBtn,  0, 0);
        top.Controls.Add(_selectAllBtn, 1, 0);
        top.Controls.Add(_clearBtn,    2, 0);
        top.Controls.Add(_countLabel,  3, 0);
        top.Controls.Add(_markBtn,     4, 0);

        // Grid
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        _grid.ColumnHeadersHeight = 30;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 248, 248);

        var selCol = new DataGridViewCheckBoxColumn
        {
            Name = "Selected",
            HeaderText = "",
            Width = 34,
            FalseValue = false,
            TrueValue = true,
            IndeterminateValue = false
        };
        _grid.Columns.Add(selCol);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "BatchNumber", HeaderText = "Batch Number", Width = 150, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TrxCount",    HeaderText = "Trx Count",    Width = 90,  ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StatusText",  HeaderText = "Status",       Width = 140, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CreatedAt",   HeaderText = "Created",      Width = 140, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ModifiedAt",  HeaderText = "Modified",     Width = 140, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Comment",     HeaderText = "Comment",      AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });

        _grid.CellClick += Grid_CellClick;
        _grid.CellValueChanged += (_, _) => UpdateSelectionState();
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var row = _grid.Rows[e.RowIndex];
            var cur = row.Cells["Selected"].Value is bool b && b;
            row.Cells["Selected"].Value = !cur;
        };

        // Status strip
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _serverLabel.Text = $"{Server} / {Database}";
        _status.Items.Add(_statusLabel);
        _status.Items.Add(new ToolStripSeparator());
        _status.Items.Add(_serverLabel);

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(_status);
    }

    private void Grid_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name != "Selected")
        {
            var row = _grid.Rows[e.RowIndex];
            var cur = row.Cells["Selected"].Value is bool b && b;
            row.Cells["Selected"].Value = !cur;
        }
    }

    private void SetAllSelection(bool value)
    {
        foreach (DataGridViewRow r in _grid.Rows)
            r.Cells["Selected"].Value = value;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var selected = CountSelected();
        _markBtn.Enabled = selected > 0 && !_busy;
        _countLabel.Text = _rows.Count == 0
            ? ""
            : $"{_rows.Count} batch(es) — {selected} selected";
    }

    private int CountSelected() =>
        _grid.Rows.Cast<DataGridViewRow>().Count(r => r.Cells["Selected"].Value is bool b && b);

    private List<string> GetSelectedBatchNumbers() =>
        _grid.Rows.Cast<DataGridViewRow>()
            .Where(r => r.Cells["Selected"].Value is bool b && b)
            .Select(r => r.Cells["BatchNumber"].Value as string ?? "")
            .Where(s => s.Length > 0)
            .ToList();

    private bool _busy;

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        _refreshBtn.Enabled = !busy;
        _selectAllBtn.Enabled = !busy;
        _clearBtn.Enabled = !busy;
        _markBtn.Enabled = !busy && CountSelected() > 0;
        _grid.Enabled = !busy;
        UseWaitCursor = busy;
        if (message != null) _statusLabel.Text = message;
    }

    private async Task LoadBatchesAsync()
    {
        var previouslySelected = new HashSet<string>(GetSelectedBatchNumbers(), StringComparer.OrdinalIgnoreCase);
        SetBusy(true, $"Loading {SourcePrefix}* batches from {Server}…");
        try
        {
            _rows = await _repo.GetPendingDvs7BatchesAsync();
            _grid.Rows.Clear();
            foreach (var r in _rows)
            {
                _grid.Rows.Add(
                    (object)previouslySelected.Contains(r.BatchNumber),
                    r.BatchNumber,
                    r.TrxCount,
                    r.StatusText,
                    (object?)r.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "",
                    (object?)r.ModifiedAt?.ToString("yyyy-MM-dd HH:mm") ?? "",
                    r.Comment ?? "");
            }
            _statusLabel.Text = $"Loaded {_rows.Count} pending {SourcePrefix}* batch(es) at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Load failed";
            MessageBox.Show(this,
                $"Failed to load batches from {Server} / {Database}.\r\n\r\n{ex.Message}",
                "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateSelectionState();
        }
    }

    private static string BuildChunkPrefix(string sourceBatch)
    {
        var datePart = DateTime.Now.ToString("MMddyy");
        var last2 = sourceBatch.Length >= 2 ? sourceBatch[^2..] : sourceBatch;
        return ChunkPrefixRoot + datePart + last2;
    }

    private async Task MarkSelectedCompletedAsync()
    {
        var selected = GetSelectedBatchNumbers();
        if (selected.Count == 0) return;

        var preview = selected.Take(10)
            .Select(b => $"{b}  →  {BuildChunkPrefix(b)}")
            .ToList();

        var confirm = MessageBox.Show(this,
            $"Split {selected.Count} batch(es) into chunks of {ChunkSize}?\r\n\r\n" +
            "Source  →  Chunk Prefix\r\n" +
            string.Join("\r\n", preview) +
            (selected.Count > 10 ? $"\r\n… and {selected.Count - 10} more" : ""),
            "Confirm Mark Completed",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
        if (confirm != DialogResult.OK) return;

        SetBusy(true, $"Splitting {selected.Count} batch(es)…");
        var failures = new List<(string Batch, string Error)>();
        var completed = 0;

        try
        {
            foreach (var batch in selected)
            {
                var prefix = BuildChunkPrefix(batch);
                _statusLabel.Text = $"Splitting {batch} → {prefix} ({completed + failures.Count + 1} of {selected.Count})…";
                Application.DoEvents();
                try
                {
                    await _repo.SplitBatchAsync(batch, prefix, ChunkSize);
                    completed++;
                }
                catch (Exception ex)
                {
                    failures.Add((batch, ex.Message));
                }
            }
        }
        finally
        {
            SetBusy(false);
        }

        if (failures.Count == 0)
        {
            MessageBox.Show(this,
                $"Completed successfully.\r\n\r\n{completed} batch(es) split (chunk size {ChunkSize}).",
                "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            var detail = string.Join("\r\n\r\n", failures.Select(f => $"{f.Batch}: {f.Error}"));
            MessageBox.Show(this,
                $"{completed} succeeded, {failures.Count} failed.\r\n\r\n{detail}",
                "Errors During Split", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        await LoadBatchesAsync();
    }
}
