using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;

using Shield.Estimator.Business.Options.KoboldOptions;
using Shield.Estimator.Business.Options.WhisperOptions;
using Shield.Estimator.Business.Services;
using Shield.Estimator.Business.Mappers;

namespace Shield.Estimator.Business;

public static class BusinessServiceExtensions
{
    public static IServiceCollection AddBusinessServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Регистрация настроек
        services.Configure<AiOptions>(configuration.GetSection("Ai"));
        services.Configure<WhisperOptions>(configuration.GetSection("Whisper"));

        // Регистрация HTTP-клиентов
        services.AddHttpClient<WhisperService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(15);
        });

        services.AddHttpClient<KoboldService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddAutoMapper(typeof(AiMapper));

        return services;
    }
}
