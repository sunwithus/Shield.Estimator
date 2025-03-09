//WhisperService.cs

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using AutoMapper;
using Shield.Estimator.Business.Exceptions;
using Shield.Estimator.Business.Options.WhisperOptions;
using System.Net.Http.Headers;

using Microsoft.Extensions.Configuration;
using Shield.Estimator.Business.Options;
using Shield.Estimator.Business.Options.KoboldOptions;

namespace Shield.Estimator.Business.Services;
public class WhisperXDockerService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<WhisperXDockerOptions> _options;

    public WhisperXDockerService(HttpClient httpClient, IOptions<WhisperXDockerOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
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

    private async Task<string> SendAudioRequestAsync(string audioFilePath, string requestUrl, string contentType = "multipart/form-data") // multipart/form-data //audio/wav
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

        
        Console.WriteLine(response.StatusCode);
        Console.WriteLine("Response headers:");
        foreach (var header in response.Headers)
        {
            Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }

        var responseText = await response.Content.ReadAsStringAsync();

        DateTime endTime = DateTime.Now;
        Console.WriteLine($"\n########## {requestUrl} \nВремя выполнения WhisperX = {((int)Math.Round((endTime - startTime).TotalSeconds))} sec.");

        return responseText;
    }
}
