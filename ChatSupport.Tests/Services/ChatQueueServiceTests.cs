using ChatSupport.API.Models;
using ChatSupport.API.Services;
using ChatSupport.Tests.TestServices;
using Xunit;

namespace ChatSupport.Tests.Services
{
    public class ChatQueueServiceTests
    {
        private SupportTeam CreateTestTeam()
        {
            return new SupportTeam
            {
                TeamName = "Test Team",
                Agents = new List<Agent>
                {
                    new() { Id = 1, Name = "Senior", Seniority = Seniority.Senior, Shift = 1, IsActive = true },
                    new() { Id = 2, Name = "Junior", Seniority = Seniority.Junior, Shift = 1, IsActive = true }
                }
            };
        }

        [Fact]
        public async Task AssignChats_With1Senior1Junior_ProperlyDistributes5Chats()
        {
            // Arrange
            var service = new TestChatQueueService(new List<SupportTeam> { CreateTestTeam() });

            // Act - Create 5 sessions
            var sessionIds = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                sessionIds.Add(await service.CreateChatSession());
            }

            // Manually process queue since timers are disabled
            service.ProcessQueue();

            // Assert
            var agents = service.GetAllAgents();
            var senior = agents.First(a => a.Seniority == Seniority.Senior);
            var junior = agents.First(a => a.Seniority == Seniority.Junior);

            Assert.Equal(1, senior.CurrentChats);  // Senior gets 1
            Assert.Equal(4, junior.CurrentChats);  // Junior gets 4 (max capacity)
        }

[Fact]
public async Task CreateChatSession_WhenQueueFull_ThrowsException()
{
    // Arrange - Team with 1 junior agent (capacity 4, queue limit 6)
    var testTeam = new SupportTeam
    {
        TeamName = "Small Team",
        Agents = new List<Agent>
        {
            new() { 
                Id = 1, 
                Name = "Junior Agent", 
                Seniority = Seniority.Junior, 
                Shift = 1, 
                IsActive = true,
                CurrentChats = 0  // Explicitly set initial state
            }
        }
    };

    var service = new TestChatQueueService(new List<SupportTeam> { testTeam });

    // Fill the queue to capacity (6)
    // Need to prevent automatic assignment to hit queue limit
    for (int i = 0; i < 6; i++)
    {
        await service.CreateChatSession();
        // Don't process queue - keep chats in queue
    }

    // Verify we've hit queue capacity
    var queueSize = service.GetQueuedSessions().Count;
    var activeSessions = service.GetActiveSessions().Count;
    Assert.Equal(6, queueSize + activeSessions); // 6 = queue limit (4*1.5)

    // Act & Assert - 7th chat should be rejected
    var exception = await Assert.ThrowsAsync<Exception>(async () => 
        await service.CreateChatSession());
    
    Assert.Equal("Chat refused - queue is full", exception.Message);
}
}
}