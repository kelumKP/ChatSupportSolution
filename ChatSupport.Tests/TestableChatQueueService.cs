using ChatSupport.Application.Services;
using ChatSupport.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace ChatSupport.Tests
{
    public class TestableChatQueueService : ChatQueueService
    {
        private readonly SupportTeam _testTeam;
        private readonly IDateTimeProvider _dateTimeProvider;

        public TestableChatQueueService(
            ILogger<ChatQueueService> logger,
            SupportTeam testTeam,
            IDateTimeProvider dateTimeProvider)
            : base(logger, new List<SupportTeam> { testTeam },
                  new SupportTeam { TeamName = "Overflow", IsOverflowTeam = true, Agents = new List<Agent>() },
                  true)
        {
            _testTeam = testTeam;
            _dateTimeProvider = dateTimeProvider;
        }

        protected override int GetCurrentShift() => _dateTimeProvider.GetCurrentShift();

        protected override bool IsDuringOfficeHours() => _dateTimeProvider.IsDuringOfficeHours();

        public void ForceAssignChats() => AssignChatsToAgents();

        protected override List<SupportTeam> InitializeTeams()
        {
            return new List<SupportTeam> { _testTeam };
        }

        protected override SupportTeam InitializeOverflowTeam()
        {
            return new SupportTeam
            {
                TeamName = "Overflow",
                IsOverflowTeam = true,
                Agents = new List<Agent>()
            };
        }

        public new void InvokeMonitorSessionsCallbackForTest()
        {
            base.MonitorSessionsCallback(null);
        }
    }
}