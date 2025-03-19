using System.Diagnostics;
using System.Text;
using FFMpegCore;
using Microsoft.Extensions.Options;
using Shield.AudioConverter.Options;

namespace Shield.AudioConverter.AudioConverterServices.Decoder;

/// <summary>
/// Сервис для конвертации аудиофайлов с использованием decoder.exe.
/// </summary>
public class DecoderConverter : IAudioConverter
{
    private readonly string _decoderPath;
    private readonly string _codecPath;
    private readonly string _tempDirectory;
    private static readonly SemaphoreSlim _semaphore2 = new SemaphoreSlim(1, 1);

    public DecoderConverter(IOptions<AudioConverterOptions> options)
    {
        var assemblyLocation = Path.GetDirectoryName(typeof(DecoderConverter).Assembly.Location);
        _decoderPath = Path.Combine(assemblyLocation, "decoder", "decoder.exe");
        _codecPath = Path.Combine(assemblyLocation, "suppdll");
        _tempDirectory = options?.Value.TempDirectory ?? Path.Combine(Path.GetTempPath(), "tempDecoder");

        Directory.CreateDirectory(_tempDirectory);
    }

    public async Task<MemoryStream> ConvertFileToStreamAsync(string inputFileName)
    {
        await _semaphore2.WaitAsync();
        var tempOutputLeft = GetTempFilePath(".wav");
        var tempOutputRight = GetTempFilePath(".wav");
        try
        {

            await RunDecoderAsync(
                eventCode: "PCM-128", // Default codec
                inputFileLeft: inputFileName,
                outputFileLeft: tempOutputLeft,
                outputFileRight: tempOutputRight,
                inputFileRight: null
            );

            return await FileToMemoryStreamAsync(tempOutputLeft);
        }
        finally
        {
            if (File.Exists(tempOutputRight)) File.Delete(tempOutputRight);
            _semaphore2.Release();
        }
    }

    public async Task<MemoryStream> ConvertByteArrayToStreamAsync(byte[] audioDataLeft, byte[] audioDataRight = null)
    {
        await _semaphore2.WaitAsync();
        try
        {
            var tempInputLeft = await WriteTempFileAsync(audioDataLeft, "_left.bin");
            var tempInputRight = audioDataRight != null
                ? await WriteTempFileAsync(audioDataRight, "_right.bin")
                : tempInputLeft;
            var tempOutputLeft = GetTempFilePath(".wav");
            var tempOutputRight = GetTempFilePath(".wav");

            //как (сами?) очищаются файлы в папке C:\Users\sharp\AppData\Local\Temp ???

            await RunDecoderAsync(eventCode: "PCM-128", inputFileLeft: tempInputLeft, outputFileLeft: tempOutputLeft, inputFileRight: tempInputRight, outputFileRight: tempOutputRight);

            // TODO SUM both strims (now is one)
            return await FileToMemoryStreamAsync(tempOutputLeft);
        }
        finally
        {
            //if (File.Exists(tempOutputRight)) File.Delete(tempOutputRight);
            _semaphore2.Release();
        }
    }

    public async Task ConvertByteArrayToFileAsync(byte[] audioDataLeft,byte[] audioDataRight,string audioFilePath,string recordType = "",string eventCode = "")
    {
        if (audioDataLeft == null) throw new ArgumentNullException(nameof(audioDataLeft));

        await _semaphore2.WaitAsync();
        try
        {
            var tempInputLeft = await WriteTempFileAsync(audioDataLeft, "_left.bin");
            var tempInputRight = await WriteTempFileAsync(audioDataRight, "_right.bin");
            var tempOutput = GetTempFilePath(".wav");

            await RunDecoderAsync(
                eventCode: string.IsNullOrEmpty(eventCode) ? "PCM-128" : eventCode,
                inputFileLeft: tempInputLeft,
                outputFileLeft: tempOutput,
                outputFileRight: tempOutput +"",
                inputFileRight: tempInputRight
            );

            Directory.CreateDirectory(Path.GetDirectoryName(audioFilePath));
            File.Copy(tempOutput, audioFilePath, true);
        }
        finally
        {
            _semaphore2.Release();
        }
    }

    public async Task<(int Duration, byte[] Left, byte[] Right)> ConvertFileToByteArrayAsync(string inputFileName)
    {
        await _semaphore2.WaitAsync();
        try
        {
            var tempLeft = GetTempFilePath(".bin");
            var tempRight = GetTempFilePath(".bin");
            var tempOutput = GetTempFilePath(".wav");

            await RunDecoderAsync(
                eventCode: "PCM-128",
                inputFileLeft: inputFileName,
                outputFileLeft: tempOutput,
                outputFileRight: tempOutput,
                inputFileRight: null
            );

            var duration = await GetAudioDurationAsync(tempOutput);
            var leftData = await ReadTempFileAsync(tempLeft);
            var rightData = File.Exists(tempRight) ? await ReadTempFileAsync(tempRight) : null;

            return (duration, leftData, rightData);
        }
        finally
        {
            _semaphore2.Release();
        }
    }

    private async Task RunDecoderAsync(
        string eventCode,
        string inputFileLeft,
        string outputFileLeft,
        string outputFileRight,
        string inputFileRight = null)
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
            RedirectStandardError = true
        };

        var tcs = new TaskCompletionSource<bool>();
        process.Exited += (s, e) => tcs.SetResult(true);


        Console.WriteLine(arguments);
        Console.WriteLine("before Start");

        process.Start();
        await tcs.Task;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Decoder failed: {await process.StandardError.ReadToEndAsync()}");
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
        var ms = new MemoryStream();
        using (var fs = new FileStream(path, FileMode.Open))
            await fs.CopyToAsync(ms);

        ms.Position = 0;
        return ms;
    }

    private async Task<int> GetAudioDurationAsync(string filePath)
    {
        var mediaInfo = await FFProbe.AnalyseAsync(filePath);
        int duration = (int)(mediaInfo.PrimaryAudioStream?.Duration.TotalSeconds ?? 0);
        int channels = mediaInfo.PrimaryAudioStream?.Channels ?? 0;

        return duration; // Заглушка для примера
    }

    private string GetTempFilePath(string extension)
    {
        return Path.Combine(_tempDirectory, $"{Guid.NewGuid()}{extension}");
    }

}
