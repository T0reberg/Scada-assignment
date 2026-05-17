using System;

namespace SCADA_AlarmSystem.Models
{
    public enum AlarmSeverity { Low, High, HiHi }

    public class Alarm
    {
        public int       AlarmID        { get; }
        public int       SensorID       { get; }
        public string    SensorName     { get; }
        public string    AlarmType      { get; }
        public double    Threshold      { get; }
        public double    TriggeredValue { get; }
        public DateTime  TriggeredAt    { get; }
        public DateTime? AcknowledgedAt { get; }
        public bool      IsAcknowledged { get; }

        public Alarm(int alarmId, int sensorId, string sensorName,
                     string alarmType, double threshold, double triggeredValue,
                     DateTime triggeredAt, DateTime? acknowledgedAt,
                     bool isAcknowledged)
        {
            AlarmID        = alarmId;
            SensorID       = sensorId;
            SensorName     = sensorName;
            AlarmType      = alarmType;
            Threshold      = threshold;
            TriggeredValue = triggeredValue;
            TriggeredAt    = triggeredAt;
            AcknowledgedAt = acknowledgedAt;
            IsAcknowledged = isAcknowledged;
        }

        public AlarmSeverity Severity
        {
            get
            {
                switch (AlarmType)
                {
                    case "HIHI": return AlarmSeverity.HiHi;
                    case "LOW":  return AlarmSeverity.Low;
                    default:     return AlarmSeverity.High;
                }
            }
        }

        public string AgeText
        {
            get
            {
                var span = DateTime.Now - TriggeredAt;
                if (span.TotalSeconds < 60)  return string.Format("{0}s ago", (int)span.TotalSeconds);
                if (span.TotalMinutes < 60)  return string.Format("{0}m ago", (int)span.TotalMinutes);
                return string.Format("{0}h ago", (int)span.TotalHours);
            }
        }
    }

    public class AlarmConfig
    {
        public int    ConfigID   { get; }
        public int    SensorID   { get; }
        public string SensorName { get; }
        public string AlarmType  { get; }
        public double Threshold  { get; }
        public bool   IsEnabled  { get; }

        public AlarmConfig(int configId, int sensorId, string sensorName,
                           string alarmType, double threshold, bool isEnabled)
        {
            ConfigID   = configId;
            SensorID   = sensorId;
            SensorName = sensorName;
            AlarmType  = alarmType;
            Threshold  = threshold;
            IsEnabled  = isEnabled;
        }
    }
}
