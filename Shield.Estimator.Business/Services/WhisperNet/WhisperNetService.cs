//WhisperNetService.cs

using Shield.Estimator.Business.Options.WhisperOptions;
using Microsoft.Extensions.Options;
using Whisper.net.LibraryLoader;
using Whisper.net;
using Whisper.net.Logger;
using Shield.Estimator.Business.AudioConverterServices;
using Whisper.net.Wave;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
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
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cuda, RuntimeLibrary.Cpu];
        //using var whisperLogger = LogProvider.AddConsoleLogging(WhisperLogLevel.Info);


    }

    public async Task<string> TranscribeAudio(string audioFilePath, string language = "auto")
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var targetModelPath = GetModelPath(language);
            if (NeedReloadModel(targetModelPath))
            {
                _logger.LogWarning("Необходимо загрузить/сменить модель!");
                await LoadModel(targetModelPath);
                _logger.LogInformation("Model load: {Time} ms", sw.ElapsedMilliseconds);
            }

            using var waveData = await GetWaveStream(audioFilePath);
            _logger.LogInformation("Audio convert: {Time} ms", sw.ElapsedMilliseconds);
            var processor = CreateProcessor(language); //var processor = _whisperFactory.CreateBuilder().WithLanguage("auto").Build();
            _logger.LogInformation("Processor init: {Time} ms", sw.ElapsedMilliseconds);
            var result = await ProcessWhisperTranscription(waveData, processor);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio transcription failed");
            throw;
        }
    }

    private string GetModelPath(string language)
    {
        if (!string.IsNullOrEmpty(language)
            && _options.Value.CustomModels.TryGetValue(language, out var customModel)
            && File.Exists(customModel))
        {
            return customModel;
        }

        if (!File.Exists(_options.Value.DefaultModelPath))
        {
            throw new FileNotFoundException("Default Whisper model not found", _options.Value.DefaultModelPath);
        }

        return _options.Value.DefaultModelPath;
    }

    private bool NeedReloadModel(string targetModelPath)
    {
        return _whisperFactory == null || _loadedModelPath != targetModelPath || !File.Exists(_loadedModelPath);
    }

    private async Task LoadModel(string modelPath)
    {
        if (_whisperFactory != null)
        {
            _logger.LogInformation("Unloading previous model: {Path}", _loadedModelPath);
            _whisperFactory.Dispose();
            await Task.Delay(100); // Даем время на выгрузку
        }

        _logger.LogInformation("Loading Whisper model: {Path}", modelPath);
        _whisperFactory = WhisperFactory.FromPath(modelPath);
        _loadedModelPath = modelPath;
    }

    private WhisperProcessor CreateProcessor(string language)
    {
        var builder = _whisperFactory.CreateBuilder()
            //.WithPrintTimestamps(false)
            .WithLanguage(GetLanguageCode(language));
        /*
        if (_options.Value.Diarization)
        {
            builder.WithDiarization();
        }
        */
        return builder.Build();
    }

    private string GetLanguageCode(string language)
    {
        return !string.IsNullOrEmpty(language) && language.Length == 2 ? language : "auto";
    }

    private async Task<WaveData> GetWaveStream(string audioFilePath)
    {

        // Вначале NAudio, если не удалось то FFMpeg
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
                    return new WaveData(stream, parser); // Возвращаем экземпляр WaveData
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

    private async Task<string> ProcessWhisperTranscription(WaveData waveData, WhisperProcessor processor)
    {
        var resultBuilder = new TranscriptionStringBuilder(waveData.Parser.Channels);

        try
        {
            var samples = await waveData.Parser.GetAvgSamplesAsync(CancellationToken.None);

            await foreach (var segment in processor.ProcessAsync(samples))
            {
                var maxEnergyChannel = await CalculateMaxEnergyChannel(waveData, segment);
                resultBuilder.AppendSegment(segment.Text, maxEnergyChannel);
            }
        }
        finally
        {
            // Принудительная очистка памяти GPU
            await Task.Run(() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced));
        }

        return resultBuilder.Build();
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
    /*
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

    
    // This section creates the whisperFactory object which is used to create the processor object.
    using var whisperFactory = WhisperFactory.FromPath("ggml-base.bin");

    var task1 = Task.Run(() => RunInParallel("Task1", "kennedy.wav", whisperFactory));
    await Task.Delay(1000);
    var task2 = Task.Run(() => RunInParallel("Task2", "kennedy.wav", whisperFactory));

    // We wait both tasks and we'll see that the results are interleaved
    await Task.WhenAll(task1, task2);

*/
}

public class WhisperProcessorBuilder
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

        //.WithLanguageDetection()
        .Build();
}

