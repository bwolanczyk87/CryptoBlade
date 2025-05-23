﻿using System.Text.Json;
using CryptoBlade.BackTesting;
using CryptoBlade.Configuration;
using CryptoBlade.Helpers;
using CryptoBlade.Optimizer.Strategies;
using CryptoBlade.Strategies.Symbols;
using GeneticSharp;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Optimizer.Fitness
{
    public class StrategyFitness : IFitness
    {
        private readonly IHistoricalDataStorage m_historicalDataStorage;
        private readonly ITradingSymbolsManager m_tradingSymbolsManager;
        private readonly IOptions<TradingBotOptions> m_initialOptions;
        private readonly CancellationToken m_cancel;
        private readonly ILogger m_logger;

        public StrategyFitness(IOptions<TradingBotOptions> initialOptions,
            IHistoricalDataStorage historicalDataStorage,
            ITradingSymbolsManager tradingSymbolsManager,
            CancellationToken cancel,
            ILogger logger)
        {
            m_historicalDataStorage = historicalDataStorage;
            m_tradingSymbolsManager = tradingSymbolsManager;
            m_initialOptions = initialOptions;
            m_cancel = cancel;
            m_logger = logger;
        }

        public double Evaluate(IChromosome chromosome)
        {
            OptimizerBacktestExecutor backtestExecutor = new OptimizerBacktestExecutor(m_historicalDataStorage, m_tradingSymbolsManager);
            var clonedOptions = Options.Create(m_initialOptions.Value.Clone());
            ITradingBotChromosome tradingBotChromosome = (ITradingBotChromosome)chromosome;
            tradingBotChromosome.ApplyGenesToTradingBotOptions(clonedOptions.Value);
            BacktestPerformanceResult? backtestResult =
                TryToLoadExistingResult(clonedOptions.Value)
                ?? backtestExecutor.ExecuteAsync(clonedOptions, m_cancel).GetAwaiter().GetResult();

            var fitness = CalculateFitness(backtestResult);

            return fitness;
        }

        private BacktestPerformanceResult? TryToLoadExistingResult(TradingBotOptions options)
        {
            var md5Options = options.CalculateMd5();
            var backtestResultPath = Path.Combine(ConfigPaths.GetBackTestResultDirectory(options.StrategyName), md5Options);
            if (Directory.Exists(backtestResultPath))
            {
                var resultFile = Path.Combine(backtestResultPath, options.BackTest.ResultFileName);
                if (File.Exists(resultFile))
                {
                    var json = File.ReadAllText(resultFile);
                    var result = JsonSerializer.Deserialize<BacktestPerformanceResult>(json);
                    return result;
                }
            }
            return null;
        }

        private double CalculateFitness(BacktestPerformanceResult result)
        {
            try
            {
                double runningDaysRatio = result.TotalDays / (double)result.ExpectedDays;
                var fitnessOptions = m_initialOptions.Value.Optimizer.GeneticAlgorithm.FitnessOptions;
                double runningDaysPreference = fitnessOptions.RunningDaysPreference;
                double avgDailyGainPreference = fitnessOptions.AvgDailyGainPreference;
                double lowestEquityToBalancePreference = fitnessOptions.LowestEquityToBalancePreference;
                double adgNrmseErrorPreference = fitnessOptions.AdgNrmseErrorPreference;
                double equityBalanceNrmsePreference = fitnessOptions.EquityBalanceNrmsePreference;
                double fitness =
                    runningDaysPreference * runningDaysRatio
                    - avgDailyGainPreference
                    - lowestEquityToBalancePreference
                    - adgNrmseErrorPreference
                    - equityBalanceNrmsePreference;
                double maxAvgDailyGainPercent = fitnessOptions.MaxAvgDailyGainPercent;
                double minAvgDailyGainPercent = fitnessOptions.MinAvgDailyGainPercent;
                if (result.FinalBalance > 0 && result.AverageDailyGainPercent > 0)
                {
                    double avgDailyGainPercent = (double)result.AverageDailyGainPercent;
                    avgDailyGainPercent = Math.Max(minAvgDailyGainPercent, avgDailyGainPercent);
                    avgDailyGainPercent = Math.Min(maxAvgDailyGainPercent, avgDailyGainPercent);
                    double normalizedAvgDailyGainPercent = avgDailyGainPercent / maxAvgDailyGainPercent;
                    fitness = runningDaysPreference * runningDaysRatio
                              + avgDailyGainPreference * normalizedAvgDailyGainPercent
                              + lowestEquityToBalancePreference * (double)result.LowestEquityToBalance
                              - adgNrmseErrorPreference * result.AdgNormalizedRootMeanSquareError
                              - equityBalanceNrmsePreference * result.EquityBalanceNormalizedRooMeanSquareError;
                }

                return fitness;
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Error calculating fitness");
                return -999;
            }
        }
    }
}