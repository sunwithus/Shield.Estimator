﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Shield.Estimator.Business.Options.KoboldOptions;
using Shield.Estimator.Business.Options.WhisperOptions;
using Shield.Estimator.Business.Services;
using Shield.Estimator.Business.Mappers;
using Shield.Estimator.Business.Services.WhisperNet;

namespace Shield.Estimator.Business;

public static class BusinessServiceExtensions
{
    public static IServiceCollection AddBusinessServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Регистрация настроек
        services.Configure<AiOptions>(configuration.GetSection("Ai"));
        services.Configure<WhisperFasterDockerOptions>(configuration.GetSection("WhisperFasterDocker"));
        services.Configure<WhisperXDockerOptions>(configuration.GetSection("WhisperXDocker"));
        services.Configure<WhisperNetOptions>(configuration.GetSection("WhisperNet"));
        services.Configure<WhisperCppOptions>(configuration.GetSection("WhisperCpp"));

       
        services.AddSingleton<WhisperNetService>();

        services.AddHttpClient<WhisperCppService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(15);
        });

        services.AddHttpClient<WhisperFasterDockerService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(15);
        });

        services.AddHttpClient<WhisperXDockerService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(15);
        });

        services.AddHttpClient<KoboldService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(20);
        });

        services.AddAutoMapper(typeof(AiMapper));

        return services;
    }
}
