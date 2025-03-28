//ReplBackGroundService.cs

using Microsoft.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.Modules.Replicator;

using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Shield.Estimator.Shared.Components.Modules._Shared;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;
using Shield.Estimator.Business.Logger;
using Shield.AudioConverter.AudioConverterServices;

using Polly;
using Polly.Retry;
using Npgsql;

public class ReplBackgroundService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private FileLogger _fileLogger;
    private readonly IHubContext<ReplicatorHub> _hubContext;
    private readonly IDbContextFactory _dbContextFactory;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AudioConverterFactory _audioConverter;

    public ReplBackgroundService(AudioConverterFactory audioConverter, IDbContextFactory dbContextFactory, IConfiguration configuration, IHubContext<ReplicatorHub> hubContext)
    {
        _dbContextFactory = dbContextFactory;
        _configuration = configuration;
        _hubContext = hubContext;
        _fileLogger = new FileLogger(Path.Combine(AppContext.BaseDirectory, "Logs/replicator.log"));
        //Эта политика повторяет операцию до N раз с выдержкой, 1,2,3... секунд
        _retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromSeconds(retryAttempt));
        _audioConverter = audioConverter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Задержка между циклами
            await Task.Delay(TimeSpan.FromSeconds(11), stoppingToken);
            try
            {
                await ProcessJsonFiles(stoppingToken); 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в ReplBackgroundService: {ex.Message}");
                _fileLogger.Log($"Ошибка в ReplBackgroundService: {ex.Message}");
            }
        }
    }

    private async Task ProcessJsonFiles(CancellationToken cancellationToken) 
    {
        string pathToAudio = _configuration["AudioPathForReplicator"];

        if (Directory.Exists(pathToAudio))
        {
            var jsonFiles = Directory.EnumerateFiles(pathToAudio, "*.json").OrderBy(f => f);
            if (!jsonFiles.Any())
            {
                //Console.WriteLine("BackGroung Repl => Нет json файлов для обработки.");
                return;
            }
          
            foreach (var file in jsonFiles) 
            {
                var jsonContent = await File.ReadAllTextAsync(file);
                JsonReplicatorQueue paramsRepl = JsonSerializer.Deserialize<JsonReplicatorQueue>(jsonContent);
                
                await ProcessAudioFiles(paramsRepl, cancellationToken);
                await Task.Delay(500, cancellationToken); // пауза перед удалением директории и json
                Files.DeleteDirectory(paramsRepl.FolderToSaveTempAudio);
                Files.DeleteFilesByPath(file);
            }
        }
        else
        {
            Console.WriteLine("BackGroung Repl => Директория для обработки аудиофайлов отсутствует.");
            return;
        }
    }

    private async Task ProcessAudioFiles(JsonReplicatorQueue paramsRepl, CancellationToken cancellationToken)
    {

        var audioFiles = Directory.EnumerateFiles(paramsRepl.FolderToSaveTempAudio)
            .OrderBy(f => new FileInfo(f).Length); // Обработка мелких файлов первыми;

        if (!audioFiles.Any()) return;
        int processedCount = 0;

        foreach (var filePath in audioFiles)
        {
            var success = await TryProcessAudioFile(filePath, paramsRepl, cancellationToken);
            if (success)
            {
                processedCount++;
                Console.WriteLine($"{processedCount} / {audioFiles.Count()} => OK. File: {filePath} ");
            }
        }
        _fileLogger.Log($"Выполнено {processedCount}/{audioFiles.Count()}. Источник: {paramsRepl.SourceName}, БД: {paramsRepl.DbType}/{paramsRepl.Scheme}.");
    }

    private async Task<bool> TryProcessAudioFile(string filePath, JsonReplicatorQueue paramsRepl, CancellationToken cancellationToken)
    {
        try
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await ProcessSingleAudio(filePath, paramsRepl, cancellationToken);
            });
            return true;
        }
        catch (Exception ex)
        {
            _fileLogger.Log($"Error processing audio file {filePath} => {ex.Message}");
            await _hubContext.Clients.All.SendAsync($"Error processing file {filePath}: {ex.Message}", cancellationToken);
            return false;
        }
    }

    private async Task ProcessSingleAudio(string filePath, JsonReplicatorQueue paramsRepl, CancellationToken cancellationToken)
    {

        var (durationOfWav, audioDataLeft, audioDataRight) = await ConvertAudioFileToByteArray(filePath);

        Parse.ParsedIdenties fileData = Parse.FormFileName(filePath); //если не удалось, возвращает {DateTime.Now, "", "", "", 2}
        string isIdentificators = (fileData.Talker == "" && fileData.Caller == "" && fileData.IMEI == "") ? "✔️ без идентификаторов" : "✅ с идентификаторами";

        // Создание таблиц записи
        string codec = "PCMA";

        try
        {
            using (var context = await _dbContextFactory.CreateDbContext(paramsRepl.DbType, paramsRepl.DbConnectionSettings, paramsRepl.Scheme))
            {
                long maxKey = await context.SprSpeechTables.MaxAsync(x => (long?)x.SInckey) ?? 0;
                var speechTableEntity = CreateSpeechTableEntity(fileData, durationOfWav, codec, maxKey, paramsRepl.SourceName);
                var data1TableEntity = CreateData1TableEntity(audioDataLeft, audioDataRight, codec, maxKey);
                await SaveEntitiesToDatabase(context, speechTableEntity, data1TableEntity, cancellationToken);
            }
            await _hubContext.Clients.All.SendAsync("ReceiveMessage", $"Записан {isIdentificators}: {filePath}", CancellationToken.None);
        }
        catch (Exception ex)
        {
            ConsoleCol.WriteLine($"Repl_SaveEntitiesToDatabase = > {ex.Message}", ConsoleColor.Red);
            ConsoleCol.WriteLine($"paramsRepl = > {paramsRepl.ToString()}", ConsoleColor.DarkRed);
        }
    }

    private async Task<(int, byte[]?, byte[]?)> ConvertAudioFileToByteArray(string audioFilePath)
    {
        foreach (var converterType in Enum.GetValues<ConverterType>())
        {
            try
            {
                var converter = _audioConverter.CreateConverter(converterType);
                ConsoleCol.WriteLine($"converterType = {converterType}", ConsoleColor.Cyan);
                return await converter.ConvertFileToByteArrayAsync(audioFilePath);
            }
            catch (Exception ex)
            {
                ConsoleCol.WriteLine("", ConsoleColor.Red);
                ConsoleCol.WriteLine($"Error with {audioFilePath}", ConsoleColor.Red);
                ConsoleCol.WriteLine($"Error with {converterType}: {ex.Message}", ConsoleColor.Red);
                // Логируем полную информацию об ошибке
                //Console.WriteLine($"Full error details: {ex}");
            }
            continue;
        }
        throw new InvalidOperationException("All audio conversions failed");
    }

    private async Task SaveEntitiesToDatabase(BaseDbContext context, SprSpeechTable speechEntry, SprSpData1Table dataEntry, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        //using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            context.SprSpeechTables.Add(speechEntry);
            context.SprSpData1Tables.Add(dataEntry);
            await context.SaveChangesAsync(cancellationToken);
            //await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ConsoleCol.WriteLine("BackGroung Repl => Error SaveEntitiesToDatabase => " + ex, ConsoleColor.Red);
            //await transaction.RollbackAsync(cancellationToken);
            throw;
        }

    }
    private SprSpeechTable CreateSpeechTableEntity(Parse.ParsedIdenties fileData, int durationOfWav, string codec, long maxKey, string sourceName)
    {
        //string durationString = string.Format("+00 {0:D2}:{1:D2}:{2:D2}.000000", durationOfWav / 3600, (durationOfWav % 3600) / 60, durationOfWav % 60);
        TimeSpan durationTimeSpan = TimeSpan.FromSeconds(durationOfWav);

        return new SprSpeechTable
        {
            SInckey = maxKey+1,
            SType = 0,
            SPrelooked = 0,
            SDeviceid = "MEDIUM_R",
            SDatetime = fileData.Timestamp,
            SDuration = durationTimeSpan,
            SSysnumber3 = fileData.IMEI,
            SSourcename = sourceName,
            STalker = fileData.Talker,
            SUsernumber = fileData.Caller,
            SCalltype = fileData.Calltype,
            SEventcode = codec
        };
    }

    private SprSpData1Table CreateData1TableEntity(byte[]? audioDataLeft, byte[]? audioDataRight, string codec, long maxKey)
    {
        return new SprSpData1Table
        {
                SInckey = maxKey+1,
                SOrder = 1,
                SRecordtype = codec,
                SFspeech = audioDataLeft,
                SRspeech = audioDataRight
        };
    }
}
