using ChatSupport.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChatSupport.API.Controllers
{
[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IChatQueueService _chatQueueService;

    public ChatController(IChatQueueService chatQueueService)
    {
        _chatQueueService = chatQueueService;
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartChat()
    {
        try
        {
            var sessionId = await _chatQueueService.CreateChatSession();
            return Ok(new { SessionId = sessionId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpGet("status/{sessionId}")]
    public IActionResult GetStatus(string sessionId)
    {
        var session = _chatQueueService.GetSessionStatus(sessionId);
        return session == null ? NotFound() : Ok(new
        {
            session.SessionId,
            session.CreatedAt,
            session.AssignedAt,
            session.AssignedAgentId,
            session.IsActive,
            Status = session.AssignedAt.HasValue ? "Assigned" : "In Queue"
        });
    }

    [HttpPost("poll/{sessionId}")]
    public IActionResult Poll(string sessionId)
    {
        var result = _chatQueueService.PollSession(sessionId);
        return Ok(new { IsActive = result });
    }
}
}