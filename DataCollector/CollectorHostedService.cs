namespace DataCollector
{
    public class CollectorHostedService : IHostedService
    {
        private readonly ICollectorManager _collectorManager;

        public CollectorHostedService(ICollectorManager strategyManager)
        {
            m_strategyManager = strategyManager;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _collectorManager.StartStrategiesAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _collectorManager.StopStrategiesAsync(cancellationToken);
        }
    }
}
