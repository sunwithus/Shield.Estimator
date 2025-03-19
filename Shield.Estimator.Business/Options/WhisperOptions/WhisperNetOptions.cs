namespace Shield.Estimator.Business.Options.WhisperOptions;

public class WhisperNetOptions
{
    public string DefaultModelPath { get; set; }
    public Dictionary<string, string> CustomModels { get; set; } = new();
    //public bool Diarization { get; set; } = true;
}


