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

using Polly;
using Polly.Retry;

public class ReplBackgroundService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private FileLogger _fileLogger;
    private LoggerToFile _logToFile;
    private readonly IHubContext<ReplicatorHub> _hubContext;
    private readonly IDbContextFactory _dbContextFactory;
    private readonly AsyncRetryPolicy _retryPolicy;


    public ReplBackgroundService(IDbContextFactory dbContextFactory, IConfiguration configuration, IHubContext<ReplicatorHub> hubContext)
    {
        _dbContextFactory = dbContextFactory;
        _configuration = configuration;
        _hubContext = hubContext;
        _fileLogger=new FileLogger(Path.Combine(AppContext.BaseDirectory, "Logs/replicator.log"));
        _logToFile = new LoggerToFile(@".\Logs\ReplicatorLog.txt");
        //Эта политика повторяет операцию до N раз с выдержкой, 1,2,3... секунд
        _retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(4, retryAttempt => TimeSpan.FromSeconds(retryAttempt));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Задержка между циклами
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
            try
            {
                await CheckFilesToReplicate(stoppingToken); 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в ReplBackgroundService: {ex.Message}");
                _fileLogger.Log($"Ошибка в ReplBackgroundService: {ex.Message}");
                await _logToFile.AddLogMessage($"Ошибка в ReplBackgroundService: {ex.Message}");
            }
        }
    }

    private async Task CheckFilesToReplicate(CancellationToken cancellationToken) 
    {
        string pathToAudio = _configuration["AudioPathForReplicator"];

        if (Directory.Exists(pathToAudio))
        {
            var JsonFiles = Directory.EnumerateFiles(pathToAudio, "*.json");
            if (!JsonFiles.Any())
            {
                //Console.WriteLine("BackGroung Repl => Нет json файлов для обработки.");
                return;
            }

            foreach (var file in JsonFiles) 
            {
                var json = await File.ReadAllTextAsync(file);
                JsonReplicatorQueue paramsRepl = JsonSerializer.Deserialize<JsonReplicatorQueue>(json);
                
                await ReplicateAudioFromDirectory(paramsRepl, cancellationToken);
                await Task.Delay(800, cancellationToken);

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

    private async Task ReplicateAudioFromDirectory(JsonReplicatorQueue paramsRepl, CancellationToken cancellationToken)
    {

        var filesAudio = Directory.EnumerateFiles(paramsRepl.FolderToSaveTempAudio);
        
        if (!filesAudio.Any())
        {
            Console.WriteLine("BackGroung Repl => Нет аудио файлов для репликации.");
            return;
        }
        int count = 0;

        foreach (var filePath in filesAudio)
        {
            try
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    await ProcessSingleAudio(filePath, paramsRepl, cancellationToken);
                    Console.WriteLine($"BackGroung Repl => Файл обработан: {filePath}");
                    count++;
                    //File.Delete(filePath);
                });
            }
            catch (OperationCanceledException ex)
            {
                Console.WriteLine($"BackGroung Repl OperationCanceledException => Ошибка при обработке файла {filePath}: {ex.Message}");
                _fileLogger.Log($"BackGroung Repl OperationCanceledException => Ошибка при обработке файла {filePath}: {ex.Message}");
                await _logToFile.AddLogMessage($"Ошибка в ReplBackgroundService OperationCanceledException: {ex.Message}");
                await _hubContext.Clients.All.SendAsync("ReceiveMessage", $"❌ OperationCanceledException Ошибка при обработке файла {filePath}: {ex.Message}", cancellationToken);
            }
            catch (Exception ex)
            {
                ConsoleCol.WriteLine($"BackGroung Repl => Ошибка при обработке файла {filePath}: {ex.Message}", ConsoleColor.Red);
                Console.WriteLine($"paramsRepl:");
                Console.WriteLine(paramsRepl.DbConnectionSettings);
                Console.WriteLine(paramsRepl.DbType);
                Console.WriteLine(paramsRepl.Scheme);
                _fileLogger.Log($"BackGroung Repl => Ошибка при обработке файла {filePath}: {ex.Message}");
                await _hubContext.Clients.All.SendAsync("ReceiveMessage", $"❌ Ошибка при обработке файла {filePath}: {ex.Message}", cancellationToken);
                //throw;
            }
        }
        _fileLogger.Log($"Выполнено {count}/{filesAudio.Count()}. Источник: {paramsRepl.SourceName}, БД: {paramsRepl.DbType}/{paramsRepl.Scheme}.");

        await _logToFile.AddLogMessage($"Выполнено {count}/{filesAudio.Count()}. Источник: {paramsRepl.SourceName}, БД: {paramsRepl.DbType}/{paramsRepl.Scheme}.");
    }

    private async Task ProcessSingleAudio(string filePath, JsonReplicatorQueue paramsRepl, CancellationToken cancellationToken)
    {


        var (durationOfWav, audioDataLeft, audioDataRight) = await AudioToDbConverter.FFmpegStream(filePath, _configuration["PathToFFmpegExeForReplicator"]);

        Parse.ParsedIdenties fileData = Parse.FormFileName(filePath); //если не удалось, возвращает {DateTime.Now, "", "", "", 2}
        string isIdentificators = (fileData.Talker == "" && fileData.Caller == "" && fileData.IMEI == "") ? "✔️ без идентификаторов" : "✅ с идентификаторами";

        // Создание талиц записи
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
