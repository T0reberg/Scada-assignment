using System;
using System.Collections.Generic;
using System.Linq;

namespace SCADA_ControlSystem.Simulation
{
    public class AirHeaterModel
    {
        public const double Theta_t      = 22.0;
        public const double Kh           = 3.5;
        public const double Tenv         = 21.5;
        public const double Ts           = 0.1;
        private const int   DelaySamples = 20;

        private double _T;
        private readonly Queue<double> _delayBuffer;
        private readonly Random _rng = new Random();

        public double Temperature => _T;

        public AirHeaterModel()
        {
            _T = Tenv;
            _delayBuffer = new Queue<double>(
                Enumerable.Repeat(0.0, DelaySamples));
        }

        public double Step(double u, bool addNoise = true)
        {
            _delayBuffer.Enqueue(u);
            double uDelayed = _delayBuffer.Dequeue();
            _T += (Ts / Theta_t) * (-_T + Kh * uDelayed + Tenv);
            _T  = Math.Max(10.0, Math.Min(80.0, _T));
            if (addNoise)
                _T += (_rng.NextDouble() - 0.5) * 0.3;
            return _T;
        }

        public void Reset()
        {
            _T = Tenv;
            _delayBuffer.Clear();
            for (int i = 0; i < DelaySamples; i++)
                _delayBuffer.Enqueue(0.0);
        }
    }
}
