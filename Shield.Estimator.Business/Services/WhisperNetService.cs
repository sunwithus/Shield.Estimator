//WhisperNetService.cs

using Shield.Estimator.Business;
using Shield.Estimator.Business.Exceptions;
using Shield.Estimator.Business.Options.WhisperOptions;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using AutoMapper;

using Whisper.net.LibraryLoader;
using Whisper.net;


using Whisper.net.Logger;
using System.Text;
using Shield.Estimator.Business.AudioConverterServices.NAudio;
using Shield.Estimator.Business.AudioConverterServices.FFMpeg;

namespace Shield.Estimator.Business.Services;
public class WhisperNetService
{
    private readonly IOptions<WhisperNetOptions> _options;
    private readonly string _modelFileName;
    public WhisperNetService(IOptions<WhisperNetOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cuda, RuntimeLibrary.Cpu];

        using var whisperLogger = LogProvider.AddLogger((level, message) =>
        {
            Console.WriteLine($"{level}: {message}");
        });

        _modelFileName = _options.Value.ModelPath;
    }

    public async Task<string> TranscribeAudio(string audioFilePath)
    {
        StringBuilder sb = new();
        using var whisperFactory = WhisperFactory.FromPath(_modelFileName);
        //using var whisperFactory = WhisperFactory.FromPath("D:\\AiModels\\Whisper\\ggml-tiny-mongol.bin");
        Console.WriteLine("whisperFactory");

        //.WithSegmentEventHandler((segment) => {Console.WriteLine($"{segment.Start}->{segment.End}: {segment.Text}");})
        using var processor = whisperFactory.CreateBuilder().WithLanguage("auto").Build();

        bool success = false;
        var wavStream = new MemoryStream();

        try
        {
            wavStream = await NAudioAudioToMemoryStream.NAudioFileToStreamAsync(audioFilePath);
            success = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("NAudioConverter Error => " + ex.Message);
        }

        if(!success)
        {
            var ffmpeg = new FFMpegFileToByteArray();
            wavStream = await ffmpeg.ProcessAudioAsync(audioFilePath);
        }

        Console.WriteLine("wavStream");

        // This section processes the audio file and prints the results (start time, end time and text) to the console.
        await foreach (var result in processor.ProcessAsync(wavStream/*fileStream*/))
        {
            Console.WriteLine($"{result.Start}->{result.End}: {result.Text}");
            sb.Append( result.Text + "\n");
        }
        return sb.ToString();
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


}
