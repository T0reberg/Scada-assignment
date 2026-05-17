using System;

namespace SCADA_ControlSystem.Simulation
{
    public class PIController
    {
        public double Kp { get; set; } = 0.8;
        public double Ti { get; set; } = 20.0;

        private const double Ts   = AirHeaterModel.Ts;
        private const double UMin = 0.0;
        private const double UMax = 5.0;

        private double _integral;
        public  double LastOutput { get; private set; }
        public  double LastError  { get; private set; }

        public double Compute(double setpoint, double measured)
        {
            double e = setpoint - measured;
            LastError = e;
            double u  = Kp * e + (Kp / Ti) * _integral;
            if (u >= UMin && u <= UMax)
                _integral += e * Ts;
            else
                u = Math.Max(UMin, Math.Min(UMax, u));
            LastOutput = u;
            return u;
        }

        public void Reset()
        {
            _integral  = 0;
            LastOutput = 0;
            LastError  = 0;
        }
    }
}
