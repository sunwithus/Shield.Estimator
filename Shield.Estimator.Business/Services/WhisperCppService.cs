using Microsoft.Extensions.Options;
using Shield.Estimator.Business.Models.WhisperCppDto;
using Shield.Estimator.Business.Options.WhisperOptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Shield.Estimator.Business.Services;

public class WhisperCppService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<WhisperCppOptions> _options;

    public WhisperCppService(HttpClient httpClient, IOptions<WhisperCppOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task LoadModelAsync(string modelPath)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(modelPath), "model");

            var response = await _httpClient.PostAsync(_options.Value.LoadUrl, form);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new FailedInferenceRequestException($"LoadModelAsync error: {ex.Message}", ex);
        }
    }

    public async Task<string> TranscribeAsync(string audioFilePath, InferenceRequestDto parameters = null)
    {
        try
        {
            if(parameters == null)
            {
                parameters = new();
            }
            using var form = new MultipartFormDataContent();

            // Добавление аудиофайла
            using var fileStream = File.OpenRead(audioFilePath);
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
            form.Add(fileContent, "file", Path.GetFileName(audioFilePath));

            // Добавление параметров
            AddParameter(form, "threads", parameters.Threads);
            AddParameter(form, "processors", parameters.Processors);
            AddParameter(form, "offset-t", parameters.OffsetT);
            AddParameter(form, "offset-n", parameters.OffsetN);
            AddParameter(form, "duration", parameters.Duration);
            AddParameter(form, "max-context", parameters.MaxContext);
            AddParameter(form, "max-len", parameters.MaxLen);
            AddParameter(form, "split-on-word", parameters.SplitOnWord);
            AddParameter(form, "best-of", parameters.BestOf);
            AddParameter(form, "beam-size", parameters.BeamSize);
            AddParameter(form, "word-thold", parameters.WordThold);
            AddParameter(form, "entropy-thold", parameters.EntropyThold);
            AddParameter(form, "logprob-thold", parameters.LogprobThold);
            AddParameter(form, "debug-mode", parameters.DebugMode);
            AddParameter(form, "translate", parameters.Translate);
            AddParameter(form, "diarize", parameters.Diarize);
            AddParameter(form, "tinydiarize", parameters.Tinydiarize);
            AddParameter(form, "no-fallback", parameters.NoFallback);
            AddParameter(form, "print-special", parameters.PrintSpecial);
            AddParameter(form, "print-colors", parameters.PrintColors);
            AddParameter(form, "print-realtime", parameters.PrintRealtime);
            AddParameter(form, "print-progress", parameters.PrintProgress);
            AddParameter(form, "no-timestamps", parameters.NoTimestamps);
            AddParameter(form, "language", parameters.Language);
            AddParameter(form, "detect-language", parameters.DetectLanguage);
            AddParameter(form, "prompt", parameters.Prompt);
            AddParameter(form, "model", parameters.Model);
            AddParameter(form, "ov-e-device", parameters.OvEDevice);
            AddParameter(form, "convert", parameters.Convert);
            AddParameter(form, "temperature", parameters.Temperature);
            AddParameter(form, "temperature_inc", parameters.TemperatureInc);
            AddParameter(form, "response_format", parameters.ResponseFormat);

            var response = await _httpClient.PostAsync(_options.Value.InferenceUrl, form);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            throw new FailedInferenceRequestException($"ProcessInferenceAsync error: {ex.Message}", ex);
        }
    }

    private void AddParameter<T>(MultipartFormDataContent form, string name, T? value) where T : struct
    {
        if (value.HasValue)
        {
            form.Add(new StringContent(value.Value.ToString()), name);
        }
    }

    private void AddParameter(MultipartFormDataContent form, string name, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            form.Add(new StringContent(value), name);
        }
    }

    private void AddParameter(MultipartFormDataContent form, string name, bool? value)
    {
        if (value.HasValue)
        {
            form.Add(new StringContent(value.Value ? "true" : "false"), name);
        }
    }
}

public class FailedInferenceRequestException : Exception
{
    public FailedInferenceRequestException(string message, Exception inner) : base(message, inner) { }
}