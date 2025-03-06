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
using Shield.Estimator.Business.Services.WhisperNet;

using Polly;
using Polly.Retry;
using System.Threading;
using System.Data;
using DocumentFormat.OpenXml.Vml.Office;
using Microsoft.Extensions.Options;
using Shield.Estimator.Business.Options.WhisperOptions;
using Microsoft.Extensions.Logging;
using DocumentFormat.OpenXml.Drawing.Diagrams;
using Microsoft.AspNetCore.Mvc.Rendering;
using Shield.Estimator.Business.AudioConverterServices;
using Npgsql.Internal;
using Whisper.net.Wave;
using Whisper.net;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;

public class AiBackgroundService : BackgroundService
{
    private const int ProcessingDelayMs = 15_011;
    private const double MinimumConfidence = 0.6;
    private const int MaxWhisperOllamaGap = 2;
    
    //private readonly AiProcessingSettings _settings;
    private readonly ILogger<AiBackgroundService> _logger;
    private readonly IHubContext<TodoHub> _hubContext;
    //private readonly AiServiceCoordinator _aiServices;
    private readonly AsyncRetryPolicy _retryPolicy;

    private readonly List<string> _ignoreRecordTypes;
    private AudioConverterFactory _audioConverterFactory;


    private readonly IConfiguration _configuration;
    private readonly IOptions<WhisperCppOptions> _options;
    private readonly WhisperDockerService _whisperDocker;
    private readonly WhisperNetService _whisperNet;
    private readonly WhisperCppService _whisperCpp;
    private readonly KoboldService _kobold;
    private readonly IDbContextFactory<SqliteDbContext> _sqliteDbContext;
    private readonly IDbContextFactory _dbContextFactory;
    private List<string> IgnoreRecordTypes;
    private string _preTextTranslate;
    private string _modelPathWhisperCpp;

    public AiBackgroundService(ILogger<AiBackgroundService> logger, IConfiguration configuration, WhisperDockerService whisperService, WhisperNetService whisperNet, WhisperCppService whisperCpp, KoboldService KoboldService, IDbContextFactory<SqliteDbContext> sqliteDbContext, IDbContextFactory dbContextFactory, IHubContext<TodoHub> hubContext, IOptions<WhisperCppOptions> options, AudioConverterFactory audioConverterFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _whisperDocker = whisperService;
        _whisperNet = whisperNet;
        _whisperCpp = whisperCpp;
        _kobold = KoboldService;
        _hubContext = hubContext;
        _options = options;
        _audioConverterFactory = audioConverterFactory;

        _sqliteDbContext = sqliteDbContext;
        _dbContextFactory = dbContextFactory;
        //Эта политика повторяет операцию до 4 раз с экспоненциальной выдержкой, начиная с 2 секунд. 1,2,4,8,16 секунд
        _retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

        // Инициализация переменных в конструкторе
        IgnoreRecordTypes = _configuration.GetSection("AudioConverter:IgnoreRecordTypes").Get<List<string>>();
        _preTextTranslate = _configuration["PretextTranslate"] ?? "";

        _modelPathWhisperCpp = "";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ProcessingDelayMs, stoppingToken);
                await ProcessSqLiteTodoItemsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not TaskCanceledException)
            {
                _logger.LogError(ex, "SqLiteDatabase processing error (AiBackGroundService)");
            }
        }
    }
    private async Task ProcessSqLiteTodoItemsAsync(CancellationToken ct)
    {
        await using var context = _sqliteDbContext.CreateDbContext();
        var items = await context.TodoItems.AsNoTracking().ToListAsync(ct);

        foreach (var item in items)
        {
            if (!await ShouldProcessStart(item))
            {
                continue;
            }
            ct.ThrowIfCancellationRequested();
            await ProcessItemAsync(item, ct);
        }
    }
    private async Task<bool> ShouldProcessStart(TodoItem item)
    {
        if (!await ReloadIsRunPressedByItemId(item.Id))
        {
            if (item.ProcessingMessage != "Готово к запуску. 💤")
            {
                await StopProcessingAsync(item, "Готово к запуску. 💤", CancellationToken.None);
            }
            return false;
        }

        if (item.IsExecutionTime)
        {
            TimeSpan now = DateTime.Now.TimeOfDay;

            bool isWithinExecutionTime = item.StartExecutionTime <= item.EndExecutionTime
                ? now > item.StartExecutionTime && now < item.EndExecutionTime
                : now > item.StartExecutionTime || now < item.EndExecutionTime;

            if (!isWithinExecutionTime)
            {
                await UpdateTodoItemStateAsync(item, $"Запуск в {item.StartExecutionTime}... ⌛", CancellationToken.None);
                return false;
            }
        }

        return true;
    }
    private async Task ProcessItemAsync(TodoItem item, CancellationToken ct)
    {
        try
        {
            
            await InitializeItemCounters(item, ct);

            var (conString, dbType, scheme) = GetDbConnectionInfo(item);
            await using var context = await _dbContextFactory.CreateDbContext(dbType, conString, scheme);

            var audioList = await EFCoreQuery.GetSpeechRecords(item.StartDateTime, item.EndDateTime, item.MoreThenDuration, context, IgnoreRecordTypes);
            Console.WriteLine("audioList");

            if (!audioList.Any())
            {
                await HandleNoResults(item, ct);
                return;
            }
            await ProcessAudioRecords(item, audioList, context, ct);
        }
        catch (Exception ex) 
        {
            _logger.LogError(ex, "Error processing item {ItemId}", item.Id);
            await UpdateTodoItemStateAsync(item, message: $"Error: {ex.Message}", ct);
        }
    }
    private (string conString, string dbType, string scheme) GetDbConnectionInfo(TodoItem item) =>
    (SelectDb.ConStringDBA(item), item.DbType, item.Scheme);

    private async Task InitializeItemCounters(TodoItem item, CancellationToken ct)
    {
        item.ResetCounters();
        await UpdateTodoItemStateAsync(item, "Идёт выполнение... ⌛", ct);
        await Task.Delay(1200, ct);
    }
    private async Task HandleNoResults(TodoItem item, CancellationToken ct)
    {
        var message = item.IsCyclic
            ? $"Обработано 0/0. Ожидание повторного запуска."
            : "Нет данных для обработки. Завершено.";

        await UpdateTodoItemStateAsync(item, message, ct);
    }

    private async Task ProcessAudioRecords(TodoItem item, List<SprSpeechTable> audioList, BaseDbContext context, CancellationToken ct)
    {
        item.TotalKeys = audioList.Count;
        item.IsRunning = true;

        var languageDetectionTask = Task.Run(async () =>
        {
            foreach (var entity in audioList)
            {
                ct.ThrowIfCancellationRequested();
                if (await ShouldStopProcessing(item))
                {
                    break;
                }
                Console.WriteLine("ProcessLanguageDetect");
                await ProcessLanguageDetect(item, entity, context, ct);
                item.CompletedLanguageDetect++;
                item.Statistic = $"Автоопределение языков => {item.CompletedLanguageDetect}";

                await UpdateTodoItemStateAsync(item, item.ProcessingMessage, ct);
            }
            await UpdateTodoItemStateAsync(item, $"{item.CompletedKeys}/{item.TotalKeys}", ct);

        }, ct);

        await Task.Delay(5000);

        var audioProcessingTask = Task.Run(async () =>
        {
            foreach (var entity in audioList)
            {
                ct.ThrowIfCancellationRequested();
                if (await ShouldStopProcessing(item))
                {
                    break;
                }
                await ProcessSingleAudioEntity(item, entity, context, ct);
            }
        }, ct);

        await Task.WhenAll(languageDetectionTask, audioProcessingTask);


        await FinalizeProcessing(item, ct);
    }

    private async Task<(string languageCode, string detectedLanguage, double confidence)> DetectLanguageAsync(string audioFilePath)
    {
        (string languageCode, string detectedLanguage, double confidence) = await _whisperDocker.DetectLanguageAsync(audioFilePath);
        if (confidence < MinimumConfidence)
        {
            languageCode = "undefined";
        }
        detectedLanguage = detectedLanguage + " " + Math.Round(confidence * 100, 1, MidpointRounding.AwayFromZero).ToString("N1") + "%";
        return (languageCode, detectedLanguage, confidence);
    }

    private async Task ProcessLanguageDetect(TodoItem item, SprSpeechTable entity, BaseDbContext context, CancellationToken ct)
    {
        string audioFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        Console.WriteLine($"Путь к временному файлу: {audioFilePath}");
        try
        {
            byte[]? audioDataLeft, audioDataRight;
            string? recordType, eventCode = string.Empty;
            (audioDataLeft, audioDataRight, recordType, eventCode) = await EFCoreQuery.GetAudioDataAsync(entity.SInckey, context);

            if (!await TryConvertAudio(audioDataLeft, audioDataRight, recordType, eventCode, audioFilePath)) return;

            (string languageCode, string detectedLanguage, double confidence) = await DetectLanguageAsync(audioFilePath);

            if(context == null) Console.WriteLine($"context");

            await EFCoreQuery.UpdateLangInfo(entity.SInckey, detectedLanguage, languageCode, context);
            Console.WriteLine($"UpdateLangInfo");
        }
        finally
        {
            Files.DeleteFilesByPath(audioFilePath);
        }
        
    }

    private async Task ProcessSingleAudioEntity(TodoItem item, SprSpeechTable entity, BaseDbContext context, CancellationToken ct)
    {
        entity = await context.SprSpeechTables.FindAsync(entity.SInckey);
        // Db => get audio (left, right, recordType, eventCode)
        byte[]? audioDataLeft, audioDataRight;
        string? recordType, eventCode = string.Empty;
        (audioDataLeft, audioDataRight, recordType, eventCode) = await EFCoreQuery.GetAudioDataAsync(entity.SInckey, context);
        Console.WriteLine($"Audio data (left, right, recordType, eventCode) for key {entity.SInckey} loaded. recordType = " + recordType);

        // FFMpeg or Decoder => audio to folder
        string audioFilePath = Path.Combine(_configuration["AudioPathForBGService"], $"{entity.SInckey}.wav");
        if (!await TryConvertAudio(audioDataLeft, audioDataRight, recordType, eventCode, audioFilePath)) return;
        await UpdateTodoItemStateAsync(item, $"Идёт выполнение: {item.CompletedKeys}/{item.TotalKeys}. Стадия: Whisper.", ct);

        // WHISPER
        ConsoleCol.WriteLine("WHISPER Started... RecognizeSpeechAsync и DetectLanguageAsync", ConsoleColor.Yellow);


        //SPostid = LanguageCode

        string languageCode = "";
        string detectedLanguage = "";
        double confidence = 0;
        if (entity?.SPostid?.Length != 2 || entity.SPostid != "")
        {
            (languageCode, detectedLanguage, confidence) = await DetectLanguageAsync(audioFilePath);
        }
        

        string _recognizedText = "";

        if (!_options.Value.CustomModels.ContainsKey(languageCode) || !_options.Value.CustomModels.TryGetValue(languageCode, out string modelPath) || !File.Exists(modelPath))
        {
            //_recognizedText = await _whisperNet.TranscribeAudio(audioFilePath, languageCode);
            _logger.LogInformation($"Распознавание _whisperDocker");
            _recognizedText = await _whisperDocker.TranscribeAsync(audioFilePath);
        }    
        else
        {
            if (_modelPathWhisperCpp != modelPath)
            {
                _logger.LogInformation($"Загрузка модели {modelPath}");
                await _whisperCpp.LoadModelAsync(modelPath);
                _modelPathWhisperCpp = modelPath;
            }

            _logger.LogInformation($"Распознавание _whisperCpp");
            _recognizedText = await _whisperCpp.ProcessInferenceAsync(audioFilePath);

        }

        //string recognizedText = await _recognizedText; //дожидаемся _recognizedText...
        string recognizedText = _recognizedText; //дожидаемся _recognizedText...
        _logger.LogWarning(recognizedText);

        item.ProcessedWhisper++;
        await UpdateTodoItemStateAsync(item, $"Идёт выполнение: {item.CompletedKeys}/{item.TotalKeys}. Стадия: Analisis.", ct);

        // Delete earlier created file
        Files.DeleteFilesByPath(audioFilePath);

        // OLLAMA + ORACLE => Run task !!!_WITHOUT await
        //item.CompletedKeys++; выполняется внутри ProcessOllamaAndUpdateEntityAsync
        
        // PreText => get PreText for operator or PreTextDefault
        string preText = await Params.GetPreTextAsync(entity.SSourcename);
        _ = ProcessOllamaAndUpdateEntityAsync(entity.SInckey, recognizedText, languageCode, detectedLanguage, preText, _configuration["AiModelName"], _configuration, item, ct);

        // разрешить "вырываться вперёд не более чем на N раз" и ProcessedAi
        while (item.ProcessedWhisper - MaxWhisperOllamaGap > item.CompletedKeys)
        {
            await Task.Delay(5000, ct);
            ConsoleCol.WriteLine($"Delay is done. OLLAMA / WHISPER => {item.CompletedKeys}/{item.ProcessedWhisper}", ConsoleColor.Yellow);
            _logger.LogWarning("Ollama / Whisper => " + item.CompletedKeys + " / " + item.ProcessedWhisper);
        }
        _logger.LogInformation("ProcessedOllama / ProcessedWhisper => " + item.CompletedKeys + "/" + item.ProcessedWhisper);
    }

    private async Task ProcessOllamaAndUpdateEntityAsync(long? entityId, string recognizedText, string languageCode, string detectedLanguage, string preText, string modelName, IConfiguration Configuration, TodoItem item, CancellationToken stoppingToken)
    {
        // OLLAMA
        var (ConStringDBA, DbType, Scheme) = GetDbConnectionInfo(item);
        try
        {
            string responseOllamaText = "0";
            if (recognizedText.Length > 10)
            {
                responseOllamaText = await _kobold.GenerateTextAsync(preText + recognizedText);
                if (languageCode != "ru" && languageCode != "uk" && !string.IsNullOrEmpty(languageCode))
                {
                    recognizedText = await _kobold.GenerateTextAsync(_preTextTranslate + recognizedText);
                    recognizedText = $"Перевод с {detectedLanguage}: " + recognizedText;
                }
            }
            await _retryPolicy.ExecuteAsync(async () =>
            {
                using (var NewContext = await _dbContextFactory.CreateDbContext(DbType, ConStringDBA, Scheme))
                {
                    await EFCoreQuery.InsertOrUpdateCommentAsync(entityId, recognizedText, detectedLanguage, responseOllamaText, languageCode, NewContext, item.BackLight);
                    await NewContext.Database.CloseConnectionAsync();
                    Console.WriteLine("InsertOrUpdateCommentAsync => NewContext: " + NewContext.ToString());
                }
            });
            item.CompletedKeys++;
        }
        catch (Exception ex)
        {
            ConsoleCol.WriteLine("Ошибка при обработке Ollama и обновлении сущности EFCore: " + ex.Message, ConsoleColor.Red);
        }

    }

    private async Task<bool> TryConvertAudio(byte[] left, byte[] right, string type, string eventCode, string path)
    {
        var result = await DbToAudioConverter.FFMpegDecoder(left, right, type, path, _configuration);
        return result || await DbToAudioConverter.FFMpegDecoder(left, right, eventCode, path, _configuration);
    }

    private async Task FinalizeProcessing(TodoItem item, CancellationToken ct)
    {
        var message = item.IsCyclic && !await ReloadIsStopPressedByItemId(item.Id)
            ? $"Обработано {item.CompletedKeys}/{item.TotalKeys}. Ожидание повтора"
            : $"Процесс остановлен. Выполнено: {item.CompletedKeys}/{item.TotalKeys}";

        await UpdateTodoItemStateAsync(item, message, ct);
    }

    private async Task<bool> ShouldStopProcessing(TodoItem item) {
        if(await ReloadIsStopPressedByItemId(item.Id))
        {
            await StopProcessingAsync(item, $"{DateTime.Now} Остановлено: {item.CompletedKeys}/{item.TotalKeys}", CancellationToken.None);
            return true;
        }
        return false;
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
    private async Task UpdateTodoItemStateAsync(TodoItem item, string message, CancellationToken _stoppingToken)
    {
        using var context = _sqliteDbContext.CreateDbContext();
        var todoItemFromDb = await context.TodoItems.FindAsync(item.Id);
        if (todoItemFromDb != null)
        {
            todoItemFromDb.Id = item.Id;
            todoItemFromDb.IsRunning = item.IsRunning;
            todoItemFromDb.CompletedKeys = item.CompletedKeys;
            todoItemFromDb.CompletedLanguageDetect = item.CompletedLanguageDetect;
            todoItemFromDb.TotalKeys = item.TotalKeys;
            todoItemFromDb.Statistic = item.Statistic;
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
}

