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

// WebSockets (Box Arena Test)
app.UseWebSockets();

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws")
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            // Get the singleton GameServer.Core.Runners.DefaultNetworkRunner
            var runner = context.RequestServices.GetService<GameServer.Core.Interfaces.INetworkRunner>() as GameServer.Core.Runners.DefaultNetworkRunner;
            if (runner != null)
            {
                await runner.WebTransport.AcceptWebSocketAsync(webSocket);
            }
            return;
        }
        else
        {
            context.Response.StatusCode = 400;
        }
    }
    else
    {
        await next(context);
    }
});

// Security and Authorization Middlewares (to be added in Step 3)
app.UseAuthentication();
app.UseAuthorization();

// Lobby APIs
app.MapGet("/api/rooms", (GameServer.Core.Managers.RoomManager roomManager) => 
{
    return Results.Ok(roomManager.GetRooms());
});

app.MapPost("/api/rooms", (GameServer.Core.Managers.RoomManager roomManager, RoomCreateArgs args) => 
{
    var room = roomManager.CreateRoom(args.Name, args.MaxPlayers);
    return Results.Ok(room);
});

// SignalR Hub Endpoint
app.MapHub<GameServer.App.Hubs.DashboardHub>("/dashboardHub");

// Minimal API veya Controller Endpoint'leri
app.MapControllers();

app.MapGet("/", () => "Game Server Dashboard (API)");

app.Run();

public record RoomCreateArgs(string Name, int MaxPlayers);
