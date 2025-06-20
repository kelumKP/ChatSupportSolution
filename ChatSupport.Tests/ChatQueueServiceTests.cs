using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using ChatSupport.Application.Services;
using ChatSupport.Domain;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ChatSupport.Tests
{
    public class ChatQueueServiceTests
    {
        private readonly Mock<ILogger<ChatQueueService>> _mockLogger;
        private readonly Mock<IConfiguration> _mockConfig;

        public ChatQueueServiceTests()
        {
            _mockLogger = new Mock<ILogger<ChatQueueService>>();

            var configurationSection = new Mock<IConfigurationSection>();
            configurationSection.Setup(x => x.Value).Returns("10000");

            _mockConfig = new Mock<IConfiguration>();
            _mockConfig.Setup(x => x.GetSection("ChatQueue:SessionMonitoringIntervalMs"))
                .Returns(configurationSection.Object);
        }

        private SupportTeam CreateTestTeam1()
        {
            return new SupportTeam
            {
                TeamName = "UnitTest Team 1",
                Agents = new List<Agent>
                    {
                        new Agent {
                            Id = 1,
                            Name = "Senior 1",
                            Seniority = Seniority.Senior,
                            Shift = 1,
                            IsActive = true
                        },
                        new Agent {
                            Id = 2,
                            Name = "Junior 1",
                            Seniority = Seniority.Junior,
                            Shift = 1,
                            IsActive = true
                        }
                    }
            };
        }

        private SupportTeam CreateTestTeam2()
        {
            return new SupportTeam
            {
                TeamName = "UnitTest Team 2",
                Agents = new List<Agent>
                    {
                        new Agent {
                            Id = 1,
                            Name = "Mid 1",
                            Seniority = Seniority.MidLevel,
                            Shift = 1,
                            IsActive = true
                        },
                        new Agent {
                            Id = 2,
                            Name = "Junior 1",
                            Seniority = Seniority.Junior,
                            Shift = 1,
                            IsActive = true
                        },
                        new Agent {
                            Id = 3,
                            Name = "Junior 2",
                            Seniority = Seniority.Junior,
                            Shift = 1,
                            IsActive = true
                    }
                }
            };
        }

        [Fact]
        public async Task UnitTest1_ShouldAssignChatsAccordingToSeniority()
        {
            // Arrange
            var testTeam = CreateTestTeam1();

            // Ensure we're testing during shift 1 (when our test agents are active)
            var mockDateTime = new Mock<IDateTimeProvider>();
            mockDateTime.Setup(x => x.GetCurrentShift()).Returns(1);

            var service = new TestableChatQueueService(
                _mockLogger.Object,
                testTeam,
                mockDateTime.Object);

            // Act - Create chat sessions
            var sessionIds = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                sessionIds.Add(await service.CreateChatSession());
            }

            // immediate assignment
            service.ForceAssignChats();

            // Assert
            var junior = testTeam.Agents.First(a => a.Seniority == Seniority.Junior);
            var senior = testTeam.Agents.First(a => a.Seniority == Seniority.Senior);

            // Get all active sessions with assignments
            var assignedSessions = sessionIds
                .Select(service.GetSessionStatus)
                .Where(s => s?.AssignedAgentId != null)
                .ToList();

            var juniorAssignments = assignedSessions.Count(s => s.AssignedAgentId == junior.Id);
            var seniorAssignments = assignedSessions.Count(s => s.AssignedAgentId == senior.Id);

            Assert.Equal(4, juniorAssignments);
            Assert.Equal(1, seniorAssignments);
        }

        [Fact]
        public async Task UnitTest2_ShouldAssignChatsToJuniorsFirst()
        {

            // Arrange
            var testTeam = CreateTestTeam2();

            // testing during shift 1 (when test agents are active)
            var mockDateTime = new Mock<IDateTimeProvider>();
            mockDateTime.Setup(x => x.GetCurrentShift()).Returns(1);

            var service = new TestableChatQueueService(
                _mockLogger.Object,
                testTeam,
                mockDateTime.Object);

            // Act - Create chat sessions
            var sessionIds = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                sessionIds.Add(await service.CreateChatSession());
            }

            // immediate assignment
            service.ForceAssignChats();

            // Assert
            var mid = testTeam.Agents.First(a => a.Seniority == Seniority.MidLevel);
            var junior1 = testTeam.Agents.First(a => a.Id == 2);
            var junior2 = testTeam.Agents.First(a => a.Id == 3);

            // Get all active sessions with assignments
            var assignedSessions = sessionIds
            .Select(service.GetSessionStatus)
            .Where(s => s?.AssignedAgentId != null)
            .ToList();

            var midAssignments = assignedSessions.Count(s => s.AssignedAgentId == mid.Id);
            var junior1Assignments = assignedSessions.Count(s => s.AssignedAgentId == junior1.Id);
            var junior2Assignments = assignedSessions.Count(s => s.AssignedAgentId == junior2.Id);

            // Juniors should get 3 each, mid should get none
            Assert.Equal(0, midAssignments);
            Assert.Equal(3, junior1Assignments);
            Assert.Equal(3, junior2Assignments);
        }



        [Fact]
        public async Task CreateChatSession_WhenQueueFull_ThrowsException()
        {

            var loggerMock = new Mock<ILogger<ChatQueueService>>();
            var configMock = new Mock<IConfiguration>();
            configMock.Setup(x => x["SomeKey"]).Returns("SomeValue");

            var service = new ChatQueueService(loggerMock.Object, null, null, false);

            var currentteam = service.GetCurrentTeam();
            var capacity = currentteam.CalculateCapacity();
            var finalCapacity = (capacity * 1.5) + capacity;
            int roundedFinalCapacity = (int)Math.Floor(finalCapacity); 

            for (int i = 0; i < roundedFinalCapacity; i++)
            {
                await service.CreateChatSession();
            }

            var exception = await Assert.ThrowsAsync<Exception>(async () =>
                await service.CreateChatSession());

            Assert.Equal("Chat refused - queue is full",
                exception.Message);


        }
    }

}