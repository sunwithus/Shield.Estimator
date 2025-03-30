//WhisperNetService.cs

using Shield.Estimator.Business.Options.WhisperOptions;
using Microsoft.Extensions.Options;
using Whisper.net.LibraryLoader;
using Whisper.net;
using Shield.AudioConverter.AudioConverterServices;
using Whisper.net.Wave;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Shield.Estimator.Business.Services.WhisperNet;

public class WhisperNetService : IDisposable
{
    private readonly IOptions<WhisperNetOptions> _options;
    private readonly AudioConverterFactory _converterFactory;
    private readonly ILogger<WhisperNetService> _logger;

    private WhisperFactory _whisperFactory;
    private string _loadedModelPath;

    public WhisperNetService(
        IOptions<WhisperNetOptions> options,
        AudioConverterFactory converterFactory,
        ILogger<WhisperNetService> logger)
    {

        _options = options ?? throw new ArgumentNullException(nameof(options));
        _converterFactory = converterFactory ?? throw new ArgumentNullException(nameof(_converterFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        InitializeRuntime();
    }
    private void InitializeRuntime()
    {
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cuda, RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];
        //using var whisperLogger = LogProvider.AddConsoleLogging(WhisperLogLevel.Info);
    }


    public async Task<string> TranscribeAsync(string audioFilePath, string selectedModel, IProgress<string> progress = null, CancellationToken ct = default)
    {
        using var _ = _logger.BeginScope("Transcription for {File}", Path.GetFileName(audioFilePath));
        var sw = Stopwatch.StartNew();
        try
        {
            string text = string.Empty;

            if (_loadedModelPath != selectedModel)
            {
                progress?.Report("Загрузка модели...");
                Trace.TraceInformation("Загрузка модели...");
            }
            else
            {
                progress?.Report("Модель уже загружена.");
                Trace.TraceInformation("Модель уже загружена.");
            }

            //selectedModel ??= SelectModelPath(language);
            
            await EnsureModelLoaded(selectedModel);
            if (_loadedModelPath == selectedModel)
            {
                progress?.Report("Начало транскрибирования...");
                Trace.TraceInformation("Начало транскрибирования...");
            }
            else
            {
                progress?.Report("Модель загружена. Начало транскрибирования...");
            }

            using (var waveData = await ConvertAudioFile(audioFilePath))
            {
                using (var processor = CreateProcessor())
                {
                    text = await ProcessAudioTranscription(waveData, processor, sw, _options.Value.PrintTimestamps);
                    progress?.Report("Транскрибирование завершено.");
                }
    
            }
            return text;
        }
        
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio transcription failed");
            progress?.Report("Audio transcription failed" + ex.Message);
            Trace.TraceError($"Ошибка: {ex.Message}");
            throw;
        }
    }

    private string SelectModelPath(string language)
    {
        if (_options.Value.CustomModels.TryGetValue(language, out var customModel)
            && File.Exists(customModel))
        {
            return customModel;
        }

        var defaultPath = _options.Value.DefaultModelPath;
        return File.Exists(defaultPath)
            ? defaultPath
            : throw new FileNotFoundException("Default Whisper model not found", defaultPath);
    }


    private async Task EnsureModelLoaded(string modelPath)
    {
        if (_whisperFactory != null && _loadedModelPath == modelPath && !File.Exists(modelPath)) return;

        _whisperFactory?.Dispose();
        await using var modelStream = File.OpenRead(modelPath);

        _logger.LogInformation("Loading model: {Model}", modelPath);
        _whisperFactory = WhisperFactory.FromPath(modelPath);
        _loadedModelPath = modelPath;
    }

    private WhisperProcessor CreateProcessor()
    {
        var options = _options.Value;
        var builder = _whisperFactory!.CreateBuilder()
            
            .WithMaxLastTextTokens(options.MaxLastTextTokens)
            .WithOffset(options.Offset)
            .WithDuration(options.Duration)
            .WithLanguage(options.Language)
            
            .WithPrintTimestamps(options.PrintTimestamps)
            .WithTokenTimestampsThreshold(options.TokenTimestampsThreshold)
            .WithTokenTimestampsSumThreshold(options.TokenTimestampsSumThreshold)
            .WithMaxSegmentLength(options.MaxSegmentLength)
            .WithMaxTokensPerSegment(options.MaxTokensPerSegment)
            .WithTemperature(options.Temperature)
            .WithMaxInitialTs(options.MaxInitialTs)
            .WithLengthPenalty(options.LengthPenalty)
            .WithTemperatureInc(options.TemperatureInc)
            .WithEntropyThreshold(options.EntropyThreshold)
            .WithLogProbThreshold(options.LogProbThreshold)
            .WithNoSpeechThreshold(options.NoSpeechThreshold);

        if (options.Threads > 0) builder.WithThreads(options.Threads);
        if (options.UseTokenTimestamps) builder.WithTokenTimestamps();
        if (options.ComputeProbabilities) builder.WithProbabilities();
        if (options.Translate) builder.WithTranslate();
        if (options.NoContext) builder.WithNoContext();
        if (options.SingleSegment) builder.WithSingleSegment();
        if (options.SplitOnWord) builder.SplitOnWord();

        return builder.Build();
    }

    private async Task<WaveData> ConvertAudioFile(string audioFilePath)
    {
        foreach (var converterType in Enum.GetValues<ConverterType>())
        {
            try
            {
                var converter = _converterFactory.CreateConverter(converterType);
                var stream = await converter.ConvertFileToStreamAsync(audioFilePath);

                if (stream.Length == 0)
                {
                    stream.Dispose(); // Освобождаем пустой поток
                    continue;
                }

                var parser = new WaveParser(stream);
                await parser.InitializeAsync();
                return new WaveData(stream, parser);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Converter} conversion failed", converterType);
            }
        }
        throw new InvalidOperationException("All audio conversions failed");
    }

    private async Task<string> ProcessAudioTranscription(WaveData waveData, WhisperProcessor processor, Stopwatch sw, bool withTimestamps)
    {
        try
        {
            var samples = await waveData.Parser.GetAvgSamplesAsync();
            var resultBuilder = new TranscriptionStringBuilder(waveData.Parser.Channels);

            await foreach (var segment in processor.ProcessAsync(samples))
            {
                var channel = await CalculateMaxEnergyChannel(waveData, segment);
                resultBuilder.AppendSegment(segment.Text, channel, withTimestamps ? $"{segment.Start:hh\\:mm\\:ss\\.f}=>{segment.End:hh\\:mm\\:ss\\.f}" : null);
            }

            _logger.LogInformation("Completed audio processing");
            return resultBuilder.Build();
        }
        finally
        {
            processor.Dispose();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
        }
    }

    private async Task<int> CalculateMaxEnergyChannel(WaveData waveData, SegmentData segment)
    {
        var (startSample, endSample) = CalculateSampleRange(segment, waveData.Parser.SampleRate);
        var buffer = await ReadSegmentWaveData(waveData, startSample, endSample);

        return EnergyAnalyzer.FindMaxEnergyChannel(buffer, waveData.Parser.Channels);
    }

    private (long Start, long End) CalculateSampleRange(SegmentData segment, uint sampleRate) => (
        (long)(segment.Start.TotalMilliseconds * sampleRate / 1000),
        (long)(segment.End.TotalMilliseconds * sampleRate / 1000)
    );

    private async Task<short[]> ReadSegmentWaveData(WaveData waveData, long startSample, long endSample)
    {
        var frameSize = waveData.Parser.BitsPerSample / 8 * waveData.Parser.Channels;
        var bufferSize = (int)(endSample - startSample) * frameSize;
        var readBuffer = new byte[bufferSize];

        waveData.Stream.Position = waveData.Parser.DataChunkPosition + startSample * frameSize;
        await waveData.Stream.ReadAsync(readBuffer.AsMemory());

        return BufferConverter.ConvertToShorts(readBuffer);
    }

    public void Dispose()
    {
        _whisperFactory?.Dispose();
        GC.SuppressFinalize(this);
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
