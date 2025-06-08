namespace ChatSupport.API.Models
{
public class SupportTeam
{
    public string TeamName { get; set; }
    public List<Agent> Agents { get; set; } = new();
    public bool IsOverflowTeam { get; set; }

    public int CalculateCapacity() => Agents.Sum(agent => agent.MaxConcurrentChats);
        public int GetMaxQueueLength()
        {
            // 1.5 x total capacity of all active agents
            return (int)(Agents
                .Where(a => a.IsActive)
                .Sum(a => a.MaxConcurrentChats) * 1.5);
        }
}
}