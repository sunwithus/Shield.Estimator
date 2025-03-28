//AiBackGroundService.cs

using Microsoft.EntityFrameworkCore;

using Shield.Estimator.Shared.Components.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.SqliteModel;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.Sprutora;
using Shield.Estimator.Business.Services;
using Polly;
using Polly.Retry;
using Microsoft.Extensions.Options;
using Shield.Estimator.Business.Options.WhisperOptions;
using MudBlazor;
using System.Collections.Concurrent;
using System.Data;
using System.Threading.Channels;
using Oracle.ManagedDataAccess.Client;
using Npgsql;


namespace Shield.Estimator.Shared.Components.Modules.AiEstimateDb.Services;
public class AiBackgroundService : BackgroundService
{
    private const int ProcessingDelayMs = 11_005;
    private const int MaxWhisperOllamaGap = 5;
    
    private readonly ILogger<AiBackgroundService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AudioProcessorService _audioProcessor;
    private readonly WhisperProcessingService _whisperProcessor;
    private readonly LanguageDetectionService _languageDetection;
    private readonly TodoItemManagerService _todoItemManager;
    private readonly KoboldService _kobold;

    private readonly IConfiguration _configuration;
    private readonly IOptions<WhisperCppOptions> _options; // для CustomModels
    
    private readonly IDbContextFactory<SqliteDbContext> _sqliteDbContext;
    private readonly IDbContextFactory _dbContextFactory;
    
    private List<string> _ignoreRecordTypes;
    private string _preTextTranslate;

    private Channel<SprSpeechTable> _whisperFasterDockerChannel;
    private Channel<SprSpeechTable> _whisperCppChannel;


    public AiBackgroundService(AudioProcessorService audioProcessor, WhisperProcessingService whisperProcessor, LanguageDetectionService languageDetection, TodoItemManagerService todoItemManager, KoboldService kobold, ILogger<AiBackgroundService> logger, IConfiguration configuration, IDbContextFactory<SqliteDbContext> sqliteDbContext, IDbContextFactory dbContextFactory, IOptions<WhisperCppOptions> options)
    {
        _logger = logger;
        _configuration = configuration;
        _kobold = kobold;
        _options = options;

        _audioProcessor = audioProcessor;
        _whisperProcessor = whisperProcessor;
        _languageDetection = languageDetection;
        _todoItemManager = todoItemManager;

        _sqliteDbContext = sqliteDbContext;
        _dbContextFactory = dbContextFactory;

        // повторяет операцию с экспоненциальной выдержкой, начиная с 2 секунд. 1,2,4,8,16 секунд
        _retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

        // Инициализация переменных в конструкторе
        _ignoreRecordTypes = _configuration.GetSection("AudioConverter:IgnoreRecordTypes").Get<List<string>>();
        _preTextTranslate = _configuration["PretextTranslate"] ?? "";

    }

    private async Task<BaseDbContext> CreateDbContext(TodoItem item) => await _dbContextFactory.CreateDbContext(item.DbType, SelectDb.ConStringDBA(item), item.Scheme);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ProcessingDelayMs, ct);
                await ProcessItemsBatchAsync(ct);
            }
            catch (TaskCanceledException ex)
            {
                // Корректная обработка отмены
                _logger.LogWarning(ex, "TaskCanceledException (AiBackGroundService)");
                break;
            }
            catch (Exception ex) when (ex is not TaskCanceledException)
            {
                _logger.LogError(ex, "SqLiteDatabase processing error (AiBackGroundService)");
            }
        }
    }
    //перебираем список задач, проверяем нужно ли запускать задачу
    private async Task ProcessItemsBatchAsync(CancellationToken ct)
    {
        await using var context = _sqliteDbContext.CreateDbContext();
        var items = await context.TodoItems.AsNoTracking().ToListAsync(ct);

        foreach (var item in items)
        {
            if (await ShouldProcessStart(item))
            {
                ct.ThrowIfCancellationRequested();
                await ProcessItemAsync(item, ct);
            }
        }
    }
    private async Task<bool> ShouldProcessStart(TodoItem item)
    {
        if (!await ReloadIsRunPressedByItemId(item.Id))
        {
            await _todoItemManager.StopProcessingAsync(item, "Готово к запуску. 💤", CancellationToken.None);
            return false;
        }

        if (item.IsExecutionTime && !IsWithinExecutionTime(item))
        {
            item.ProcessingMessage = $"Запуск в {item.StartExecutionTime}... ⌛";
            await _todoItemManager.UpdateItemStateAsync(item, CancellationToken.None);
            return false;
        }
        return true;
    }

    private bool IsWithinExecutionTime(TodoItem item)
    {
        var now = DateTime.Now.TimeOfDay;
        return item.StartExecutionTime <= item.EndExecutionTime
            ? now > item.StartExecutionTime && now < item.EndExecutionTime
            : now > item.StartExecutionTime || now < item.EndExecutionTime;
    }

    // задача в списке todo => извлечение аудиоданных, обработка, запись результата в БД
    private async Task ProcessItemAsync(TodoItem item, CancellationToken ct)
    {
        try
        {
            await InitializeItemCounters(item, ct);
            await using var context = await CreateDbContext(item);

            var audioList = await EFCoreQuery.GetSpeechRecords(item.StartDateTime, item.EndDateTime, item.MoreThenDuration, context, _ignoreRecordTypes);
            if (!audioList.Any())
            {
                await HandleNoResults(item, ct);
                return;
            }

            _whisperFasterDockerChannel = Channel.CreateUnbounded<SprSpeechTable>();
            _whisperCppChannel = Channel.CreateUnbounded<SprSpeechTable>();

            _logger.LogInformation($"\nAudioList count = {audioList.Count}");
            await ProcessAudioRecords(item, audioList, ct);
        }
        catch (Npgsql.NpgsqlException ex)
        {
            item.LastError = DateTime.Now + " => " + ex.Message;
            _logger.LogError($"Npgsql.NpgsqlException => Ошибка подключения к БД \n {ex.Message}");
            await HandleProcessingError(item, ex, ct);
        }
        catch (Oracle.ManagedDataAccess.Client.OracleException ex)
        {
            item.LastError = DateTime.Now + " => " + ex.Message;
            _logger.LogError($"Oracle.ManagedDataAccess => Ошибка подключения к БД \n {ex.Message}");
            await HandleProcessingError(item, ex, ct);
        }
        catch (Exception ex) 
        {
            item.LastError = DateTime.Now + " => " + ex.Message;
            _logger.LogError($"{ex.Source} \n {ex.Message} \n {ex.GetType}");
            await HandleProcessingError(item, ex, ct);
        }
    }


    
    // TotalKeys, CompletedKeys и др. выставить в нулевые значения
    private async Task InitializeItemCounters(TodoItem item, CancellationToken ct)
    {
        item.ResetCounters();
        item.ProcessingMessage = "Идёт выполнение... ⌛";
        await _todoItemManager.UpdateItemStateAsync(item, ct);
        await Task.Delay(1200, ct);
    }

    // Когда нет данных для обработки
    private async Task HandleNoResults(TodoItem item, CancellationToken ct)
    {
        if (!item.IsCyclic)
        {
            item.IsRunPressed = false;
            item.IsStopPressed = true;
            item.ProcessingMessage = "Нет данных для обработки. Завершено.";
        }
        else
        {
            item.ProcessingMessage = $"Нет данных для обработки. Ожидание повторного запуска.";
        }

        await _todoItemManager.UpdateItemStateAsync(item, ct);
    }

    private async Task HandleProcessingError(TodoItem item, Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException)
        {
            _logger.LogWarning("Processing was cancelled by user");
            item.ProcessingMessage = "Обработка успешно отменена";
        }
        else
        {
            _logger.LogError(ex, "Error processing item {Title}", item.Title);
            item.ProcessingMessage = $"Error: {ex.Message}";
            item.LastError = $"{DateTime.Now}: {ex.Message}";

            if (ex is Npgsql.PostgresException or Npgsql.NpgsqlException or Oracle.ManagedDataAccess.Client.OracleException)
            {
                item.IsRunning = false;
                item.IsStopPressed = true;
                item.IsRunPressed = false;
                item.ProcessingMessage = $"Ошибка при обращении к БД.";
                item.LastError = $"Ошибка при обращении к БД.";
            }
        }
        await _todoItemManager.UpdateItemStateAsync(item, ct);
    }

    private async Task ProcessAudioRecords(TodoItem item, List<SprSpeechTable> audioList, CancellationToken ct)
    {
        item.Statistic = "";
        item.TotalKeys = audioList.Count;
        item.IsRunning = true;
        item.ProcessingMessage = "Идёт выполнение... ⌛";

        await using var context = await CreateDbContext(item);

        var processingTasks = new List<Task>
        {
            ProcessLanguageDetectionAsync(item, audioList, context, ct),
            ProcessChannelAsync(_whisperFasterDockerChannel, item, ct),
            ProcessChannelAsync(_whisperCppChannel, item, ct)
        };

        await Task.WhenAll(processingTasks);
        await FinalizeProcessing(item, ct);
    }

    private async Task ProcessLanguageDetectionAsync(TodoItem item, List<SprSpeechTable> audioList, BaseDbContext context, CancellationToken ct)
    {
        // Словарь для подсчета количества языков по их коду
        var languageCounts = new ConcurrentDictionary<string, int>();
        // Словарь для хранения соответствия кодов и названий языков
        var languageNames = new ConcurrentDictionary<string, string>();

        foreach (var audioRecord in audioList)
        {
            if (await ShouldStopProcessing(item)) return;

            Dictionary<string, string> lang = await ProcessLanguageDetect(item, audioRecord, context, ct);
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

            // Добавляем запись в соответствующую очередь
            if (_options.Value.CustomModels.ContainsKey(lang.Keys.FirstOrDefault()))
            {
                await _whisperCppChannel.Writer.WriteAsync(audioRecord, ct);
            }
            else
            {
                await _whisperFasterDockerChannel.Writer.WriteAsync(audioRecord, ct);
            }
            await _todoItemManager.UpdateItemStateAsync(item, ct);
        }
        // Сигнализируем о завершении
        _whisperCppChannel.Writer.Complete();
        _whisperFasterDockerChannel.Writer.Complete();


        item.Statistic = $"{DateTime.Now}: {item.CompletedLanguageDetect}/{item.TotalKeys}";
        await _todoItemManager.UpdateItemStateAsync(item, ct);
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

            await _audioProcessor.ConvertByteArrayToFile(audioDataLeft, audioDataRight, audioFilePath, recordType, eventCode);

            (string languageCode, string detectedLanguage, double confidence) = await _languageDetection.DetectLanguageAsync(audioFilePath);

            await using var localContext = await CreateDbContext(item);
            await EFCoreQuery.UpdateLangInfo(entity.SInckey, detectedLanguage, languageCode, localContext);
            return new Dictionary<string, string>() { { languageCode, detectedLanguage.Split(" ")[0] } };
        }
        finally
        {
            Files.DeleteFilesByPath(audioFilePath);
        }
    }

    private async Task ProcessChannelAsync(Channel<SprSpeechTable> channel, TodoItem item, CancellationToken ct)
    {
        item.ProcessingMessage = $"Выполнение: {item.CompletedKeys}/{item.TotalKeys}";
        await _todoItemManager.UpdateItemStateAsync(item, ct);
        try
        {
            await foreach (var entity in channel.Reader.ReadAllAsync(ct))
            {
                if (await ShouldStopProcessing(item)) return;

                await ProcessSingleAudioEntity(item, entity, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ProcessChannelAsync => OperationCanceledException");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"ProcessChannelAsync => OperationCanceledException {ex.Message}");
            await HandleProcessingError(item, ex, ct);
        }
    }

    private async Task ProcessSingleAudioEntity(TodoItem item, SprSpeechTable entity, CancellationToken ct)
    {
        if (await ShouldStopProcessing(item)) return;

        await using var context = await CreateDbContext(item);
        var freshEntity = await ReloadEntityFromDatabase(entity.SInckey, context);

        // Db => get audio (left, right, recordType, eventCode)
        var (audioDataLeft, audioDataRight, recordType, eventCode) = await EFCoreQuery.GetAudioDataAsync(freshEntity.SInckey, context);
        _logger.LogInformation($"Audio data (left, right, recordType, eventCode) for key {freshEntity.SInckey} loaded. recordType = " + recordType);

        // FFMpeg or Decoder => audio to folder + Whisper
        string audioFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + $"{freshEntity.SInckey}.wav"); //string audioFilePath = Path.Combine(_configuration["AudioPathForBGService"], $"{entity.SInckey}.wav");
        await _audioProcessor.ConvertByteArrayToFile(audioDataLeft, audioDataRight, audioFilePath, recordType, eventCode);
        if (!File.Exists(audioFilePath)) return;

        var transcribedText = await _whisperProcessor.TranscribeAudioAsync(audioFilePath, freshEntity); //внутри метода удаляем файл audioFilePath
        item.ProcessedWhisper++;

        if (transcribedText == null) return;
        _logger.LogInformation("\n" + transcribedText);

        _ = ProcessAiResultsAndUpdateEntityAsync(freshEntity, transcribedText, item, ct);

        _logger.LogWarning($"item.ProcessedWhisper - item.CompletedKeys = {item.ProcessedWhisper - item.CompletedKeys}");
        // разрешить "вырываться вперёд не более чем на N раз" и ProcessedAi
        int nnn = 60 * (MaxWhisperOllamaGap - 1 + item.ProcessedWhisper - item.CompletedKeys); // 60 * 3 раз по 5 сек = 600с = 10м
        while (item.ProcessedWhisper - MaxWhisperOllamaGap > item.CompletedKeys)
        {
            if (await ShouldStopProcessing(item)) return;

            nnn--;
            await Task.Delay(5000, ct);
            _logger.LogWarning($"Delay is done. AI / WHISPER => {item.CompletedKeys}/{item.ProcessedWhisper} \nWait until nnn == 0. Current nnn => {nnn} ");
            if (nnn <= 0)
            {
                item.CompletedKeys = item.ProcessedWhisper; //сброс счетчика
                break;
            }
        }
    }

    private async Task ProcessAiResultsAndUpdateEntityAsync(SprSpeechTable entity, string transcribedText, TodoItem item, CancellationToken ct)
    {
        try
        {
            if (transcribedText.Length > 20)
            {
                string preText = await Params.GetPreTextAsync(entity.SSourcename);
                string languageCode = entity.SPostid;
                string detectedLanguage = entity.SBelong;


                var aiResponse = await _kobold.GenerateTextAsync(preText + transcribedText);
                if (await ShouldStopProcessing(item)) return;

                if (languageCode != "ru" && !string.IsNullOrEmpty(languageCode))
                {
                    transcribedText = $"Перевод с {detectedLanguage}: " + await _kobold.GenerateTextAsync(_preTextTranslate + transcribedText);
                }

                await UpdateEntityInDatabase(entity, transcribedText, aiResponse, item);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "\nError in ProcessAiResultsAndUpdateEntityAsync");
        }
        finally
        {
            item.CompletedKeys++;
            item.ProcessingMessage = $"Обработано: {item.CompletedKeys}/{item.TotalKeys}";
            await _todoItemManager.UpdateItemStateAsync(item, ct);
        }
    }

    private async Task UpdateEntityInDatabase(SprSpeechTable entity, string text, string aiResponse, TodoItem item)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            await using var context = await CreateDbContext(item);
            await EFCoreQuery.InsertOrUpdateCommentAsync(entity.SInckey, text, entity.SBelong, aiResponse, entity.SPostid, context, item.BackLight);
        });
    }

    private async Task<SprSpeechTable> ReloadEntityFromDatabase(long? sInckey, BaseDbContext context)
    {
        //В Oralce 11.2 по другому не работает
        return (await context.SprSpeechTables
            .Where(x => x.SInckey == sInckey)
            .ToListAsync())
            .FirstOrDefault();
    }

    private async Task FinalizeProcessing(TodoItem item, CancellationToken ct)
    {
        if (!item.IsCyclic || await ReloadIsStopPressedByItemId(item.Id))
        {
            item.IsRunning = false;
            item.IsRunPressed = false;
            item.IsStopPressed = true;
        }
        item.ProcessingMessage = $"Выполнено: {item.CompletedKeys}/{item.TotalKeys}." + (item.IsCyclic ? " Ожидание повторного запуска..." : " Завершено");
        item.Statistic = string.Empty;

        await _todoItemManager.UpdateItemStateAsync(item, ct);
    }
    private async Task<bool> ShouldStopProcessing(TodoItem item)
    {
        if(await ReloadIsStopPressedByItemId(item.Id))
        {
            await _todoItemManager.StopProcessingAsync(item, $"{DateTime.Now} Остановлено: {item.CompletedKeys}/{item.TotalKeys}", CancellationToken.None);
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

}