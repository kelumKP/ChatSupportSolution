using ChatSupport.Domain;


namespace ChatSupport.Application.Services
{
    public interface IChatQueueService
    {
        Task<string> CreateChatSession();
        ChatSession GetSessionStatus(string sessionId);
        bool PollSession(string sessionId);
    }
}