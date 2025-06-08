using ChatSupport.API.Models;
using ChatSupport.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ChatSupport.Tests.TestServices
{
    public class TestChatQueueService : ChatQueueService
    {
        private DateTime? _testTime;

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

            // Initialize overflow team
            typeof(ChatQueueService)
                .GetField("_overflowTeam", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(this, InitializeOverflowTeam());
        }

        public void SetTestTime(DateTime testTime) => _testTime = testTime;

        // Completely independent time handling for tests
        private DateTime GetCurrentTime() => _testTime ?? DateTime.UtcNow;

        private int GetCurrentShift()
        {
            var hour = GetCurrentTime().Hour;
            if (hour >= 0 && hour < 8) return 3;
            if (hour >= 8 && hour < 16) return 1;
            return 2;
        }

        private bool IsDuringOfficeHours()
        {
            var now = GetCurrentTime().TimeOfDay;
            return now >= TimeSpan.FromHours(8) && now <= TimeSpan.FromHours(18);
        }

        public int CalculateTeamCapacity(SupportTeam team)
        {
            int capacity = 0;
            foreach (var agent in team.Agents)
            {
                capacity += GetAgentCapacity(agent);
            }
            return capacity;
        }

        public int GetAgentCapacity(Agent agent)
        {
            double multiplier = agent.Seniority switch
            {
                Seniority.Junior => 0.4,
                Seniority.MidLevel => 0.6,
                Seniority.Senior => 0.8,
                Seniority.TeamLead => 0.5,
                _ => 0.4
            };
            return (int)(10 * multiplier); // 10 is MaxConcurrentChatsPerAgent
        }

        private static IConfiguration BuildTestConfig() => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                {"ChatQueue:QueueProcessingIntervalMs", "5000"},
                {"ChatQueue:SessionMonitoringIntervalMs", "10000"}
            })
            .Build();

        public List<ChatSession> GetActiveSessions() => 
            (List<ChatSession>)typeof(ChatQueueService)
                .GetField("_activeSessions", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(this);

        public List<ChatSession> GetQueuedSessions() => 
            new List<ChatSession>((Queue<ChatSession>)typeof(ChatQueueService)
                .GetField("_chatQueue", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(this));

        public List<Agent> GetAllAgents()
        {
            var teams = (List<SupportTeam>)typeof(ChatQueueService)
                .GetField("_teams", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(this);

            var overflowTeam = (SupportTeam)typeof(ChatQueueService)
                .GetField("_overflowTeam", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(this);

            var allAgents = teams.SelectMany(t => t.Agents).ToList();
            allAgents.AddRange(overflowTeam.Agents);
            return allAgents;
        }
    }
}