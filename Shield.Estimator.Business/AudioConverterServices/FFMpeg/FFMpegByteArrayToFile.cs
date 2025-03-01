//DbToAudioConverter.cs
using FFMpegCore.Pipes;
using FFMpegCore;
using System.Diagnostics;
using System.Configuration;
using FFMpegCore.Enums;
using System.Threading.Channels;

namespace Shield.Estimator.Business.AudioConverterServices.FFMpeg
{
    public class FFMpegByteArrayToFile
    {
        /*
        public static async Task<bool> FFMpegDecoder(byte[] audioDataLeft, byte[] audioDataRight, string? recordType, string outputFilePath, IConfiguration conf)
        {
            try
            {
                if (audioDataLeft != null)
                {
                    if (recordType != null && conf.GetSection("AudioConverter:Codecs").Get<List<string>>().Contains(recordType))
                    {
                        Console.WriteLine("using decoder!!! recordType is " + recordType);
                        await UsingDecoderAsync(audioDataLeft, audioDataRight, outputFilePath, recordType, conf);
                        await Task.Delay(100); //возможно файл не успевает сохраниться, поэтому пауза
                    }
                    else
                    {
                        Console.WriteLine("using ffmpeg!!! recordType is " + recordType);
                        await UsingStreamAsync(audioDataLeft, audioDataRight, outputFilePath, conf);
                        await Task.Delay(100); //возможно файл не успевает сохраниться, поэтому пауза
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ошибка в методе FFMpeg: " + ex.Message);
                if (!File.Exists(outputFilePath)) Console.WriteLine("FFMpegDecoder не выполнил задачу, отсутструет файл => " + outputFilePath);
            }
            return File.Exists(outputFilePath);
        }

        public static async Task UsingStreamAsync(byte[] audioDataLeft, byte[] audioDataRight, string outputFilePath, IConfiguration conf)
        {
            using var streamLeft = new MemoryStream(audioDataLeft);
            using var streamRight = audioDataRight != null ? new MemoryStream(audioDataRight) : null;
            var ffmpegArgs = FFMpegArguments.FromPipeInput(new StreamPipeSource(streamLeft));
            string rightArgument = "";

            if (streamRight != null)
            {
                ffmpegArgs = ffmpegArgs.AddPipeInput(new StreamPipeSource(streamRight));
                rightArgument = "-filter_complex amix=inputs=2:duration=first:dropout_transition=2";
            }
            await ffmpegArgs
            .OutputToFile(outputFilePath, true, options => options
                .ForceFormat("wav")
                //.WithAudioCodec(AudioCodec.LibOpus) //Опус (Opus) Опус является одним из самых высококачественных.lossy аудио кодеков, поддерживаемых FFmpeg.Он известен своей высокой эффективностью и широкой поддержко
                .WithCustomArgument("-codec:a pcm_s16le -b:a 128k -ar 16000 -ac 1")
                .WithCustomArgument($"{rightArgument}")
            ).ProcessAsynchronously(true, new FFOptions { BinaryFolder = conf["PathToFFmpegExe"] });
            Console.WriteLine("UsingStreamAsync success!!! outputFilePath: " + outputFilePath);
        }

        public static async Task UsingStreamsMakeStereoAsync(byte[] audioDataLeft, byte[] audioDataRight, string outputFilePath, IConfiguration conf)
        {
            using var streamLeft = new MemoryStream(audioDataLeft);
            using var streamRight = audioDataRight != null ? new MemoryStream(audioDataRight) : null;
            var ffmpegArgs = FFMpegArguments.FromPipeInput(new StreamPipeSource(streamLeft));
            string rightArgument = "";

            if (streamRight != null)
            {
                ffmpegArgs = ffmpegArgs.AddPipeInput(new StreamPipeSource(streamRight));
                rightArgument = "-filter_complex amerge=inputs=2 -ac 2";
            }
            await ffmpegArgs
            .OutputToFile(outputFilePath, true, options => options
                .ForceFormat("wav")
                //.WithAudioCodec(AudioCodec.LibOpus) //Опус (Opus) Опус является одним из самых высококачественных.lossy аудио кодеков, поддерживаемых FFmpeg.Он известен своей высокой эффективностью и широкой поддержко
                .WithCustomArgument("-codec:a pcm_s16le -b:a 128k -ar 16000 ")
                .WithCustomArgument($"{rightArgument}")
            ).ProcessAsynchronously(true, new FFOptions { BinaryFolder = conf["PathToFFmpegExe"] });
            Console.WriteLine("UsingStreamAsync success!!! outputFilePath: " + outputFilePath);
        }

        public static async Task UsingFilesAsync(byte[] audioDataLeft, byte[] audioDataRight, string outputFilePath, IConfiguration conf)
        {
            var ramdomFileName = Path.GetRandomFileName();
            var ramdomFileNameWithPath = Path.Combine(Path.GetDirectoryName(outputFilePath), ramdomFileName);
            string fileNameLeft = ramdomFileNameWithPath + "_left";
            string fileNameRight = ramdomFileNameWithPath + "_right";
            string rightArgument = "";
            await File.WriteAllBytesAsync(fileNameLeft, audioDataLeft);
            var ffmpegArgs = FFMpegArguments.FromFileInput(fileNameLeft);

            if (audioDataRight != null)
            {
                await File.WriteAllBytesAsync(fileNameRight, audioDataRight);
                ffmpegArgs.AddFileInput(fileNameRight);
                rightArgument = "-filter_complex amix=inputs=2:duration=first:dropout_transition=2";
            }

            await ffmpegArgs
                .OutputToFile(outputFilePath, true, options => options
                    .ForceFormat("wav")
                    .WithCustomArgument("-codec:a pcm_s16le -b:a 128k -ar 16000 -ac 1")
                    .WithCustomArgument(rightArgument)
                ).ProcessAsynchronously(true, new FFOptions { BinaryFolder = conf["PathToFFmpegExe"] });

            Files.DeleteFilesByPath(fileNameLeft, fileNameRight);
            Console.WriteLine("UsingFilesAsync success!!! outputFilePath: " + outputFilePath);
        }

        public static async Task UsingDecoderAsync(byte[] audioDataLeft, byte[] audioDataRight, string outputFilePath, string recordType, IConfiguration conf)
        {
            string rightArgument = "-filter_complex amix=inputs=2:duration=first:dropout_transition=2"; // для ffmpeg
            if (audioDataRight != null)
            {
                rightArgument = "-filter_complex amerge=inputs=2 -ac 2";
            }

            var ramdomFileName = Path.GetRandomFileName();
            string? directoryName = Path.GetDirectoryName(outputFilePath);
            //directoryName += "_for_decoder";
            if (!Directory.Exists(directoryName)) Directory.CreateDirectory(directoryName);
            var ramdomFileNameWithPath = Path.Combine(directoryName, ramdomFileName);
            string fileNameLeft = ramdomFileNameWithPath + "_left"; // имя входного файла
            string fileNameRight = ramdomFileNameWithPath + "_right";
            string fileNameLeftWav = fileNameLeft + ".wav"; // имя выходного файла
            string fileNameRightWav = fileNameRight + ".wav";

            try
            {
                await File.WriteAllBytesAsync(fileNameLeft, audioDataLeft);

                if (audioDataRight != null)
                {
                    await File.WriteAllBytesAsync(fileNameRight, audioDataRight);
                }
                else
                {
                    await File.WriteAllBytesAsync(fileNameRight, audioDataLeft);
                }

                string decoderCommandParams = $" -c_dir \"{conf["PathToDecoderDll"]}\" -c \"{recordType}\" -f \"{fileNameLeft}\" \"{fileNameLeftWav}\" -r \"{fileNameRight}\" \"{fileNameRightWav}\"";
                Console.WriteLine("decoderCommandParams: " + decoderCommandParams);

                await Cmd.RunProcess(conf["PathToDecoderExe"], decoderCommandParams);

                var ffmpegArgs = FFMpegArguments.FromFileInput(fileNameLeftWav);
                await ffmpegArgs
                    .AddFileInput(fileNameRightWav)
                    .OutputToFile(outputFilePath, true, options => options
                        .ForceFormat("wav")
                        .WithCustomArgument("-codec:a pcm_s16le -b:a 128k -ar 16000 ")
                        .WithCustomArgument(rightArgument)
                    ).ProcessAsynchronously(true, new FFOptions { BinaryFolder = conf["PathToFFmpegExe"] });

                Console.WriteLine("UsingDecoderAsync success!!! outputFilePath: " + outputFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error UsingDecoderAsync: {ex.Message}");
            }
            finally
            {
                Files.DeleteFilesByPath(fileNameLeft, fileNameRight, fileNameLeftWav, fileNameRightWav);
            }
        }*/
    }
}


