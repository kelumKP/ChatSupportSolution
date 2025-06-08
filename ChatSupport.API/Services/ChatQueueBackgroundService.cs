namespace ChatSupport.API.Services
{
    public class ChatQueueBackgroundService : BackgroundService
    {
        private readonly IChatQueueService _chatQueueService;

        public ChatQueueBackgroundService(IChatQueueService chatQueueService)
        {
            _chatQueueService = chatQueueService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}