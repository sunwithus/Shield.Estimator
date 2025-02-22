//WhisperService.cs

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using AutoMapper;
using Shield.Estimator.Business.Exceptions;
using Shield.Estimator.Business.Options.WhisperOptions;
using System.Net.Http.Headers;
//using Shield.Estimator.Business.Models.WhisperDto;

using Microsoft.Extensions.Configuration;
using Shield.Estimator.Business.Options;
using Shield.Estimator.Business.Options.KoboldOptions;

namespace Shield.Estimator.Business.Services;
public class WhisperService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<WhisperOptions> _options;

    public WhisperService(HttpClient httpClient, IOptions<WhisperOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<(string, string)> DetectLanguageAsync(string audioFilePath)
    {
        try
        {
            var jsonResponse = await SendAudioRequestAsync(audioFilePath, _options.Value.DetectLanguageUrl);

            var result = JsonDocument.Parse(jsonResponse);
            return (result.RootElement.GetProperty("language_code").GetString().ToLower(),
                    result.RootElement.GetProperty("detected_language").GetString());
        }
        catch (Exception ex)
        {
            throw new FailedWhisperRequestException($"DetectLanguageAsync Ошибка при тринскрибировании текста: {ex.Message}", ex);
        }
    }

    public async Task<string> TranscribeAsync(string audioFilePath)
    {
        try
        {
            var responseText = await SendAudioRequestAsync(audioFilePath, _options.Value.TranscribeUrl);
            return responseText;
        }
        catch (Exception ex)
        {
            throw new FailedWhisperRequestException($"TranscribeAsync Ошибка при тринскрибировании текста: {ex.Message}", ex); ;
        }
    }

    private async Task<string> 
        
        SendAudioRequestAsync(string audioFilePath, string requestUrl, string contentType = "audio/wav")
    {
        DateTime startTime = DateTime.Now;
        Console.WriteLine(audioFilePath);
        Console.WriteLine(requestUrl);

        using var form = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(audioFilePath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(fileContent, "audio_file", Path.GetFileName(audioFilePath));

        var response = await _httpClient.PostAsync(requestUrl, form);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();

        DateTime endTime = DateTime.Now;
        Console.WriteLine($"\n########## {requestUrl} \nВремя выполнения Whisper = {((int)Math.Round((endTime - startTime).TotalSeconds))} sec.");

        return responseText;
    }
}
