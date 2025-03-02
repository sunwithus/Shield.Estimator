//WhisperNetService.cs

using Shield.Estimator.Business.Options.WhisperOptions;
using Microsoft.Extensions.Options;

using Whisper.net.LibraryLoader;
using Whisper.net;
using Whisper.net.Logger;
using System.Text;
using Shield.Estimator.Business.AudioConverterServices;
using Whisper.net.Wave;
using Microsoft.Extensions.Logging;
using System.IO;


namespace Shield.Estimator.Business.Services;

public interface IWhisperNetService
{
    Task<string> TranscribeAudio(string audioFilePath);
}

public sealed class WaveData : IDisposable
{
    public MemoryStream Stream { get; }
    public WaveParser Parser { get; }

    public WaveData(MemoryStream stream, WaveParser parser)
    {
        Stream = stream;
        Parser = parser;
    }

    public void Dispose()
    {
        Stream?.Dispose();
        // Если WaveParser реализует IDisposable, добавить:
        // (Parser as IDisposable)?.Dispose();
    }
}

public class WhisperNetService : IDisposable
{
    private readonly IOptions<WhisperNetOptions> _options;
    private readonly string _modelFileName;
    private readonly AudioConverterFactory _converterFactory;
    private readonly WhisperFactory _whisperFactory;
    private readonly IWhisperProcessorBuilder _processorBuilder;
    private readonly ILogger<WhisperNetService> _logger;

    public WhisperNetService(
        IOptions<WhisperNetOptions> options,
        AudioConverterFactory converterFactory,
        ILogger<WhisperNetService> logger,
        IWhisperProcessorBuilder processorBuilder)
    {
        
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _converterFactory = converterFactory ?? throw new ArgumentNullException(nameof(_converterFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processorBuilder = processorBuilder ?? throw new ArgumentNullException(nameof(processorBuilder));

        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cuda, RuntimeLibrary.Cpu];

        _modelFileName = _options.Value.ModelPath;

        InitializeRuntime();
        _whisperFactory = WhisperFactory.FromPath(_options.Value.ModelPath);
    }
    private void InitializeRuntime()
    {
        //RuntimeOptions.RuntimeLibraryOrder = _options.RuntimeLibraries;
        //LogProvider.AddLogger(LogHandler);
    }
    private void LogHandler(LogLevel level, string message)
    {
        var logLevel = level switch
        {
            LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
        _logger.Log(logLevel, "Whisper.NET: {Message}", message);
    }

    public async Task<string> TranscribeAudio(string audioFilePath)
    {
        try
        {
            using var waveData = await GetWaveStream(audioFilePath);
            var processor = _processorBuilder.Build();
            return await ProcessAudio(waveData.Stream, processor, waveData.Parser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio transcription failed");
            throw;
        }
    }

    private async Task<(MemoryStream Stream, WaveParser Parser)> GetWaveStream(string audioFilePath)
    {
        foreach (var converterType in Enum.GetValues<ConverterType>())
        {
            MemoryStream stream = null;
            try
            {
                var converter = _converterFactory.CreateConverter(converterType);
                stream = await converter.ConvertFileToStreamAsync(audioFilePath);

                if (stream.Length > 0)
                {
                    _logger.LogInformation("{Converter} conversion successful", converterType);
                    var parser = new WaveParser(stream);
                    await parser.InitializeAsync();
                    return (stream, parser);
                }
            }
            catch (Exception ex)
            {
                stream?.Dispose();
                _logger.LogWarning(ex, "{Converter} conversion failed", converterType);
            }
        }

        throw new InvalidOperationException("All audio conversions failed");
    }

    private async Task<string> ProcessAudio(MemoryStream waveStream, WhisperProcessor processor, WaveParser parser)
    {
        var resultBuilder = new TranscriptionResultBuilder(parser.Channels);
        var samples = await parser.GetAvgSamplesAsync(CancellationToken.None);

        await foreach (var segment in processor.ProcessAsync(samples))
        {
            var maxEnergyChannel = await CalculateMaxEnergyChannel(waveStream, parser, segment);
            resultBuilder.AppendSegment(segment.Text, maxEnergyChannel);
        }

        return resultBuilder.Build();
    }

    private async Task<int> CalculateMaxEnergyChannel(MemoryStream stream, WaveParser parser, SegmentData segment)
    {
        var (startSample, endSample) = CalculateSampleRange(segment, parser.SampleRate);
        var buffer = await ReadWaveData(stream, parser, startSample, endSample);

        return EnergyAnalyzer.FindMaxEnergyChannel(buffer, parser.Channels);
    }

    private (long Start, long End) CalculateSampleRange(SegmentData segment, uint sampleRate) => (
        (long)(segment.Start.TotalMilliseconds * sampleRate / 1000),
        (long)(segment.End.TotalMilliseconds * sampleRate / 1000)
    );

    private async Task<short[]> ReadWaveData(MemoryStream stream, WaveParser parser, long startSample, long endSample)
    {
        var frameSize = parser.BitsPerSample / 8 * parser.Channels;
        var bufferSize = (int)(endSample - startSample) * frameSize;
        var readBuffer = new byte[bufferSize];

        stream.Position = parser.DataChunkPosition + startSample * frameSize;
        await stream.ReadAsync(readBuffer.AsMemory());

        return BufferConverter.ConvertToShorts(readBuffer);
    }

    public void Dispose()
    {
        _whisperFactory?.Dispose();
        GC.SuppressFinalize(this);
    }

    ////////////////////////////////////////////////
    ///
        public async Task<string> TranscribeAudio2(string audioFilePath)
        {
            StringBuilder sb = new();
            //var whisperFactory = WhisperFactory.FromPath(_modelFileName);
            var whisperFactory = WhisperFactory.FromPath("D:\\_Ai\\Whisper\\ggml-base.bin");
            Console.WriteLine("whisperFactory");

            //.WithSegmentEventHandler((segment) => {Console.WriteLine($"{segment.Start}->{segment.End}: {segment.Text}");})
            await using var processor = whisperFactory.CreateBuilder().WithLanguage("auto").Build();

            MemoryStream wavStream = null;

            try
            {
                var converter = _converterFactory.CreateConverter(ConverterType.NAudio);
                wavStream = await converter.ConvertFileToStreamAsync(audioFilePath);
                Console.WriteLine("NAudioConverter SUCCESS");
            }
            catch (Exception ex)
            {
                Console.WriteLine("NAudioConverter Error => " + ex.Message);
            }

            if (wavStream == null || wavStream.Length == 0)
            {
                try
                {
                    var converter = _converterFactory.CreateConverter(ConverterType.FFMpeg);
                    wavStream = await converter.ConvertFileToStreamAsync(audioFilePath);
                    Console.WriteLine("FFMpegConverter SUCCESS");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return ex.Message;
                }
            }
            Console.WriteLine("wavStream");


            var waveParser = new WaveParser(wavStream);
            await waveParser.InitializeAsync();
            var channels = waveParser.Channels;
            var sampleRate = waveParser.SampleRate;
            var bitsPerSample = waveParser.BitsPerSample;
            var headerSize = waveParser.DataChunkPosition;
            var frameSize = bitsPerSample / 8 * channels;

            var samples = await waveParser.GetAvgSamplesAsync(CancellationToken.None);

            Console.WriteLine("samples");

            int PrevMaxEnergy = 555;

            await foreach (var result in processor.ProcessAsync(samples))
            {
                // Get the wave position for the specified time interval
                var startSample = (long)result.Start.TotalMilliseconds * sampleRate / 1000;
                var endSample = (long)result.End.TotalMilliseconds * sampleRate / 1000;

                // Calculate buffer size.
                var bufferSize = (int)(endSample - startSample) * frameSize;
                var readBuffer = new byte[bufferSize];

                // Set fileStream position.
                wavStream.Position = headerSize + startSample * frameSize;

                // Read the wave data for the specified time interval, into the readBuffer.
                var read = await wavStream.ReadAsync(readBuffer.AsMemory());

                // Process the readBuffer and convert to shorts.
                var buffer = new short[read / 2];
                for (var i = 0; i < buffer.Length; i++)
                {
                    // Handle endianess manually and convert bytes to Int16.
                    buffer[i] = BitConverter.IsLittleEndian
                        ? (short)(readBuffer[i * 2] | (readBuffer[i * 2 + 1] << 8))
                        : (short)((readBuffer[i * 2] << 8) | readBuffer[i * 2 + 1]);
                }

                // Iterate in the wave data to calculate total energy in each channel, and find the channel with the maximum energy.
                var energy = new double[channels];
                var maxEnergy = 0d;
                var maxEnergyChannel = 0;
                for (var i = 0; i < buffer.Length; i++)
                {
                    var channel = i % channels;
                    energy[channel] += Math.Pow(buffer[i], 2);

                    if (energy[channel] > maxEnergy)
                    {
                        maxEnergy = energy[channel];
                        maxEnergyChannel = channel;
                    }
                }
                Console.WriteLine($"{result.Start}->{result.End}: {result.Text}. Max energy in channel: {maxEnergyChannel}");
                if (PrevMaxEnergy == maxEnergyChannel)
                {
                    sb.Append($"{result.Text} ");
                }
                else
                {
                    sb.Append($"\nСобеседник {maxEnergyChannel + 1}: {result.Text} ");
                }
                PrevMaxEnergy = maxEnergyChannel;

            }


            // This section processes the audio file and prints the results (start time, end time and text) to the console.
            /*await foreach (var result in processor.ProcessAsync(wavStream))
            {
                Console.WriteLine($"{result.Start}->{result.End}: {result.Text}");
                sb.Append( result.Text + "\n");
            }
            */
            whisperFactory.Dispose(); ;
            return sb.ToString();
        }



        public static async Task RunInParallel(string name, string wavFileName, WhisperFactory whisperFactory)
        {

            // This section creates the processor object which is used to process the audio file, it uses language `auto` to detect the language of the audio file.
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguage("auto")
                .Build();

            using var fileStream = File.OpenRead(wavFileName);

            // This section processes the audio file and prints the results (start time, end time and text) to the console.
            await foreach (var result in processor.ProcessAsync(fileStream))
            {
                Console.WriteLine($"{name} =====> {result.Start}->{result.End}: {result.Text}");

                // Add some delay, otherwise we might get the results too fast
                await Task.Delay(1000);
            }
        }

        /*
        // This section creates the whisperFactory object which is used to create the processor object.
        using var whisperFactory = WhisperFactory.FromPath("ggml-base.bin");

        var task1 = Task.Run(() => RunInParallel("Task1", "kennedy.wav", whisperFactory));
        await Task.Delay(1000);
        var task2 = Task.Run(() => RunInParallel("Task2", "kennedy.wav", whisperFactory));

        // We wait both tasks and we'll see that the results are interleaved
        await Task.WhenAll(task1, task2);

    */
    

}

public class WhisperProcessorBuilder : IWhisperProcessorBuilder
{
    private readonly WhisperFactory _factory;
    private readonly string _language;

    public WhisperProcessorBuilder(WhisperFactory factory, string language = "auto")
    {
        _factory = factory;
        _language = language;
    }

    public WhisperProcessor Build() => _factory
        .CreateBuilder()
        .WithLanguage(_language)
        .Build();
}

// Вспомогательные классы
public class TranscriptionResultBuilder
{
    private readonly StringBuilder _sb = new();
    private int? _previousChannel;
    private readonly int _totalChannels;

    public TranscriptionResultBuilder(int totalChannels)
    {
        _totalChannels = totalChannels;
    }

    public void AppendSegment(string text, int currentChannel)
    {
        if (_previousChannel == currentChannel)
        {
            _sb.Append($"{text} ");
        }
        else
        {
            var speakerLabel = currentChannel < _totalChannels
                ? $"Собеседник {currentChannel + 1}"
                : "Неизвестный";
            _sb.Append($"\n{speakerLabel}: {text} ");
        }
        _previousChannel = currentChannel;
    }

    public string Build() => _sb.ToString().Trim();
}

public static class BufferConverter
{
    public static short[] ConvertToShorts(byte[] buffer)
    {
        var result = new short[buffer.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = BitConverter.ToInt16(buffer, i * 2);
        }
        return result;
    }
}

public static class EnergyAnalyzer
{
    public static int FindMaxEnergyChannel(short[] buffer, int channels)
    {
        var energy = new double[channels];
        for (var i = 0; i < buffer.Length; i++)
        {
            energy[i % channels] += Math.Pow(buffer[i], 2);
        }
        return Array.IndexOf(energy, energy.Max());
    }
}

public interface IWhisperProcessorBuilder
{
    WhisperProcessor Build();
}

