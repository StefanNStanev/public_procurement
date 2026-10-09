using Microsoft.EntityFrameworkCore;
using Porachki.Api.Api;
using Porachki.Api.Data;
using Porachki.Api.Estimates;
using Porachki.Api.Sync;
using Porachki.Api.Sync.Eop;
using Porachki.Api.Sync.Ted;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.Configure<SyncOptions>(config.GetSection("Sync"));
builder.Services.Configure<EstimateOptions>(config.GetSection("Estimates"));

var connectionString = config.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Липсва ConnectionStrings:Default");
var serverVersion = new MySqlServerVersion(Version.Parse(config["MySqlServerVersion"] ?? "8.4.0"));
builder.Services.AddDbContext<AppDbContext>(o => o.UseMySql(connectionString, serverVersion,
    my => my.EnableRetryOnFailure(5).CommandTimeout(120)));

const string userAgent = "Porachki/1.0 (+https://github.com/; public procurement aggregator)";
builder.Services.AddHttpClient<EopOcdsSource>(c => { c.Timeout = TimeSpan.FromMinutes(10); c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent); });
builder.Services.AddHttpClient<TedSource>(c => { c.Timeout = TimeSpan.FromMinutes(2); c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent); });

// AI оценка на поръчки
builder.Services.AddHttpClient<EopDocumentClient>(c => { c.Timeout = TimeSpan.FromMinutes(5); c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent); });
builder.Services.AddHttpClient<ClaudeClient>(c => c.Timeout = TimeSpan.FromMinutes(15));
builder.Services.AddSingleton<DocumentTextExtractor>();
builder.Services.AddScoped<EstimateService>();
builder.Services.AddSingleton<EstimateWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EstimateWorker>());

builder.Services.AddSingleton<MunicipalityResolver>();
builder.Services.AddScoped<TenderWriter>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<TenderQuery>();
builder.Services.AddSingleton<SyncWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SyncWorker>());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Обществени поръчки API", Version = "v1" });
    o.SupportNonNullableReferenceTypes();
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

await EnsureDatabaseAsync(app);

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.MapControllers();
app.MapGet("/health", () => Results.Ok("ok"));

app.Run();

static async Task EnsureDatabaseAsync(WebApplication app)
{
    var log = app.Services.GetRequiredService<ILogger<Program>>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // За начало: схемата се създава автоматично. За продукция преминете към миграции
            // (dotnet ef migrations add Initial) и заменете с db.Database.MigrateAsync().
            await db.Database.EnsureCreatedAsync();
            await SchemaUpgrader.UpgradeAsync(db);
            return;
        }
        catch (Exception ex) when (attempt < 15)
        {
            log.LogWarning("Базата не е готова ({Message}). Нов опит след 4s…", ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(4));
        }
    }
}
