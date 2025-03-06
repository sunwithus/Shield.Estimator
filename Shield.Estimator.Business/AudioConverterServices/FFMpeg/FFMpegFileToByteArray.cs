//AudioToDbConverter.cs
using FFMpegCore.Pipes;
using FFMpegCore;
using System.Diagnostics;
using System.Configuration;
using FFMpegCore.Enums;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Polly;

namespace Shield.Estimator.Business.AudioConverterServices.FFMpeg;

public class FFMpegFileToByteArray
{
    // Настройка путей при инициализации
    private static FFOptions _ffOptions;
    private static IConfiguration _configuration;
    private IAsyncPolicy<HttpResponseMessage> _retryPolicy;

    public FFMpegFileToByteArray(string ffmpegPath = null)
    {
        if (ffmpegPath == null)
        {
            ffmpegPath = _configuration["PathToFFmpegExe"];
        }
        _ffOptions = new FFOptions { BinaryFolder = ffmpegPath };
        GlobalFFOptions.Configure(_ffOptions);
        _retryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .OrResult(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<(int Duration, byte[] Left, byte[] Right)> ProcessAudioAsync(string inputFileName)
    {
        var (duration, channels) = await RetryAsync(() => AnalyzeAudioAsync(inputFileName));

        var tasks = channels switch
        {
            1 => new List<Task<byte[]>> { ProcessChannelAsync(inputFileName, "") },
            _ => new List<Task<byte[]>> { ProcessChannelAsync(inputFileName, "-filter_complex \"[0:0]pan=1|c0=c0\""), ProcessChannelAsync(inputFileName, "-filter_complex \"[0:0]pan=1|c0=c1\"") }
        };

        var results = await Task.WhenAll(tasks);

        return (duration, results[0], results.Count() > 1 ? results[1] : null);
    }

    private static async Task<(int Duration, int Channels)> AnalyzeAudioAsync(string inputFileName)
    {
        var mediaInfo = await FFProbe.AnalyseAsync(inputFileName);
        return (
            (int)(mediaInfo.PrimaryAudioStream?.Duration.TotalSeconds ?? 0),
            mediaInfo.PrimaryAudioStream?.Channels ?? 0
        );
    }

    // Общий метод построения аргументов
    private static FFMpegArgumentOptions BuildArguments(FFMpegArgumentOptions options, string customArguments,
        int bitrate = 128_000,
        int sampleRate = 8000,
        string audioCodec = "pcm_alaw")
    {
        return options
            .WithCustomArgument(customArguments)
            .WithAudioCodec(audioCodec)
            .WithAudioBitrate(bitrate)
            .WithAudioSamplingRate(sampleRate)
            .ForceFormat("wav");
    }

    private static async Task<T> RetryAsync<T>(Func<Task<T>> operation, int maxRetries = 5)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (i < maxRetries - 1)
            {
                await Task.Delay(200 * (i + 1));
                Debug.WriteLine($"Retry {i + 1}: {ex.Message}");
            }
        }
        throw new InvalidOperationException("Max retries exceeded");
    }

    private static async Task<byte[]> ProcessChannelAsync(string inputFile, string filter)
    {
        using var ms = new MemoryStream();
        await FFMpegArguments
            .FromFileInput(inputFile)
            .OutputToPipe(new StreamPipeSink(ms), o => BuildArguments(o, filter))
            .ProcessAsynchronously(true, _ffOptions);
        return ms.ToArray();
    }

    public async Task<MemoryStream> FileToSingle16000StreamAsync(string inputFileName)
    {
        var (duration, channels) = await RetryAsync(() => AnalyzeAudioAsync(inputFileName));

        // Используем MemoryStream для объединения аудио
        using var combinedStream = new MemoryStream();

        var customArguments = channels switch
        {
            1 => "",
            _ => "-filter_complex \"[0:0]pan=1|c0=c0+1|c1=c1\""
        };

        // Процесс аудио в один поток
        await FFMpegArguments
            .FromFileInput(inputFileName)
            .OutputToPipe(new StreamPipeSink(combinedStream), o => BuildArguments(o, customArguments))
            .ProcessAsynchronously(true, _ffOptions);

        combinedStream.Position = 0; // Перемещаем курсор в начало

        return combinedStream;

    }

}


