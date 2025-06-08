using ChatSupport.API.Models;
using ChatSupport.API.Services;
using ChatSupport.Tests.TestServices;
using Xunit;

namespace ChatSupport.Tests.Services
{
    public class ChatQueueServiceTests
    {
        [Fact]
        public async Task AssignChats_With1Senior1Junior_ProperlyDistributes5Chats()
        {
            // Arrange
            var testTeam = new SupportTeam
            {
                TeamName = "Test Team",
                Agents = new List<Agent>
                {
                    new() { Id = 1, Name = "Senior", Seniority = Seniority.Senior, Shift = 1, IsActive = true },
                    new() { Id = 2, Name = "Junior", Seniority = Seniority.Junior, Shift = 1, IsActive = true }
                }
            };

            var service = new TestChatQueueService(new List<SupportTeam> { testTeam });
            service.SetTestTime(new DateTime(2023, 1, 1, 10, 0, 0)); // 10AM - office hours

            // Act - Create 5 sessions
            for (int i = 0; i < 5; i++)
            {
                await service.CreateChatSession();
            }
            service.ProcessQueue();

            // Assert
            var agents = service.GetAllAgents();
            var senior = agents.First(a => a.Id == 1);
            var junior = agents.First(a => a.Id == 2);

            // Verify chat distribution (4 to junior, 1 to senior)
            Assert.Equal(4, junior.CurrentChats);
            Assert.Equal(1, senior.CurrentChats);
        }

        [Fact]
        public async Task OverflowTeam_ActivatesDuringOfficeHours_WhenMainTeamFull()
        {
            // Arrange
            var testTeam = new SupportTeam
            {
                TeamName = "Test Team",
                Agents = new List<Agent>
                {
                    new() { Id = 1, Name = "Junior 1", Seniority = Seniority.Junior, Shift = 1, IsActive = true },
                    new() { Id = 2, Name = "Junior 2", Seniority = Seniority.Junior, Shift = 1, IsActive = true }
                }
            };

            var service = new TestChatQueueService(new List<SupportTeam> { testTeam });
            service.SetTestTime(new DateTime(2023, 1, 1, 10, 0, 0)); // 10AM - office hours

            // Fill main team capacity (8 chats total - 4 per junior)
            for (int i = 0; i < 8; i++)
            {
                await service.CreateChatSession();
            }
            service.ProcessQueue();

            // Verify main team is full
            var mainTeamAgents = service.GetAllAgents().Where(a => a.Id <= 2).ToList();
            Assert.True(mainTeamAgents[0].CurrentChats == 4, "Junior 1 should have 4 chats");
            Assert.True(mainTeamAgents[1].CurrentChats == 4, "Junior 2 should have 4 chats");

            // Act - Add more chats to trigger overflow
            var overflowSessionId = await service.CreateChatSession();
            service.ProcessQueue();

            // Assert - Should be assigned to overflow agent
            var overflowAgents = service.GetAllAgents()
                .Where(a => a.Id >= 11) // Overflow agents have IDs >= 11
                .ToList();

            Assert.True(overflowAgents.Any(a => a.CurrentChats > 0), "Chat should be assigned to overflow agent");
            Assert.Equal(1, overflowAgents.Sum(a => a.CurrentChats));
        }
    }
}