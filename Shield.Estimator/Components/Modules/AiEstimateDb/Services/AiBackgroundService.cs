//AiBackGroundService.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Shield.Estimator.Shared.Components.Modules._Shared;

using Shield.Estimator.Shared.Components.Modules.AiEstimateDb;
using Shield.Estimator.Shared.Components.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.SqliteModel;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;
using Shield.Estimator.Business.Services;
using Shield.Estimator.Business.Services.WhisperNet;
using Shield.Estimator.Business.Exceptions;

using Polly;
using Polly.Retry;
using Microsoft.Extensions.Options;
using Shield.Estimator.Business.Options.WhisperOptions;
using MudBlazor;
using System.Collections.Concurrent;
using System.Data;

using Shield.AudioConverter.AudioConverterServices;

public class AiBackgroundService : BackgroundService
{
    private const int ProcessingDelayMs = 11_005;
    private const double MinimumConfidence = 0.65;
    private const int MaxWhisperOllamaGap = 2;
    
    private readonly ILogger<AiBackgroundService> _logger;
    private readonly IHubContext<TodoHub> _hubContext;
    private readonly AsyncRetryPolicy _retryPolicy;

    private readonly List<string> _ignoreRecordTypes;

    private readonly IConfiguration _configuration;
    private readonly IOptions<WhisperCppOptions> _options;
    private readonly WhisperFasterDockerService _whisperFasterDocker;
    private readonly WhisperNetService _whisperNet;
    private readonly WhisperCppService _whisperCpp;
    private readonly KoboldService _kobold;
    private readonly IDbContextFactory<SqliteDbContext> _sqliteDbContext;
    private readonly IDbContextFactory _dbContextFactory;
    private List<string> IgnoreRecordTypes;
    private string _preTextTranslate;
    private string _modelPathWhisperCpp;

    private readonly AudioConverterFactory _audioConverter;

    

    public AiBackgroundService(AudioConverterFactory audioConverter, ILogger<AiBackgroundService> logger, IConfiguration configuration, WhisperFasterDockerService whisperFasterService, WhisperNetService whisperNet, WhisperCppService whisperCpp, KoboldService KoboldService, IDbContextFactory<SqliteDbContext> sqliteDbContext, IDbContextFactory dbContextFactory, IHubContext<TodoHub> hubContext, IOptions<WhisperCppOptions> options, AudioConverterFactory audioConverterFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _whisperFasterDocker = whisperFasterService;
        _whisperNet = whisperNet;
        _whisperCpp = whisperCpp;
        _kobold = KoboldService;
        _hubContext = hubContext;
        _options = options;
        
        _audioConverter = audioConverter;

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
                await ProcessItemsBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not TaskCanceledException)
            {
                _logger.LogError(ex, "SqLiteDatabase processing error (AiBackGroundService)");
            }
        }
    }
    private async Task ProcessItemsBatchAsync(CancellationToken ct)
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
                item.ProcessingMessage = $"Запуск в {item.StartExecutionTime}... ⌛";
                await UpdateTodoItemStateAsync(item, CancellationToken.None);
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
            _logger.LogError(ex, "Error processing item {0}", item.Title);
            item.ProcessingMessage = $"Error: {ex.Message}";
            item.LastError = $"{DateTime.Now}: {ex.Message}";
            if (ex is Npgsql.PostgresException || ex is Npgsql.NpgsqlException || ex is Oracle.ManagedDataAccess.Client.OracleException)
            {
                item.IsRunning = false;
                item.IsStopPressed = true;
                item.IsRunPressed = false;
                item.ProcessingMessage = $"Ошибка при обращении к БД.";
            }
            await UpdateTodoItemStateAsync(item, ct);
        }
    }
    private (string conString, string dbType, string scheme) GetDbConnectionInfo(TodoItem item) =>
    (SelectDb.ConStringDBA(item), item.DbType, item.Scheme);

    private async Task InitializeItemCounters(TodoItem item, CancellationToken ct)
    {
        item.ResetCounters();
        item.ProcessingMessage = "Идёт выполнение... ⌛";
        await UpdateTodoItemStateAsync(item, ct);
        await Task.Delay(1200, ct);
    }
    private async Task HandleNoResults(TodoItem item, CancellationToken ct)
    {
        item.ProcessingMessage = item.IsCyclic
            ? $"Обработано 0/0. Ожидание повторного запуска."
            : "Нет данных для обработки. Завершено.";

        await UpdateTodoItemStateAsync(item, ct);
    }

    private async Task ProcessAudioRecords(TodoItem item, List<SprSpeechTable> audioList, BaseDbContext context, CancellationToken ct)
    {
        item.Statistic = "";
        item.TotalKeys = audioList.Count;
        item.IsRunning = true;

        var languageDetectionTask = Task.Run(async () =>
        {
            var (conString, dbType, scheme) = GetDbConnectionInfo(item);
            await using var localContext = await _dbContextFactory.CreateDbContext(dbType, conString, scheme);

            // Словарь для подсчета количества языков по их коду
            var languageCounts = new ConcurrentDictionary<string, int>();
            // Словарь для хранения соответствия кодов и названий языков
            var languageNames = new ConcurrentDictionary<string, string>();
            foreach (var entity in audioList)
            {
                ct.ThrowIfCancellationRequested();
                if (await ShouldStopProcessing(item)) break;

                Console.WriteLine("ProcessLanguageDetect");
                Dictionary<string, string> lang = await ProcessLanguageDetect(item, entity, localContext, ct);
                item.CompletedLanguageDetect++;
                item.Statistic = $"Анализ языков: {item.CompletedLanguageDetect}/{item.TotalKeys}";

                // Обновляем статистику
                if (lang != null)
                {
                    foreach (var kvp in lang)
                    {
                        // Обновляем счетчик
                        languageCounts.AddOrUpdate(kvp.Key, 1, (key, oldVal) => oldVal + 1);
                        // Сохраняем актуальное название языка
                        languageNames.AddOrUpdate(kvp.Key, kvp.Value, (key, oldVal) => kvp.Value);
                        item.LanguageCounts = languageCounts;
                        item.LanguageNames = languageNames;
                    }
                }

                await UpdateTodoItemStateAsync(item, ct);
            }

            item.Statistic = $"{DateTime.Now}: {item.CompletedLanguageDetect}/{item.TotalKeys}";
            await UpdateTodoItemStateAsync(item, ct);

            // Выводим статистику
            Console.WriteLine("\nСтатистика языков:");
            foreach (var entry in languageCounts)
            {
                var languageCode = entry.Key;
                var count = entry.Value;
                var languageName = languageNames.TryGetValue(languageCode, out var name)
                    ? name
                    : "неизвестный язык";

                Console.WriteLine($"- {languageName} ({languageCode}): {count} шт.");
            }

        }, ct);

        await Task.Delay(3000);

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
    private async Task<Dictionary<string, string>> ProcessLanguageDetect(TodoItem item, SprSpeechTable entity, BaseDbContext context, CancellationToken ct)
    {
        string audioFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        Console.WriteLine($"Путь к временному файлу: {audioFilePath}");
        try
        {
            byte[]? audioDataLeft, audioDataRight;
            string? recordType, eventCode = string.Empty;
            (audioDataLeft, audioDataRight, recordType, eventCode) = await EFCoreQuery.GetAudioDataAsync(entity.SInckey, context);

            //if (!await TryConvertAudio(audioDataLeft, audioDataRight, recordType, eventCode, audioFilePath)) return null;
            await ConvertByteArrayToFile(audioDataLeft, audioDataRight, audioFilePath, recordType, eventCode);

            (string languageCode, string detectedLanguage, double confidence) = await DetectLanguageAsync(audioFilePath);

            if (context == null) Console.WriteLine($"context");

            await EFCoreQuery.UpdateLangInfo(entity.SInckey, detectedLanguage, languageCode, context);
            Console.WriteLine($"UpdateLangInfo");
            return new Dictionary<string, string>() { { languageCode, detectedLanguage.Split(" ")[0] } };
        }
        finally
        {
            Files.DeleteFilesByPath(audioFilePath);
        }
    }

    private async Task ConvertByteArrayToFile(byte[]? AudioDataLeft, byte[]? AudioDataRight, string audioFilePath, string RecordType, string Eventcode)
    {
        foreach (var converterType in Enum.GetValues<ConverterType>())
        {
            try
            {
                ConsoleCol.WriteLine($"Attempting conversion with {converterType}", ConsoleColor.Cyan);

                var converter = _audioConverter.CreateConverter(converterType);
                ConsoleCol.WriteLine($"converterType = {converterType}", ConsoleColor.Cyan);
                await converter.ConvertByteArrayToFileAsync(AudioDataLeft, AudioDataRight, audioFilePath, RecordType, Eventcode);

                if (File.Exists(audioFilePath))
                {
                    ConsoleCol.WriteLine($"Conversion successful using {converterType}", ConsoleColor.Green);
                    return;
                }
                else
                {
                    ConsoleCol.WriteLine($"Conversion with {converterType} did not produce a file", ConsoleColor.Yellow);
                }
            }
            catch (Exception ex)
            {
                ConsoleCol.WriteLine($"Error with {converterType}: {ex.Message}", ConsoleColor.Red);
                // Логируем полную информацию об ошибке
                //Console.WriteLine($"Full error details: {ex}");
            }
            continue;
        }
        throw new InvalidOperationException("All audio conversions failed");
    }

    private async Task<(string languageCode, string detectedLanguage, double confidence)> DetectLanguageAsync(string audioFilePath)
    {
        string languageCode = "none";
        string detectedLanguage = "undefined";
        double confidence = 0;
        try
        {
            await _retryPolicy.ExecuteAsync(async () =>
            {
                (languageCode, detectedLanguage, confidence) = await _whisperFasterDocker.DetectLanguageAsync(audioFilePath);
                detectedLanguage = detectedLanguage + " " + Math.Round(confidence * 100, 1, MidpointRounding.AwayFromZero).ToString("N1") + "%";

                if (confidence < MinimumConfidence)
                {
                    languageCode = "none";
                    detectedLanguage = "undefined";
                }
                
            });
            return (languageCode, detectedLanguage, confidence);
        }
        catch (Exception ex)
        {
            detectedLanguage = "error";
            Console.WriteLine("General Error AiBGService DetectLanguageAsync => " + ex.Message);
            return (languageCode, detectedLanguage, confidence);
        }
    }

    private async Task ProcessSingleAudioEntity(TodoItem item, SprSpeechTable entity, BaseDbContext context, CancellationToken ct)
    {
        item.ProcessingMessage = $"Выполнение: {item.CompletedKeys}/{item.TotalKeys}";
        await UpdateTodoItemStateAsync(item, ct);

        //В Oralce 11.2 по другому не работает
        var entityTemp = await context.SprSpeechTables.Where(x => x.SInckey == entity.SInckey).ToListAsync();
        entity = entityTemp.FirstOrDefault();
        ConsoleCol.WriteLine($"entity = await context.SprSpeechTables.FindAsync(entity.SInckey);", ConsoleColor.Yellow);

        // Db => get audio (left, right, recordType, eventCode)
        var (audioDataLeft, audioDataRight, recordType, eventCode) = await EFCoreQuery.GetAudioDataAsync(entity.SInckey, context);
        Console.WriteLine($"Audio data (left, right, recordType, eventCode) for key {entity.SInckey} loaded. recordType = " + recordType);

        // FFMpeg or Decoder => audio to folder + Whisper
        string audioFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + $"{entity.SInckey}.wav"); //string audioFilePath = Path.Combine(_configuration["AudioPathForBGService"], $"{entity.SInckey}.wav");

        await ConvertByteArrayToFile(audioDataLeft, audioDataRight, audioFilePath, recordType, eventCode);
        if (!File.Exists(audioFilePath)) return;
        //if (!await TryConvertAudio(audioDataLeft, audioDataRight, recordType, eventCode, audioFilePath)) return;

        var (recognizedText, detectedLanguage, languageCode) = await ProcessWhisper(entity, audioFilePath, ct);
        if(recognizedText == null) return;

        _logger.LogInformation(recognizedText);

        item.ProcessedWhisper++;

        // PreText => get PreText for operator or PreTextDefault
        string preText = await Params.GetPreTextAsync(entity.SSourcename);

        _ = ProcessOllamaAndUpdateEntityAsync(entity.SInckey, recognizedText, languageCode, detectedLanguage, preText, _configuration["AiModelName"], _configuration, item, ct);

        // разрешить "вырываться вперёд не более чем на N раз" и ProcessedAi
        int nnn = 60 * (MaxWhisperOllamaGap - 1 + item.ProcessedWhisper - item.CompletedKeys); // 60 * 3 раз по 5 сек = 600с = 10м
        while (item.ProcessedWhisper - MaxWhisperOllamaGap > item.CompletedKeys)
        {
            nnn--;
            await Task.Delay(5000, ct);
            ConsoleCol.WriteLine($"Delay is done. OLLAMA / WHISPER => {item.CompletedKeys}/{item.ProcessedWhisper}", ConsoleColor.Yellow);
            ConsoleCol.WriteLine($"Wait until nnn == 0. Current nnn => {nnn}", ConsoleColor.Yellow);
            _logger.LogWarning("Ollama / Whisper => " + item.CompletedKeys + " / " + item.ProcessedWhisper);
            if (nnn <= 0)
            {
                item.CompletedKeys = item.ProcessedWhisper; //сброс счетчика
                break;
            }
        }
        _logger.LogInformation("ProcessedOllama / ProcessedWhisper => " + item.CompletedKeys + "/" + item.ProcessedWhisper);
    }

    private async Task<(string, string, string)> ProcessWhisper(SprSpeechTable entity, string audioFilePath, CancellationToken ct)
    {
        string? recognizedText, detectedLanguage, languageCode = "";
        try
        {
            ConsoleCol.WriteLine("WHISPER Started... RecognizeSpeechAsync и DetectLanguageAsync", ConsoleColor.Yellow);
            languageCode = entity?.SPostid;
            detectedLanguage = entity?.SBelong;
            double confidence = 0;

            if (languageCode?.Length != 2 || languageCode != "undefined" || languageCode == "none" || languageCode != "yue" || languageCode != "haw" ) //SPostid = LanguageCode
            {
                (languageCode, detectedLanguage, confidence) = await DetectLanguageAsync(audioFilePath);
            }

            // Если язык не из списка, на который есть модель - Default через Docker Api
            if (!_options.Value.CustomModels.ContainsKey(languageCode) || !_options.Value.CustomModels.TryGetValue(languageCode, out string modelPath) || !File.Exists(modelPath))
            {
                //_recognizedText = await _whisperNet.TranscribeAsync(audioFilePath, languageCode);
                _logger.LogInformation($"Распознавание _whisperFasterDocker");
                _logger.LogWarning($"##############");
                recognizedText = await _whisperFasterDocker.TranscribeAsync(audioFilePath);
            }
            // Иначе - WhisperCpp Api
            else
            {
                try
                {
                    if (_modelPathWhisperCpp != modelPath)
                    {
                        _logger.LogInformation($"Загрузка модели {modelPath}");
                        await _whisperCpp.LoadModelAsync(modelPath);
                        _modelPathWhisperCpp = modelPath;
                    }

                    _logger.LogInformation($"Распознавание _whisperCpp");
                    _logger.LogWarning($"##############");
                    recognizedText = await _whisperCpp.TranscribeAsync(audioFilePath);
                }
                catch
                {
                    recognizedText = await _whisperFasterDocker.TranscribeAsync(audioFilePath);
                }
            }
            return (recognizedText, detectedLanguage, languageCode);
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            throw new FailedWhisperRequestException("Whisper Error: ", e);
        }
        finally
        {
            Files.DeleteFilesByPath(audioFilePath);
        }
    }

    private async Task ProcessOllamaAndUpdateEntityAsync(long? entityId, string recognizedText, string languageCode, string detectedLanguage, string preText, string modelName, IConfiguration Configuration, TodoItem item, CancellationToken stoppingToken)
    {
        // OLLAMA
        var (ConStringDBA, DbType, Scheme) = GetDbConnectionInfo(item);
        try
        {
            string responseOllamaText = "0";
            if (recognizedText.Length > 20)
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
        }
        catch (Exception ex)
        {
            item.ProcessingMessage = ex.StackTrace;
            item.LastError = ex.Message;
            ConsoleCol.WriteLine("Ошибка при обработке Ollama и обновлении сущности EFCore: " + ex.Message, ConsoleColor.Red);
        }
        finally
        {
            item.CompletedKeys++;
        }
    }

    private async Task<bool> TryConvertAudio(byte[] left, byte[] right, string type, string eventCode, string path)
    {
        bool result = await DbToAudioConverter.FFMpegDecoder(left, right, type, path, _configuration);
        return result || await DbToAudioConverter.FFMpegDecoder(left, right, eventCode, path, _configuration);
    }

    private async Task FinalizeProcessing(TodoItem item, CancellationToken ct)
    {
        item.ProcessingMessage = item.IsCyclic && !await ReloadIsStopPressedByItemId(item.Id)
            ? $"Обработано {item.CompletedKeys}/{item.TotalKeys}. Ожидание повторного запуска..."
            : $"Процесс остановлен. Выполнено: {item.CompletedKeys}/{item.TotalKeys}";

        item.Statistic = "";

        await UpdateTodoItemStateAsync(item, ct);
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
    private async Task UpdateTodoItemStateAsync(TodoItem item, CancellationToken _stoppingToken)
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
            todoItemFromDb.ProcessingMessage = item.ProcessingMessage;
            todoItemFromDb.LanguageCounts = item.LanguageCounts;
            todoItemFromDb.LanguageNames = item.LanguageNames;
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
            todoItemFromDb.Statistic = string.Empty;
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

