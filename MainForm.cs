using System.Diagnostics;
using System.Globalization;

namespace CSFloatDealFinder;

internal sealed class MainForm : Form
{
    private readonly CsFloatClient _client = new();
    private readonly TextBox _apiKeyBox = new();
    private readonly TextBox _itemNameBox = new();
    private readonly TextBox _minPriceBox = new();
    private readonly TextBox _maxPriceBox = new();
    private readonly TextBox _minDiscountBox = new();
    private readonly ComboBox _sortBox = new();
    private readonly NumericUpDown _pagesBox = new();
    private readonly Button _searchButton = new();
    private readonly DataGridView _resultsGrid = new();
    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progressBar = new();
    private CancellationTokenSource? _searchCancellation;

    public MainForm()
    {
        Text = "CSFloat — deal finder";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(930, 560);
        Size = new Size(1220, 720);
        Font = new Font("Segoe UI", 9F);
        BuildInterface();
        FormClosing += (_, _) => _searchCancellation?.Cancel();
    }

    private void BuildInterface()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var heading = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 10) };
        heading.Controls.Add(new Label
        {
            Text = "CSFloat listing search",
            Font = new Font("Segoe UI", 17F, FontStyle.Bold),
            AutoSize = true,
            Dock = DockStyle.Top
        });
        heading.Controls.Add(new Label
        {
            Text = "Compares against the CSFloat base price of an item, without adding a premium for a specific float.",
            ForeColor = Color.DimGray,
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Padding = new Padding(0, 4, 0, 0)
        });
        layout.Controls.Add(heading, 0, 0);

        var filterGroup = new GroupBox
        {
            Text = "Filters",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10)
        };
        var filters = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 6,
            RowCount = 5,
            Padding = new Padding(2)
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        for (int i = 0; i < 5; i++)
            filters.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddLabel(filters, "CSFloat API key", 0, 0);
        _apiKeyBox.UseSystemPasswordChar = true;
        _apiKeyBox.Dock = DockStyle.Fill;
        _apiKeyBox.Margin = new Padding(4);
        filters.Controls.Add(_apiKeyBox, 1, 0);
        filters.SetColumnSpan(_apiKeyBox, 5);

        AddLabel(filters, "Exact skin name", 1, 0);
        _itemNameBox.Dock = DockStyle.Fill;
        _itemNameBox.Margin = new Padding(4);
        filters.Controls.Add(_itemNameBox, 1, 1);
        filters.SetColumnSpan(_itemNameBox, 5);

        AddLabel(filters, "Price from, $", 2, 0);
        AddTextBox(filters, _minPriceBox, 2, 1);
        AddLabel(filters, "Price to, $", 2, 2);
        AddTextBox(filters, _maxPriceBox, 2, 3);
        AddLabel(filters, "Min. discount, %", 2, 4);
        _minDiscountBox.Text = "10";
        AddTextBox(filters, _minDiscountBox, 2, 5);

        AddLabel(filters, "Sort", 3, 0);
        _sortBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sortBox.Items.AddRange(["CSFloat base discount", "Cheapest"]);
        _sortBox.SelectedIndex = 0;
        _sortBox.Dock = DockStyle.Fill;
        _sortBox.Margin = new Padding(4);
        filters.Controls.Add(_sortBox, 1, 3);

        AddLabel(filters, "Pages (up to 100)", 3, 2);
        _pagesBox.Minimum = 1;
        _pagesBox.Maximum = 100;
        _pagesBox.Value = 3;
        _pagesBox.Width = 85;
        _pagesBox.Margin = new Padding(4);
        filters.Controls.Add(_pagesBox, 3, 3);

        _searchButton.Text = "Find deals";
        _searchButton.AutoSize = true;
        _searchButton.Anchor = AnchorStyles.Right;
        _searchButton.Margin = new Padding(4);
        _searchButton.Click += SearchButton_Click;
        filters.Controls.Add(_searchButton, 4, 3);
        filters.SetColumnSpan(_searchButton, 2);

        var note = new Label
        {
            Text = "Only listings with a CSFloat base from at least 10 sales are included. Stickers and special patterns are excluded.",
            ForeColor = Color.DimGray,
            AutoSize = true,
            Margin = new Padding(4, 8, 4, 2)
        };
        filters.Controls.Add(note, 0, 4);
        filters.SetColumnSpan(note, 6);
        filterGroup.Controls.Add(filters);
        layout.Controls.Add(filterGroup, 0, 1);

        ConfigureResultsGrid();
        layout.Controls.Add(_resultsGrid, 0, 2);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _statusLabel.Text = "Ready to search";
        _statusLabel.AutoSize = true;
        _statusLabel.Anchor = AnchorStyles.Left;
        footer.Controls.Add(_statusLabel, 0, 0);
        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        footer.Controls.Add(_progressBar, 1, 0);
        layout.Controls.Add(footer, 0, 3);
    }

    private static void AddLabel(TableLayoutPanel panel, string text, int row, int column)
    {
        panel.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(4, 7, 6, 4)
        }, column, row);
    }

    private static void AddTextBox(TableLayoutPanel panel, TextBox textBox, int row, int column)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(4);
        panel.Controls.Add(textBox, column, row);
    }

    private void ConfigureResultsGrid()
    {
        _resultsGrid.Dock = DockStyle.Fill;
        _resultsGrid.ReadOnly = true;
        _resultsGrid.AllowUserToAddRows = false;
        _resultsGrid.AllowUserToDeleteRows = false;
        _resultsGrid.AllowUserToResizeRows = false;
        _resultsGrid.MultiSelect = false;
        _resultsGrid.RowHeadersVisible = false;
        _resultsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _resultsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _resultsGrid.BackgroundColor = SystemColors.Window;
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "name",
            HeaderText = "Item",
            FillWeight = 330,
            MinimumWidth = 220
        });
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "price",
            HeaderText = "Price, $",
            FillWeight = 80,
            MinimumWidth = 75
        });
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "base",
            HeaderText = "CSFloat base, $",
            FillWeight = 110,
            MinimumWidth = 100
        });
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "discount",
            HeaderText = "Discount vs base",
            FillWeight = 100,
            MinimumWidth = 95
        });
        _resultsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "float",
            HeaderText = "Float",
            FillWeight = 100,
            MinimumWidth = 90
        });
        _resultsGrid.Columns.Add(new DataGridViewLinkColumn
        {
            Name = "listing",
            HeaderText = "Listing",
            Text = "Open",
            UseColumnTextForLinkValue = true,
            FillWeight = 75,
            MinimumWidth = 70,
            TrackVisitedState = false
        });
        _resultsGrid.CellContentClick += ResultsGrid_CellContentClick;
        _resultsGrid.CellDoubleClick += ResultsGrid_CellDoubleClick;
    }

    private async void SearchButton_Click(object? sender, EventArgs e)
    {
        if (_searchCancellation is not null)
            return;

        SearchOptions options;
        try
        {
            long? minPrice = ParseMoney(_minPriceBox.Text, "Price from");
            long? maxPrice = ParseMoney(_maxPriceBox.Text, "Price to");
            if (minPrice is long min && maxPrice is long max && min > max)
                throw new FormatException("The 'from' price cannot be greater than the 'to' price.");

            if (!decimal.TryParse(
                    _minDiscountBox.Text.Trim().Replace(',', '.'),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal minDiscount) ||
                minDiscount < 0 || minDiscount > 100)
                throw new FormatException("Minimum discount must be a number from 0 to 100.");

            options = new SearchOptions(
                _apiKeyBox.Text.Trim(),
                _itemNameBox.Text.Trim(),
                minPrice,
                maxPrice,
                (double)minDiscount,
                (int)_pagesBox.Value,
                _sortBox.SelectedIndex == 1 ? DealSort.LowestPrice : DealSort.HighestDiscount);
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Check filters",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _searchCancellation = new CancellationTokenSource();
        _searchButton.Enabled = false;
        _progressBar.Value = 0;
        _statusLabel.Text = "Searching for deals below the CSFloat base price…";
        _resultsGrid.Rows.Clear();
        var progress = new Progress<(int Done, int Total)>(update =>
        {
            _statusLabel.Text = $"Loaded pages: {update.Done} of {update.Total}…";
            _progressBar.Value = Math.Min(100, update.Done * 100 / Math.Max(1, update.Total));
        });

        try
        {
            SearchResult result = await _client.FindDealsAsync(
                options,
                progress,
                _searchCancellation.Token);

            foreach (Deal deal in result.Deals)
            {
                int rowIndex = _resultsGrid.Rows.Add(
                    deal.Name,
                    FormatMoney(deal.PriceCents),
                    FormatMoney(deal.BasePriceCents),
                    deal.DiscountPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                    deal.FloatValue?.ToString("0.000000000", CultureInfo.InvariantCulture) ?? "—",
                    "Open");
                _resultsGrid.Rows[rowIndex].Tag = deal;
            }

            string summary =
                $"Found {result.Deals.Count} of {result.LoadedCount} listings ({result.PagesFetched} pages).";
            _statusLabel.Text = result.RateLimitWarning is null
                ? summary + " Double-click to open the listing."
                : summary + " " + result.RateLimitWarning + " Partial results are shown.";
            _progressBar.Value = 100;
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Search canceled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Search failed.";
            MessageBox.Show(this, ex.Message, "Search error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _searchCancellation.Dispose();
            _searchCancellation = null;
            _searchButton.Enabled = true;
        }
    }

    private static long? ParseMoney(string text, string fieldName)
    {
        string normalized = text.Trim().Replace(',', '.');
        if (normalized.Length == 0)
            return null;
        if (!decimal.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal amount) ||
            amount <= 0)
            throw new FormatException($"{fieldName}: enter an amount greater than zero, for example 25.50.");

        decimal cents = decimal.Truncate(amount * 100m);
        if (cents > long.MaxValue)
            throw new FormatException($"{fieldName}: amount is too large.");
        return (long)cents;
    }

    private static string FormatMoney(long cents) =>
        (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private void ResultsGrid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
            _resultsGrid.Columns[e.ColumnIndex].Name == "listing")
            OpenListing(_resultsGrid.Rows[e.RowIndex]);
    }

    private void ResultsGrid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
            OpenListing(_resultsGrid.Rows[e.RowIndex]);
    }

    private void OpenListing(DataGridViewRow row)
    {
        if (row.Tag is not Deal deal)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(
                $"https://csfloat.com/item/{Uri.EscapeDataString(deal.ListingId)}")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to open the listing page: " + ex.Message,
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
