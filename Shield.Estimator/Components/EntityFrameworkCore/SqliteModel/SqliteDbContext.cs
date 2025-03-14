//SqliteDbContext.cs

using Microsoft.EntityFrameworkCore;
using System.Text.Json;

using Shield.Estimator.Shared.Components.Pages;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Concurrent;



namespace Shield.Estimator.Shared.Components.EntityFrameworkCore.SqliteModel;

// Определение контекста базы данных для работы с Entity Framework Core
public class SqliteDbContext : DbContext
{
    public DbSet<TodoItem> TodoItems { get; set; }

    public SqliteDbContext() => Database.EnsureCreated();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TodoItem>()
            .Property(e => e.LanguageCounts)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<ConcurrentDictionary<string, int>>(v, (JsonSerializerOptions)null));

        modelBuilder.Entity<TodoItem>()
            .Property(e => e.LanguageNames)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                v => JsonSerializer.Deserialize<ConcurrentDictionary<string, string>>(v, (JsonSerializerOptions)null));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        string pathToSqlite = Path.Combine(AppContext.BaseDirectory, "todos.db");
        optionsBuilder.UseSqlite($"Data Source={pathToSqlite}");
    }

    public async Task<List<TodoItem>> LoadTodos()
    {
        return await TodoItems.ToListAsync();
    }

    public async Task UpdateTodo(TodoItem todo)
    {
        TodoItems.Update(todo);
        await SaveChangesAsync();
    }

    public async Task DeleteTodo(TodoItem todo)
    {
        TodoItems.Remove(todo);
        await SaveChangesAsync();
    }
    public async Task AddTodo(TodoItem todo)
    {
        await TodoItems.AddAsync(todo);
        await SaveChangesAsync();
    }

    public async Task<TodoItem> LoadTodoItem(int id)
    {
        var todoItem = await TodoItems.FindAsync(id);
        if (todoItem == null)
        {
            throw new KeyNotFoundException($"TodoItem with ID {id} not found.");
        }
        return todoItem;
    }

    public async Task<TodoItem> LoadTodoItem(string title)
    {
        var todoItem = await TodoItems.FirstOrDefaultAsync(t => t.Title == title);
        if (todoItem == null)
        {
            throw new KeyNotFoundException($"TodoItem with Title '{title}' not found.");
        }
        return todoItem;
    }
}
