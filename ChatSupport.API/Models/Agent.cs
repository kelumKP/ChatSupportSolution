namespace ChatSupport.API.Models
{
    public class Agent
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Seniority Seniority { get; set; }
        public int Shift { get; set; }
        public bool IsActive { get; set; } = true;
        public int CurrentChats { get; set; }

        public double EfficiencyMultiplier => Seniority switch
        {
            Seniority.Junior => 0.4,
            Seniority.MidLevel => 0.6,
            Seniority.Senior => 0.8,
            Seniority.TeamLead => 0.5,
            _ => 0.4
        };

        public int MaxConcurrentChats => (int)(10 * EfficiencyMultiplier);
    }
}