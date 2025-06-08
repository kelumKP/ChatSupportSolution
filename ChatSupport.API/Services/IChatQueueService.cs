using ChatSupport.API.Models;

namespace ChatSupport.API.Services
{
    public interface IChatQueueService
    {
        Task<string> CreateChatSession();
        ChatSession GetSessionStatus(string sessionId);
        bool PollSession(string sessionId);
        void ProcessQueue();
    }
}