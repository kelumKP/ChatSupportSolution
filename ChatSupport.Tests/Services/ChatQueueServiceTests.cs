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

        [Fact]
public async Task AssignChats_With2Junior1Mid_ProperlyDistributes6Chats()
{
    // Arrange - Team with 2 juniors and 1 mid-level agent
    var testTeam = new SupportTeam
    {
        TeamName = "Test Team",
        Agents = new List<Agent>
        {
            new() { Id = 1, Name = "Junior 1", Seniority = Seniority.Junior, Shift = 1, IsActive = true },
            new() { Id = 2, Name = "Junior 2", Seniority = Seniority.Junior, Shift = 1, IsActive = true },
            new() { Id = 3, Name = "Mid-Level", Seniority = Seniority.MidLevel, Shift = 1, IsActive = true }
        }
    };

    var service = new TestChatQueueService(new List<SupportTeam> { testTeam });

    // Act - Create 6 sessions
    var sessionIds = new List<string>();
    for (int i = 0; i < 6; i++)
    {
        sessionIds.Add(await service.CreateChatSession());
    }

    // Process the queue to assign chats
    service.ProcessQueue();

    // Assert
    var agents = service.GetAllAgents();
    var junior1 = agents.First(a => a.Id == 1);
    var junior2 = agents.First(a => a.Id == 2);
    var midLevel = agents.First(a => a.Id == 3);

    // Each junior should get 3 chats (round-robin distribution to juniors first)
    Assert.Equal(3, junior1.CurrentChats);
    Assert.Equal(3, junior2.CurrentChats);
    
    // Mid-level should get 0 chats (juniors have capacity remaining)
    Assert.Equal(0, midLevel.CurrentChats);

    // Verify all 6 chats were assigned
    var activeSessions = service.GetActiveSessions();
    Assert.Equal(6, activeSessions.Count);
    
    // Verify distribution between juniors
    var junior1Sessions = activeSessions.Count(s => s.AssignedAgentId == 1);
    var junior2Sessions = activeSessions.Count(s => s.AssignedAgentId == 2);
    Assert.Equal(3, junior1Sessions);
    Assert.Equal(3, junior2Sessions);
}
    }
}