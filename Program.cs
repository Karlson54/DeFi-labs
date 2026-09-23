using System.Text;
using DeFi.Data;
using DeFi.Models;
using DeFi.Services;
using Microsoft.EntityFrameworkCore;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables("DEFILAB_");

builder.Services.Configure<Web3Settings>(builder.Configuration.GetSection("Web3Settings"));
builder.Services.Configure<IndexerSettings>(builder.Configuration.GetSection("IndexerSettings"));
builder.Services.Configure<CorsSettings>(builder.Configuration.GetSection("CorsSettings"));

builder.Services.AddSingleton<IWeb3Factory, Web3Factory>();
builder.Services.AddSingleton<IDeploymentStateStore, DeploymentStateStore>();

builder.Services.AddDbContext<SwapIndexerDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SwapIndexer")));

const string CorsPolicyName = "ReactFrontend";
var corsOrigin = builder.Configuration["CorsSettings:AllowedOrigin"] ?? "http://localhost:5173";

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
        policy.WithOrigins(corsOrigin).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddHostedService<SwapIndexerService>();

var app = builder.Build();

app.UseCors(CorsPolicyName);

app.MapGet("/api/swaps", async (string? trader, SwapIndexerDbContext db) =>
{
    var query = db.SwapRecords.AsNoTracking().OrderByDescending(r => r.BlockNumber).AsQueryable();

    if (!string.IsNullOrWhiteSpace(trader))
    {
        var normalized = trader.ToLowerInvariant();
        query = query.Where(r => r.Trader.ToLower() == normalized);
    }

    var records = await query.Take(500).ToListAsync();

    return Results.Ok(records.Select(r => new
    {
        r.TransactionHash,
        r.BlockNumber,
        r.Trader,
        r.TokenIn,
        r.AmountIn,
        r.AmountOut,
        r.FeeBps,
        r.IndexedAtUtc
    }));
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

Console.WriteLine("Web3-індексатор та REST API запущено на http://localhost:5000.");

await app.RunAsync();