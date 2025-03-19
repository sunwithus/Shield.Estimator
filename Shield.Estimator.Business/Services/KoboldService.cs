using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AutoMapper;
using Polly;
using Shield.Estimator.Business.Exceptions;
using Shield.Estimator.Business.Options.KoboldOptions;
using Shield.Estimator.Business.Models.KoboldDto;
using System.Net;

namespace Shield.Estimator.Business.Services;

public class KoboldService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<AiOptions> _options;
    private readonly IMapper _aiMapper;
    private readonly ILogger<KoboldService> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;
    private const string PropertyRole = "KoboldSimpleLogical";

    public KoboldService(HttpClient httpClient, IOptions<AiOptions> options, IMapper aiMapper, ILogger<KoboldService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _aiMapper = aiMapper ?? throw new ArgumentNullException(nameof(aiMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Validate configuration early
        ValidateOptions(options.Value);

        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpClient.BaseAddress = new Uri(options.Value.BaseUrl);

        // Configure retry policy with exponential backoff
        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<string> GenerateTextAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt cannot be empty", nameof(prompt));
        }

        var requestBody = _aiMapper.Map<AiRequestDto>(_options.Value.PromptOptions[PropertyRole]);
        requestBody.Prompt = $"{_options.Value.PromptBefore}{prompt}{_options.Value.PromptAfter}";

        try
        {
            var response = await _retryPolicy.ExecuteAsync(async () =>
            {
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "")
                {
                    Content = JsonContent.Create(requestBody)
                };

                return await _httpClient.SendAsync(requestMessage, cancellationToken);
            });

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new FailedAiRequestException("Rate limit exceeded");
            }

            response.EnsureSuccessStatusCode();
             
            var result = await response.Content.ReadFromJsonAsync<AiResponseDto>(cancellationToken: cancellationToken);
            //_logger.LogTrace(result.ToString());

            return result?.Results?[0]?.Text
                ?? throw new FailedAiRequestException("Empty response content from AI service");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while processing AI request");
            throw new FailedAiRequestException($"AI service communication error: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("AI request was cancelled");
            throw new OperationCanceledException("AI request was cancelled", ex);
        }
    }

    private void ValidateOptions(AiOptions options)
    {
        if (string.IsNullOrEmpty(options.BaseUrl))
            throw new ArgumentException("Base URL is required in configuration");

        if (!options.PromptOptions.TryGetValue(PropertyRole, out var promptOptions))
            throw new ArgumentException($"Prompt options for {PropertyRole} are missing");

        if (string.IsNullOrEmpty(options.PromptBefore) || string.IsNullOrEmpty(options.PromptAfter))
            throw new ArgumentException("Prompt wrappers are required in configuration");
    }

}
