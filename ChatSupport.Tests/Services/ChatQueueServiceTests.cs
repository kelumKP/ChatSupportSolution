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
            service.SetTestTime(new DateTime(2023, 1, 1, 10, 0, 0)); // 10AM - office hours

            // Act - Create 5 sessions
            var sessionIds = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                sessionIds.Add(await service.CreateChatSession());
            }

            // Manually process queue
            service.ProcessQueue();

            // Assert
            var agents = service.GetAllAgents();
            var senior = agents.First(a => a.Seniority == Seniority.Senior);
            var junior = agents.First(a => a.Seniority == Seniority.Junior);

            Assert.Equal(1, senior.CurrentChats);
            Assert.Equal(4, junior.CurrentChats);
        }

        [Fact]
        public async Task CreateChatSession_WhenQueueFull_ThrowsException()
        {
            // Arrange
            var testTeam = new SupportTeam
            {
                TeamName = "Small Team",
                Agents = new List<Agent>
                {
                    new() { Id = 1, Name = "Junior Agent", Seniority = Seniority.Junior, Shift = 1, IsActive = true }
                }
            };

            var service = new TestChatQueueService(new List<SupportTeam> { testTeam });
            service.SetTestTime(new DateTime(2023, 1, 1, 10, 0, 0)); // 10AM - office hours

            // Fill the queue to capacity (6)
            for (int i = 0; i < 36; i++)
            {
                await service.CreateChatSession();
            }

            // Act & Assert
            var exception = await Assert.ThrowsAsync<Exception>(async () =>
                await service.CreateChatSession());

            Assert.Equal("Chat refused - queue and overflow are full", exception.Message);
        }

        [Fact]
        public async Task AssignChats_With2Junior1Mid_ProperlyDistributes6Chats()
        {
            // Arrange
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
            service.SetTestTime(new DateTime(2023, 1, 1, 10, 0, 0)); // 10AM - office hours

            // Act - Create 6 sessions
            var sessionIds = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                sessionIds.Add(await service.CreateChatSession());
            }

            service.ProcessQueue();

            // Assert
            var agents = service.GetAllAgents();
            var junior1 = agents.First(a => a.Id == 1);
            var junior2 = agents.First(a => a.Id == 2);
            var midLevel = agents.First(a => a.Id == 3);

            Assert.Equal(3, junior1.CurrentChats);
            Assert.Equal(3, junior2.CurrentChats);
            Assert.Equal(0, midLevel.CurrentChats);
        }
    }
}