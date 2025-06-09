namespace ChatSupport.Domain
{
    public class ChatSession
    {
        public string SessionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? AssignedAt { get; set; }
        public int? AssignedAgentId { get; set; }
        public DateTime LastPollTime { get; set; }
        public int MissedPolls { get; set; }
        public bool IsActive { get; set; } = true;
    }
}