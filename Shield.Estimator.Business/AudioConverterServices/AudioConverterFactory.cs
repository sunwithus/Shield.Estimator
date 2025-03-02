using Microsoft.Extensions.DependencyInjection;
using Shield.Estimator.Business.AudioConverterServices.FFMpeg;
using Shield.Estimator.Business.AudioConverterServices.NAudio;

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
}
