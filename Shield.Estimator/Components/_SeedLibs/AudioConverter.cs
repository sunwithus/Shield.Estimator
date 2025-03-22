//AudioConverter.cs
using FFMpegCore.Pipes;
using FFMpegCore;
using System.Diagnostics;
using System.Configuration;
using FFMpegCore.Enums;
using System.Threading.Channels;
using FFMpegCore.Arguments;
using Shield.AudioConverter.AudioConverterServices.Decoder;

namespace Shield.Estimator.Shared.Components._SeedLibs
{
    public class DbToAudioConverter
    {
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
                ConsoleCol.WriteLine("ошибка в методе FFMpeg: " + ex.Message, ConsoleColor.DarkRed);
                if (!File.Exists(outputFilePath)) ConsoleCol.WriteLine("FFMpegDecoder не выполнил задачу, отсутструет файл => " + outputFilePath, ConsoleColor.Red);
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
            ConsoleCol.WriteLine("UsingFilesAsync success!!! outputFilePath: " + outputFilePath, ConsoleColor.Cyan);
        }

        public static async Task UsingDecoderAsync2(byte[] audioDataLeft, byte[] audioDataRight, string outputFilePath, string recordType, IConfiguration conf)
        {
            //string _procPath = Path.GetFullPath(".\\decoder\\decoder.exe");
            //string _codecPath = Path.GetFullPath(".\\suppdll");

            var assemblyLocation = Path.GetDirectoryName(typeof(DecoderConverter).Assembly.Location);
            string _procPath = Path.Combine(assemblyLocation, "decoder", "decoder.exe");
            string _codecPath = Path.Combine(assemblyLocation, "decoder", "suppdll");

            var randomFileName = Path.GetRandomFileName();
            string directoryName = Path.GetDirectoryName(outputFilePath)!;

            if (!Directory.Exists(directoryName))
                Directory.CreateDirectory(directoryName);

            string tempBasePath = Path.Combine(directoryName, randomFileName);
            string fileNameLeft = tempBasePath + "_left.bin";
            string fileNameRight = tempBasePath + "_right.bin";
            string fileNameLeftWav = tempBasePath + "_left.wav";
            string fileNameRightWav = tempBasePath + "_right.wav";

            try
            {
                // Записываем бинарные данные во временные файлы
                await File.WriteAllBytesAsync(fileNameLeft, audioDataLeft);
                await File.WriteAllBytesAsync(fileNameRight, audioDataRight ?? audioDataLeft);

                // Формируем команду для декодера
                //string decoderArgs = $"-c_dir \"{conf["PathToDecoderDll"]}\" -c \"{recordType}\" " +
                string decoderArgs = $"-c_dir \"{_codecPath}\" -c \"{recordType}\" " +
                                     $"-f \"{fileNameLeft}\" \"{fileNameLeftWav}\" " +
                                     $"-r \"{fileNameRight}\" \"{fileNameRightWav}\"";

                string arguments = "-c_dir \"" + _codecPath + "\" -c " + recordType + " -f \"" +
                            fileNameLeft + "\" \"" + fileNameLeftWav + "\" -r \"" + fileNameRight + "\" \"" +
                            fileNameRightWav + "\"";

                Console.WriteLine($"Для сравнения: {decoderArgs}");
                Console.WriteLine($"Запуск декодера: {arguments}");
                await Cmd.RunProcess(_procPath, arguments);

                // Объединяем декодированные WAV-файлы
                if (!File.Exists(fileNameLeftWav) && !File.Exists(fileNameRightWav))
                    throw new Exception("Декодер не создал выходные файлы");

                // Читаем и объединяем аудиоданные
                byte[] leftData = await File.ReadAllBytesAsync(fileNameLeftWav);
                byte[] rightData = await File.ReadAllBytesAsync(fileNameRightWav);

                Console.WriteLine($"Создаем объединенный WAV");
                // Создаем объединенный WAV
                var mergedWav = MergeWavChannels(leftData, rightData);
                await File.WriteAllBytesAsync(outputFilePath, mergedWav);

                Console.WriteLine($"Файл успешно создан: {outputFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка UsingDecoderAsync2: {ex.Message}");
                throw;
            }
            finally
            {
                // Очистка временных файлов
                Files.DeleteFilesByPath(fileNameLeft, fileNameRight, fileNameLeftWav, fileNameRightWav);
            }
        }

        private static byte[] MergeWavChannels(byte[] leftChannel, byte[] rightChannel)
        {
            const int headerSize = 44;

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


                ConsoleCol.WriteLine("decoderCommandParams: " + decoderCommandParams, ConsoleColor.Cyan);

                await Cmd.RunProcess(conf["PathToDecoderExe"], decoderCommandParams);

                var ffmpegArgs = FFMpegArguments.FromFileInput(fileNameLeftWav);
                await ffmpegArgs
                    .AddFileInput(fileNameRightWav)
                    .OutputToFile(outputFilePath, true, options => options
                        .ForceFormat("wav")
                        .WithCustomArgument("-codec:a pcm_s16le -b:a 128k -ar 16000 ")
                        .WithCustomArgument(rightArgument)
                    ).ProcessAsynchronously(true, new FFOptions { BinaryFolder = conf["PathToFFmpegExe"] });

                ConsoleCol.WriteLine("UsingDecoderAsync success!!! outputFilePath: " + outputFilePath, ConsoleColor.Cyan);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error UsingDecoderAsync: {ex.Message}");
            }
            finally
            {
                Files.DeleteFilesByPath(fileNameLeft, fileNameRight, fileNameLeftWav, fileNameRightWav);
            }
        }

    }

    public class AudioToDbConverter
    {
        public static async Task<(int, byte[], byte[])> FFmpegStream(string inputFileName, string pathToFFmpegExe)
        {
            // Проверка валидности пути к FFmpeg
            if (string.IsNullOrWhiteSpace(pathToFFmpegExe))
            {
                throw new ArgumentException("Path to FFmpeg executable cannot be empty", nameof(pathToFFmpegExe));
            }

            if (!Path.Exists(pathToFFmpegExe))
            {
                var errorMessage = $"FFmpeg executable not found at: {pathToFFmpegExe}";
                Console.WriteLine(errorMessage);
                throw new FileNotFoundException(errorMessage, pathToFFmpegExe);
            }

            int DurationOfWav = 0;
            int Channels = 1;

            // Analyse AudioFile
            int retryCount = 0;
            const int maxRetries = 5;

            while (retryCount < maxRetries)
            {
                try
                {
                    var MediaInfo = await FFProbe.AnalyseAsync(inputFileName);
                    DurationOfWav = (int)(MediaInfo.PrimaryAudioStream?.Duration.TotalSeconds ?? 0);
                    Channels = MediaInfo.PrimaryAudioStream?.Channels ?? 0;
                    // Process mediaInfo
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FFProbe.AnalyseAsync {inputFileName} => " + ex);
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        throw;
                    }
                    await Task.Delay(211); // Wait before retrying
                }
            }

            byte[] fileDataLeft = null;
            byte[] fileDataRight = null;
            string leftArguments = Channels >= 2 ? "-filter_complex \"[0:0]pan=1|c0=c0\"" : ""; 
            string rightArguments = "-filter_complex \"[0:0]pan=1|c0=c1\"";
            var ffOptions = new FFOptions { BinaryFolder = pathToFFmpegExe };

            // Front Channel
            using (var leftMemoryStream = new MemoryStream())
            {
                await FFMpegArguments
                    .FromFileInput(inputFileName)
                    .OutputToPipe(new StreamPipeSink(leftMemoryStream), options => options
                        .WithCustomArgument(leftArguments)
                        .WithAudioCodec("pcm_alaw")
                        .WithAudioBitrate(128_000)
                        .WithAudioSamplingRate(8000)
                        .ForceFormat("wav"))
                    .ProcessAsynchronously(true, ffOptions);

                fileDataLeft = leftMemoryStream.ToArray();
            }

            // Rear Channel
            if (Channels >= 2)
            {
                using (var rightMemoryStream = new MemoryStream())
                {
                    await FFMpegArguments
                        .FromFileInput(inputFileName)
                        .OutputToPipe(new StreamPipeSink(rightMemoryStream), options => options
                            .WithCustomArgument(rightArguments)
                            .WithAudioCodec("pcm_alaw")
                            .WithAudioBitrate(128_000)
                            .WithAudioSamplingRate(8000)
                            .ForceFormat("wav"))
                        .ProcessAsynchronously(true, ffOptions);

                    fileDataRight = rightMemoryStream.ToArray();

                }
            }

            return (DurationOfWav, fileDataLeft, fileDataRight);
        }

        public static async Task<(int, byte[], byte[])> FFmpegFiles(string inputFileName, IConfiguration conf)
        {
            // Анализ аудиофайла
            var MediaInfo = await FFProbe.AnalyseAsync(inputFileName);
            int DurationOfWav = (int)(MediaInfo.PrimaryAudioStream?.Duration.TotalSeconds ?? 0);
            int Channels = MediaInfo.PrimaryAudioStream?.Channels ?? 0;

            string? outputDirectory = Path.GetDirectoryName(inputFileName);

            string leftChannelFileName = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(inputFileName)}_left.wav");
            string rightChannelFileName = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(inputFileName)}_right.wav");
            string monoChannelFileName = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(inputFileName)}_mono.wav");



            if (Channels >= 2)
            {
                // Если два канала, разделяем на левый и правый
                await FFMpegArguments
                    .FromFileInput(inputFileName)
                    .OutputToFile(leftChannelFileName, true, options => options
                        .WithCustomArgument("-filter_complex \"[0:0]pan=1|c0=c0\"")
                        .WithAudioCodec("pcm_alaw") //g726, g726le, adpcm_ms
                        .WithAudioBitrate(128_000)
                        .WithAudioSamplingRate(8000)
                    )

                    .ProcessAsynchronously(true, new FFOptions { BinaryFolder = conf["PathToFFmpegExeForReplicator"] });

                await FFMpegArguments
                    .FromFileInput(inputFileName)

                    .OutputToFile(rightChannelFileName, true, options => options
                        .WithCustomArgument("-filter_complex \"[0:0]pan=1|c0=c1\"")
                        .WithAudioCodec("pcm_alaw")
                        .WithAudioBitrate(128_000)
                        .WithAudioSamplingRate(8000)
                    )
                    //.ProcessAsynchronously();
                    .ProcessAsynchronously(true, new FFOptions { BinaryFolder = conf["PathToFFmpegExeForReplicator"] });
            }
            else
            {
                await FFMpegArguments
                    .FromFileInput(inputFileName)
                    .OutputToFile(monoChannelFileName, true, options => options
                        .WithAudioCodec("pcm_alaw")
                        .WithAudioBitrate(128_000)
                        .WithAudioSamplingRate(8000)
                    //.WithAudioChannels(1)
                    )
                    //.ProcessAsynchronously();
                    .ProcessAsynchronously(true, new FFOptions { BinaryFolder = conf["PathToFFmpegExeForReplicator"] });
            }
            await Task.Delay(10);
            byte[]? fileDataLeft = File.Exists(leftChannelFileName) ? File.ReadAllBytes(leftChannelFileName) : null;
            if (fileDataLeft == null) fileDataLeft = File.Exists(monoChannelFileName) ? File.ReadAllBytes(monoChannelFileName) : null;

            byte[]? fileDataRight = File.Exists(rightChannelFileName) ? File.ReadAllBytes(rightChannelFileName) : null;

            return (DurationOfWav, fileDataLeft, fileDataRight);
        }
    }
}


