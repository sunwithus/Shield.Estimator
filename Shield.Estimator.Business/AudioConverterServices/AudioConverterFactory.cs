using Microsoft.Extensions.DependencyInjection;
using Shield.Estimator.Business.AudioConverterServices.FFMpeg;
using Shield.Estimator.Business.AudioConverterServices.NAudio;
using Whisper.net.Wave;

namespace Shield.Estimator.Business.AudioConverterServices;

public enum ConverterType { NAudio, FFMpeg }

public class AudioConverterFactory 
{
    private readonly IServiceProvider _serviceProvider;

    public AudioConverterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IAudioConverter CreateConverter(ConverterType type)
    {
        return type switch
        {
            ConverterType.NAudio => _serviceProvider.GetRequiredService<NAudioConverter>(),
            ConverterType.FFMpeg => _serviceProvider.GetRequiredService<FFMpegConverter>(),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    public async Task<WaveData> GetWaveStreamFromByteArray(byte[] audioDataLeft, byte[] audioDataRight)
    {

        // Вначале NAudio, если не удалось то FFMpeg
        foreach (var converterType in Enum.GetValues<ConverterType>())
        {
            MemoryStream stream = null;
            try
            {
                var converter = this.CreateConverter(converterType);
                stream = await converter.ConvertByteArrayToStreamAsync(audioDataLeft, audioDataRight);

                if (stream.Length > 0)
                {
                    var parser = new WaveParser(stream);
                    await parser.InitializeAsync();
                    return new WaveData(stream, parser);
                }
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                Console.WriteLine($"conversion failed {converterType}: {ex.Message}");
            }
        }

        throw new InvalidOperationException("All audio conversions failed");
    }

    public async Task<WaveData> GetWaveStreamFromFile(string audioFilePath)
    {

        // Вначале NAudio, если не удалось то FFMpeg
        foreach (var converterType in Enum.GetValues<ConverterType>())
        {
            MemoryStream stream = null;
            try
            {
                var converter = this.CreateConverter(converterType);
                stream = await converter.ConvertFileToStreamAsync(audioFilePath);

                if (stream.Length > 0)
                {
                    var parser = new WaveParser(stream);
                    await parser.InitializeAsync();
                    return new WaveData(stream, parser); // Возвращаем экземпляр WaveData
                }
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                Console.WriteLine($"conversion failed {converterType}: {ex.Message}");
            }
        }

        throw new InvalidOperationException("All audio conversions failed");
    }
}
