using System;
using Opc.UaFx.Client;

namespace SCADA_AlarmSystem.Services
{
    /// <summary>
    /// OPC UA client for the Alarm System using Opc.UaFx.Client.
    /// Reads live Temperature/ControlSignal/Setpoint and
    /// writes the AlarmHighActive flag back to the server.
    /// </summary>
    public class OpcUaMonitor : IDisposable
    {
        private OpcClient _client;

        public bool   IsConnected { get; private set; }
        public string LastError   { get; private set; } = string.Empty;

        // ── Connect ───────────────────────────────────────────────────────
        public bool Connect(string serverUrl = "opc.tcp://localhost:49320/")
        {
            try
            {
                _client = new OpcClient(serverUrl);
                _client.Connect();
                IsConnected = true;
                LastError   = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError   = ex.Message;
                IsConnected = false;
                return false;
            }
        }

        // ── Read live values ──────────────────────────────────────────────
        public (double temp, double signal, double sp)? ReadLive()
        {
            if (!IsConnected || _client == null) return null;
            try
            {
                double temp   = (double)_client.ReadNode("ns=2;s=Temperature").Value;
                double signal = (double)_client.ReadNode("ns=2;s=ControlSignal").Value;
                double sp     = (double)_client.ReadNode("ns=2;s=Setpoint").Value;
                return (temp, signal, sp);
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
        }

        // ── Write alarm flag ──────────────────────────────────────────────
        public bool SetAlarmFlag(bool active)
        {
            if (!IsConnected || _client == null) return false;
            try
            {
                _client.WriteNode("ns=2;s=AlarmHighActive", active);
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public void Disconnect()
        {
            try { _client?.Disconnect(); }
            catch { /* ignore */ }
            IsConnected = false;
        }

        public void Dispose() => Disconnect();
    }
}
