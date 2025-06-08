using ChatSupport.API.Models;
using ChatSupport.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Reflection;

namespace ChatSupport.Tests.TestServices
{
    public class TestChatQueueService : ChatQueueService
    {
    public TestChatQueueService(List<SupportTeam> customTeams) 
        : base(Mock.Of<ILogger<ChatQueueService>>(), BuildTestConfig())
    {
        // Disable all automatic processing
        typeof(ChatQueueService)
            .GetField("_queueTimer", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(this, null);
        
        typeof(ChatQueueService)
            .GetField("_monitorTimer", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(this, null);
        
        // Set test teams
        typeof(ChatQueueService)
            .GetField("_teams", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(this, customTeams);
    }

        private static IConfiguration BuildTestConfig()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    {"ChatQueue:QueueProcessingIntervalMs", "5000"},
                    {"ChatQueue:SessionMonitoringIntervalMs", "10000"}
                })
                .Build();
        }

        public List<ChatSession> GetActiveSessions()
        {
            var field = typeof(ChatQueueService).GetField("_activeSessions", BindingFlags.NonPublic | BindingFlags.Instance);
            return (List<ChatSession>)field.GetValue(this);
        }

public List<ChatSession> GetQueuedSessions()
{
    var field = typeof(ChatQueueService).GetField("_chatQueue", BindingFlags.NonPublic | BindingFlags.Instance);
    return new List<ChatSession>((Queue<ChatSession>)field.GetValue(this));
}
        public List<Agent> GetAllAgents()
        {
            var teamsField = typeof(ChatQueueService).GetField("_teams", BindingFlags.Instance | BindingFlags.NonPublic);
            var teams = (List<SupportTeam>)teamsField.GetValue(this);

            var overflowField = typeof(ChatQueueService).GetField("_overflowTeam", BindingFlags.Instance | BindingFlags.NonPublic);
            var overflowTeam = (SupportTeam)overflowField.GetValue(this);

            var allAgents = teams.SelectMany(t => t.Agents).ToList();
            allAgents.AddRange(overflowTeam.Agents);
            return allAgents;
        }
    }
}