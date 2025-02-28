using Microsoft.Extensions.Diagnostics.HealthChecks;
using CryptoBlade.Services;

namespace CryptoBlade.HealthChecks {
public class BacktestExecutionHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken())
        {
            return Task.FromResult(new HealthCheckResult(HealthStatus.Healthy, "Backtest is healthy."));
        }
    }

public class TradeExecutionHealthCheck : IHealthCheck
    {
        private readonly ITradeStrategyManager m_tradeStrategyManager;

        public TradeExecutionHealthCheck(ITradeStrategyManager tradeStrategyManager)
        {
            m_tradeStrategyManager = tradeStrategyManager;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken())
        {
            DateTime lastExecution = m_tradeStrategyManager.LastExecution;
            DateTime utcNow = DateTime.UtcNow;
            TimeSpan maxHealthyTime = TimeSpan.FromMinutes(5);
            TimeSpan elapsed = utcNow - lastExecution;
            HealthStatus status = elapsed > maxHealthyTime ? HealthStatus.Unhealthy : HealthStatus.Healthy;
            string message = status == HealthStatus.Unhealthy
                ? $"Trade strategy manager has not executed for {elapsed}."
                : $"Trade strategy manager has executed within the last {elapsed}.";
            return Task.FromResult(new HealthCheckResult(status, message));
        }
    }
}
