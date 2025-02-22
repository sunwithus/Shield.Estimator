using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Shield.Estimator.Business.Models.KoboldDto;

public class AiRequestDto
{
    [JsonPropertyName("n")]
    public int N { get; set; }

    [JsonPropertyName("max_context_length")]
    public int MaxContextLength { get; set; }

    [JsonPropertyName("max_length")]
    public int MaxLength { get; set; }

    [JsonPropertyName("rep_pen")]
    public double RepPen { get; set; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("top_p")]
    public double TopP { get; set; }

    [JsonPropertyName("top_k")]
    public int TopK { get; set; }

    [JsonPropertyName("top_a")]
    public int TopA { get; set; }

    [JsonPropertyName("typical")]
    public int Typical { get; set; }

    [JsonPropertyName("tfs")]
    public int Tfs { get; set; }

    [JsonPropertyName("rep_pen_range")]
    public int RepPenRange { get; set; }

    [JsonPropertyName("rep_pen_slope")]
    public double RepPenSlope { get; set; }

    [JsonPropertyName("sampler_order")]
    public List<int> SamplerOrder { get; set; }

    [JsonPropertyName("memory")]
    public string Memory { get; set; }

    [JsonPropertyName("trim_stop")]
    public bool TrimStop { get; set; }

    [JsonPropertyName("genkey")]
    public string Genkey { get; set; }

    [JsonPropertyName("min_p")]
    public int MinP { get; set; }

    [JsonPropertyName("dynatemp_range")]
    public int DynatempRange { get; set; }

    [JsonPropertyName("dynatemp_exponent")]
    public int DynatempExponent { get; set; }

    [JsonPropertyName("smoothing_factor")]
    public int SmoothingFactor { get; set; }

    [JsonPropertyName("banned_tokens")]
    public List<string> BannedTokens { get; set; }

    [JsonPropertyName("render_special")]
    public bool RenderSpecial { get; set; }

    [JsonPropertyName("logprobs")]
    public bool Logprobs { get; set; }

    [JsonPropertyName("presence_penalty")]
    public int PresencePenalty { get; set; }

    [JsonPropertyName("logit_bias")]
    public Dictionary<string, int> LogitBias { get; set; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; }
    /*
    [JsonPropertyName("promptbefore")]
    public string PromptBefore { get; set; }

    [JsonPropertyName("promptafter")]
    public string PromptAfter { get; set; }
    */
    [JsonPropertyName("quiet")]
    public bool Quiet { get; set; }

    [JsonPropertyName("stop_sequence")]
    public List<string> StopSequence { get; set; }

    [JsonPropertyName("use_default_badwordsids")]
    public bool UseDefaultBadwordsIds { get; set; }

    [JsonPropertyName("bypass_eos")]
    public bool BypassEos { get; set; }
}

