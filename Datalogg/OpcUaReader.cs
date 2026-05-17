using System;
using Opc.UaFx.Client;
using SCADA_Datalogging.Models;

namespace SCADA_Datalogging.Services
{
    /// <summary>
    /// OPC UA client using Opc.UaFx.Client.
    /// Connects to the embedded server in the Control System.
    ///
    /// Node IDs (Opc.UaFx auto-assigns ns=2;s=NodeName):
    ///   ns=2;s=Temperature
    ///   ns=2;s=ControlSignal
    ///   ns=2;s=Setpoint
    /// </summary>
    public class OpcUaReader : IDisposable
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

        // ── Read all three tags in one call ───────────────────────────────
        public OpcSnapshot ReadSnapshot()
        {
            if (!IsConnected || _client == null) return null;
            try
            {
                double temp   = (double)_client.ReadNode("ns=2;s=Temperature").Value;
                double signal = (double)_client.ReadNode("ns=2;s=ControlSignal").Value;
                double sp     = (double)_client.ReadNode("ns=2;s=Setpoint").Value;

                return new OpcSnapshot(temp, signal, sp, DateTime.Now);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
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
