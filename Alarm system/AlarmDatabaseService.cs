using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using SCADA_AlarmSystem.Models;

namespace SCADA_AlarmSystem.Services
{
    public class AlarmDatabaseService : IDisposable
    {
        private readonly string _connectionString;
        private SqlConnection   _connection;

        public bool   IsConnected { get; private set; }
        public string LastError   { get; private set; } = string.Empty;

        public AlarmDatabaseService(string connectionString)
        {
            _connectionString = connectionString;
        }

        // ── Connect ───────────────────────────────────────────────────────
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

        // ── Get active (unacknowledged) alarms ────────────────────────────
        public async Task<List<Alarm>> GetActiveAlarmsAsync()
        {
            var list = new List<Alarm>();
            if (!IsConnected || _connection == null) return list;
            try
            {
                using (var cmd = new SqlCommand("sp_GetActiveAlarms", _connection)
                    { CommandType = CommandType.StoredProcedure })
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        list.Add(ReadAlarm(reader));
                }
            }
            catch (Exception ex) { LastError = ex.Message; }
            return list;
        }

        // ── Get alarm history ─────────────────────────────────────────────
        public async Task<List<Alarm>> GetAlarmHistoryAsync(
            DateTime? from = null, DateTime? to = null, int maxRows = 500)
        {
            var list = new List<Alarm>();
            if (!IsConnected || _connection == null) return list;
            try
            {
                using (var cmd = new SqlCommand("sp_GetAlarmHistory", _connection)
                    { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@From",
                        from.HasValue ? (object)from.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@To",
                        to.HasValue   ? (object)to.Value   : DBNull.Value);
                    cmd.Parameters.AddWithValue("@MaxRows", maxRows);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            list.Add(ReadAlarm(reader));
                    }
                }
            }
            catch (Exception ex) { LastError = ex.Message; }
            return list;
        }

        // ── Acknowledge one alarm ─────────────────────────────────────────
        public async Task<bool> AcknowledgeAlarmAsync(int alarmId)
        {
            if (!IsConnected || _connection == null) return false;
            try
            {
                using (var cmd = new SqlCommand("sp_AcknowledgeAlarm", _connection)
                    { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@AlarmID",        alarmId);
                    cmd.Parameters.AddWithValue("@AcknowledgedAt", DateTime.Now);
                    await cmd.ExecuteNonQueryAsync();
                    return true;
                }
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        // ── Acknowledge all active alarms ─────────────────────────────────
        public async Task<int> AcknowledgeAllAsync()
        {
            if (!IsConnected || _connection == null) return 0;
            try
            {
                using (var cmd = new SqlCommand("sp_AcknowledgeAll", _connection)
                    { CommandType = CommandType.StoredProcedure })
                {
                    cmd.Parameters.AddWithValue("@AcknowledgedAt", DateTime.Now);
                    var result = await cmd.ExecuteScalarAsync();
                    return Convert.ToInt32(result);
                }
            }
            catch (Exception ex) { LastError = ex.Message; return 0; }
        }

        // ── Get alarm statistics ──────────────────────────────────────────
        public async Task<AlarmStats> GetStatsAsync()
        {
            var stats = new AlarmStats();
            if (!IsConnected || _connection == null) return stats;
            try
            {
                using (var cmd = new SqlCommand("sp_GetAlarmStats", _connection)
                    { CommandType = CommandType.StoredProcedure })
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        stats.Active     = reader.GetInt32(reader.GetOrdinal("ActiveCount"));
                        stats.TodayCount = reader.GetInt32(reader.GetOrdinal("TodayCount"));
                        stats.TotalCount = reader.GetInt32(reader.GetOrdinal("TotalCount"));
                    }
                }
            }
            catch (Exception ex) { LastError = ex.Message; }
            return stats;
        }

        // ── Get alarm config thresholds ───────────────────────────────────
        public async Task<List<AlarmConfig>> GetAlarmConfigAsync()
        {
            var list = new List<AlarmConfig>();
            if (!IsConnected || _connection == null) return list;
            try
            {
                using (var cmd = new SqlCommand("sp_GetAlarmConfig", _connection)
                    { CommandType = CommandType.StoredProcedure })
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        list.Add(new AlarmConfig(
                            reader.GetInt32(reader.GetOrdinal("ConfigID")),
                            reader.GetInt32(reader.GetOrdinal("SensorID")),
                            reader.GetString(reader.GetOrdinal("SensorName")),
                            reader.GetString(reader.GetOrdinal("AlarmType")),
                            reader.GetDouble(reader.GetOrdinal("Threshold")),
                            reader.GetBoolean(reader.GetOrdinal("IsEnabled"))
                        ));
                    }
                }
            }
            catch (Exception ex) { LastError = ex.Message; }
            return list;
        }

        // ── Helper ────────────────────────────────────────────────────────
        private static Alarm ReadAlarm(SqlDataReader r)
        {
            return new Alarm(
                r.GetInt32(r.GetOrdinal("AlarmID")),
                r.GetInt32(r.GetOrdinal("SensorID")),
                r.GetString(r.GetOrdinal("SensorName")),
                r.GetString(r.GetOrdinal("AlarmType")),
                r.GetDouble(r.GetOrdinal("Threshold")),
                r.IsDBNull(r.GetOrdinal("TriggeredValue"))
                    ? 0.0 : r.GetDouble(r.GetOrdinal("TriggeredValue")),
                r.GetDateTime(r.GetOrdinal("TriggeredAt")),
                r.IsDBNull(r.GetOrdinal("AcknowledgedAt"))
                    ? (DateTime?)null
                    : r.GetDateTime(r.GetOrdinal("AcknowledgedAt")),
                r.GetBoolean(r.GetOrdinal("IsAcknowledged"))
            );
        }

        public void Dispose()
        {
            _connection?.Dispose();
            IsConnected = false;
        }
    }

    /// <summary>Simple stats container — avoids tuple return (not net48 friendly).</summary>
    public class AlarmStats
    {
        public int Active     { get; set; }
        public int TodayCount { get; set; }
        public int TotalCount { get; set; }
    }
}
