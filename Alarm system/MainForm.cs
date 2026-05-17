using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SCADA_AlarmSystem.Models;
using SCADA_AlarmSystem.Services;

namespace SCADA_AlarmSystem
{
    public class MainForm : Form
    {
        // ── Services ──────────────────────────────────────────────────────
        private readonly OpcUaMonitor        _opc = new OpcUaMonitor();
        private          AlarmDatabaseService _db;

        // ── Timers ────────────────────────────────────────────────────────
        private readonly System.Windows.Forms.Timer _pollTimer =
            new System.Windows.Forms.Timer { Interval = 2000 };
        private readonly System.Windows.Forms.Timer _liveTimer =
            new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly System.Windows.Forms.Timer _blinkTimer =
            new System.Windows.Forms.Timer { Interval = 600 };

        private bool         _blinkState   = false;
        private List<Alarm>  _activeAlarms = new List<Alarm>();

        // ── UI controls ───────────────────────────────────────────────────
        private TabControl   _tabs           = null;
        private DataGridView _gridActive     = null;
        private DataGridView _gridHistory    = null;
        private DataGridView _gridConfig     = null;
        private Button       _btnAckSelected = null;
        private Button       _btnAckAll      = null;
        private Button       _btnRefreshHist = null;
        private Button       _btnOpcConnect  = null;
        private Button       _btnDbConnect   = null;
        private TextBox      _txtOpcEndpoint = null;
        private TextBox      _txtConnStr     = null;
        private Label        _lblOpcStatus   = null;
        private Label        _lblDbStatus    = null;
        private Label        _lblTout        = null;
        private Label        _lblU           = null;
        private Label        _lblSp          = null;
        private Label        _lblActiveCount = null;
        private Label        _lblTodayCount  = null;
        private Label        _lblTotalCount  = null;
        private Panel        _pnlBanner      = null;
        private Label        _lblBannerText  = null;
        private DateTimePicker _dtpFrom      = null;
        private DateTimePicker _dtpTo        = null;

        public MainForm()
        {
            BuildUi();
            _pollTimer.Tick  += async (s, e) => await PollAlarmsAsync();
            _liveTimer.Tick  += OnLiveTick;
            _blinkTimer.Tick += OnBlink;
        }

        // ─────────────────────────────────────────────────────────────────
        // Polling
        // ─────────────────────────────────────────────────────────────────
        private async Task PollAlarmsAsync()
        {
            if (_db == null || !_db.IsConnected) return;

            _activeAlarms = await _db.GetActiveAlarmsAsync();

            if (InvokeRequired)
                Invoke(new Action(() => RefreshActiveGrid(_activeAlarms)));
            else
                RefreshActiveGrid(_activeAlarms);

            if (_opc.IsConnected)
                _opc.SetAlarmFlag(_activeAlarms.Count > 0);

            var stats = await _db.GetStatsAsync();

            if (InvokeRequired)
                Invoke(new Action(() =>
                {
                    _lblActiveCount.Text = stats.Active.ToString();
                    _lblTodayCount.Text  = stats.TodayCount.ToString();
                    _lblTotalCount.Text  = stats.TotalCount.ToString();
                    UpdateBanner(stats.Active);
                }));
            else
            {
                _lblActiveCount.Text = stats.Active.ToString();
                _lblTodayCount.Text  = stats.TodayCount.ToString();
                _lblTotalCount.Text  = stats.TotalCount.ToString();
                UpdateBanner(stats.Active);
            }
        }

        private void OnLiveTick(object sender, EventArgs e)
        {
            if (!_opc.IsConnected) return;
            var snap = _opc.ReadLive();
            if (snap == null) return;
            _lblTout.Text = string.Format("{0:F2} °C", snap.Value.temp);
            _lblU.Text    = string.Format("{0:F3} V",  snap.Value.signal);
            _lblSp.Text   = string.Format("{0:F1} °C", snap.Value.sp);
        }

        private void OnBlink(object sender, EventArgs e)
        {
            if (_activeAlarms.Count == 0) return;
            _blinkState = !_blinkState;
            bool hihi = _activeAlarms.Any(a => a.Severity == AlarmSeverity.HiHi);
            _pnlBanner.BackColor = _blinkState
                ? (hihi ? Color.FromArgb(200, 40, 40)  : Color.FromArgb(220, 120, 30))
                : (hihi ? Color.FromArgb(160, 20, 20)  : Color.FromArgb(180, 90, 10));
        }

        // ─────────────────────────────────────────────────────────────────
        // Grid helpers
        // ─────────────────────────────────────────────────────────────────
        private void RefreshActiveGrid(List<Alarm> alarms)
        {
            _gridActive.Rows.Clear();
            foreach (var a in alarms)
            {
                int idx = _gridActive.Rows.Add(
                    a.AlarmID,
                    a.SensorName,
                    a.AlarmType,
                    string.Format("{0:F2}", a.TriggeredValue),
                    string.Format("{0:F1}", a.Threshold),
                    a.TriggeredAt.ToString("HH:mm:ss"),
                    a.AgeText,
                    a.IsAcknowledged ? "✓" : "Pending"
                );

                var row = _gridActive.Rows[idx];
                row.DefaultCellStyle.BackColor =
                    a.Severity == AlarmSeverity.HiHi ? Color.FromArgb(255, 210, 210) :
                    a.Severity == AlarmSeverity.Low  ? Color.FromArgb(210, 230, 255) :
                                                       Color.FromArgb(255, 235, 200);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(40, 40, 40);
                row.Tag = a.AlarmID;
            }

            _btnAckSelected.Enabled = alarms.Count > 0;
            _btnAckAll.Enabled      = alarms.Count > 0;
        }

        private async Task RefreshHistoryAsync()
        {
            if (_db == null || !_db.IsConnected) return;
            var history = await _db.GetAlarmHistoryAsync(
                _dtpFrom.Value, _dtpTo.Value.AddDays(1));

            if (InvokeRequired)
                Invoke(new Action(() => DrawHistory(history)));
            else
                DrawHistory(history);
        }

        private void DrawHistory(List<Alarm> history)
        {
            _gridHistory.Rows.Clear();
            foreach (var a in history)
            {
                int idx = _gridHistory.Rows.Add(
                    a.AlarmID,
                    a.SensorName,
                    a.AlarmType,
                    string.Format("{0:F2}", a.TriggeredValue),
                    string.Format("{0:F1}", a.Threshold),
                    a.TriggeredAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    a.IsAcknowledged
                        ? (a.AcknowledgedAt.HasValue
                            ? a.AcknowledgedAt.Value.ToString("HH:mm:ss") : "—")
                        : "—",
                    a.IsAcknowledged ? "Acknowledged" : "Active"
                );
                if (!a.IsAcknowledged)
                    _gridHistory.Rows[idx].DefaultCellStyle.BackColor =
                        Color.FromArgb(255, 235, 200);
            }
        }

        private async Task RefreshConfigAsync()
        {
            if (_db == null || !_db.IsConnected) return;
            var configs = await _db.GetAlarmConfigAsync();
            if (InvokeRequired)
                Invoke(new Action(() => DrawConfig(configs)));
            else
                DrawConfig(configs);
        }

        private void DrawConfig(List<AlarmConfig> configs)
        {
            _gridConfig.Rows.Clear();
            foreach (var c in configs)
                _gridConfig.Rows.Add(
                    c.SensorName, c.AlarmType,
                    string.Format("{0:F1}", c.Threshold),
                    c.IsEnabled ? "✓ Enabled" : "Disabled");
        }

        private void UpdateBanner(int activeCount)
        {
            if (activeCount == 0)
            {
                _pnlBanner.BackColor = Color.FromArgb(29, 158, 117);
                _lblBannerText.Text  = "✓  No active alarms";
                _blinkTimer.Stop();
            }
            else
            {
                _lblBannerText.Text =
                    string.Format("⚠  {0} active alarm{1} — acknowledge required",
                        activeCount, activeCount == 1 ? "" : "s");
                if (!_blinkTimer.Enabled) _blinkTimer.Start();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Button handlers
        // ─────────────────────────────────────────────────────────────────
        private async void AcknowledgeSelected()
        {
            if (_db == null) return;
            var ids = _gridActive.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(r => r.Tag is int)
                .Select(r => (int)r.Tag)
                .ToList();

            if (ids.Count == 0)
            {
                MessageBox.Show("Select one or more alarms first.",
                    "Acknowledge", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            foreach (int id in ids)
                await _db.AcknowledgeAlarmAsync(id);

            await PollAlarmsAsync();
        }

        private async void AcknowledgeAll()
        {
            if (_db == null) return;
            if (MessageBox.Show("Acknowledge all active alarms?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    != DialogResult.Yes) return;

            int count = await _db.AcknowledgeAllAsync();
            MessageBox.Show(
                string.Format("{0} alarm{1} acknowledged.",
                    count, count == 1 ? "" : "s"),
                "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await PollAlarmsAsync();
        }

        private void ConnectOpc()
        {
            _btnOpcConnect.Enabled  = false;
            _lblOpcStatus.Text      = "Connecting…";
            _lblOpcStatus.ForeColor = Color.Gray;

            bool ok = _opc.Connect(_txtOpcEndpoint.Text.Trim());

            _lblOpcStatus.Text      = ok ? "● Connected" : ("✕ " + _opc.LastError);
            _lblOpcStatus.ForeColor = ok ? Color.FromArgb(29, 158, 117) : Color.IndianRed;
            _btnOpcConnect.Text    = ok ? "Disconnect OPC" : "Connect OPC";
            _btnOpcConnect.Enabled = true;

            if (ok) _liveTimer.Start();
            else    _liveTimer.Stop();
        }

        private async void ConnectDb()
        {
            _btnDbConnect.Enabled  = false;
            _lblDbStatus.Text      = "Connecting…";
            _lblDbStatus.ForeColor = Color.Gray;

            _db?.Dispose();
            _db = new AlarmDatabaseService(_txtConnStr.Text.Trim());
            bool ok = await _db.ConnectAsync();

            _lblDbStatus.Text      = ok ? "● Connected" : ("✕ " + _db.LastError);
            _lblDbStatus.ForeColor = ok ? Color.FromArgb(29, 158, 117) : Color.IndianRed;
            _btnDbConnect.Text    = ok ? "Disconnect DB" : "Connect DB";
            _btnDbConnect.Enabled = true;

            if (ok)
            {
                _pollTimer.Start();
                await PollAlarmsAsync();
                await RefreshConfigAsync();
            }
            else _pollTimer.Stop();
        }

        // ─────────────────────────────────────────────────────────────────
        // UI construction
        // ─────────────────────────────────────────────────────────────────
        private void BuildUi()
        {
            Text        = "SCADA — Alarm System";
            Size        = new Size(1150, 720);
            MinimumSize = new Size(950, 600);
            BackColor   = Color.FromArgb(245, 244, 240);
            Font        = new Font("Segoe UI", 9f);

            // Banner
            _pnlBanner = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 40,
                BackColor = Color.FromArgb(29, 158, 117),
            };
            _lblBannerText = new Label
            {
                Dock      = DockStyle.Fill,
                Text      = "✓  No active alarms",
                Font      = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            _pnlBanner.Controls.Add(_lblBannerText);
            Controls.Add(_pnlBanner);

            var split = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                SplitterDistance = 260,
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
            var badge = MkLbl("🔔  ALARM SYSTEM", bold: true,
                fg: Color.FromArgb(150, 40, 40),
                bg: Color.FromArgb(255, 235, 235));
            badge.Size      = new Size(238, 28);
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.Margin    = new Padding(0, 0, 0, 8);
            flow.Controls.Add(badge);

            // OPC UA
            flow.Controls.Add(MkHdr("OPC UA"));
            _txtOpcEndpoint = MkTxt("opc.tcp://localhost:49320/", mono: true);
            flow.Controls.Add(_txtOpcEndpoint);
            _btnOpcConnect = MkBtn("Connect OPC", Color.FromArgb(55, 138, 221));
            _btnOpcConnect.Click += (s, e) => ConnectOpc();
            flow.Controls.Add(_btnOpcConnect);
            _lblOpcStatus = MkLbl("● Not connected", fg: Color.Gray);
            _lblOpcStatus.Margin = new Padding(0, 2, 0, 6);
            flow.Controls.Add(_lblOpcStatus);

            // SQL Server
            flow.Controls.Add(MkHdr("SQL Server"));
            _txtConnStr = MkTxt(
                @"Server=localhost\SQLEXPRESS;Database=SCADA_DB;" +
                "User Id=scada_user;Password=Scada123!;" +
                "TrustServerCertificate=True;",
                mono: true, multiline: true, height: 60);
            flow.Controls.Add(_txtConnStr);
            _btnDbConnect = MkBtn("Connect DB", Color.FromArgb(55, 138, 221));
            _btnDbConnect.Click += (s, e) => ConnectDb();
            flow.Controls.Add(_btnDbConnect);
            _lblDbStatus = MkLbl("● Not connected", fg: Color.Gray);
            _lblDbStatus.Margin = new Padding(0, 2, 0, 6);
            flow.Controls.Add(_lblDbStatus);

            // Live readings
            flow.Controls.Add(MkHdr("Live readings"));
            flow.Controls.Add(MkReadout("T_out",    out _lblTout));
            flow.Controls.Add(MkReadout("u",        out _lblU));
            flow.Controls.Add(MkReadout("Setpoint", out _lblSp));

            // Stats
            flow.Controls.Add(MkHdr("Alarm statistics"));
            flow.Controls.Add(MkStatRow("Active now", out _lblActiveCount,
                Color.FromArgb(200, 40, 40)));
            flow.Controls.Add(MkStatRow("Today",      out _lblTodayCount,
                Color.FromArgb(220, 120, 30)));
            flow.Controls.Add(MkStatRow("All time",   out _lblTotalCount,
                Color.FromArgb(136, 135, 128)));

            // Actions
            flow.Controls.Add(MkHdr("Actions"));
            _btnAckSelected = MkBtn("✓  Acknowledge selected",
                Color.FromArgb(29, 158, 117));
            _btnAckSelected.Enabled = false;
            _btnAckSelected.Click  += (s, e) => AcknowledgeSelected();
            flow.Controls.Add(_btnAckSelected);

            _btnAckAll = MkBtn("✓✓  Acknowledge all",
                Color.FromArgb(136, 135, 128));
            _btnAckAll.Enabled = false;
            _btnAckAll.Click  += (s, e) => AcknowledgeAll();
            flow.Controls.Add(_btnAckAll);
        }

        private void BuildRight(Panel parent)
        {
            _tabs = new TabControl { Dock = DockStyle.Fill };
            parent.Controls.Add(_tabs);

            // Active alarms tab
            var tabActive = new TabPage("Active alarms")
                { BackColor = Color.White };
            _tabs.TabPages.Add(tabActive);

            _gridActive = MkGrid(new[]
            {
                new[] { "ID", "50" }, new[] { "Sensor", "120" },
                new[] { "Type", "60" }, new[] { "Value", "70" },
                new[] { "Threshold", "80" }, new[] { "Time", "70" },
                new[] { "Age", "80" }, new[] { "Status", "90" },
            });
            _gridActive.MultiSelect   = true;
            _gridActive.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            tabActive.Controls.Add(_gridActive);

            // History tab
            var tabHistory = new TabPage("History") { BackColor = Color.White };
            _tabs.TabPages.Add(tabHistory);

            var histTop = new Panel
            {
                Dock    = DockStyle.Top,
                Height  = 44,
                Padding = new Padding(8, 8, 8, 0),
            };
            tabHistory.Controls.Add(histTop);

            histTop.Controls.Add(new Label
            {
                Text = "From:", Location = new Point(6, 12), AutoSize = true,
                Font = new Font("Segoe UI", 9f), ForeColor = Color.Gray,
            });
            _dtpFrom = new DateTimePicker
            {
                Location = new Point(45, 8), Width = 140,
                Format   = DateTimePickerFormat.Short,
                Value    = DateTime.Today.AddDays(-7),
            };
            histTop.Controls.Add(_dtpFrom);

            histTop.Controls.Add(new Label
            {
                Text = "To:", Location = new Point(196, 12), AutoSize = true,
                Font = new Font("Segoe UI", 9f), ForeColor = Color.Gray,
            });
            _dtpTo = new DateTimePicker
            {
                Location = new Point(215, 8), Width = 140,
                Format   = DateTimePickerFormat.Short,
                Value    = DateTime.Today,
            };
            histTop.Controls.Add(_dtpTo);

            _btnRefreshHist = MkBtn("↻ Load", Color.FromArgb(55, 138, 221));
            _btnRefreshHist.Size     = new Size(80, 28);
            _btnRefreshHist.Location = new Point(366, 7);
            _btnRefreshHist.Font     = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            _btnRefreshHist.Click   += async (s, e) => await RefreshHistoryAsync();
            histTop.Controls.Add(_btnRefreshHist);

            _gridHistory = MkGrid(new[]
            {
                new[] { "ID","50" }, new[] { "Sensor","120" },
                new[] { "Type","60" }, new[] { "Value","70" },
                new[] { "Threshold","80" }, new[] { "Triggered","150" },
                new[] { "Acknowledged","90" }, new[] { "Status","100" },
            });
            tabHistory.Controls.Add(_gridHistory);

            // Config tab
            var tabConfig = new TabPage("Thresholds") { BackColor = Color.White };
            _tabs.TabPages.Add(tabConfig);

            tabConfig.Controls.Add(new Label
            {
                Text = "Read-only — edit thresholds in AlarmConfig table in SSMS.",
                Dock = DockStyle.Top, Height = 32,
                Padding = new Padding(10, 8, 0, 0),
                Font    = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                ForeColor = Color.Gray,
            });

            _gridConfig = MkGrid(new[]
            {
                new[] { "Sensor","140" }, new[] { "Type","100" },
                new[] { "Threshold","100" }, new[] { "Status","100" },
            });
            tabConfig.Controls.Add(_gridConfig);
        }

        // ─────────────────────────────────────────────────────────────────
        // UI helpers
        // ─────────────────────────────────────────────────────────────────
        private static DataGridView MkGrid(string[][] cols)
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true,
                AllowUserToAddRows = false, RowHeadersVisible = false,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
                GridColor = Color.FromArgb(220, 219, 215),
                Font = new Font("Segoe UI", 8.5f),
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowTemplate = { Height = 26 },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(245, 244, 240),
                    Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(61, 61, 58),
                    Padding   = new Padding(4, 0, 0, 0),
                },
            };
            foreach (var c in cols)
                g.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = c[0],
                    Width      = int.Parse(c[1]),
                });
            return g;
        }

        private static Label MkHdr(string t) => new Label
        {
            Text = t.ToUpperInvariant(),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(136, 135, 128),
            Width = 238, Height = 20, Margin = new Padding(0, 10, 0, 2),
        };

        private static Label MkLbl(string t, bool bold = false,
            Color? fg = null, Color? bg = null) => new Label
        {
            Text      = t,
            Font      = bold ? new Font("Segoe UI", 9f, FontStyle.Bold)
                             : new Font("Segoe UI", 9f),
            ForeColor = fg ?? Color.FromArgb(61, 61, 58),
            BackColor = bg ?? Color.Transparent,
            AutoSize  = false, Width = 238,
            Padding   = new Padding(4, 0, 4, 0),
        };

        private static TextBox MkTxt(string t, bool mono = false,
            bool multiline = false, int height = 24) => new TextBox
        {
            Text      = t,
            Width     = 238, Height = height,
            Multiline = multiline, WordWrap = multiline,
            Font      = mono ? new Font("Consolas", 8f) : new Font("Segoe UI", 9f),
            Margin    = new Padding(0, 2, 0, 2),
        };

        private static Button MkBtn(string t, Color bg) => new Button
        {
            Text = t, Width = 238, Height = 32,
            BackColor = bg, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin = new Padding(0, 3, 0, 3), Cursor = Cursors.Hand,
        };

        private static Panel MkReadout(string lbl, out Label val)
        {
            var row = new Panel
                { Width = 238, Height = 24, Margin = new Padding(0, 1, 0, 1) };
            row.Controls.Add(new Label
            {
                Text = lbl, Location = new Point(0, 4), Width = 70,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(136, 135, 128),
            });
            val = new Label
            {
                Text = "—", Location = new Point(74, 4), Width = 164,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(61, 61, 58),
            };
            row.Controls.Add(val);
            return row;
        }

        private static Panel MkStatRow(string lbl, out Label val, Color numColor)
        {
            var row = new Panel
                { Width = 238, Height = 28, Margin = new Padding(0, 2, 0, 2) };
            row.Controls.Add(new Label
            {
                Text = lbl, Location = new Point(0, 6), Width = 100,
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(136, 135, 128),
            });
            val = new Label
            {
                Text = "—", Location = new Point(104, 4), Width = 60,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = numColor,
            };
            row.Controls.Add(val);
            return row;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _pollTimer.Stop();
            _liveTimer.Stop();
            _blinkTimer.Stop();
            _opc.Dispose();
            _db?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
