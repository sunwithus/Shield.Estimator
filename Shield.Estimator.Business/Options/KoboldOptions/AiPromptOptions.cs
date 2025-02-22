namespace Shield.Estimator.Business.Options.KoboldOptions;

public class AiPromptOptions
{
    public int N { get; set; }
    public int MaxContextLength { get; set; }
    public int MaxLength { get; set; }
    public double RepPen { get; set; }
    public double Temperature { get; set; }
    public double TopP { get; set; }
    public int TopK { get; set; }
    public int TopA { get; set; }
    public int Typical { get; set; }
    public int Tfs { get; set; }
    public int RepPenRange { get; set; }
    public double RepPenSlope { get; set; }
    public List<int> SamplerOrder { get; set; }
    public string Memory { get; set; }
    public bool TrimStop { get; set; }
    public string Genkey { get; set; }
    public int MinP { get; set; }
    public int DynatempRange { get; set; }
    public int DynatempExponent { get; set; }
    public int SmoothingFactor { get; set; }
    public List<string> BannedTokens { get; set; }
    public bool RenderSpecial { get; set; }
    public bool Logprobs { get; set; }
    public int PresencePenalty { get; set; }
    public Dictionary<string, int> LogitBias { get; set; }
    /*public string PromptBefore { get; set; }
    public string PromptAfter { get; set; }*/
    public bool Quiet { get; set; }
    public List<string> StopSequence { get; set; }
    public bool UseDefaultBadwordsIds { get; set; }
    public bool BypassEos { get; set; }
}
