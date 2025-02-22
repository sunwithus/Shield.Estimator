namespace Shield.Estimator.Business.Options.KoboldOptions;

public class AiOptions
{
    public string BaseUrl { get; set; }
    public string PromptBefore { get; set; }
    public string PromptAfter { get; set; }
    public Dictionary<string, AiPromptOptions> PromptOptions { get; set; }
}


