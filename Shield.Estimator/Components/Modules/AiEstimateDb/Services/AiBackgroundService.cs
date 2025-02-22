//AiBackGroundService.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Shield.Estimator.Shared.Components.Modules._Shared;

using Shield.Estimator.Shared.Components.Modules.AiEstimateDb;
using Shield.Estimator.Shared.Components.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.SqliteModel;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Shield.Estimator.Business.Services;

using Polly;
using Polly.Retry;
using System.Threading;

public class AiBackgroundService : BackgroundService
{
    private readonly ILogger<AiBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly WhisperService _whisper;
    private readonly KoboldService _kobold;
    private readonly IHubContext<TodoHub> _hubContext;

    private readonly IDbContextFactory<SqliteDbContext> _sqliteDbContext;
    private readonly IDbContextFactory _dbContextFactory;

    private readonly AsyncRetryPolicy _retryPolicy;

    private List<string> IgnoreRecordTypes;

    private string _preTextTranslate;

    public AiBackgroundService(ILogger<AiBackgroundService> logger, IConfiguration configuration, WhisperService whisperService, KoboldService KoboldService, IDbContextFactory<SqliteDbContext> sqliteDbContext, IDbContextFactory dbContextFactory, IHubContext<TodoHub> hubContext)
    {
        _logger = logger;
        _configuration = configuration;
        _whisper = whisperService;
        _kobold = KoboldService;
        _hubContext = hubContext;

        _sqliteDbContext = sqliteDbContext;
        _dbContextFactory = dbContextFactory;
        //Эта политика повторяет операцию до 4 раз с экспоненциальной выдержкой, начиная с 2 секунд. 1,2,4,8,16 секунд
        _retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

        // Инициализация переменных в конструкторе
        IgnoreRecordTypes = _configuration.GetSection("AudioConverter:IgnoreRecordTypes").Get<List<string>>();
        _preTextTranslate = _configuration["PretextTranslate"] ?? "";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(15011, stoppingToken); // delay
            try
            {
                await AiProcessDatabaseAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing database");
                Console.WriteLine($"Ошибка в AiBackGroundService: {ex.Message}");
            }
        }
    }

    private async Task UpdateTodoItemStateAsync(TodoItem item, string message, CancellationToken _stoppingToken)
    {
        using var context = _sqliteDbContext.CreateDbContext();
        var todoItemFromDb = await context.TodoItems.FindAsync(item.Id);
        if (todoItemFromDb != null)
        {
            todoItemFromDb.Id = item.Id;
            todoItemFromDb.IsRunning = item.IsRunning;
            todoItemFromDb.CompletedKeys = item.CompletedKeys;
            todoItemFromDb.TotalKeys = item.TotalKeys;
            todoItemFromDb.ProcessingMessage = message;
            await context.SaveChangesAsync();
        }
        await _hubContext.Clients.All.SendAsync("UpdateTodos", todoItemFromDb, _stoppingToken);

    }
    private async Task StopProcessingAsync(TodoItem item, string message, CancellationToken _stoppingToken)
    {
        using var context = _sqliteDbContext.CreateDbContext();
        var todoItemFromDb = await context.TodoItems.FindAsync(item.Id);
        if (todoItemFromDb != null)
        {
            todoItemFromDb.Id = item.Id;
            todoItemFromDb.IsRunning = item.IsRunning;
            todoItemFromDb.CompletedKeys = item.CompletedKeys;
            todoItemFromDb.TotalKeys = item.TotalKeys;
            todoItemFromDb.IsRunPressed = false;
            todoItemFromDb.IsStopPressed = true;
            todoItemFromDb.ProcessingMessage = message;
            await context.SaveChangesAsync();
        }
        await _hubContext.Clients.All.SendAsync("UpdateTodos", todoItemFromDb, _stoppingToken);
    }
    private async Task HandleExceptionAsync(TodoItem item, string ex, CancellationToken _stoppingToken)
    {
        using var context = _sqliteDbContext.CreateDbContext();
        var todoItemFromDb = await context.TodoItems.FindAsync(item.Id);

        if (todoItemFromDb != null)
        {
            todoItemFromDb.Id = item.Id;
            todoItemFromDb.IsRunning = item.IsRunning;
            todoItemFromDb.CompletedKeys = item.CompletedKeys;
            todoItemFromDb.TotalKeys = item.TotalKeys;
            todoItemFromDb.IsRunPressed = false;
            todoItemFromDb.IsStopPressed = true;
            todoItemFromDb.ProcessingMessage = $"{DateTime.Now} Error: {ex}";
            todoItemFromDb.LastError = $"{DateTime.Now} Error => {ex}";
            await context.SaveChangesAsync();
        }
        await _hubContext.Clients.All.SendAsync("UpdateTodos", todoItemFromDb, _stoppingToken);
        Console.WriteLine("Процесс остановлен. Ошибка:" + ex);
    }

    private async Task<bool> ReloadIsStopPressedByItemId(int Id)
    {
        var ReloadedTodoItemById = await _sqliteDbContext.CreateDbContext().LoadTodoItem(Id);
        return ReloadedTodoItemById.IsStopPressed;
    }
    private async Task<bool> ReloadIsRunPressedByItemId(int Id)
    {
        var ReloadedTodoItemById = await _sqliteDbContext.CreateDbContext().LoadTodoItem(Id);
        return ReloadedTodoItemById.IsRunPressed;
    }
    private async Task AiProcessDatabaseAsync(CancellationToken stoppingToken)
    {
        // получение списка TODO задач для обработки
        using var sqlite = _sqliteDbContext.CreateDbContext();
        var todoItems = await sqlite.TodoItems.ToListAsync();

        foreach (var item in todoItems)
        {
            string conStringDBA = SelectDb.ConStringDBA(item);
            string DbType = item.DbType;
            string Scheme = item.Scheme;

            if (!await ReloadIsRunPressedByItemId(item.Id))
            {
                if(item.ProcessingMessage != "Готово к запуску. 💤")
                {
                    await StopProcessingAsync(item, "Готово к запуску. 💤", stoppingToken);
                }
                continue;
            }

            if (item.IsExecutionTime)
            {
                TimeSpan now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);

                if (item.StartExecutionTime <= item.EndExecutionTime)
                {
                    if (!(now > item.StartExecutionTime && now < item.EndExecutionTime))
                    {
                        await UpdateTodoItemStateAsync(item, $"Запуск в {item.StartExecutionTime}... ⌛", stoppingToken);
                        continue;
                    }
                }
                else
                {
                    // Интервал перекидывается на следующий день
                    if (!(now > item.StartExecutionTime || now < item.EndExecutionTime))
                    {
                        await UpdateTodoItemStateAsync(item, $"Запуск в {item.StartExecutionTime}... ⌛", stoppingToken);
                        continue;
                    }
                }
            }

            item.CompletedKeys = 0;
            item.TotalKeys = 0;
            await UpdateTodoItemStateAsync(item, "Идёт выполнение... ⌛", stoppingToken);
            await Task.Delay(1500, stoppingToken);

            try
            {
                List<SprSpeechTable> AudioList = null;
                using (var context = await _dbContextFactory.CreateDbContext(DbType, conStringDBA, Scheme))
                {
                    //_logger.LogInformation(context.ToString());
                    //_logger.LogInformation($"DbContext created with DbType: {DbType}, ConnectionString: {conStringDBA}, Scheme: {Scheme}");
                    AudioList = await EFCoreQuery.GetSpeechRecords(item.StartDateTime, item.EndDateTime, item.MoreThenDuration, context, IgnoreRecordTypes);
                    item.TotalKeys = AudioList.Count;

                //если записи отсутствуют => к следующему TODO json
                if (item.TotalKeys <= 0)
                {
                    if (item.IsCyclic)
                    {
                        await UpdateTodoItemStateAsync(item, $"Обработано {item.CompletedKeys}/{item.TotalKeys}. Ожидание повторного запуска.", stoppingToken);
                    }
                    else
                    {
                        await StopProcessingAsync(item, $"Обработано {item.CompletedKeys}/{item.TotalKeys}. Завершено.", stoppingToken);
                    }
                        await context.Database.CloseConnectionAsync();
                        continue; //к следующей итерации todoItems
                }

                //если записи есть => действие с записями
                int ProcessedWhisper = 0; //выполнено Whisper = 0
                item.IsRunning = true;
                foreach (var entity in AudioList)
                {
                    // Остановить процесс, если нажата кнопка
                    if (await ReloadIsStopPressedByItemId(item.Id))
                    {
                        await StopProcessingAsync(item, $"{DateTime.Now} Остановлено: {item.CompletedKeys} / {item.TotalKeys}", stoppingToken);
                            await context.Database.CloseConnectionAsync();
                            break;
                    }

                    // PreText => get PreText for operator or PreTextDefault
                    string preText = await Params.GetPreTextAsync(entity.SSourcename);

                    // Db => get audio (left, right, recordType)
                    byte[]? audioDataLeft, audioDataRight;
                    string recordType = string.Empty;

                    (audioDataLeft, audioDataRight, recordType) = await EFCoreQuery.GetAudioDataAsync(entity.SInckey, context);
 
                    
                    Console.WriteLine($"Audio data for key {entity.SInckey} loaded successfully. recordType = " + recordType);

                    // FFMpeg or Decoder => audio to folder
                    string audioFilePath = Path.Combine(_configuration["AudioPathForProcessing"], $"{entity.SInckey}.wav");
                    bool result = await DbToAudioConverter.FFMpegDecoder(audioDataLeft, audioDataRight, recordType, audioFilePath, _configuration);
                    if (!result) continue;

                    await UpdateTodoItemStateAsync(item, $"Идёт выполнение: {item.CompletedKeys}/{item.TotalKeys}. Стадия: Whisper.", stoppingToken);
                    // WHISPER
                    ConsoleCol.WriteLine("RecognizeSpeechAsync Task и далее DetectLanguageAsync", ConsoleColor.Yellow);
                    //Task<string> _recognizedText = _whisper.RecognizeSpeechAsync(audioFilePath, _configuration); //асинхронно, не ждём
                    Task<string> _recognizedText = _whisper.TranscribeAsync(audioFilePath); //асинхронно, не ждём
                    //(string languageCode, string detectedLanguage) = await _whisper.DetectLanguageAsync(audioFilePath, _configuration);
                    (string languageCode, string detectedLanguage) = await _whisper.DetectLanguageAsync(audioFilePath);
                    string recognizedText = await _recognizedText; //дожидаемся _recognizedText...
                    ProcessedWhisper++;
                    await UpdateTodoItemStateAsync(item, $"Идёт выполнение: {item.CompletedKeys}/{item.TotalKeys}. Стадия: Analisis.", stoppingToken);

                    // Delete earlier created file
                    Files.DeleteFilesByPath(audioFilePath);

                    // OLLAMA + ORACLE => Run task !!!_WITHOUT await
                    //item.CompletedKeys++; выполняется внутри ProcessOllamaAndUpdateEntityAsync
                    _ = ProcessOllamaAndUpdateEntityAsync(conStringDBA, DbType, Scheme, entity.SInckey, recognizedText, languageCode, detectedLanguage, preText, _configuration["OllamaModelName"], _configuration, item, stoppingToken);

                    // разрешить "вырываться вперёд не более чем на N раз" и ProcessedAi
                    while (ProcessedWhisper - 1 > item.CompletedKeys)
                    {
                        await Task.Delay(5000, stoppingToken);
                        ConsoleCol.WriteLine("Delay is done. OLLAMA / WHISPER => " + item.CompletedKeys + "/" + ProcessedWhisper, ConsoleColor.Yellow);
                        _logger.LogWarning("Ollama / Whisper => " + item.CompletedKeys + " / " + ProcessedWhisper);
                    }
                    _logger.LogInformation("ProcessedOllama / ProcessedWhisper => " + item.CompletedKeys + "/" + ProcessedWhisper);
                }
                if (item.IsCyclic && !await ReloadIsStopPressedByItemId(item.Id))
                {
                    await UpdateTodoItemStateAsync(item, $"Выполнено: {item.CompletedKeys}/{item.TotalKeys}. Ожидание повторного запуска.", stoppingToken);
                }
                else
                {
                    await StopProcessingAsync(item, $"Процесс остановлен. Выполнено: {item.CompletedKeys}/{item.TotalKeys}.", stoppingToken);
                }



                    await context.Database.CloseConnectionAsync();
                }
            }
            catch (OracleException ex)
            {
                await HandleExceptionAsync(item, "OracleException: " + ex.Message, stoppingToken);
            }
            catch (NpgsqlException ex)
            {
                await HandleExceptionAsync(item, "PostgresException: " + ex.Message, stoppingToken);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(item, "General: " + ex.Message, stoppingToken);
            }
            finally
            {
                item.IsRunning = false;
            }


        }
    }

    private async Task ProcessOllamaAndUpdateEntityAsync(string conStringDBA, string DbType, string Scheme, long? entityId, string recognizedText, string languageCode, string detectedLanguage, string preText, string modelName, IConfiguration Configuration, TodoItem item, CancellationToken stoppingToken)
    {
        // OLLAMA
        try
        {
            string responseOllamaText = await _kobold.GenerateTextAsync(preText + recognizedText);
            if (languageCode != "ru" && languageCode != "uk" && !string.IsNullOrEmpty(languageCode))
            {
                recognizedText = await _kobold.GenerateTextAsync(_preTextTranslate + recognizedText);
                recognizedText = $"Перевод с {detectedLanguage}: " + recognizedText;
            }
            await _retryPolicy.ExecuteAsync(async () =>
            {
                using (var NewContext = await _dbContextFactory.CreateDbContext(DbType, conStringDBA, Scheme))
                {
                    await EFCoreQuery.InsertOrUpdateCommentAsync(entityId, recognizedText, detectedLanguage, responseOllamaText, Configuration["OllamaModelName"], NewContext, item.BackLight);
                    await NewContext.Database.CloseConnectionAsync();
                    Console.WriteLine("InsertOrUpdateCommentAsync => NewContext: " + NewContext.ToString());
                }
            });

            item.CompletedKeys++;
        }
        catch (Exception ex)
        {
            ConsoleCol.WriteLine("Ошибка при обработке Ollama и обновлении сущности EFCore: " + ex.Message, ConsoleColor.Red);
            item.CompletedKeys++;
        }

    }    
}

