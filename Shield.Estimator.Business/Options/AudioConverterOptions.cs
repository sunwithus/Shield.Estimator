using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shield.Estimator.Business.Options;

public class AudioConverterOptions
{
    public int TargetSampleRate { get; set; }
    public int TargetBitRate { get; set; }
    public string TempDirectory { get; set; }
}
