using GameServer.Core.Managers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.App.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = "ApiKey")]
    public class AdminController : ControllerBase
    {
        private readonly PlayerManager _playerManager;
        private readonly RoomManager _roomManager;
        private readonly GameServer.App.Services.ServerLifecycleManager _lifecycleManager;

        public AdminController(PlayerManager playerManager, RoomManager roomManager, GameServer.App.Services.ServerLifecycleManager lifecycleManager)
        {
            _playerManager = playerManager;
            _roomManager = roomManager;
            _lifecycleManager = lifecycleManager;
        }

        // [POST] /api/admin/server/start
        [HttpPost("server/start")]
        public IActionResult StartServer()
        {
            if (_lifecycleManager.IsRunning)
                return BadRequest(new { message = "Server is already running." });

            _lifecycleManager.StartServer(7777);
            return Ok(new { message = "Game Server started successfully." });
        }

        // [POST] /api/admin/server/stop
        [HttpPost("server/stop")]
        public IActionResult StopServer()
        {
            if (!_lifecycleManager.IsRunning)
                return BadRequest(new { message = "Server is not running." });

            _lifecycleManager.StopServer();
            return Ok(new { message = "Game Server stopped successfully." });
        }

        // [POST] /api/admin/kick/{playerId}
        [HttpPost("kick/{playerId}")]
        public IActionResult KickPlayer(string playerId)
        {
            var success = _playerManager.KickPlayer(playerId);
            if (success)
            {
                return Ok(new { message = $"Player {playerId} has been kicked." });
            }
            return NotFound(new { message = $"Player {playerId} not found or could not be kicked." });
        }

        // [POST] /api/admin/room/{roomId}/close
        [HttpPost("room/{roomId}/close")]
        public IActionResult CloseRoom(string roomId)
        {
            var success = _roomManager.CloseRoom(roomId);
            if (success)
            {
                return Ok(new { message = $"Room {roomId} has been closed." });
            }
            return NotFound(new { message = $"Room {roomId} not found or could not be closed." });
        }

        // [POST] /api/admin/broadcast
        [HttpPost("broadcast")]
        public IActionResult BroadcastMessage([FromBody] BroadcastRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { message = "Message cannot be empty." });
            }

            _playerManager.BroadcastMessage(request.Message);
            return Ok(new { message = "Broadcast message sent to all players." });
        }
    }

    public class BroadcastRequest
    {
        public string Message { get; set; } = string.Empty;
    }
}
