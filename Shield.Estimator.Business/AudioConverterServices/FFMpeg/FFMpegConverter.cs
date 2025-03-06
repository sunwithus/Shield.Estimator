using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Pipes;
using Shield.Estimator.Business.AudioConverterServices;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Shield.Estimator.Business.Options;
using FFMpegCore.Arguments;

namespace Shield.Estimator.Business.AudioConverterServices.FFMpeg;

public class FFMpegConverter : IAudioConverter
{
    private readonly FFOptions _ffOptions;
    private readonly IConfiguration _configuration;

    private readonly int _targetSampleRate;
    private readonly int _targetBitRate;
    private readonly string _tempDirectory;

    public FFMpegConverter(IConfiguration configuration, IOptions<AudioConverterOptions> options)
    {
        _configuration = configuration;
        _ffOptions = new FFOptions { BinaryFolder = _configuration["PathToFFmpegExe"] };
        GlobalFFOptions.Configure(_ffOptions);

        _targetSampleRate = options.Value.TargetSampleRate;
        _targetBitRate = options.Value.TargetBitRate;
        _tempDirectory = options.Value.TempDirectory;
    }

    public async Task<MemoryStream> ConvertFileToStreamAsync(string inputFileName)
    {
        string customArguments = "";
/*
        var (duration, channels) = await RetryAsync(() => AnalyzeAudioAsync(inputFileName));
        string customArguments = channels switch
        {
            1 => "",
            _ => "-filter_complex amix=inputs=2:duration=first:dropout_transition=2"
        };
*/

        var combinedStream = new MemoryStream();

        // Процесс аудио в один поток
        await FFMpegArguments
            .FromFileInput(inputFileName)
            .OutputToPipe(new StreamPipeSink(combinedStream), o => BuildArguments(o, customArguments, _targetSampleRate, _targetBitRate))
            .ProcessAsynchronously(true, _ffOptions);

        combinedStream.Position = 0; // Перемещаем курсор в начало

        return combinedStream;

    }
    
    public async Task<MemoryStream> ConvertByteArrayToStreamAsync(byte[] audioDataLeft, byte[] audioDataRight = null)
    {
        string customArguments = "";
        var outputStream = new MemoryStream();
        
        if (audioDataRight != null && audioDataLeft != null)
        {
            //customArguments = "-filter_complex amix=inputs=2:duration=first:dropout_transition=2";
            using var inputStreamLeft = new MemoryStream(audioDataLeft);
            using var inputStreamRight = new MemoryStream(audioDataRight);

            var arguments = FFMpegArguments
                .FromPipeInput(new StreamPipeSource(inputStreamLeft))
                .AddPipeInput(new StreamPipeSource(inputStreamRight))
                .OutputToPipe(new StreamPipeSink(outputStream), o => BuildArguments(o, customArguments, _targetSampleRate, _targetBitRate));

                await arguments.ProcessAsynchronously(true, _ffOptions);
            
        }
        else if (audioDataLeft != null)
        {
            using var inputStreamLeft = new MemoryStream(audioDataLeft);

            var arguments = FFMpegArguments
                .FromPipeInput(new StreamPipeSource(inputStreamLeft))
                .OutputToPipe(new StreamPipeSink(outputStream), o => BuildArguments(o, customArguments, _targetSampleRate, _targetBitRate));

            await arguments.ProcessAsynchronously(true, _ffOptions);
        }

        outputStream.Position = 0;
        
        return outputStream;
    }
    

    private static FFMpegArgumentOptions BuildArguments(FFMpegArgumentOptions options, string customArguments,
        int sampleRate = 16000,
        int bitrate = 128_000,
        string audioCodec = "pcm_s16le")
    //.WithCustomArgument("-codec:a pcm_s16le -b:a 128k -ar 16000 -ac 1")
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

    private static async Task<(int Duration, int Channels)> AnalyzeAudioAsync(string inputFileName)
    {
        var mediaInfo = await FFProbe.AnalyseAsync(inputFileName);
        return (
            (int)(mediaInfo.PrimaryAudioStream?.Duration.TotalSeconds ?? 0),
            mediaInfo.PrimaryAudioStream?.Channels ?? 0
        );
    }
}
