using NAudio.Wave.SampleProviders;
using NAudio.Wave;
using static System.Net.Mime.MediaTypeNames;

namespace Shield.Estimator.Business.AudioConverterServices.NAudio;

public static class NAudioAudioToMemoryStream
{
    public static async Task<MemoryStream> NAudioFileToStreamAsync(string inputFileName)
    {
        using var fileStream = new FileStream(inputFileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var wavStream = new MemoryStream();

        await Task.Run(() =>
        {
            ISampleProvider sampleProvider;
            if (Path.GetExtension(inputFileName).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new Mp3FileReader(fileStream);
                sampleProvider = reader.ToSampleProvider();
            }
            else if (Path.GetExtension(inputFileName).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new WaveFileReader(fileStream);
                sampleProvider = reader.ToSampleProvider();
            }
            else
            {
                using var reader = new AudioFileReader(inputFileName);
                sampleProvider = reader.ToSampleProvider();
            }

            ISampleProvider resampler; //var resampler = new WdlResamplingSampleProvider(sampleProvider, 16000);
            if (sampleProvider.WaveFormat.SampleRate != 16000)
            {
                resampler = new WdlResamplingSampleProvider(sampleProvider, 16000);
            }
            else
            {
                resampler = sampleProvider;
            }
            WaveFileWriter.WriteWavFileToStream(wavStream, resampler.ToWaveProvider16());
        });
        wavStream.Seek(0, SeekOrigin.Begin);
        return wavStream;

    }
    //AudioFileReader принимает строку - путь к файлу
    //audioDataLeft и audioDataRight - уже разделенные из одного стерео на моно 
    public static async Task<MemoryStream> NAudioByteArrayToStreamAsync(byte[] audioDataLeft, byte[] audioDataRight = null, string recordType = "")
    {
        var outputStream = new MemoryStream();

        string fileLeft = $@"C:\temp\{Guid.NewGuid().ToString()}.tmp";
        string fileRight = $@"C:\temp\{Guid.NewGuid().ToString()}.tmp";
        await File.WriteAllBytesAsync(fileLeft, audioDataLeft);
        if (audioDataRight != null)
            await File.WriteAllBytesAsync(fileRight, audioDataRight);

        await Task.Run(() =>
        {
            ISampleProvider sampleProvider;

            using (var memoryStream = new MemoryStream(audioDataLeft))
            {
                if (audioDataRight == null)
                {
                    // Mono audio
                    using var readerLeft = new AudioFileReader(fileLeft);
                    sampleProvider = readerLeft.ToSampleProvider();
                }
                else
                {
                    // Stereo audio
                    using var readerLeft = new AudioFileReader(fileLeft);
                    using var readerRight = new AudioFileReader(fileRight);

                    var leftChannel = readerLeft.ToSampleProvider();
                    var rightChannel = readerRight.ToSampleProvider();
                    sampleProvider = new MultiplexingSampleProvider(new[] { leftChannel, rightChannel }, 2);
                }
            }
            ISampleProvider resampler;

            if (sampleProvider.WaveFormat.SampleRate != 16000)
            {
                resampler = new WdlResamplingSampleProvider(sampleProvider, 16000);
            }
            else
            {
                resampler = sampleProvider;
            }

            WaveFileWriter.WriteWavFileToStream(outputStream, resampler.ToWaveProvider16());
        });

        outputStream.Seek(0, SeekOrigin.Begin);
        return outputStream;
    }
}
