using GameServer.App.Services;
using GameServer.Core.Managers;
using GameServer.Core.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// --- DI Registrations (Singleton) ---
builder.Services.AddSingleton<PlayerManager>();
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<NetworkMetrics>();

// Main classes of the game engine
builder.Services.AddSingleton<GameServer.Core.Interfaces.INetworkRunner, GameServer.Core.Runners.DefaultNetworkRunner>(); 
builder.Services.AddSingleton<GameServer.App.Services.ServerLifecycleManager>();

// --- Background Service (Game Server) ---
// Added as a HostedService. This allows it to run simultaneously with Kestrel.
builder.Services.AddHostedService<GameServerHostedService>();

// Preparation for Web API or Controllers (for Step 3)
builder.Services.AddControllers();
// SignalR (for Step 2)
builder.Services.AddSignalR();
// Broadcaster service
builder.Services.AddHostedService<SystemMetricsBroadcasterService>();

// Preparation for authorizing incoming requests
builder.Services.AddAuthentication(GameServer.App.Security.ApiKeyAuthenticationOptions.DefaultScheme)
    .AddScheme<GameServer.App.Security.ApiKeyAuthenticationOptions, GameServer.App.Security.ApiKeyAuthenticationHandler>(
        GameServer.App.Security.ApiKeyAuthenticationOptions.DefaultScheme, null);

builder.Services.AddAuthorization();

var app = builder.Build();

// --- Middleware Pipeline ---

// Serving files under wwwroot for SPA or Dashboard UI
app.UseStaticFiles();

app.UseRouting();

// Security and Authorization Middlewares (to be added in Step 3)
app.UseAuthentication();
app.UseAuthorization();


// SignalR Hub Endpoint
app.MapHub<GameServer.App.Hubs.DashboardHub>("/dashboardHub");

// Minimal API veya Controller Endpoint'leri
app.MapControllers();

app.MapGet("/", () => "Game Server Dashboard (API)");

app.Run();
