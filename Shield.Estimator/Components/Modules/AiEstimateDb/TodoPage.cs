//TodoPage.cs

using Shield.Estimator.Shared.Components.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using System;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.SqliteModel;
using Shield.Estimator.Shared.Components.Pages;

namespace Shield.Estimator.Shared.Components.Modules.AiEstimateDb
{
    public class DatabaseConnection
    {
        public static async Task<(bool, string)> Test(BaseDbContext context)
        {
            try
            {
                long? maxKey = null;
                await context.Database.OpenConnectionAsync();
                maxKey = await context.SprSpeechTables.MaxAsync(x => x.SInckey);
                ConsoleCol.WriteLine($"maxKey = {maxKey}", ConsoleColor.Green);
                await context.Database.CloseConnectionAsync();

                return (true, $"maxKey = {maxKey}");
            }
            catch (Exception ex)
            {
                ConsoleCol.WriteLine($"Connection Error: {ex.Message}", ConsoleColor.Red);
                return (false, $"Error: {ex.Message}");
            }
        }
    }
/*
    public class TodoListBase : ComponentBase
    {
        [Inject]
        public SqliteDbContext Sqlite { get; set; }

        public async Task<List<TodoItem>> LoadTodos()
        {
            return await Sqlite.TodoItems.ToListAsync();
        }

        public async Task UpdateTodo(TodoItem todo)
        {
            Sqlite.TodoItems.Update(todo);
            await Sqlite.SaveChangesAsync();
        }

        public async Task DeleteTodo(TodoItem todo)
        {
            Sqlite.TodoItems.Remove(todo);
            await Sqlite.SaveChangesAsync();
        }
        public async Task AddTodo(TodoItem todo)
        {
            await Sqlite.TodoItems.AddAsync(todo);
            await Sqlite.SaveChangesAsync();
        }
    }
*/
}
