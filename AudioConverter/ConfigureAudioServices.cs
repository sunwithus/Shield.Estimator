using Shield.AudioConverter.Options;
using Shield.AudioConverter.AudioConverterServices;
using Shield.AudioConverter.AudioConverterServices.NAudio;
using Shield.AudioConverter.AudioConverterServices.FFMpeg;
using Shield.AudioConverter.AudioConverterServices.Decoder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Shield.AudioConverter;

public static class ConfigureAudioServices
{
    public static IServiceCollection AddAudioConverterServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Регистрация настроек
        services.Configure<AudioConverterOptions>(configuration.GetSection("AudioConverterConfig"));
        services.AddTransient<NAudioConverter>();
        services.AddTransient<FFMpegConverter>();
        services.AddTransient<DecoderConverter>();
        services.AddTransient<AudioConverterFactory>();

        return services;
    }
}
