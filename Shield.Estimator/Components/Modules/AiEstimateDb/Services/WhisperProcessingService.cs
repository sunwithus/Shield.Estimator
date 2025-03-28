using Microsoft.Extensions.Options;
using Shield.Estimator.Business.Exceptions;
using Shield.Estimator.Business.Options.WhisperOptions;
using Shield.Estimator.Business.Services;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;

namespace Shield.Estimator.Shared.Components.Modules.AiEstimateDb.Services;

public class WhisperProcessingService
{
    private readonly IOptions<WhisperCppOptions> _options;
    private readonly WhisperFasterDockerService _whisperFaster;
    private readonly WhisperCppService _whisperCpp;
    private readonly ILogger<WhisperProcessingService> _logger;

    private string _modelPathWhisperCpp = "";

    public WhisperProcessingService(
        IOptions<WhisperCppOptions> options,
        WhisperFasterDockerService whisperFaster,
        WhisperCppService whisperCpp,
        ILogger<WhisperProcessingService> logger)
    {
        _options = options;
        _whisperFaster = whisperFaster;
        _whisperCpp = whisperCpp;
        _logger = logger;
    }

    public async Task<string> TranscribeAudioAsync(string audioPath, SprSpeechTable entity)
    {
        string recognizedText = "";
        try
        {
            _logger.LogInformation("WHISPER Started...");

            // Если язык не из списка, на который есть модель - Default через Docker Api
            if (!_options.Value.CustomModels.ContainsKey(entity.SPostid) || !_options.Value.CustomModels.TryGetValue(entity.SPostid, out string modelPath) || !File.Exists(modelPath))
            {
                _logger.LogInformation($"Распознавание _whisperFasterDocker");
                recognizedText = await _whisperFaster.TranscribeAsync(audioPath);
            }
            // Иначе - WhisperCpp Api
            else
            {
                try
                {
                    if (_modelPathWhisperCpp != modelPath)
                    {
                        _logger.LogInformation($"\nЗагрузка модели {modelPath}");
                        await _whisperCpp.LoadModelAsync(modelPath);
                        _modelPathWhisperCpp = modelPath;
                    }

                    _logger.LogInformation($"\nРаспознавание _whisperCpp");
                    recognizedText = await _whisperCpp.TranscribeAsync(audioPath);
                }
                catch
                {
                    recognizedText = await _whisperFaster.TranscribeAsync(audioPath);
                }
            }
            return recognizedText;
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            throw new FailedWhisperRequestException("Whisper Error: ", e);
        }
        finally
        {
            Files.DeleteFilesByPath(audioPath);
        }
    }
}
