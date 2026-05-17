using System;
using Opc.UaFx;
using Opc.UaFx.Server;

namespace SCADA_ControlSystem.Services
{
    /// <summary>
    /// Embedded OPC UA server using Opc.UaFx.Advanced.
    /// Runs inside the Control System process on opc.tcp://localhost:49320
    ///
    /// Exposes four nodes (ns=2):
    ///   Temperature      Double  — filtered T_out   [°C]
    ///   ControlSignal    Double  — PI output u       [V]
    ///   Setpoint         Double  — operator setpoint [°C]
    ///   AlarmHighActive  Boolean — alarm flag
    /// </summary>
    public class OpcUaServer : IDisposable
    {
        private OpcServer _server;

        // Node references — updated each tick
        private OpcDataVariableNode<double> _nodeTemp;
        private OpcDataVariableNode<double> _nodeSignal;
        private OpcDataVariableNode<double> _nodeSetpoint;
        private OpcDataVariableNode<bool> _nodeAlarm;

        public bool IsRunning { get; private set; }
        public string Endpoint => "opc.tcp://localhost:49320/";
        public string LastError { get; private set; } = string.Empty;

        // ── Start ─────────────────────────────────────────────────────────
        public bool Start()
        {
            try
            {
                // Create the four data variable nodes
                _nodeTemp = new OpcDataVariableNode<double>("Temperature", 21.5);
                _nodeSignal = new OpcDataVariableNode<double>("ControlSignal", 0.0);
                _nodeSetpoint = new OpcDataVariableNode<double>("Setpoint", 35.0);
                _nodeAlarm = new OpcDataVariableNode<bool>("AlarmHighActive", false);

                // Create server with all four nodes
                _server = new OpcServer(
                    Endpoint,
                    _nodeTemp,
                    _nodeSignal,
                    _nodeSetpoint,
                    _nodeAlarm);

                _server.Start();

                IsRunning = true;
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                IsRunning = false;
                return false;
            }
        }

        // ── Update — called from control loop every 500 ms ────────────────
        public void Update(double temperature, double controlSignal,
                           double setpoint, bool alarmActive)
        {
            if (!IsRunning) return;
            try
            {
                _nodeTemp.Value = temperature;
                _nodeSignal.Value = controlSignal;
                _nodeSetpoint.Value = setpoint;
                _nodeAlarm.Value = alarmActive;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        // ── Stop / Dispose ────────────────────────────────────────────────
        public void Stop()
        {
            try { _server?.Stop(); }
            catch { /* ignore */ }
            IsRunning = false;
        }

        public void Dispose() => Stop();
    }
}