using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Microsoft.Extensions.Options;
using Shield.Estimator.Business.Options;
using System;
using System.IO;
using System.Threading.Tasks;
using Shield.Estimator.Business.AudioConverterServices;

namespace Shield.Estimator.Business.AudioConverterServices.NAudio;

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

    public async Task<MemoryStream> ConvertByteArrayToStreamAsync(byte[] audioDataLeft, byte[] audioDataRight = null)
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
        var tempFile = Path.Combine(_tempDirectory, $"{Guid.NewGuid()}.tmp");
        await File.WriteAllBytesAsync(tempFile, data);
        return tempFile;
    }
}
