using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Shield.AudioConverter.Options;
using Microsoft.Extensions.Options;
using System.Threading;

namespace Shield.AudioConverter.AudioConverterServices.NAudio;

public class NAudioConverter : IAudioConverter
{
    private readonly int _targetSampleRate;
    private readonly string _tempDirectory;

    public NAudioConverter(IOptions<AudioConverterOptions> config)
    {
        _targetSampleRate = config.Value.TargetSampleRate;
        _tempDirectory = config.Value.TempDirectory;
    }

    public async Task<MemoryStream> ConvertFileToStreamAsync(string inputFileName)
    {
        using var fileStream = new FileStream(inputFileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var sampleProvider = CreateSampleProvider(inputFileName, fileStream);
        return await ConvertToMemoryStreamAsync(sampleProvider);
    }

    public async Task<MemoryStream> ConvertByteArrayToStreamAsync(byte[] audioDataLeft, byte[] audioDataRight = null, string recordType = "", string eventCode = "")
    {
        var leftFile = await CreateTempFileAsync(audioDataLeft);
        var rightFile = audioDataRight != null ? await CreateTempFileAsync(audioDataRight) : null;

        try
        {
            var sampleProvider = CreateSampleProviderFromFiles(leftFile, rightFile);
            return await ConvertToMemoryStreamAsync(sampleProvider);
        }
        finally
        {
            File.Delete(leftFile);
            if (rightFile != null) File.Delete(rightFile);
        }
    }

    /// <summary>
    /// Конвертирует RAW аудио данные из байтовых массивов в аудиофайл и сохраняет по указанному пути
    /// </summary>
    /// <param name="audioDataLeft">Байтовый массив PCM данных для левого канала (обязательный)</param>
    /// <param name="audioDataRight">Байтовый массив PCM данных для правого канала (опционально)</param>
    /// <param name="audioFilePath">Полный путь для сохранения итогового файла</param>
    /// <param name="recordType">Тип записи (из таблицы SPR_SP_DATA_1_TABLE)</param>
    /// <param name="eventCode">Тип записи тоже (из таблицы SPR_SPEECH_TABLE)</param>
    /// <returns>Task представляющий асинхронную операцию</returns>
    /// <exception cref="ArgumentNullException">Если audioDataLeft или audioFilePath не указаны</exception>
    /// <exception cref="IOException">При ошибках записи файла</exception>
    /// <remarks>
    /// Особенности реализации:
    /// - Автоматически создает структуру директорий при необходимости
    /// - Поддерживает как моно (только left), так и стерео записи
    /// - Сохраняет в формате WAV PCM 16-bit
    /// - Использует временные файлы для промежуточной обработки
    /// </remarks>
    public async Task ConvertByteArrayToFileAsync(byte[] audioDataLeft, byte[] audioDataRight, string audioFilePath, string recordType = "", string eventCode = "")
    {
        /*
        if (audioDataLeft == null || audioDataLeft.Length == 0)
            throw new ArgumentNullException(nameof(audioDataLeft));

        if (string.IsNullOrWhiteSpace(audioFilePath))
            throw new ArgumentNullException(nameof(audioFilePath));
        */
        string leftFile = "";
        string rightFile = "";
        try
        {
            leftFile = await CreateTempFileAsync(audioDataLeft);
            rightFile = audioDataRight != null ? await CreateTempFileAsync(audioDataRight) : null;


            await using (var conversionStream = await ConvertByteArrayToStreamAsync(audioDataLeft, audioDataRight))
            await using (var fileStream = new FileStream(audioFilePath,FileMode.Create,FileAccess.Write,FileShare.None,bufferSize: 4096,FileOptions.Asynchronous))
            {
                conversionStream.Position = 0;
                await conversionStream.CopyToAsync(fileStream);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NAudio conversion error: {ex.Message}");
            throw; // Перебрасываем исключение для обработки в вызывающем коде
        }
        finally
        {
            if (File.Exists(audioFilePath)) Console.WriteLine("ConvertByteArrayToFileAsync NAudio EXIST");
            File.Delete(leftFile);
            if (rightFile != null) File.Delete(rightFile);
        }
    }

    /// <summary>
    /// Конвертирует аудиофайл в сырые PCM данные с разделением каналов
    /// </summary>
    /// <param name="inputFileName">Путь к входному файлу</param>
    /// <returns>Кортеж с длительностью и данными каналов</returns>
    /// <exception cref="FileNotFoundException">Если файл не существует</exception>
    public async Task<(int Duration, byte[] Left, byte[] Right)> ConvertFileToByteArrayAsync(string inputFileName)
    {
        if (!File.Exists(inputFileName))
            throw new FileNotFoundException("Input file not found", inputFileName);

        try
        {
            using var reader = new AudioFileReader(inputFileName);
            var duration = (int)reader.TotalTime.TotalSeconds;
            var samples = new float[reader.Length / sizeof(float)];
            await Task.Run(() => reader.Read(samples, 0, samples.Length));

            var interleaved = samples;
            var channelData = SplitChannels(interleaved, reader.WaveFormat.Channels);

            return (duration, ConvertToByteArray(channelData[0]),channelData.Count > 1 ? ConvertToByteArray(channelData[1]) : null);
        }
        finally
        {
        }
    }

    private List<float[]> SplitChannels(float[] interleaved, int channels)
    {
        var result = new List<float[]>();
        for (int ch = 0; ch < channels; ch++)
        {
            var channelData = new float[interleaved.Length / channels];
            for (int i = 0; i < channelData.Length; i++)
            {
                channelData[i] = interleaved[i * channels + ch];
            }
            result.Add(channelData);
        }
        return result;
    }

    private byte[] ConvertToByteArray(float[] samples)
    {
        var byteBuffer = new byte[samples.Length * sizeof(short)];
        var resampled = new short[samples.Length];

        for (int i = 0; i < samples.Length; i++)
        {
            resampled[i] = (short)(samples[i] * short.MaxValue);
        }

        Buffer.BlockCopy(resampled, 0, byteBuffer, 0, byteBuffer.Length);
        return byteBuffer;
    }

    private ISampleProvider CreateSampleProvider(string fileName, Stream stream)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mp3" => new Mp3FileReader(stream).ToSampleProvider(),
            ".wav" => new WaveFileReader(stream).ToSampleProvider(),
            _ => new AudioFileReader(fileName).ToSampleProvider()
        };
    }

    private ISampleProvider CreateSampleProviderFromFiles(string leftFile, string rightFile)
    {
        var leftChannel = new AudioFileReader(leftFile).ToSampleProvider();
        if (rightFile == null) return leftChannel;

        var rightChannel = new AudioFileReader(rightFile).ToSampleProvider();
        return new MultiplexingSampleProvider(new[] { leftChannel, rightChannel }, 2);
    }

    private async Task<MemoryStream> ConvertToMemoryStreamAsync(ISampleProvider sampleProvider)
    {
        var resampler = sampleProvider.WaveFormat.SampleRate != _targetSampleRate
            ? new WdlResamplingSampleProvider(sampleProvider, _targetSampleRate)
            : sampleProvider;

        var outputStream = new MemoryStream();
        await Task.Run(() => WaveFileWriter.WriteWavFileToStream(outputStream, resampler.ToWaveProvider16()));
        outputStream.Seek(0, SeekOrigin.Begin);
        return outputStream;
    }

    private async Task<string> CreateTempFileAsync(byte[] data)
    {
        if (!Directory.Exists(_tempDirectory))
            Directory.CreateDirectory(_tempDirectory);

        var tempFile = Path.Combine(_tempDirectory, $"{Guid.NewGuid()}.tmp");
        await File.WriteAllBytesAsync(tempFile, data);
        return tempFile;
    }
}
