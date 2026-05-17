using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SCADA_Datalogging.Models;

namespace SCADA_Datalogging.Services
{
    public class DatabaseService : IDisposable
    {
        private readonly string _connectionString;
        private SqlConnection   _connection;

        public bool   IsConnected { get; private set; }
        public string LastError   { get; private set; } = string.Empty;

        public DatabaseService(string connectionString)
            => _connectionString = connectionString;

        public async Task<bool> ConnectAsync()
        {
            try
            {
                _connection = new SqlConnection(_connectionString);
                await _connection.OpenAsync();
                IsConnected = true;
                LastError   = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                LastError   = ex.Message;
                return false;
            }
        }

        public async Task<bool> InsertMeasurementAsync(
            int sensorId, double value, DateTime timestamp)
        {
            if (!IsConnected || _connection == null) return false;
            try
            {
                using var cmd = new SqlCommand("sp_InsertMeasurement", _connection)
                {
                    CommandType = CommandType.StoredProcedure,
                };
                cmd.Parameters.AddWithValue("@SensorID",  sensorId);
                cmd.Parameters.AddWithValue("@Value",     value);
                cmd.Parameters.AddWithValue("@Timestamp", timestamp);
                await cmd.ExecuteNonQueryAsync();
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public async Task<bool> InsertSnapshotAsync(OpcSnapshot snap)
        {
            if (!IsConnected || _connection == null) return false;
            // Run sequentially — concurrent inserts on one SqlConnection
            // require MARS which is not always available
            bool ok = true;
            ok &= await InsertMeasurementAsync(1, snap.Temperature,   snap.Timestamp);
            ok &= await InsertMeasurementAsync(2, snap.ControlSignal, snap.Timestamp);
            ok &= await InsertMeasurementAsync(3, snap.Setpoint,      snap.Timestamp);
            return ok;
        }

        public async Task<List<Measurement>> GetRecentAsync(
            int sensorId, int count = 200)
        {
            var list = new List<Measurement>();
            if (!IsConnected || _connection == null) return list;
            try
            {
                using var cmd = new SqlCommand("sp_GetRecentMeasurements", _connection)
                {
                    CommandType = CommandType.StoredProcedure,
                };
                cmd.Parameters.AddWithValue("@SensorID", sensorId);
                cmd.Parameters.AddWithValue("@Count",    count);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(new Measurement(
                        sensorId:   reader.GetInt32(reader.GetOrdinal("SensorID")),
                        sensorName: reader.GetString(reader.GetOrdinal("Name")),
                        value:      reader.GetDouble(reader.GetOrdinal("Value")),
                        unit:       reader.GetString(reader.GetOrdinal("Unit")),
                        timestamp:  reader.GetDateTime(reader.GetOrdinal("Timestamp"))
                    ));
                }
                list.Reverse();
            }
            catch (Exception ex) { LastError = ex.Message; }
            return list;
        }

        public async Task<long> GetTotalRowsAsync()
        {
            if (!IsConnected || _connection == null) return 0;
            try
            {
                using var cmd = new SqlCommand(
                    "SELECT COUNT(*) FROM Measurements", _connection);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt64(result);
            }
            catch { return 0; }
        }

        public void Dispose()
        {
            _connection?.Dispose();
            IsConnected = false;
        }
    }
}
