using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using AutoMapper;
using Shield.Estimator.Business.Exceptions;
using Shield.Estimator.Business.Options.KoboldOptions;
using Shield.Estimator.Business.Models.KoboldDto;

namespace Shield.Estimator.Business.Services;

public class KoboldService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<AiOptions> _options;
    private readonly IMapper _aiMapper;
    private readonly string _propertyRole = "KoboldSimpleLogical";

    public KoboldService(HttpClient httpClient, IOptions<AiOptions> options, IMapper aiMapper)
    {
        _options = options;
        _aiMapper = aiMapper;
        _httpClient = httpClient;
        //_httpClient.BaseAddress = new Uri(options.Value.BaseUrl);
    }

    public async Task<string> GenerateTextAsync(string prompt/*, CancellationToken cts*/)
    {
        var requestBody = _aiMapper.Map<AiRequestDto>(_options.Value.PromptOptions[_propertyRole]);
        requestBody.Prompt = _options.Value.PromptBefore + prompt + _options.Value.PromptAfter;

        try
        {
            //Todo сейчас без задержки - ошибка
            await Task.Delay(300);
            var response = await _httpClient.PostAsJsonAsync(new Uri(_options.Value.BaseUrl)/*string.Empty*/, requestBody);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<AiResponseDto>();
            return result?.Results?[0]?.Text ?? throw new Exception("Kobold Пустой ответ от сервера");
        }
        catch (Exception ex)
        {
            throw new FailedAiRequestException($"Kobold Ошибка при генерации текста: {ex.Message}", ex);
        }
    }
}
