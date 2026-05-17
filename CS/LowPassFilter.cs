using System;

namespace SCADA_ControlSystem.Simulation
{
    public class LowPassFilter
    {
        private const double Ts = AirHeaterModel.Ts;
        private double _tauF;
        private double _prev;

        public double TauF
        {
            get => _tauF;
            set { _tauF = Math.Max(0, value); Alpha = Ts / (_tauF + Ts); }
        }
        public double Alpha { get; private set; }

        public LowPassFilter(double tauF = 1.0)
        {
            _prev = AirHeaterModel.Tenv;
            TauF  = tauF;
        }

        public double Filter(double input)
        {
            _prev = Alpha * input + (1.0 - Alpha) * _prev;
            return _prev;
        }

        public void Reset() => _prev = AirHeaterModel.Tenv;
    }
}
