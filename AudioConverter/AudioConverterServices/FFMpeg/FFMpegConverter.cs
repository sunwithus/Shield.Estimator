using FFMpegCore;
using FFMpegCore.Pipes;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Shield.AudioConverter.Options;
using System.Reflection;
using FFMpegCore.Arguments;

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

    private readonly int _targetSampleRate;
    private readonly int _targetBitRate;
    private readonly string _tempDirectory;

    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

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
        string ffmpegPath = Path.Combine(assemblyLocation, "ffmpeg");

        _targetSampleRate = options?.Value.TargetSampleRate ?? 16000;
        _targetBitRate = options?.Value.TargetBitRate ?? 128000;
        _tempDirectory = options?.Value.TempDirectory ?? Path.Combine(Path.GetTempPath(), "tempFFMpeg");

        _ffOptions = new FFOptions { BinaryFolder = ffmpegPath, TemporaryFilesFolder = _tempDirectory };
        GlobalFFOptions.Configure(_ffOptions);
    }

    /// <summary>
    /// Конвертирует аудиофайл в MemoryStream с заданными параметрами
    /// </summary>
    /// <param name="inputFileName">Полный путь к входному файлу</param>
    /// <param name="customArguments">Кастомные аргументы FFMpeg (переопределяют стандартные)</param>
    /// <returns>MemoryStream с конвертированным аудио в формате WAV/PCM</returns>
    /// <exception cref="FileNotFoundException">Если входной файл не существует</exception>
    /// <exception cref="InvalidOperationException">При ошибках FFMpeg</exception>
    /// <remarks>
    /// Особенности реализации:
    /// - Использует SemaphoreSlim для гарантии единственного одновременного вызова FFMpeg
    /// - Автоматически сбрасывает позицию потока в начало
    /// - Поддерживает кастомные аргументы конвертации
    /// </remarks>
    public async Task<MemoryStream> ConvertFileToStreamAsync(string inputFileName)
    {
        await _semaphore.WaitAsync();
        try
        {
            var outputStream = new MemoryStream();
            await FFMpegArguments
                .FromFileInput(inputFileName)
                .OutputToPipe(new StreamPipeSink(outputStream), o => BuildArguments(o, ""))
                .ProcessAsynchronously(true, _ffOptions);

            outputStream.Position = 0;
            return outputStream;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Конвертирует RAW аудио данные из байтовых массивов в MemoryStream
    /// </summary>
    /// <param name="audioDataLeft">Байтовый массив для левого канала</param>
    /// <param name="audioDataRight">Опциональный байтовый массив для правого канала</param>
    /// <param name="customArguments">Кастомные аргументы FFMpeg</param>
    /// <returns>MemoryStream с конвертированным аудио</returns>
    /// <remarks>
    /// Особенности:
    /// - Если указан audioDataRight, создается стерео-аудио
    /// - Для моно-аудио передавать только audioDataLeft
    /// - Для работы с необработанными данными требуется правильная настройка кодеков
    /// </remarks>
    public async Task<MemoryStream> ConvertByteArrayToStreamAsync(byte[] audioDataLeft, byte[] audioDataRight = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            var outputStream = new MemoryStream();
            var arguments = FFMpegArguments.FromPipeInput(new StreamPipeSource(new MemoryStream(audioDataLeft)));

            if (audioDataRight != null)
            {
                arguments.AddPipeInput(new StreamPipeSource(new MemoryStream(audioDataRight)));
            }
            await arguments
                .OutputToPipe(new StreamPipeSink(outputStream), o => BuildArguments(o, ""))
                .ProcessAsynchronously(true, _ffOptions);

            outputStream.Position = 0;
            return outputStream;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Конвертирует RAW аудио данные из байтовых массивов в аудиофайл и сохраняет по указанному пути
    /// </summary>
    /// <param name="audioDataLeft">Байтовый массив PCM данных для левого канала (обязательный)</param>
    /// <param name="audioDataRight">Байтовый массив PCM данных для правого канала (опционально)</param>
    /// <param name="recordType">Тип записи (из таблицы SPR_SP_DATA_1_TABLE)</param>
    /// <param name="eventCode">Тип записи тоже (из таблицы SPR_SPEECH_TABLE)</param>
    /// <param name="audioFilePath">Полный путь для сохранения итогового файла</param>
    /// <returns>Путь к сохраненному файлу</returns>
    /// <exception cref="ArgumentNullException">Если audioDataLeft или audioFilePath не указаны</exception>
    /// <exception cref="IOException">При ошибках записи файла</exception>
    /// <remarks>
    /// Особенности реализации:
    /// - Автоматически создает структуру директорий при необходимости
    /// - Поддерживает как моно (только left), так и стерео записи
    /// - Сохраняет в формате WAV PCM 16-bit LE
    /// </remarks>
    public async Task ConvertByteArrayToFileAsync(byte[] audioDataLeft, byte[] audioDataRight, string audioFilePath, string recordType = "", string eventCode = "")
    {
        if (audioDataLeft == null || audioDataLeft.Length == 0)
            throw new ArgumentNullException(nameof(audioDataLeft));

        if (string.IsNullOrWhiteSpace(audioFilePath))
            throw new ArgumentNullException(nameof(audioFilePath));

        await _semaphore.WaitAsync();
        try
        {
            using var audioStream = await ConvertByteArrayToStreamAsync(audioDataLeft, audioDataRight);

            await using (var fileStream = new FileStream(audioFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                audioStream.Position = 0;
                await audioStream.CopyToAsync(fileStream);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Конвертирует аудиофайл в сырые PCM данные с разделением каналов
    /// </summary>
    /// <param name="inputFileName">Путь к входному файлу</param>
    /// <returns>Кортеж с длительностью и данными каналов</returns>
    /// <exception cref="FileNotFoundException">Если файл не существует</exception>
    /// <exception cref="InvalidOperationException">При ошибках обработки</exception>
    public async Task<(int Duration, byte[] Left, byte[] Right)> ConvertFileToByteArrayAsync(string inputFileName)
    {
        if (!File.Exists(inputFileName))
            throw new FileNotFoundException("Input file not found", inputFileName);

        await _semaphore.WaitAsync();
        try
        {
            (int duration, int channels) = await AnalyzeAudioAsync(inputFileName);

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
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<byte[]> ProcessChannelAsync(string inputFile, string filter)
    {
        using var ms = new MemoryStream();
        await FFMpegArguments
            .FromFileInput(inputFile)
            .OutputToPipe(new StreamPipeSink(ms), o => BuildArguments(o, filter))
            .ProcessAsynchronously(true, _ffOptions);
        return ms.ToArray();
    }

    private FFMpegArgumentOptions BuildArguments(FFMpegArgumentOptions options, string customArguments, string audioCodec = "pcm_s16le")
    //.WithCustomArgument("-codec:a pcm_s16le -b:a 128k -ar 16000 -ac 1")
    {
        return options
            .WithCustomArgument(customArguments)
            .WithAudioCodec(audioCodec)
            .WithAudioBitrate(_targetBitRate)
            .WithAudioSamplingRate(_targetSampleRate)
            .ForceFormat("wav");
    }

    public static async Task<(int Duration, int Channels)> AnalyzeAudioAsync(string inputFileName)
    {
        var mediaInfo = await FFProbe.AnalyseAsync(inputFileName);
        return (
            (int)(mediaInfo.PrimaryAudioStream?.Duration.TotalSeconds ?? 0),
            mediaInfo.PrimaryAudioStream?.Channels ?? 0
        );
    }
}
