using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shield.Estimator.Business.Services.WhisperNet;

public static class EnergyAnalyzer
{
    public static int FindMaxEnergyChannel(short[] buffer, int channels)
    {
        var energy = new double[channels];
        for (var i = 0; i < buffer.Length; i++)
        {
            energy[i % channels] += Math.Pow(buffer[i], 2);
        }
        return Array.IndexOf(energy, energy.Max());
    }
}
