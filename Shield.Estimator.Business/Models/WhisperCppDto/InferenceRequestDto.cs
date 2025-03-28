using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Shield.Estimator.Business.Models.WhisperCppDto;

public class InferenceRequestDto
{
    //[JsonPropertyName("threads")]
    public int? Threads { get; set; }
    public int? Processors { get; set; }
    public int? OffsetT { get; set; }
    public int? OffsetN { get; set; }
    public int? Duration { get; set; }
    public int? MaxContext { get; set; }
    public int? MaxLen { get; set; }
    public bool? SplitOnWord { get; set; }
    public int? BestOf { get; set; }
    public int? BeamSize { get; set; }
    public double? WordThold { get; set; }
    public double? EntropyThold { get; set; }
    public double? LogprobThold { get; set; }
    public bool? DebugMode { get; set; }
    public bool? Translate { get; set; } = false;
    public bool? Diarize { get; set; } = true;
    public bool? Tinydiarize { get; set; }
    public bool? NoFallback { get; set; }
    public bool? PrintSpecial { get; set; }
    public bool? PrintColors { get; set; } = true;
    public bool? PrintRealtime { get; set; } = true;
    public bool? PrintProgress { get; set; } = true;
    public bool? NoTimestamps { get; set; }
    public string Language { get; set; } = "auto";
    public bool? DetectLanguage { get; set; }
    public string Prompt { get; set; }
    public string Model { get; set; }
    public string OvEDevice { get; set; }
    public bool? Convert { get; set; }
    public double? Temperature { get; set; } = 0.0;
    public double? TemperatureInc { get; set; }
    public string ResponseFormat { get; set; } = "text";
}
