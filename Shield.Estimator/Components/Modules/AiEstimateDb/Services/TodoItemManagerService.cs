using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.SqliteModel;
using Shield.Estimator.Shared.Components.Modules._Shared;

namespace Shield.Estimator.Shared.Components.Modules.AiEstimateDb.Services;

public class TodoItemManagerService
{
    private readonly IHubContext<TodoHub> _hubContext;
    private readonly IDbContextFactory<SqliteDbContext> _sqliteFactory;
    private readonly ILogger<TodoItemManagerService> _logger;

    public TodoItemManagerService(
        IHubContext<TodoHub> hubContext,
        IDbContextFactory<SqliteDbContext> sqliteFactory,
        ILogger<TodoItemManagerService> logger)
    {
        _hubContext = hubContext;
        _sqliteFactory = sqliteFactory;
        _logger = logger;
    }

    public async Task UpdateItemStateAsync(TodoItem item, CancellationToken ct)
    {
        using var context = _sqliteFactory.CreateDbContext();
        var todoItemFromDb = await context.TodoItems.FindAsync(item.Id);
        if (todoItemFromDb != null)
        {
            todoItemFromDb.Id = item.Id;
            todoItemFromDb.IsRunning = item.IsRunning;
            todoItemFromDb.CompletedKeys = item.CompletedKeys;
            todoItemFromDb.CompletedLanguageDetect = item.CompletedLanguageDetect;
            todoItemFromDb.TotalKeys = item.TotalKeys;
            todoItemFromDb.IsStopPressed = item.IsStopPressed;
            todoItemFromDb.IsRunPressed = item.IsRunPressed;
            todoItemFromDb.Statistic = item.Statistic;
            todoItemFromDb.ProcessingMessage = item.ProcessingMessage;
            todoItemFromDb.LastError = item.LastError;
            todoItemFromDb.LanguageCounts = item.LanguageCounts;
            todoItemFromDb.LanguageNames = item.LanguageNames;
            await context.SaveChangesAsync();
        }
        await _hubContext.Clients.All.SendAsync("UpdateTodos", todoItemFromDb, ct);
    }

    public async Task StopProcessingAsync(TodoItem item, string message, CancellationToken ct)
    {
        
        using var context = _sqliteFactory.CreateDbContext();
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
            todoItemFromDb.LastError = item.LastError;
            await context.SaveChangesAsync();
        }
        await _hubContext.Clients.All.SendAsync("UpdateTodos", todoItemFromDb, ct);
    }

}

