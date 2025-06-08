using ChatSupport.API.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChatSupport.API.Services
{
    public class ChatQueueService : IChatQueueService, IDisposable
    {

        private const int MaxConcurrentChatsPerAgent = 10;
        private readonly Queue<ChatSession> _chatQueue = new();
        private readonly List<ChatSession> _activeSessions = new();
        private readonly List<SupportTeam> _teams;
        private readonly SupportTeam _overflowTeam;
        private readonly object _lock = new();
        private readonly Timer _queueTimer;
        private readonly Timer _monitorTimer;
        private readonly ILogger<ChatQueueService> _logger;

        private const int PollIntervalMs = 1000;
        private const int MaxMissedPolls = 3;
        private readonly int _queueProcessingIntervalMs;
        private readonly int _sessionMonitoringIntervalMs;

        public async Task<string> CreateChatSession()
{
    var sessionId = Guid.NewGuid().ToString();
    var newSession = new ChatSession
    {
        SessionId = sessionId,
        CreatedAt = CurrentTime,
        LastPollTime = CurrentTime,
        IsActive = true
    };

    lock (_lock)
    {
        var currentTeam = GetCurrentTeam();
        var teamCapacity = CalculateTeamCapacity(currentTeam);
        var maxQueueSize = (int)(teamCapacity * 1.5);
        var totalSystemCapacity = teamCapacity + maxQueueSize; // 16 + 24 = 40
        var totalActiveChats = _activeSessions.Count(s => s.IsActive);
        var currentQueueSize = _chatQueue.Count;
        var totalChatsInSystem = totalActiveChats + currentQueueSize;
        bool isOfficeHours = IsDuringOfficeHours();

        _logger.LogInformation(
            "New chat request. Current system status: " +
            "Active: {ActiveChats}/{TeamCapacity}, " +
            "Queued: {QueuedChats}/{MaxQueueSize}, " +
            "Total: {TotalChats}/{TotalCapacity}",
            totalActiveChats, teamCapacity,
            currentQueueSize, maxQueueSize,
            totalChatsInSystem, totalSystemCapacity);

        // Check main system capacity (40 in the example)
        if (totalChatsInSystem >= totalSystemCapacity)
        {
            if (isOfficeHours && _overflowTeam != null)
            {
                var overflowCapacity = CalculateTeamCapacity(_overflowTeam);
                var overflowMaxQueue = (int)(overflowCapacity * 1.5);
                var totalOverflowCapacity = overflowCapacity + overflowMaxQueue;
                
                if (totalChatsInSystem >= totalSystemCapacity + totalOverflowCapacity)
                {
                    _logger.LogWarning(
                        "Chat refused - system at full capacity (Main: {MainCapacity}, Overflow: {OverflowCapacity})",
                        totalSystemCapacity, totalOverflowCapacity);
                    throw new Exception("Chat refused - system and overflow are at full capacity");
                }
                
                _logger.LogInformation(
                    "Main team at capacity ({TotalSystemCapacity}), using overflow capacity ({OverflowCapacity})",
                    totalSystemCapacity, totalOverflowCapacity);
            }
            else
            {
                _logger.LogWarning(
                    "Chat refused - system at full capacity ({TotalCapacity}) and not during office hours",
                    totalSystemCapacity);
                throw new Exception("Chat refused - system is at full capacity");
            }
        }

        _chatQueue.Enqueue(newSession);
        _logger.LogInformation(
            "Created new chat session: {SessionId}. " +
            "System totals - Active: {ActiveChats}, Queued: {QueuedChats}",
            sessionId, totalActiveChats, currentQueueSize + 1);
    }

    return sessionId;
}

        private int CalculateTeamCapacity(SupportTeam team)
        {
            int capacity = 0;
            foreach (var agent in team.Agents)
            {
                capacity += GetAgentCapacity(agent);
            }
            return capacity;
        }

        private int GetAgentCapacity(Agent agent)
        {
            double multiplier = agent.Seniority switch
            {
                Seniority.Junior => 0.4,
                Seniority.MidLevel => 0.6,
                Seniority.Senior => 0.8,
                Seniority.TeamLead => 0.5,
                _ => 0.4
            };

            return (int)(MaxConcurrentChatsPerAgent * multiplier);
        }
        protected virtual DateTime CurrentTime => DateTime.UtcNow;

        public ChatQueueService(ILogger<ChatQueueService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _queueProcessingIntervalMs = configuration.GetValue("ChatQueue:QueueProcessingIntervalMs", 5000);
            _sessionMonitoringIntervalMs = configuration.GetValue("ChatQueue:SessionMonitoringIntervalMs", 10000);

            _teams = InitializeTeams();
            _overflowTeam = InitializeOverflowTeam();

            _queueTimer = new Timer(ProcessQueueCallback, null, 0, _queueProcessingIntervalMs);
            _monitorTimer = new Timer(MonitorSessionsCallback, null, 0, _sessionMonitoringIntervalMs);

            _logger.LogInformation("ChatQueueService initialized with {TeamCount} teams", _teams.Count);
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
                    session.LastPollTime = CurrentTime;
                    session.MissedPolls = 0;
                    return true;
                }

                return _chatQueue.Any(s => s.SessionId == sessionId);
            }
        }

        public void ProcessQueue()
        {
            ProcessQueueCallback(null);
        }

        private void ProcessQueueCallback(object state)
{
    lock (_lock)
    {
        try
        {
            var currentTeam = GetCurrentTeam();
            var teamCapacity = CalculateTeamCapacity(currentTeam);
            var totalActiveChats = _activeSessions.Count(s => s.IsActive);
            var currentQueueSize = _chatQueue.Count;

            _logger.LogInformation(
                "Queue processing started. " +
                "Active chats: {ActiveChats}/{TeamCapacity}, " +
                "Queued chats: {QueuedChats}/{MaxQueueSize}",
                totalActiveChats, teamCapacity,
                currentQueueSize, (int)(teamCapacity * 1.5));

            var activeAgents = GetActiveAgents(currentTeam);
            bool useOverflow = false;

            // Only use overflow if during office hours AND either:
            // 1. Active chats >= team capacity, OR
            // 2. Queue size > max queue size
            if (IsDuringOfficeHours() && 
                (totalActiveChats >= teamCapacity || currentQueueSize > (int)(teamCapacity * 1.5)))
            {
                useOverflow = true;
                var overflowAgents = GetActiveAgents(_overflowTeam);
                activeAgents.AddRange(overflowAgents);
                
                _logger.LogInformation(
                    "Activating overflow team. " +
                    "Added {OverflowAgentCount} agents with capacity {OverflowCapacity}",
                    overflowAgents.Count, CalculateTeamCapacity(_overflowTeam));
            }

            int assignedCount = 0;
            while (_chatQueue.Count > 0 && activeAgents.Any(a => a.CurrentChats < GetAgentCapacity(a)))
            {
                var session = _chatQueue.Dequeue();
                var agent = FindAvailableAgent(activeAgents);

                if (agent != null)
                {
                    agent.CurrentChats++;
                    session.AssignedAt = CurrentTime;
                    session.AssignedAgentId = agent.Id;
                    session.IsActive = true;
                    _activeSessions.Add(session);
                    assignedCount++;

                    _logger.LogDebug(
                        "Assigned session {SessionId} to agent {AgentName} ({Seniority}). " +
                        "Now handling {CurrentChats}/{Capacity} chats",
                        session.SessionId, agent.Name, agent.Seniority,
                        agent.CurrentChats, GetAgentCapacity(agent));
                }
                else
                {
                    _chatQueue.Enqueue(session);
                    break;
                }
            }

            _logger.LogInformation(
                "Queue processing complete. " +
                "Assigned {AssignedCount} chats. " +
                "Current totals - Active: {ActiveChats}, Queued: {QueuedChats}",
                assignedCount,
                _activeSessions.Count(s => s.IsActive),
                _chatQueue.Count);

            // Log detailed agent utilization
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var team in _teams.Append(_overflowTeam))
                {
                    var teamAgents = GetActiveAgents(team);
                    if (teamAgents.Any())
                    {
                        _logger.LogDebug(
                            "{TeamName} agent utilization:", 
                            team.TeamName);
                        
                        foreach (var agent in teamAgents.OrderBy(a => a.Seniority))
                        {
                            _logger.LogDebug(
                                "  {AgentName} ({Seniority}): {CurrentChats}/{Capacity} chats",
                                agent.Name, agent.Seniority,
                                agent.CurrentChats, GetAgentCapacity(agent));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing queue");
        }
    }
}
        private void MonitorSessionsCallback(object state)
        {
            lock (_lock)
            {
                try
                {
                    var now = CurrentTime;
                    int expiredCount = 0;

                    foreach (var session in _activeSessions.Where(s => s.IsActive).ToList())
                    {
                        if ((now - session.LastPollTime).TotalSeconds > MaxMissedPolls * PollIntervalMs / 1000)
                        {
                            session.MissedPolls++;

                            if (session.MissedPolls >= MaxMissedPolls)
                            {
                                session.IsActive = false;
                                var agent = GetAllAgents().FirstOrDefault(a => a.Id == session.AssignedAgentId);
                                if (agent != null)
                                {
                                    agent.CurrentChats--;
                                    _logger.LogDebug("Released agent {AgentId} due to expired session", agent.Id);
                                }
                                expiredCount++;
                            }
                        }
                    }

                    _activeSessions.RemoveAll(s => !s.IsActive);

                    var currentShift = GetCurrentShift();
                    foreach (var agent in GetAllAgents())
                    {
                        agent.IsActive = agent.Shift == currentShift || agent.Shift == 0;
                    }

                    if (expiredCount > 0)
                    {
                        _logger.LogInformation("Session monitoring - expired {ExpiredCount} sessions", expiredCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error monitoring sessions");
                }
            }
        }

        private Agent FindAvailableAgent(List<Agent> agents)
        {
            return agents
                .Where(a => a.IsActive && a.CurrentChats < a.MaxConcurrentChats)
                .OrderBy(a => a.Seniority)
                .ThenBy(a => a.Seniority == Seniority.Junior ? 0 : 1)
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

        protected virtual int GetCurrentShift()
        {
            var hour = CurrentTime.Hour;
            if (hour >= 0 && hour < 8) return 3;
            if (hour >= 8 && hour < 16) return 1;
            return 2;
        }

        protected virtual bool IsDuringOfficeHours()
        {
            var now = CurrentTime.TimeOfDay;
            return now >= TimeSpan.FromHours(8) && now <= TimeSpan.FromHours(18);
        }

        public void Dispose()
        {
            _queueTimer?.Dispose();
            _monitorTimer?.Dispose();
            GC.SuppressFinalize(this);
            _logger.LogInformation("ChatQueueService disposed");
        }

        protected virtual int GetCurrentShiftForTime(TimeSpan time)
        {
            var hour = time.Hours;
            if (hour >= 0 && hour < 8) return 3;
            if (hour >= 8 && hour < 16) return 1;
            return 2;
        }

        protected virtual bool IsDuringOfficeHoursForTime(TimeSpan time)
        {
            return time >= TimeSpan.FromHours(8) && time <= TimeSpan.FromHours(18);
        }
    }
}