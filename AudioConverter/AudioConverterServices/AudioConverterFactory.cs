using Microsoft.Extensions.DependencyInjection;
using Shield.AudioConverter.AudioConverterServices.Decoder;
using Shield.AudioConverter.AudioConverterServices.FFMpeg;
using Shield.AudioConverter.AudioConverterServices.NAudio;

namespace Shield.AudioConverter.AudioConverterServices;
public enum ConverterType { /*NAudio, */FFMpeg, Decoder }
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
            //ConverterType.NAudio => _serviceProvider.GetRequiredService<NAudioConverter>(),
            ConverterType.FFMpeg => _serviceProvider.GetRequiredService<FFMpegConverter>(),
            ConverterType.Decoder => _serviceProvider.GetRequiredService<DecoderConverter>(),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }
}
