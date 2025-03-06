using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;

using Shield.Estimator.Business.AudioConverterServices.NAudio;
using Shield.Estimator.Business.Options.KoboldOptions;
using Shield.Estimator.Business.Options.WhisperOptions;
using Shield.Estimator.Business.Options;
using Shield.Estimator.Business.Services;
using Shield.Estimator.Business.Mappers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shield.Estimator.Business.AudioConverterServices;
using Shield.Estimator.Business.AudioConverterServices.FFMpeg;
using Microsoft.Extensions.Options;
using Whisper.net;
using Shield.Estimator.Business.Services.WhisperNet;

namespace Shield.Estimator.Business;

public static class BusinessServiceExtensions
{
    public static IServiceCollection AddBusinessServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Регистрация настроек
        services.Configure<AiOptions>(configuration.GetSection("Ai"));
        services.Configure<WhisperDockerOptions>(configuration.GetSection("WhisperDocker"));
        services.Configure<WhisperNetOptions>(configuration.GetSection("WhisperNet"));
        services.Configure<WhisperCppOptions>(configuration.GetSection("WhisperCpp"));


        services.Configure<AudioConverterOptions>(configuration.GetSection("AudioConverterConfig"));
        services.AddTransient<NAudioConverter>();
        services.AddTransient<FFMpegConverter>();
        services.AddTransient<AudioConverterFactory>();

        
        services.AddSingleton<WhisperNetService>();

        services.AddHttpClient<WhisperCppService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(15);
        });

        services.AddHttpClient<WhisperDockerService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(15);
        });

        services.AddHttpClient<KoboldService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(15);
        });

        services.AddAutoMapper(typeof(AiMapper));

        return services;
    }
}
