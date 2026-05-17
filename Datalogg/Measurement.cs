using System;

namespace SCADA_Datalogging.Models
{
    public class Measurement
    {
        public int      SensorId   { get; }
        public string   SensorName { get; }
        public double   Value      { get; }
        public string   Unit       { get; }
        public DateTime Timestamp  { get; }

        public Measurement(int sensorId, string sensorName,
                           double value, string unit, DateTime timestamp)
        {
            SensorId   = sensorId;
            SensorName = sensorName;
            Value      = value;
            Unit       = unit;
            Timestamp  = timestamp;
        }
    }

    public class OpcSnapshot
    {
        public double   Temperature   { get; }
        public double   ControlSignal { get; }
        public double   Setpoint      { get; }
        public DateTime Timestamp     { get; }

        public OpcSnapshot(double temperature, double controlSignal,
                           double setpoint,    DateTime timestamp)
        {
            Temperature   = temperature;
            ControlSignal = controlSignal;
            Setpoint      = setpoint;
            Timestamp     = timestamp;
        }
    }
}
