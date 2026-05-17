using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScottPlot;
using ScottPlot.WinForms;
using SCADA_Datalogging.Models;
using SCADA_Datalogging.Services;

namespace SCADA_Datalogging
{
    public class MainForm : Form
    {
        // ── Services ──────────────────────────────────────────────────────
        private readonly OpcUaReader      _opc = new OpcUaReader();
        private          DatabaseService  _db;

        // ── Timers ────────────────────────────────────────────────────────
        private readonly System.Windows.Forms.Timer _logTimer   =
            new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly System.Windows.Forms.Timer _chartTimer =
            new System.Windows.Forms.Timer { Interval = 5000 };
        private readonly System.Windows.Forms.Timer _statusTimer =
            new System.Windows.Forms.Timer { Interval = 3000 };

        private bool _logging    = false;
        private long _rowsLogged = 0;

        private OpcSnapshot _lastSnap;

        // ── UI controls ───────────────────────────────────────────────────
        private FormsPlot    _chart          = null!;
        private TabControl   _tabs           = null!;
        private DataGridView _grid           = null!;
        private TextBox      _txtOpcEndpoint = null!;
        private TextBox      _txtConnStr     = null!;
        private Button       _btnOpcConnect  = null!;
        private Button       _btnDbConnect   = null!;
        private Button       _btnStartStop   = null!;
        private Label        _lblOpcStatus   = null!;
        private Label        _lblDbStatus    = null!;
        private Label        _lblTout        = null!;
        private Label        _lblU           = null!;
        private Label        _lblSp          = null!;
        private Label        _lblRowsLogged  = null!;
        private Label        _lblLastWrite   = null!;
        private NumericUpDown _nudInterval   = null!;
        private ComboBox     _cboSensor      = null!;

        public MainForm()
        {
            BuildUi();
            _logTimer.Tick    += OnLogTick;
            _chartTimer.Tick  += OnChartTick;
            _statusTimer.Tick += OnStatusTick;
        }

        private async void OnChartTick(object sender, EventArgs e)
        {
            await RefreshChartAsync();
        }

        private async void OnStatusTick(object sender, EventArgs e)
        {
            await RefreshRowCountAsync();
        }

        // ─────────────────────────────────────────────────────────────────
        // Log tick — every 1 s
        // ─────────────────────────────────────────────────────────────────
        private async void OnLogTick(object sender, EventArgs e)
        {
            // 1. Read from OPC UA
            _lastSnap = _opc.ReadSnapshot();

            if (_lastSnap == null)
            {
                UpdateStatus("OPC read failed — " + _opc.LastError, error: true);
                return;
            }

            // 2. Update live readouts
            _lblTout.Text = string.Format("{0:F2} °C", _lastSnap.Temperature);
            _lblU.Text    = string.Format("{0:F3} V",  _lastSnap.ControlSignal);
            _lblSp.Text   = string.Format("{0:F1} °C", _lastSnap.Setpoint);

            // 3. Write to database
            if (_db != null && _db.IsConnected)
            {
                bool ok = await _db.InsertSnapshotAsync(_lastSnap);
                if (ok)
                {
                    _rowsLogged += 3;
                    _lblRowsLogged.Text = string.Format(
                        "{0:N0} rows this session", _rowsLogged);
                    _lblLastWrite.Text  = "Last write: " +
                        DateTime.Now.ToString("HH:mm:ss");
                    _lblLastWrite.ForeColor = Color.FromArgb(136, 135, 128);
                }
                else
                {
                    UpdateStatus("DB write failed — " + _db.LastError, error: true);
                }
            }
        }

        private async Task RefreshChartAsync()
        {
            if (_db == null || !_db.IsConnected) return;

            // Read combobox on UI thread — safe here
            int sensorId = _cboSensor.SelectedIndex == 0 ? 1
                         : _cboSensor.SelectedIndex == 1 ? 2 : 3;

            var data = await _db.GetRecentAsync(sensorId, 300);
            if (data.Count < 2) return;

            double[] xs = data.Select(m => m.Timestamp.ToOADate()).ToArray();
            double[] ys = data.Select(m => m.Value).ToArray();

            // Already on UI thread — draw directly
            DrawChart(xs, ys, sensorId, data);
        }

        private void DrawChart(double[] xs, double[] ys, int sensorId,
                               List<Measurement> data)
        {
            var plt = _chart.Plot;
            plt.Clear();

            var scatter = plt.AddScatter(xs, ys,
                color: sensorId == 1 ? Color.FromArgb(29, 158, 117)
                     : sensorId == 2 ? Color.FromArgb(239, 159, 39)
                     :                 Color.FromArgb(55, 138, 221));
            scatter.LineWidth = 1.5f;
            scatter.MarkerSize = 0;

            plt.XAxis.DateTimeFormat(true);
            string unit = data.Count > 0 ? data[0].Unit : "";
            string name = data.Count > 0 ? data[0].SensorName : "";
            plt.YLabel(unit);
            plt.Title(string.Format("{0} — last {1} samples from DB",
                name, data.Count));
            plt.AxisAuto();   // scale axes to fit actual data
            _chart.Refresh();

            RefreshGrid(data);
        }

        private void RefreshGrid(List<Measurement> data)
        {
            _grid.Rows.Clear();
            var reversed = data.AsEnumerable().Reverse().Take(100);
            foreach (var m in reversed)
            {
                _grid.Rows.Add(
                    m.Timestamp.ToString("HH:mm:ss.fff"),
                    m.SensorName,
                    string.Format("{0:F3}", m.Value),
                    m.Unit
                );
            }
        }

        private async Task RefreshRowCountAsync()
        {
            if (_db == null || !_db.IsConnected) return;
            long total = await _db.GetTotalRowsAsync();
            _lblDbStatus.Text = string.Format(
                "● Connected  ({0:N0} total rows in DB)", total);
        }

        // ─────────────────────────────────────────────────────────────────
        // Button handlers
        // ─────────────────────────────────────────────────────────────────
        private void ConnectOpc()
        {
            _btnOpcConnect.Enabled  = false;
            _lblOpcStatus.Text      = "Connecting…";
            _lblOpcStatus.ForeColor = Color.Gray;

            // Opc.UaFx.Client is synchronous — no Task.Run needed
            bool ok = _opc.Connect(_txtOpcEndpoint.Text.Trim());

            _lblOpcStatus.Text      = ok ? "● Connected" : ("✕ " + _opc.LastError);
            _lblOpcStatus.ForeColor = ok ? Color.FromArgb(29, 158, 117) : Color.IndianRed;
            _btnOpcConnect.Text    = ok ? "Disconnect OPC" : "Connect OPC";
            _btnOpcConnect.Enabled = true;
            UpdateStartButton();
        }

        private async void ConnectDb()
        {
            _btnDbConnect.Enabled  = false;
            _lblDbStatus.Text      = "Connecting…";
            _lblDbStatus.ForeColor = Color.Gray;

            _db?.Dispose();
            _db = new DatabaseService(_txtConnStr.Text.Trim());
            bool ok = await _db.ConnectAsync();

            _lblDbStatus.Text      = ok ? "● Connected" : ("✕ " + _db.LastError);
            _lblDbStatus.ForeColor = ok
                ? Color.FromArgb(29, 158, 117) : Color.IndianRed;
            _btnDbConnect.Text    = ok ? "Disconnect DB" : "Connect DB";
            _btnDbConnect.Enabled = true;
            UpdateStartButton();
        }

        private void StartStopLogging()
        {
            _logging = !_logging;
            if (_logging)
            {
                _rowsLogged = 0;
                _logTimer.Interval = (int)_nudInterval.Value * 1000;
                _logTimer.Start();
                _chartTimer.Start();
                _statusTimer.Start();
                _btnStartStop.Text      = "⏹  Stop logging";
                _btnStartStop.BackColor = Color.FromArgb(180, 50, 50);
            }
            else
            {
                _logTimer.Stop();
                _chartTimer.Stop();
                _statusTimer.Stop();
                _btnStartStop.Text      = "▶  Start logging";
                _btnStartStop.BackColor = Color.FromArgb(29, 158, 117);
            }
        }

        private void UpdateStartButton()
        {
            _btnStartStop.Enabled =
                _opc.IsConnected && _db != null && _db.IsConnected;
        }

        private void UpdateStatus(string msg, bool error = false)
        {
            if (InvokeRequired) { Invoke(new Action(() => UpdateStatus(msg, error))); return; }
            _lblLastWrite.ForeColor = error ? Color.IndianRed
                                            : Color.FromArgb(136, 135, 128);
            _lblLastWrite.Text = msg;
        }

        // ─────────────────────────────────────────────────────────────────
        // UI construction
        // ─────────────────────────────────────────────────────────────────
        private void BuildUi()
        {
            Text        = "SCADA — Datalogging System";
            Size        = new Size(1100, 700);
            MinimumSize = new Size(900, 580);
            BackColor   = Color.FromArgb(245, 244, 240);
            Font        = new Font("Segoe UI", 9f);

            var split = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                SplitterDistance = 280,
                FixedPanel       = FixedPanel.Panel1,
                BorderStyle      = BorderStyle.None,
                BackColor        = Color.FromArgb(245, 244, 240),
            };
            Controls.Add(split);

            BuildLeft(split.Panel1);
            BuildRight(split.Panel2);
        }

        private void BuildLeft(Panel parent)
        {
            var flow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents  = false,
                AutoScroll    = true,
                Padding       = new Padding(10, 10, 4, 10),
            };
            parent.Controls.Add(flow);

            // Badge
            var badge = MakeLabel("📋  DATALOGGING SYSTEM", bold: true,
                fg: Color.FromArgb(24, 95, 165),
                bg: Color.FromArgb(230, 241, 251));
            badge.Size      = new Size(258, 28);
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.Margin    = new Padding(0, 0, 0, 8);
            flow.Controls.Add(badge);

            // OPC UA
            flow.Controls.Add(MakeHeader("OPC UA server"));
            _txtOpcEndpoint = MakeTxt("opc.tcp://localhost:49320", mono: true);
            flow.Controls.Add(_txtOpcEndpoint);
            _btnOpcConnect = MakeBtn("Connect OPC",
                Color.FromArgb(55, 138, 221));
            _btnOpcConnect.Click += (s, e) => ConnectOpc();
            flow.Controls.Add(_btnOpcConnect);
            _lblOpcStatus = MakeLabel("● Not connected", fg: Color.Gray);
            _lblOpcStatus.Margin = new Padding(0, 2, 0, 6);
            flow.Controls.Add(_lblOpcStatus);

            // SQL Server
            flow.Controls.Add(MakeHeader("SQL Server"));
            _txtConnStr = MakeTxt(
                @"Server=localhost\SQLEXPRESS;Database=SCADA_DB;" +
                "User Id=scada_user;Password=Scada123!;" +
                "TrustServerCertificate=True;",
                mono: true, multiline: true, height: 60);
            flow.Controls.Add(_txtConnStr);
            _btnDbConnect = MakeBtn("Connect DB",
                Color.FromArgb(55, 138, 221));
            _btnDbConnect.Click += (s, e) => ConnectDb();
            flow.Controls.Add(_btnDbConnect);
            _lblDbStatus = MakeLabel("● Not connected", fg: Color.Gray);
            _lblDbStatus.Margin = new Padding(0, 2, 0, 6);
            flow.Controls.Add(_lblDbStatus);

            // Logging settings
            flow.Controls.Add(MakeHeader("Logging settings"));
            var row = new Panel
                { Width = 258, Height = 30, Margin = new Padding(0, 2, 0, 4) };
            row.Controls.Add(new Label
            {
                Text = "Interval", Location = new Point(0, 7), AutoSize = true,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(61, 61, 58),
            });
            _nudInterval = new NumericUpDown
            {
                Minimum = 1, Maximum = 60, Value = 1,
                Location = new Point(70, 3), Width = 60,
                Font = new Font("Segoe UI", 9f),
            };
            _nudInterval.ValueChanged += (s, e) =>
            {
                if (_logging)
                    _logTimer.Interval = (int)_nudInterval.Value * 1000;
            };
            row.Controls.Add(_nudInterval);
            row.Controls.Add(new Label
            {
                Text = "seconds", Location = new Point(136, 7),
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.Gray, AutoSize = true,
            });
            flow.Controls.Add(row);

            _btnStartStop = MakeBtn("▶  Start logging",
                Color.FromArgb(29, 158, 117));
            _btnStartStop.Enabled = false;
            _btnStartStop.Click += (s, e) => StartStopLogging();
            flow.Controls.Add(_btnStartStop);

            // Live readings
            flow.Controls.Add(MakeHeader("Live OPC readings"));
            flow.Controls.Add(MakeReadout("T_out",    out _lblTout));
            flow.Controls.Add(MakeReadout("u",        out _lblU));
            flow.Controls.Add(MakeReadout("Setpoint", out _lblSp));

            // Session stats
            flow.Controls.Add(MakeHeader("Session stats"));
            _lblRowsLogged = MakeLabel("0 rows this session",
                fg: Color.FromArgb(61, 61, 58));
            _lblRowsLogged.Margin = new Padding(0, 2, 0, 0);
            flow.Controls.Add(_lblRowsLogged);
            _lblLastWrite = MakeLabel("Last write: —",
                fg: Color.FromArgb(136, 135, 128));
            _lblLastWrite.Margin = new Padding(0, 2, 0, 0);
            flow.Controls.Add(_lblLastWrite);
        }

        private void BuildRight(Panel parent)
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };
            parent.Controls.Add(_tabs);

            // Chart tab
            var tabChart = new TabPage("Chart")
                { BackColor = Color.FromArgb(245, 244, 240) };
            _tabs.TabPages.Add(tabChart);

            var top = new Panel
                { Dock = DockStyle.Top, Height = 38,
                  Padding = new Padding(8, 6, 8, 0) };
            tabChart.Controls.Add(top);

            top.Controls.Add(new Label
            {
                Text = "Sensor:", Location = new Point(8, 10),
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(61, 61, 58), AutoSize = true,
            });
            _cboSensor = new ComboBox
            {
                Location = new Point(65, 7), Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f),
            };
            _cboSensor.Items.AddRange(new object[]
            {
                "Temperature [°C]",
                "Control signal [V]",
                "Setpoint [°C]",
            });
            _cboSensor.SelectedIndex = 0;
            _cboSensor.SelectedIndexChanged += async (s, e) =>
            {
                await RefreshChartAsync();
            };
            top.Controls.Add(_cboSensor);

            var btnRefresh = MakeBtn("↻ Refresh",
                Color.FromArgb(136, 135, 128));
            btnRefresh.Size     = new Size(90, 26);
            btnRefresh.Location = new Point(240, 5);
            btnRefresh.Font     = new Font("Segoe UI", 8.5f);
            btnRefresh.Click   += async (s, e) =>
            {
                await RefreshChartAsync();
            };
            top.Controls.Add(btnRefresh);

            _chart = new FormsPlot { Dock = DockStyle.Fill };
            tabChart.Controls.Add(_chart);

            _chart.Plot.Title("Measurements from database");
            _chart.Plot.XLabel("Time");
            _chart.Plot.YLabel("Value");
            _chart.Refresh();

            // Table tab
            var tabTable = new TabPage("Recent records")
                { BackColor = Color.White };
            _tabs.TabPages.Add(tabTable);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode =
                    DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 8.5f),
                SelectionMode =
                    DataGridViewSelectionMode.FullRowSelect,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(245, 244, 240),
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(61, 61, 58),
                },
            };
            _grid.Columns.Add("Timestamp", "Timestamp");
            _grid.Columns.Add("Sensor",    "Sensor");
            _grid.Columns.Add("Value",     "Value");
            _grid.Columns.Add("Unit",      "Unit");
            _grid.Columns["Timestamp"].FillWeight = 30;
            _grid.Columns["Sensor"].FillWeight    = 30;
            _grid.Columns["Value"].FillWeight     = 25;
            _grid.Columns["Unit"].FillWeight      = 15;
            tabTable.Controls.Add(_grid);
        }

        // ─────────────────────────────────────────────────────────────────
        // UI helpers
        // ─────────────────────────────────────────────────────────────────
        private static Label MakeHeader(string text) => new Label
        {
            Text      = text.ToUpperInvariant(),
            Font      = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(136, 135, 128),
            Width = 258, Height = 20,
            Margin = new Padding(0, 10, 0, 2),
        };

        private static Label MakeLabel(string text, bool bold = false,
            Color? fg = null, Color? bg = null) => new Label
        {
            Text      = text,
            Font      = bold
                ? new Font("Segoe UI", 9f, FontStyle.Bold)
                : new Font("Segoe UI", 9f),
            ForeColor = fg ?? Color.FromArgb(61, 61, 58),
            BackColor = bg ?? Color.Transparent,
            AutoSize  = false,
            Width     = 258,
            Padding   = new Padding(4, 0, 4, 0),
        };

        private static TextBox MakeTxt(string text, bool mono = false,
            bool multiline = false, int height = 24) => new TextBox
        {
            Text      = text,
            Width     = 258, Height = height,
            Multiline = multiline,
            WordWrap  = multiline,
            Font      = mono
                ? new Font("Consolas", 8f)
                : new Font("Segoe UI", 9f),
            Margin = new Padding(0, 2, 0, 2),
        };

        private static Button MakeBtn(string text, Color bg) => new Button
        {
            Text      = text,
            Width     = 258, Height = 32,
            BackColor = bg, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin    = new Padding(0, 3, 0, 3),
            Cursor    = Cursors.Hand,
        };

        private static Panel MakeReadout(string label, out Label val)
        {
            var row = new Panel
                { Width = 258, Height = 24, Margin = new Padding(0, 1, 0, 1) };
            row.Controls.Add(new Label
            {
                Text = label, Location = new Point(0, 4), Width = 70,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(136, 135, 128),
            });
            val = new Label
            {
                Text = "—", Location = new Point(74, 4), Width = 184,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(61, 61, 58),
            };
            row.Controls.Add(val);
            return row;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _logTimer.Stop();
            _chartTimer.Stop();
            _statusTimer.Stop();
            _opc.Dispose();
            _db?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
