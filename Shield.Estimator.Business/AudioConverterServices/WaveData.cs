using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Whisper.net.Wave;

namespace Shield.Estimator.Business.AudioConverterServices
{
    public sealed class WaveData : IDisposable
    {
        public MemoryStream Stream { get; }
        public WaveParser Parser { get; }

        public WaveData(MemoryStream stream, WaveParser parser)
        {
            Stream = stream;
            Parser = parser;
        }
        public void Dispose()
        {
            Stream?.Dispose();
        }
    }
}
