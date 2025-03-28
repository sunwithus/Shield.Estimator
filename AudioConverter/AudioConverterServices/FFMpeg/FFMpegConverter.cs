using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Options;
using Shield.AudioConverter.Options;
using System.Reflection;
using Shield.AudioConverter.AudioConverterServices;

namespace Shield.AudioConverter.AudioConverterServices.FFMpeg;

/// <summary>
/// Сервис для конвертации аудиофайлов с использованием FFMpeg.
/// </summary>
/// <remarks>
/// Реализует потокобезопасное преобразование аудио в форматы WAV/PCM с настраиваемыми параметрами.
/// Поддерживает работу с файлами и байтовыми массивами, используя ограниченный параллелизм через SemaphoreSlim.
/// </remarks>
public class FFMpegConverter : IAudioConverter
{
    private readonly FFOptions _ffOptions;
    private readonly string _customArguments;

    private static readonly SemaphoreSlim _semaphoreFfmpeg = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Инициализирует новый экземпляр конвертера с настройками из конфигурации
    /// </summary>
    /// <param name="options">Настройки аудио конвертации из DI-контейнера</param>
    /// <exception cref="ArgumentNullException">Выбрасывается если options.Value равно null</exception>
    /// <remarks>
    /// Автоматически настраивает глобальные параметры FFMpeg:
    /// - Ищет бинарники ffmpeg в папке сборки
    /// - Использует временную директорию из настроек или системную temp
    /// </remarks>
    public FFMpegConverter(IOptions<AudioConverterOptions> options)
    {
        // Директория проекта
        string assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        _ffOptions = new FFOptions
        {
            BinaryFolder = Path.Combine(assemblyLocation, "ffmpeg"),
            TemporaryFilesFolder = Path.Combine(Path.GetTempPath(), "tempFFMpeg")
        };
        GlobalFFOptions.Configure(_ffOptions);
        _customArguments = "-codec:a pcm_s16le -b:a 128k -ar 16000";
    }

    public async Task<MemoryStream> ConvertFileToStreamAsync(string inputFileName)
    {
        var outputStream = new MemoryStream();
        await _semaphoreFfmpeg.WaitAsync();
        try
        {
            await FFMpegArguments
                .FromFileInput(inputFileName)
                .OutputToPipe(new StreamPipeSink(outputStream), o => BuildArguments(o, _customArguments))
                .ProcessAsynchronously(true, _ffOptions);

            outputStream.Position = 0;
            return outputStream;
        }
        finally
        {
            _semaphoreFfmpeg.Release();
        }
    }

    public async Task<MemoryStream> ConvertByteArrayToStreamAsync(byte[] audioDataLeft, byte[] audioDataRight = null, string recordType = "", string eventCode = "")
    {
        var outputStream = new MemoryStream();
        await _semaphoreFfmpeg.WaitAsync();

        try
        {
            using var streamLeft = new MemoryStream(audioDataLeft);
            using var streamRight = audioDataRight != null ? new MemoryStream(audioDataRight) : null;
            var ffmpegArgs = FFMpegArguments.FromPipeInput(new StreamPipeSource(streamLeft));

            string rightArgument = "";
            if (streamRight != null)
            {
                ffmpegArgs = ffmpegArgs.AddPipeInput(new StreamPipeSource(streamRight));
                rightArgument = "-filter_complex amerge=inputs=2 -ac 2";
            }

            await ffmpegArgs
                .OutputToPipe(new StreamPipeSink(outputStream), o => BuildArguments(o, $"{_customArguments} {rightArgument}"))
                .ProcessAsynchronously(true, _ffOptions);

            Console.WriteLine("ConvertByteArrayToStreamAsync SUCCESS!!!");

            outputStream.Position = 0;
            return outputStream;
        }

        finally
        {
            _semaphoreFfmpeg.Release();
        }
    }

    public async Task ConvertByteArrayToFileAsync(byte[] audioDataLeft, byte[] audioDataRight, string audioFilePath, string recordType = "", string eventCode = "")
    {
        
        if (audioDataLeft == null || audioDataLeft.Length == 0)
            throw new ArgumentNullException(nameof(audioDataLeft));

        if (string.IsNullOrWhiteSpace(audioFilePath))
            throw new ArgumentNullException(nameof(audioFilePath));

        try
        {
            using var audioStream = await ConvertByteArrayToStreamAsync(audioDataLeft, audioDataRight);

            await using var fileStream = new FileStream(audioFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await audioStream.CopyToAsync(fileStream);
            
            Console.WriteLine("FFMpeg conversion completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FFMpeg conversion error: {ex.Message}");
            //throw; // Перебрасываем исключение для обработки в вызывающем коде
        }
    }

    public async Task<(int Duration, byte[] Left, byte[] Right)> ConvertFileToByteArrayAsync(string inputFileName)
    {
        if (!File.Exists(inputFileName))
            throw new FileNotFoundException("Input file not found", inputFileName);

        try
        {
            (int duration, int channels) = await AudioInfo.AnalyzeAudioAsync(inputFileName);

            var tasks = new List<Task<byte[]>>();
            if (channels == 1)
            {
                tasks.Add(ProcessChannelAsync(inputFileName, ""));
            }
            else
            {
                tasks.Add(ProcessChannelAsync(inputFileName, "-filter_complex \"[0:a]pan=1|c0=c0[L]\" -map \"[L]\""));
                tasks.Add(ProcessChannelAsync(inputFileName, "-filter_complex \"[0:a]pan=1|c0=c1[R]\" -map \"[R]\""));
            }

            var results = await Task.WhenAll(tasks);
            return (duration, results[0], results.Length > 1 ? results[1] : null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("FFmpeg ConvertFileToByteArrayAsync => " + ex.Message);
            throw;
        }
    }

    private async Task<byte[]> ProcessChannelAsync(string inputFile, string filter)
    {
        await _semaphoreFfmpeg.WaitAsync();
        try
        {
            using var ms = new MemoryStream();
            await FFMpegArguments
                .FromFileInput(inputFile)
                .OutputToPipe(new StreamPipeSink(ms), options => options
                            .WithCustomArgument(filter)
                            .WithAudioCodec("pcm_alaw")
                            .WithAudioBitrate(128_000)
                            .WithAudioSamplingRate(8000)
                            .ForceFormat("wav"))
                .ProcessAsynchronously(true, _ffOptions);
            return ms.ToArray();
        }
        finally
        {
            _semaphoreFfmpeg.Release();
        }
    }

    private FFMpegArgumentOptions BuildArguments(FFMpegArgumentOptions options, string customArguments = "-codec:a pcm_s16le -b:a 128k -ar 16000 -ac 1")
    {
        return options
            .ForceFormat("wav")
            .WithCustomArgument(customArguments);
    }

}
