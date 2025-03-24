using System.Diagnostics;
using System.IO;
using System.Text;
using FFMpegCore;
using Microsoft.Extensions.Options;
using Shield.AudioConverter.Options;
using Shield.Estimator.AudioConverter._SeedLibs;

namespace Shield.AudioConverter.AudioConverterServices.Decoder;

/// <summary>
/// Сервис для конвертации аудиофайлов с использованием decoder.exe.
/// </summary>
public class DecoderConverter : IAudioConverter
{
    private readonly string _decoderPath;
    private readonly string _codecPath;
    private readonly List<string> _codecs = new List<string>()
        {
            "WAVE_FILE", "RPE-LTP", "DAMPS", "GSM", "PCM-128", "QCELP-8", "EVRC", "QCELP-13", "ADPCM", "AMBE.HR_IRIDIUM", "A-LAW", "AMBE_INMARSAT_MM", "APC.HR_INMARSAT_B", "IMBE_INMARSAT_M",
            "AME", "ACELP_TETRA", "GSM.EFR_ABIS", "GSM.HR_ABIS", "GSM.AMR_ABIS", "GSM_ABIS", "LD-CELP", "E-QCELP", "ATC", "PSI-CELP", "AMBE.GMR1", "AMBE.GMR2", "AMBE.INMARSAT_BGAN", "ADM.UAV",
            "PCMA", "PCMU", "IPCMA", "IPCMU", "L8", "IL8", "L16", "IL16", "G.723.1", "G.726-32", "G.728", "G.729", "GSM.0610", "ILBC-13", "ILBC-15", "UMTS_AMR", "PDC.FR", "PDC.EFR", "PDC.HR",
            "IDEN.FR", "APCO-25", "RP-CELP", "IDEN.HR"
        };

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "tempDecoder");
    private static readonly SemaphoreSlim _semaphoreDecoder = new (1, 1);
    private const int WavHeaderSize = 44;

    public DecoderConverter(IOptions<AudioConverterOptions> options)
    {
        var assemblyLocation = Path.GetDirectoryName(typeof(DecoderConverter).Assembly.Location);
        _decoderPath = Path.Combine(assemblyLocation, "decoder", "decoder.exe");
        _codecPath = Path.Combine(assemblyLocation, "decoder", "suppdll");
        Directory.CreateDirectory(_tempDirectory);
    }

    public async Task<MemoryStream> ConvertFileToStreamAsync(string inputFileName)
    {
        var tempOutputLeft = GetTempFilePath(".wav");
        var tempOutputRight = GetTempFilePath(".wav");
        try
        {
            await RunDecoderAsync(
                eventCode: "PCMA", // Default codec
                inputFileLeft: inputFileName,
                outputFileLeft: tempOutputLeft,
                outputFileRight: tempOutputRight,
                inputFileRight: null
            );

            return await FileToMemoryStreamAsync(tempOutputLeft);
        }
        catch(Exception ex)
        {
            Console.WriteLine("Error ConvertFileToStreamAsync => " + ex.Message);
            throw;
        }
        finally
        {
            Files.DeleteFilesByPath(inputFileName, tempOutputLeft, tempOutputRight);
        }
    }

    public async Task<MemoryStream> ConvertByteArrayToStreamAsync(byte[]? audioDataLeft, byte[]? audioDataRight = null, string recordType = "", string eventCode = "")
    {
        string tempFilePath = Path.GetTempFileName();
        try
        {
            await ConvertByteArrayToFileAsync(audioDataLeft, audioDataRight, tempFilePath, recordType, eventCode);
            using var fileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read);
            var result = new MemoryStream();
            await fileStream.CopyToAsync(result);
            result.Position = 0;
            return result;
        }
        finally
        {
            Files.DeleteFilesByPath(tempFilePath);
        }
    }

    public async Task ConvertByteArrayToFileAsync(byte[]? audioDataLeft, byte[]? audioDataRight, string audioFilePath, string recordType = "", string eventCode = "")
    {
        if (audioDataLeft == null) throw new ArgumentNullException(nameof(audioDataLeft));
        if (!_codecs.Contains(eventCode)) return;

        // Записываем бинарные данные во временные файлы (input)
        string? tempInputLeft = await WriteTempFileAsync(audioDataLeft, "_left.bin");
        string? tempInputRight = audioDataRight?.Length > 0 ? await WriteTempFileAsync(audioDataRight, "_right.bin") : null;

        // Файлы аудио (обязательно 2 даже для одного канала)
        string? tempOutputLeft = GetTempFilePath("_left.wav");
        string? tempOutputRight = GetTempFilePath("_right.wav");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(audioFilePath)!);

            //Записываем выходные аудиофайлы
            await RunDecoderAsync(
                eventCode: string.IsNullOrEmpty(eventCode) ? "PCMA" : eventCode,
                inputFileLeft: tempInputLeft,
                outputFileLeft: tempOutputLeft,
                inputFileRight: tempInputRight,
                outputFileRight: tempOutputRight
                );

            if (!File.Exists(tempOutputLeft) && !File.Exists(tempOutputRight))
                throw new Exception("Декодер не создал выходные файлы");

            // Читаем и объединяем аудиоданные
            byte[] leftData = await File.ReadAllBytesAsync(tempOutputLeft);
            if (audioDataRight?.Length > 0)
            {
                byte[] rightData = await File.ReadAllBytesAsync(tempOutputRight);
                var mergedWav = MergeWavChannels(leftData, rightData); // Создаем объединенный WAV
                await File.WriteAllBytesAsync(audioFilePath, mergedWav);
            }
            else
            {
                await File.WriteAllBytesAsync(audioFilePath, leftData);
            }

            var status = File.Exists(audioFilePath) ? "SUCCESS!!!" : "FAILED";
            Console.WriteLine($"DecoderConverter ConvertByteArrayToFileAsync => {status}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Decoder conversion error: {ex.Message}");
            throw; // Перебрасываем исключение для обработки в вызывающем коде
        }
        finally
        {
            Files.DeleteFilesByPath(tempInputLeft, tempInputRight, tempOutputLeft, tempOutputRight);
        }
    }

    public async Task<(int Duration, byte[] Left, byte[] Right)> ConvertFileToByteArrayAsync(string inputFileName)
    {
        var tempOutputLeft = GetTempFilePath(".wav");
        var tempOutputRight = GetTempFilePath(".wav");
        try
        {
            await RunDecoderAsync(
                eventCode: "PCMA",
                inputFileLeft: inputFileName,
                outputFileLeft: tempOutputLeft,
                outputFileRight: tempOutputRight,
                inputFileRight: null
            );

            (var duration, var channels) = await AudioInfo.AnalyzeAudioAsync(inputFileName);//GetAudioDurationAsync(inputFileName);

            byte[]? leftData = await ReadTempFileAsync(tempOutputLeft);
            byte[]? rightData = null;
            if (channels > 1)
            {
                rightData = await ReadTempFileAsync(tempOutputLeft);
            }

            return (duration, leftData, rightData);
        }
        finally
        {
            Files.DeleteFilesByPath(inputFileName, tempOutputLeft, tempOutputRight);
        }
    }

    private async Task RunDecoderAsync(
        string eventCode,
        string inputFileLeft,
        string inputFileRight,
        string outputFileLeft,
        string outputFileRight
        )
    {
        await _semaphoreDecoder.WaitAsync();
        try
        {
            string arguments = $"-c_dir \"{_codecPath}\" -c \"{eventCode}\" -f \"{inputFileLeft}\" \"{outputFileLeft}\" -r \"{inputFileLeft}\" \"{outputFileRight}\"";

            if (inputFileRight != null)
                arguments = $"-c_dir \"{_codecPath}\" -c \"{eventCode}\" -f \"{inputFileLeft}\" \"{outputFileLeft}\" -r \"{inputFileRight}\" \"{outputFileRight}\"";


            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _decoderPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            var tcs = new TaskCompletionSource<bool>();
            process.EnableRaisingEvents = true; // Включить события завершения
            process.Exited += (s, e) => tcs.SetResult(true);

            Console.WriteLine(arguments);

            process.Start();
            // Начать асинхронное чтение ошибок и вывода, чтобы избежать блокировки
            string errorOutput = await process.StandardError.ReadToEndAsync();
            string standardOutput = await process.StandardOutput.ReadToEndAsync();

            await tcs.Task; // Ожидание завершения процесса
            Console.WriteLine("RunDecoderAsync is FINISHED!!!");

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Decoder failed: {await process.StandardError.ReadToEndAsync()}");
        }
        finally
        {
            _semaphoreDecoder.Release();
        }
    }

    private async Task<byte[]> ReadTempFileAsync(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        var ms = new MemoryStream();
        await fs.CopyToAsync(ms);
        return ms.ToArray();
    }

    private async Task<string> WriteTempFileAsync(byte[] data, string suffix)
    {
        if (data == null) return null;

        var tempPath = GetTempFilePath(suffix);
        await File.WriteAllBytesAsync(tempPath, data);
        return tempPath;
    }

    private async Task<MemoryStream> FileToMemoryStreamAsync(string path)
    {
        if (!File.Exists(path)) Console.WriteLine("FileToMemoryStreamAsync => File не существует (DecoderConverter)");
        
        var ms = new MemoryStream();
        using (var fs = new FileStream(path, FileMode.Open))
            await fs.CopyToAsync(ms);

        ms.Position = 0;
        return ms;
    }

    private string GetTempFilePath(string extension)
    {
        return Path.Combine(_tempDirectory, $"{Guid.NewGuid()}{extension}");
    }

    private static byte[] MergeWavChannels(byte[] leftChannel, byte[] rightChannel)
    {
        const int headerSize = WavHeaderSize; // 44

        // Извлекаем данные без заголовков (как в оригинале)
        var leftData = new byte[leftChannel.Length - headerSize];
        var rightData = new byte[rightChannel.Length - headerSize];
        Buffer.BlockCopy(leftChannel, headerSize, leftData, 0, leftData.Length);
        Buffer.BlockCopy(rightChannel, headerSize, rightData, 0, rightData.Length);

        // Создаем базовый заголовок (как в оригинале)
        var result = new List<byte> {
        82, 73, 70, 70, 0, 0, 0, 0, 87, 65, 86, 69, 102, 109, 116, 32, 16, 0, 0, 0,
        1, 0, 2, 0, 64, 31, 0, 0, 0, 125, 0, 0, 4, 0, 16, 0, 100, 97, 116, 97, 0, 0, 0, 0
    };

        // Объединяем данные (исправленная версия)
        for (int j = 0; j < leftData.Length; j += 2)
        {
            // Проверка границ для правого канала
            if (j + 1 >= leftData.Length) break;
            result.Add(leftData[j]);
            result.Add(leftData[j + 1]);

            if (j + 1 >= rightData.Length)
            {
                // Добавляем тишину, если правый канал короче
                result.Add(0);
                result.Add(0);
            }
            else
            {
                result.Add(rightData[j]);
                result.Add(rightData[j + 1]);
            }
        }

        // Рассчет размеров (как в оригинале)
        int len = result.Count - 44;
        int len1 = result.Count - 8;

        // Обновление заголовка
        result[4] = (byte)(len1 & 0xff);
        result[5] = (byte)((len1 >> 8) & 0xff);
        result[6] = (byte)((len1 >> 16) & 0xff);
        result[7] = (byte)((len1 >> 24) & 0xff);

        result[40] = (byte)(len & 0xff);
        result[41] = (byte)((len >> 8) & 0xff);
        result[42] = (byte)((len >> 16) & 0xff);
        result[43] = (byte)((len >> 24) & 0xff);

        return result.ToArray();
    }

}
