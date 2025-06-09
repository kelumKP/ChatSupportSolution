using ChatSupport.Domain;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace ChatSupport.Application.Services
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
            ILogger<ChatQueueService> logger=null,
            List<SupportTeam>? overrideTeams = null,
            SupportTeam? overrideOverflowTeam = null,
            bool disableMonitoring = false)
        {
            _logger = logger;
            _disableMonitoring = disableMonitoring;
        

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

        protected void AssignChatsToAgents()
        {
            var currentTeam = GetCurrentTeam();
            _logger.LogInformation($"Current processing team: {currentTeam.TeamName}");
            var activeAgents = GetActiveAgents(currentTeam);

            if (IsDuringOfficeHours() && _chatQueue.Count > currentTeam.GetMaxQueueLength())
            {
                _logger.LogInformation($"Queue length exceeded for {currentTeam.TeamName}, activating overflow team");
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
            
                    _logger.LogInformation($"Assigned chat session {session.SessionId} to agent {agent.Name} " +
                                 $"(ID: {agent.Id}, Seniority: {agent.Seniority}, " +
                                 $"Current chats: {agent.CurrentChats}/{agent.MaxConcurrentChats})");
                }
                else
                {
                    _logger.LogWarning("No available agents found for chat session, requeuing");
                    _chatQueue.Enqueue(session);
                    break;
                }
            }
        }

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

                var currentQueueSize = _chatQueue.Count() + _activeSessions.Count(s => s.IsActive);
                var maxQueueSize = currentTeam.GetMaxQueueLength();
                bool isOfficeHours = IsDuringOfficeHours();

                if (_chatQueue.Count() >= maxQueueSize)
                {
                    if (isOfficeHours && _overflowTeam != null)
                    {
                        var overflowQueueSize = currentQueueSize;
                        var overflowMaxSize = _overflowTeam.GetMaxQueueLength();

                        if (overflowQueueSize >= overflowMaxSize)
                        {
                            _logger.LogWarning("Chat refused - queue and overflow are full");
                            throw new Exception("Chat refused - queue and overflow are full");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Chat refused - queue is full");
                        throw new Exception("Chat refused - queue is full");
                    }
                }

                _chatQueue.Enqueue(newSession);
                _logger.LogInformation("Created new chat session: {SessionId}", sessionId);
                 ContinuousTrigger();

            }

            return sessionId;
        }

        private void ContinuousTrigger()
        {

            lock (_lock)
            {
                try
                {
                    _logger.LogDebug($"Triggering queue processing - Active: {_activeSessions.Count(s => s.IsActive)}, Queued: {_chatQueue.Count}");


                    var currentTeam = GetCurrentTeam();
                    var activeAgents = GetActiveAgents(currentTeam);

                    bool useOverflow = false;
                    if (IsDuringOfficeHours() && _chatQueue.Count > currentTeam.GetMaxQueueLength())
                    {
                        useOverflow = true;
                        activeAgents.AddRange(GetActiveAgents(_overflowTeam));
                        _logger.LogDebug("Using overflow team for queue processing");
                    }

                    int assignedCount = 0;
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
                            assignedCount++;
                            _logger.LogDebug("Assigned session {SessionId} to agent {AgentId}", session.SessionId, agent.Id);
                        }
                        else
                        {
                            _chatQueue.Enqueue(session);
                            break;
                        }
                    }

                    if (assignedCount > 0)
                    {
                        _logger.LogInformation("Processed queue - assigned {AssignedCount} sessions", assignedCount);
                    }
                    
                                _logger.LogDebug($"Queue processing completed - Active: {_activeSessions.Count(s => s.IsActive)}, Queued: {_chatQueue.Count}, Assigned: {assignedCount}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing queue");
                }
            }
        }
        protected void MonitorSessionsCallback(object state)
        {
            _logger.LogDebug("Starting session monitoring cycle");

            lock (_lock)
            {
                        _logger.LogInformation($"Current Queue Status - Active Sessions: {_activeSessions.Count(s => s.IsActive)}, " +
                             $"Queued Sessions: {_chatQueue.Count}, " +
                             $"Total Sessions: {_activeSessions.Count(s => s.IsActive) + _chatQueue.Count}");


                var now = DateTime.UtcNow;
                var currentShift = GetCurrentShift();
                _logger.LogInformation($"Current shift: {currentShift}");

                foreach (var agent in GetAllAgents())
                {
                    var wasActive = agent.IsActive;
                    agent.IsActive = agent.Shift == currentShift || agent.Shift == 0;

                    if (wasActive != agent.IsActive)
                    {
                        _logger.LogInformation($"Agent {agent.Name} (ID: {agent.Id}) activity changed to {agent.IsActive}");
                    }
                }

                foreach (var session in _activeSessions.Where(s => s.IsActive).ToList())
                {
                    if ((now - session.LastPollTime).TotalSeconds > MaxMissedPolls * PollIntervalMs / 1000)
                    {
                        session.MissedPolls++;
                        _logger.LogWarning($"Session {session.SessionId} missed poll ({session.MissedPolls}/{MaxMissedPolls})");

                        if (session.MissedPolls >= MaxMissedPolls)
                        {
                            var agent = GetAllAgents().FirstOrDefault(a => a.Id == session.AssignedAgentId);
                        }
                    }
                }
            }

            _logger.LogDebug("Completed session monitoring cycle");
        }

        private Agent FindAvailableAgent(List<Agent> agents)
        {
            var availableAgents = agents
                .Where(a => a.IsActive && a.CurrentChats < a.MaxConcurrentChats)
                .OrderBy(a => a.Seniority)
                .ThenBy(a => a.CurrentChats)
                .ToList();

            _logger.LogDebug($"Available agents count: {availableAgents.Count}");
    
            foreach (var agent in availableAgents)
            {
                _logger.LogTrace($"Available agent: {agent.Name} (ID: {agent.Id}, Seniority: {agent.Seniority}, " +
                       $"Current chats: {agent.CurrentChats}/{agent.MaxConcurrentChats})");
            }

            return availableAgents.FirstOrDefault();
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

        public SupportTeam GetCurrentTeam()
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