using Shield.Estimator.Business.Options.KoboldOptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shield.Estimator.Business.Options.WhisperOptions
{
    public class WhisperCppOptions
    {
        public string InferenceUrl { get; set; }
        public string LoadUrl { get; set; }
        public Dictionary<string, string> CustomModels { get; set; }
    }
}
