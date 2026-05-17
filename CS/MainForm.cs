using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScottPlot;
using ScottPlot.WinForms;
using SCADA_ControlSystem.Simulation;
using SCADA_ControlSystem.Services;

namespace SCADA_ControlSystem
{
    public class MainForm : Form
    {
        // ── Core objects ──────────────────────────────────────────────────
        private readonly AirHeaterModel _plant = new AirHeaterModel();
        private readonly PIController   _pid   = new PIController();
        private readonly LowPassFilter  _lpf   = new LowPassFilter();
        private readonly OpcUaServer    _opc   = new OpcUaServer();

        // ── Control loop ──────────────────────────────────────────────────
        private readonly System.Windows.Forms.Timer _timer =
            new System.Windows.Forms.Timer { Interval = 100 };
        private bool   _running   = false;
        private double _elapsed   = 0;
        private int    _tickCount = 0;

        // ── Chart buffers ─────────────────────────────────────────────────
        private const int MaxPts = 600;
        private readonly List<double> _bufTime = new List<double>();
        private readonly List<double> _bufTemp = new List<double>();
        private readonly List<double> _bufSp   = new List<double>();
        private readonly List<double> _bufU    = new List<double>();

        // ── UI fields ─────────────────────────────────────────────────────
        private FormsPlot    _chart        = null!;
        private TrackBar     _tbSp         = null!;
        private Label        _lblSpVal     = null!;
        private TrackBar     _tbKp         = null!;
        private Label        _lblKpVal     = null!;
        private TrackBar     _tbTi         = null!;
        private Label        _lblTiVal     = null!;
        private TrackBar     _tbTauF       = null!;
        private Label        _lblTauFVal   = null!;
        private Button       _btnStartStop = null!;
        private Button       _btnReset     = null!;
        private Button       _btnOpc       = null!;
        private Label        _lblOpcStatus = null!;
        private Label        _lblTout      = null!;
        private Label        _lblU         = null!;
        private Label        _lblError     = null!;
        private CheckBox     _chkNoise     = null!;
        private CheckBox     _chkShowU     = null!;

        public MainForm()
        {
            BuildUi();
            _timer.Tick += OnTick;
        }

        // ── Control loop ──────────────────────────────────────────────────
        private void OnTick(object sender, EventArgs e)
        {
            double tsp  = _tbSp.Value;
            double tRaw = _plant.Step(_pid.LastOutput, _chkNoise.Checked);
            double tFilt= _lpf.Filter(tRaw);
            double u    = _pid.Compute(tsp, tFilt);

            // Push to OPC UA server every 500 ms
            if (_opc.IsRunning && _tickCount % 5 == 0)
                _opc.Update(tFilt, u, tsp, false);

            _lblTout.Text  = string.Format("{0:F2} °C", tFilt);
            _lblU.Text     = string.Format("{0:F3} V",  u);
            _lblError.Text = string.Format("{0:F2} °C", _pid.LastError);

            _elapsed += AirHeaterModel.Ts;
            _bufTime.Add(_elapsed); _bufTemp.Add(tFilt);
            _bufSp.Add(tsp);        _bufU.Add(u);
            if (_bufTime.Count > MaxPts)
            {
                _bufTime.RemoveAt(0); _bufTemp.RemoveAt(0);
                _bufSp.RemoveAt(0);   _bufU.RemoveAt(0);
            }

            if (_tickCount % 5 == 0) RefreshChart();
            _tickCount++;
        }

        private void RefreshChart()
        {
            if (_bufTime.Count < 2) return;
            var plt = _chart.Plot;
            plt.Clear();

            var pT = plt.AddScatter(_bufTime.ToArray(), _bufTemp.ToArray(),
                Color.FromArgb(29, 158, 117), label: "T_out [°C]");
            pT.LineWidth = 1.5f; pT.MarkerSize = 0;

            var pS = plt.AddScatter(_bufTime.ToArray(), _bufSp.ToArray(),
                Color.FromArgb(55, 138, 221), label: "Setpoint [°C]");
            pS.LineWidth = 1.5f; pS.MarkerSize = 0;
            pS.LineStyle = LineStyle.Dash;

            if (_chkShowU.Checked)
            {
                double[] scaledU = _bufU.Select(v => v / 5.0 * 30.0 + 15.0).ToArray();
                var pU = plt.AddScatter(_bufTime.ToArray(), scaledU,
                    Color.FromArgb(239, 159, 39), label: "u (scaled)");
                pU.LineWidth = 1f; pU.MarkerSize = 0;
            }

            plt.YLabel("Temperature [°C]");
            plt.XLabel("Time [s]");
            plt.Legend(location: Alignment.UpperRight);
            plt.SetAxisLimitsY(10, 55);
            _chart.Refresh();
        }

        // ── Handlers ──────────────────────────────────────────────────────
        private void StartStop()
        {
            _running = !_running;
            if (_running)
            {
                _timer.Start();
                _btnStartStop.Text      = "⏹  Stop";
                _btnStartStop.BackColor = Color.FromArgb(180, 50, 50);
            }
            else
            {
                _timer.Stop();
                _btnStartStop.Text      = "▶  Start";
                _btnStartStop.BackColor = Color.FromArgb(29, 158, 117);
            }
        }

        private void ResetSim()
        {
            bool was = _running;
            if (_running) StartStop();
            _plant.Reset(); _pid.Reset(); _lpf.Reset();
            _elapsed = 0; _tickCount = 0;
            _bufTime.Clear(); _bufTemp.Clear();
            _bufSp.Clear();   _bufU.Clear();
            _chart.Plot.Clear(); _chart.Refresh();
            _lblTout.Text = "—"; _lblU.Text = "—"; _lblError.Text = "—";
            if (was) StartStop();
        }

        private void StartOpcServer()
        {
            _btnOpc.Enabled     = false;
            _lblOpcStatus.Text  = "Starting…";
            _lblOpcStatus.ForeColor = Color.Gray;

            Task.Run(() =>
            {
                bool ok = _opc.Start();
                Invoke(new Action(() =>
                {
                    _lblOpcStatus.Text = ok
                        ? string.Format("● Running  —  {0}", _opc.Endpoint)
                        : string.Format("✕ {0}", _opc.LastError);
                    _lblOpcStatus.ForeColor = ok
                        ? Color.FromArgb(29, 158, 117)
                        : Color.IndianRed;
                    _btnOpc.Text    = ok ? "Stop server" : "Start server";
                    _btnOpc.Enabled = true;
                }));
            });
        }

        // ── UI builder ────────────────────────────────────────────────────
        private void BuildUi()
        {
            Text        = "SCADA — Air Heater Control System";
            Size        = new Size(1100, 880);
            MinimumSize = new Size(900, 560);
            BackColor   = Color.FromArgb(245, 244, 240);
            Font        = new Font("Segoe UI", 9f);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill, SplitterDistance = 270,
                FixedPanel = FixedPanel.Panel1,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(245, 244, 240),
            };
            Controls.Add(split);
            BuildLeft(split.Panel1);
            BuildChart(split.Panel2);
        }

        private void BuildLeft(Panel p)
        {
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true,
                Padding = new Padding(10, 10, 4, 10),
            };
            p.Controls.Add(flow);
            // Setpoint
            flow.Controls.Add(Hdr("Setpoint"));
            (_tbSp, _lblSpVal) = Slider(flow, "T_sp", 22, 50, 35, "°C");

            // PID
            flow.Controls.Add(Hdr("PID parameters"));
            (_tbKp,   _lblKpVal)   = Slider(flow, "Kp",  1, 50, 8,  "",  10, v => string.Format("{0:F1}", v / 10.0));
            (_tbTi,   _lblTiVal)   = Slider(flow, "Ti",  5, 120, 20, "s");
            (_tbTauF, _lblTauFVal) = Slider(flow, "τ_f", 0, 50,  10, "s", 10, v => string.Format("{0:F1}", v / 10.0));
            _tbKp.ValueChanged   += (s, e) => { _pid.Kp   = _tbKp.Value / 10.0;   _lblKpVal.Text   = string.Format("{0:F1}", _pid.Kp); };
            _tbTi.ValueChanged   += (s, e) => { _pid.Ti   = _tbTi.Value;           _lblTiVal.Text   = string.Format("{0} s", _pid.Ti); };
            _tbTauF.ValueChanged += (s, e) => { _lpf.TauF = _tbTauF.Value / 10.0; _lblTauFVal.Text = string.Format("{0:F1} s", _lpf.TauF); };

            // Options
            flow.Controls.Add(Hdr("Options"));
            _chkNoise = Chk("Add sensor noise", true);  flow.Controls.Add(_chkNoise);
            _chkShowU = Chk("Show control signal u", true); flow.Controls.Add(_chkShowU);

            // Control
            flow.Controls.Add(Hdr("Control"));
            _btnStartStop = Btn("▶  Start", Color.FromArgb(29, 158, 117));
            _btnStartStop.Click += (s, e) => StartStop();
            flow.Controls.Add(_btnStartStop);

            _btnReset = Btn("↺  Reset", Color.FromArgb(136, 135, 128));
            _btnReset.Click += (s, e) => ResetSim();
            flow.Controls.Add(_btnReset);

            // Live readings
            flow.Controls.Add(Hdr("Live readings"));
            flow.Controls.Add(Readout("T_out", out _lblTout));
            flow.Controls.Add(Readout("u",     out _lblU));
            flow.Controls.Add(Readout("Error", out _lblError));

            // OPC UA
            flow.Controls.Add(Hdr("OPC UA server"));
            var ep = Lbl("opc.tcp://localhost:49320/",
                fg: Color.FromArgb(83, 74, 183));
            ep.Font   = new Font("Consolas", 8f);
            ep.Margin = new Padding(0, 2, 0, 4);
            flow.Controls.Add(ep);

            _btnOpc = Btn("Start server", Color.FromArgb(55, 138, 221));
            _btnOpc.Click += (s, e) => StartOpcServer();
            flow.Controls.Add(_btnOpc);

            _lblOpcStatus = Lbl("● Not started", fg: Color.Gray);
            _lblOpcStatus.Margin = new Padding(0, 2, 0, 0);
            flow.Controls.Add(_lblOpcStatus);
        }

        private void BuildChart(Panel p)
        {
            _chart = new FormsPlot { Dock = DockStyle.Fill };
            p.Controls.Add(_chart);
            _chart.Plot.Title("Air heater — temperature control");
            _chart.Plot.YLabel("Temperature [°C]");
            _chart.Plot.XLabel("Time [s]");
            _chart.Plot.SetAxisLimitsY(10, 55);
            _chart.Refresh();
        }

        // ── UI helpers ────────────────────────────────────────────────────
        private static Label Hdr(string t) => new Label
        {
            Text = t.ToUpperInvariant(),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(136, 135, 128),
            Width = 248, Height = 20, Margin = new Padding(0, 10, 0, 2),
        };

        private static Label Lbl(string t, bool bold = false,
            Color? fg = null, Color? bg = null) => new Label
        {
            Text = t,
            Font = bold ? new Font("Segoe UI", 9f, FontStyle.Bold)
                        : new Font("Segoe UI", 9f),
            ForeColor = fg ?? Color.FromArgb(61, 61, 58),
            BackColor = bg ?? Color.Transparent,
            AutoSize = false, Width = 248,
            Padding = new Padding(4, 0, 4, 0),
        };

        private static Button Btn(string t, Color bg) => new Button
        {
            Text = t, Width = 248, Height = 32,
            BackColor = bg, ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Margin = new Padding(0, 3, 0, 3), Cursor = Cursors.Hand,
        };

        private static CheckBox Chk(string t, bool v) => new CheckBox
        {
            Text = t, Checked = v, Width = 248, Height = 22,
            Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 2, 0, 0),
        };

        private static (TrackBar tb, Label lbl) Slider(
            FlowLayoutPanel parent, string name, int min, int max,
            int init, string unit, int div = 1, Func<int, string> fmt = null)
        {
            string Disp(int v) => fmt != null ? fmt(v)
                                              : string.Format("{0}{1}", v / div, unit);
            var row = new Panel { Width = 248, Height = 36, Margin = new Padding(0, 2, 0, 0) };
            var lbl = new Label { Text = name, Location = new Point(0, 10), Width = 42,
                Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(61, 61, 58) };
            var val = new Label { Text = Disp(init), Location = new Point(194, 10), Width = 54,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(61, 61, 58) };
            var tb = new TrackBar { Minimum = min, Maximum = max, Value = init,
                Location = new Point(44, 6), Width = 148, TickStyle = TickStyle.None };
            tb.ValueChanged += (s, e) => val.Text = Disp(tb.Value);
            row.Controls.AddRange(new Control[] { lbl, tb, val });
            parent.Controls.Add(row);
            return (tb, val);
        }

        private static Panel Readout(string lbl, out Label val)
        {
            var row = new Panel { Width = 248, Height = 24, Margin = new Padding(0, 1, 0, 1) };
            row.Controls.Add(new Label { Text = lbl, Location = new Point(0, 4), Width = 60,
                Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(136, 135, 128) });
            val = new Label { Text = "—", Location = new Point(64, 4), Width = 184,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(61, 61, 58) };
            row.Controls.Add(val);
            return row;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop(); _opc.Dispose();
            base.OnFormClosed(e);
        }
    }
}
