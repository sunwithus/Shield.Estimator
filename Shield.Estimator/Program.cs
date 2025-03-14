//Program.cs
global using Shield.Estimator.Shared.Components._SeedLibs;

using Shield.Estimator.Shared.Components;
using Shield.Estimator.Shared.Components.EntityFrameworkCore.SqliteModel;
using Shield.Estimator.Shared.Components.Modules._Shared;
using Shield.Estimator.Shared.Components.EntityFrameworkCore;
using Shield.Estimator.Business;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextFactory<SqliteDbContext>();

//WhisperService, KoboldService
builder.Services.AddBusinessServices(builder.Configuration);

//BackgroundService
builder.Services.AddHostedService<AiBackgroundService>();
builder.Services.AddHostedService<ReplBackgroundService>();

builder.Services.AddSingleton<IDbContextFactory, DbContextFactory>();

// SignalR
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 1024 * 1024 * 512;
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(60);
    options.HandshakeTimeout = TimeSpan.FromMinutes(60);
});

builder.Services.AddMudServices();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();

SelectDb.Configure(builder.Configuration); //for static Toolkit.cs
/*
if(!Directory.Exists(@"C:\temp"))
    Directory.CreateDirectory(@"C:\temp");
*/

var app = builder.Build();




app.UseRouting();
app.UseAntiforgery();
app.MapHub<ReplicatorHub>("/replicatorhub");
app.MapHub<TodoHub>("/todohub");

app.Map("about", () => "About page");
app.Map("todolist", () =>
{
    return "";
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

//app.Run();
app.Run("http://0.0.0.0:555");


///////////////////////////////////////////////////////////
// �������� ����������
var config = app.Services.GetRequiredService<IConfiguration>();
CreateDirectories(config);
///////////////////////////////////////////////////////////
void CreateDirectories(IConfiguration config)
{
    var directories = new[]
    {
    config["TempFilesDirectory"],
    config["AudioPathForReplicator"],
    config["TranslatedFilesFolder"]
    };

    foreach (var dir in directories)
    {
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}