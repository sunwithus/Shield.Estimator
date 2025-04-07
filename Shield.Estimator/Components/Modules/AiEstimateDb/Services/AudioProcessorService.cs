using Shield.AudioConverter.AudioConverterServices;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;
using Shield.Estimator.Shared.Components.EntityFrameworkCore;

namespace Shield.Estimator.Shared.Components.Modules.AiEstimateDb.Services;

public class AudioProcessorService
{
    private readonly AudioConverterFactory _audioConverter;
    private readonly ILogger<AudioProcessorService> _logger;

    public AudioProcessorService(
        AudioConverterFactory audioConverter,
        ILogger<AudioProcessorService> logger)
    {
        _audioConverter = audioConverter;
        _logger = logger;
    }

    internal async Task ConvertByteArrayToFile(byte[]? AudioDataLeft, byte[]? AudioDataRight, string audioFilePath, string RecordType, string Eventcode)
    {
        foreach (var converterType in Enum.GetValues<ConverterType>())
        {
            try
            {
                _logger.Log(LogLevel.Information, $"Attempting conversion with {converterType}");

                var converter = _audioConverter.CreateConverter(converterType);
                _logger.LogInformation($"converterType = {converterType}");
                await converter.ConvertByteArrayToFileAsync(AudioDataLeft, AudioDataRight, audioFilePath, RecordType, Eventcode);

                if (File.Exists(audioFilePath))
                {
                    _logger.Log(LogLevel.Information, $"Conversion successful using {converterType}");
                    return;
                }
                else
                {
                    _logger.Log(LogLevel.Warning, $"Conversion with {converterType} did not produce a file");
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error with {converterType}: {ex.Message}", ConsoleColor.Red);
                // Логируем полную информацию об ошибке
                //Console.WriteLine($"Full error details: {ex}");
            }
            continue;
        }
        throw new InvalidOperationException("All audio conversions failed");
    }

}
