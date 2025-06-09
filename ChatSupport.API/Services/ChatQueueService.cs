using ChatSupport.API.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ChatSupport.API.Services
{
    public class ChatQueueService : IChatQueueService, IDisposable
    {
        private readonly bool _disableMonitoring;
        private readonly Queue<ChatSession> _chatQueue = new();
        private readonly List<ChatSession> _activeSessions = new();
        private readonly List<SupportTeam> _teams;
        private readonly SupportTeam _overflowTeam;
        private readonly object _lock = new();
        private readonly Timer _monitorTimer;
        private readonly ILogger<ChatQueueService> _logger;

        private const int PollIntervalMs = 1000;
        private const int MaxMissedPolls = 3;
        private readonly int _sessionMonitoringIntervalMs;

        public ChatQueueService(
            ILogger<ChatQueueService> logger,
            IConfiguration configuration,
            List<SupportTeam>? overrideTeams = null,
            SupportTeam? overrideOverflowTeam = null,
            bool disableMonitoring = false)
        {
            _logger = logger;
            _disableMonitoring = disableMonitoring;
            _sessionMonitoringIntervalMs = configuration.GetValue("ChatQueue:SessionMonitoringIntervalMs", 10000);

            _teams = overrideTeams ?? InitializeTeams();
            _overflowTeam = overrideOverflowTeam ?? InitializeOverflowTeam();

            if (!_disableMonitoring)
            {
                _monitorTimer = new Timer(MonitorSessionsCallback, null, 0, _sessionMonitoringIntervalMs);
            }
        }


        protected virtual List<SupportTeam> InitializeTeams() => new()
        {
            new SupportTeam
            {
                TeamName = "Team A",
                Agents = new List<Agent>
                {
                    new() { Id = 1, Name = "TeamLead A", Seniority = Seniority.TeamLead, Shift = 1 },
                    new() { Id = 2, Name = "Mid A1", Seniority = Seniority.MidLevel, Shift = 1 },
                    new() { Id = 3, Name = "Mid A2", Seniority = Seniority.MidLevel, Shift = 1 },
                    new() { Id = 4, Name = "Junior A", Seniority = Seniority.Junior, Shift = 1 }
                }
            },
            new SupportTeam
            {
                TeamName = "Team B",
                Agents = new List<Agent>
                {
                    new() { Id = 5, Name = "Senior B", Seniority = Seniority.Senior, Shift = 2 },
                    new() { Id = 6, Name = "Mid B", Seniority = Seniority.MidLevel, Shift = 2 },
                    new() { Id = 7, Name = "Junior B1", Seniority = Seniority.Junior, Shift = 2 },
                    new() { Id = 8, Name = "Junior B2", Seniority = Seniority.Junior, Shift = 2 }
                }
            },
            new SupportTeam
            {
                TeamName = "Team C",
                Agents = new List<Agent>
                {
                    new() { Id = 9, Name = "Mid C1", Seniority = Seniority.MidLevel, Shift = 3 },
                    new() { Id = 10, Name = "Mid C2", Seniority = Seniority.MidLevel, Shift = 3 }
                }
            }
        };

        protected virtual SupportTeam InitializeOverflowTeam() => new()
        {
            TeamName = "Overflow Team",
            IsOverflowTeam = true,
            Agents = Enumerable.Range(11, 6).Select(i =>
                new Agent { Id = i, Name = $"Overflow {i}", Seniority = Seniority.Junior, Shift = 0 }
            ).ToList()
        };

        public async Task<string> CreateChatSession()
        {
            var sessionId = Guid.NewGuid().ToString();
            var newSession = new ChatSession
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastPollTime = DateTime.UtcNow
            };

            lock (_lock)
            {
                var currentTeam = GetCurrentTeam();
                var currentQueueSize = _chatQueue.Count + _activeSessions.Count(s => s.IsActive);
                var maxQueueSize = currentTeam.GetMaxQueueLength();
                bool isOfficeHours = IsDuringOfficeHours();

                if (currentQueueSize >= maxQueueSize)
                {
                    if (isOfficeHours)
                    {
                        var overflowMaxSize = _overflowTeam.GetMaxQueueLength();
                        if (currentQueueSize >= overflowMaxSize)
                        {
                            throw new Exception("Chat refused - queue and overflow are full");
                        }
                    }
                    else
                    {
                        throw new Exception("Chat refused - queue is full");
                    }
                }

                _chatQueue.Enqueue(newSession);
                AssignChatsToAgents();
            }

            return sessionId;
        }

        protected void AssignChatsToAgents()
        {
            var currentTeam = GetCurrentTeam();
            var activeAgents = GetActiveAgents(currentTeam);

            if (IsDuringOfficeHours() && _chatQueue.Count > currentTeam.GetMaxQueueLength())
            {
                activeAgents.AddRange(GetActiveAgents(_overflowTeam));
            }

            while (_chatQueue.Count > 0 && activeAgents.Any(a => a.CurrentChats < a.MaxConcurrentChats))
            {
                var session = _chatQueue.Dequeue();
                var agent = FindAvailableAgent(activeAgents);

                if (agent != null)
                {
                    agent.CurrentChats++;
                    session.AssignedAt = DateTime.UtcNow;
                    session.AssignedAgentId = agent.Id;
                    _activeSessions.Add(session);
                }
                else
                {
                    _chatQueue.Enqueue(session);
                    break;
                }
            }
        }

        public ChatSession GetSessionStatus(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Session ID cannot be empty", nameof(sessionId));

            lock (_lock)
            {
                return _activeSessions.FirstOrDefault(s => s.SessionId == sessionId) ??
                       _chatQueue.FirstOrDefault(s => s.SessionId == sessionId);
            }
        }

        public bool PollSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Session ID cannot be empty", nameof(sessionId));

            lock (_lock)
            {
                var session = _activeSessions.FirstOrDefault(s => s.SessionId == sessionId);
                if (session != null)
                {
                    session.LastPollTime = DateTime.UtcNow;
                    session.MissedPolls = 0;
                    return true;
                }

                return _chatQueue.Any(s => s.SessionId == sessionId);
            }
        }

        protected void MonitorSessionsCallback(object state)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var currentShift = GetCurrentShift();

                foreach (var agent in GetAllAgents())
                {
                    agent.IsActive = agent.Shift == currentShift || agent.Shift == 0;
                }

                foreach (var session in _activeSessions.Where(s => s.IsActive).ToList())
                {
                    if ((now - session.LastPollTime).TotalSeconds > MaxMissedPolls * PollIntervalMs / 1000)
                    {
                        session.MissedPolls++;

                        if (session.MissedPolls >= MaxMissedPolls)
                        {
                            var agent = GetAllAgents().FirstOrDefault(a => a.Id == session.AssignedAgentId);
            
                        }
                    }
                }
            }
        }

        private Agent FindAvailableAgent(List<Agent> agents)
        {
            return agents
                .Where(a => a.IsActive && a.CurrentChats < a.MaxConcurrentChats)
                .OrderBy(a => a.Seniority) // Juniors first
                .ThenBy(a => a.CurrentChats)
                .FirstOrDefault();
        }

        private SupportTeam GetCurrentTeam()
        {
            var currentShift = GetCurrentShift();
            return _teams.FirstOrDefault(t => t.Agents.Any(a => a.Shift == currentShift)) ?? _teams.First();
        }

        private List<Agent> GetActiveAgents(SupportTeam team)
        {
            var currentShift = GetCurrentShift();
            return team.Agents
                .Where(a => a.IsActive && (a.Shift == currentShift || team.IsOverflowTeam))
                .ToList();
        }

        private List<Agent> GetAllAgents()
        {
            var allAgents = _teams.SelectMany(t => t.Agents).ToList();
            allAgents.AddRange(_overflowTeam.Agents);
            return allAgents;
        }

// In ChatQueueService.cs
protected virtual int GetCurrentShift()
{
    var hour = DateTime.UtcNow.Hour;
    return hour switch
    {
        >= 0 and < 8 => 3,
        >= 8 and < 16 => 1,
        _ => 2
    };
}

protected virtual bool IsDuringOfficeHours()
{
    var now = DateTime.Now.TimeOfDay;
    return now >= TimeSpan.FromHours(8) && now <= TimeSpan.FromHours(18);
}

        public void Dispose()
        {
            _monitorTimer?.Dispose();
            GC.SuppressFinalize(this);
        }
        
        public void InvokeMonitorSessionsCallbackForTest()
{
    MonitorSessionsCallback(null);
}
    }
}