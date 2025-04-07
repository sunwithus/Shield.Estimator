namespace Shield.Estimator.Business.Options.WhisperOptions;

public class WhisperNetOptions
{
    public int MaxConcurrentTasks { get; set; } = 4;
    public int Threads { get; set; } = 4; //Environment.ProcessorCount;
    public int MaxLastTextTokens { get; set; } = 16384;
    public TimeSpan Offset { get; set; } = TimeSpan.Zero;
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    public bool Translate { get; set; } = false;
    public bool NoContext { get; set; } = false;
    public bool SingleSegment { get; set; } = false;
    public bool PrintSpecialTokens { get; set; } = false;
    public bool PrintProgress { get; set; } = false;
    public bool PrintResults { get; set; } = true;
    public bool PrintTimestamps { get; set; } = true;
    public bool UseTokenTimestamps { get; set; } = false;
    public float TokenTimestampsThreshold { get; set; } = 0.01f;
    public float TokenTimestampsSumThreshold { get; set; } = 0.01f;
    public int MaxSegmentLength { get; set; } = 0;
    public bool SplitOnWord { get; set; } = false;
    public int MaxTokensPerSegment { get; set; } = 0;
    public int AudioContextSize { get; set; } = 0;
    public string Language { get; set; } = "auto";
    public bool SuppressBlank { get; set; } = true;
    public float Temperature { get; set; } = 0.2f;
    public float MaxInitialTs { get; set; } = 1.0f;
    public float LengthPenalty { get; set; } = 1.0f;
    public float TemperatureInc { get; set; } = 0.2f;
    public float EntropyThreshold { get; set; } = 2.4f;
    public float LogProbThreshold { get; set; } = -1.0f;
    public float NoSpeechThreshold { get; set; } = 0.6f;
    public bool ComputeProbabilities { get; set; } = false;
    /*
    public string DefaultModelPath { get; set; } = "Models/ggml-base.bin";
    public Dictionary<string, string> CustomModels { get; set; } = new();
    */
}


