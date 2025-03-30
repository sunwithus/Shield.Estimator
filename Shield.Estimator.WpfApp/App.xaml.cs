//App.xaml.cs

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shield.AudioConverter.AudioConverterServices.FFMpeg;
using Shield.AudioConverter.AudioConverterServices.NAudio;
using Shield.AudioConverter.AudioConverterServices;
using Shield.AudioConverter.Options;
using Shield.Estimator.Business.Options.WhisperOptions;
using Shield.Estimator.Business.Services.WhisperNet;
using System.IO;
using System.Windows;
using System.Diagnostics;


namespace Shield.Estimator.Wpf;

public partial class App : Application
{
    private IHost _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Настройка вывода в файл
        Trace.Listeners.Add(new TextWriterTraceListener("log.txt"));
        Trace.AutoFlush = true;

        base.OnStartup(e);
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(config =>
                {
                    config.SetBasePath(Directory.GetCurrentDirectory())
                          .AddJsonFile("appsettings.json");
                })
                .ConfigureServices((context, services) =>
                {
                    // AutoMapper
                    //services.AddAutoMapper(typeof(AiMapper).Assembly);

                    // Options
                    services.Configure<WhisperNetOptions>(context.Configuration.GetSection("WhisperNet"));

                    services.Configure<AudioConverterOptions>(context.Configuration.GetSection("AudioConverterConfig"));
                    services.AddSingleton<NAudioConverter>();
                    services.AddSingleton<FFMpegConverter>();
                    services.AddSingleton<AudioConverterFactory>();
                    // Services
                    services.AddSingleton<ProcessStateWpf>();
                    services.AddSingleton<WhisperNetService>();
                    services.AddSingleton<FileProcessor>();

                    services.AddSingleton<IConfiguration>(context.Configuration);


                    // Main Window
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            // Validate dependencies
            var sp = _host.Services;
            //sp.GetRequiredService<IMapper>();
            sp.GetRequiredService<WhisperNetService>();
            sp.GetRequiredService<FileProcessor>();
            sp.GetRequiredService<ProcessStateWpf>();

            // Show main window
            var mainWindow = sp.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"FATAL ERROR: {ex.Message}",
                          "Initialization Error",
                          MessageBoxButton.OK,
                          MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        base.OnExit(e);
    }

}
