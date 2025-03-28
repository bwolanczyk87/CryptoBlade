﻿using Binance.Net.Clients;
using CryptoBlade.BackTesting;
using CryptoBlade.BackTesting.Binance;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Optimizer.Fitness;
using CryptoBlade.Optimizer.Strategies;
using CryptoBlade.Optimizer.Strategies.AutoHedge;
using CryptoBlade.Optimizer.Strategies.MfiRsiEriTrend;
using CryptoBlade.Optimizer.Strategies.Qiqi;
using CryptoBlade.Optimizer.Strategies.Tartaglia;
using CryptoBlade.Strategies;
using CryptoBlade.Strategies.Symbols;
using GeneticSharp;
using Microsoft.Extensions.Options;
using CryptoExchange.Net.Clients;

namespace CryptoBlade.Optimizer
{
    public class GeneticAlgorithmOptimizer : IOptimizer
    {
        private readonly IOptions<TradingBotOptions> m_options;
        private CancellationTokenSource? m_cancellationTokenSource;
        private Task? m_executionTask;
        private readonly ILogger<GeneticAlgorithmOptimizer> m_logger;
        private readonly IHostApplicationLifetime m_applicationLifetime;
        private readonly ITradingSymbolsManager m_tradingSymbolsManager;

        public GeneticAlgorithmOptimizer(IOptions<TradingBotOptions> options,
            ILogger<GeneticAlgorithmOptimizer> logger,
            IHostApplicationLifetime applicationLifetime,
            ITradingSymbolsManager symbolsManager)
        {
            m_options = options;
            m_logger = logger;
            m_applicationLifetime = applicationLifetime;
            m_tradingSymbolsManager = symbolsManager;

        }

        public Task RunAsync(CancellationToken cancel)
        {
            m_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            CancellationToken cancelMain = m_cancellationTokenSource.Token;
            m_executionTask = Task.Run(async () =>
            {
                try
                {
                    await OptimizeAsync(cancelMain);
                }
                catch (Exception a)
                {
                    m_logger.LogError(a, "Error while running optimizer");
                }
            }, cancelMain);

            return Task.CompletedTask;
        }

        private async Task OptimizeAsync(CancellationToken cancel)
        {
            var geneticAlgorithmOptions = m_options.Value.Optimizer.GeneticAlgorithm;
            await DownloadDataAsync(cancel);
            ISelection selection;
            switch (geneticAlgorithmOptions.SelectionStrategy)
            {
                case SelectionStrategy.RankSelection:
                    m_logger.LogInformation("Using RankSelection");
                    selection = new RankSelection();
                    break;
                case SelectionStrategy.TournamentSelection:
                    m_logger.LogInformation("Using TournamentSelection");
                    selection = new TournamentSelection();
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Invalid selection strategy");
            }
            var crossover = new UniformCrossover();
            IMutation mutation;
            switch (geneticAlgorithmOptions.MutationStrategy)
            {
                case MutationStrategy.FlipBitMutation:
                    m_logger.LogInformation("Using FlipBitMutation");
                    mutation = new FlipBitMutation();
                    break;
                case MutationStrategy.UniformMutation:
                    m_logger.LogInformation("Using UniformMutation");
                    mutation = new UniformMutation(true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Invalid mutation strategy");
            }
            var historicalDataStorage = HistoricalDataStorageFactory.CreateHistoricalDataStorage(m_options);
            var fitness = new StrategyFitness(m_options, historicalDataStorage, m_tradingSymbolsManager, cancel, m_logger);
            IChromosome chromosome;
            switch (m_options.Value.StrategyName)
            {
                case StrategyNames.Tartaglia:
                    var tartagliaOptions = CreateChromosomeOptions<TartagliaChromosomeOptions>(m_options.Value,
                        options =>
                        {
                            options.ChannelLengthLong = m_options.Value.Optimizer.Tartaglia.ChannelLengthLong;
                            options.ChannelLengthShort = m_options.Value.Optimizer.Tartaglia.ChannelLengthShort;
                            options.StandardDeviationLong = m_options.Value.Optimizer.Tartaglia.StandardDeviationLong;
                            options.StandardDeviationShort = m_options.Value.Optimizer.Tartaglia.StandardDeviationShort;
                            options.MinReentryPositionDistanceLong =
                                m_options.Value.Optimizer.Tartaglia.MinReentryPositionDistanceLong;
                            options.MinReentryPositionDistanceShort = m_options.Value.Optimizer.Tartaglia
                                .MinReentryPositionDistanceShort;
                        });
                    chromosome = new TartagliaChromosome(tartagliaOptions);
                    break;
                case StrategyNames.AutoHedge:
                    var autoHedgeOptions = CreateChromosomeOptions<AutoHedgeChromosomeOptions>(m_options.Value,
                        options =>
                        {
                            options.MinReentryPositionDistanceLong = m_options.Value.Optimizer.AutoHedge.MinReentryPositionDistanceLong;
                            options.MinReentryPositionDistanceShort = m_options.Value.Optimizer.AutoHedge.MinReentryPositionDistanceShort;
                        });
                    chromosome = new AutoHedgeChromosome(autoHedgeOptions);
                    break;
                case StrategyNames.MfiRsiEriTrend:
                    var mfiRsiTrendOptions = CreateChromosomeOptions<MfiRsiTrendChromosomeOptions>(m_options.Value,
                        options =>
                        {
                            options.MinReentryPositionDistanceLong = m_options.Value.Optimizer.MfiRsiEriTrend.MinReentryPositionDistanceLong;
                            options.MinReentryPositionDistanceShort = m_options.Value.Optimizer.MfiRsiEriTrend.MinReentryPositionDistanceShort;
                            options.MfiRsiLookbackPeriod = m_options.Value.Optimizer.MfiRsiEriTrend.MfiRsiLookbackPeriod;
                            options.UseEriOnly = m_options.Value.Optimizer.MfiRsiEriTrend.UseEriOnly;
                        });
                    chromosome = new MfiRsiTrendChromosome(mfiRsiTrendOptions);
                    break;
                case StrategyNames.Qiqi:
                    var qiqiOptions = CreateRecursiveGridChromosomeOptions<QiqiChromosomeOptions>(m_options.Value,
                        options =>
                        {
                            options.QflBellowPercentEnterLong = m_options.Value.Optimizer.Qiqi.QflBellowPercentEnterLong;
                            options.RsiTakeProfitLong = m_options.Value.Optimizer.Qiqi.RsiTakeProfitLong;
                            options.QflAbovePercentEnterShort = m_options.Value.Optimizer.Qiqi.QflAbovePercentEnterShort;
                            options.RsiTakeProfitShort = m_options.Value.Optimizer.Qiqi.RsiTakeProfitShort;
                            options.TakeProfitPercentLong = m_options.Value.Optimizer.Qiqi.TakeProfitPercentLong;
                            options.TakeProfitPercentShort = m_options.Value.Optimizer.Qiqi.TakeProfitPercentShort;
                        });
                    chromosome = new QiqiChromosome(qiqiOptions);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Invalid strategy name");
            }
            string optimizerDirectory = ConfigPaths.GetOptimizerResultDirectory(m_options.Value.StrategyName);
            var resultsDir = m_options.Value.Optimizer.SessionId;
            if (!Directory.Exists(optimizerDirectory))
                Directory.CreateDirectory(optimizerDirectory);
            string targetDir = Path.Combine(optimizerDirectory, resultsDir);
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);
            var population = new SerializablePopulation(
                Options.Create(new SerializablePopulationOptions
                {
                    PopulationFile = Path.Combine(targetDir, "current_population.json"),
                }),
                geneticAlgorithmOptions.MinPopulationSize,
                geneticAlgorithmOptions.MaxPopulationSize, 
                chromosome);
            var ga = new GeneticAlgorithm(population, fitness, selection, crossover, mutation)
            {
                Termination = new GenerationNumberTermination(geneticAlgorithmOptions.GenerationCount),
                TaskExecutor = new CustomParallelTaskExecutor(m_options.Value.Optimizer.ParallelTasks, m_logger),
                CrossoverProbability = geneticAlgorithmOptions.CrossoverProbability,
                MutationProbability = geneticAlgorithmOptions.MutationProbability,
            };
            ga.GenerationRan += (_, _) =>
            {
                var uniquePopulation = ga.Population.CurrentGeneration.Chromosomes
                    .Select(x => x.ToString())
                    .Distinct()
                    .Count();
                if (uniquePopulation < ga.Population.CurrentGeneration.Chromosomes.Count 
                    && ga.MutationProbability <= geneticAlgorithmOptions.MaxMutationProbability)
                {
                    // Increase mutation probability if the population is not diverse enough
                    ga.MutationProbability *= geneticAlgorithmOptions.MutationMultiplier;
                    if(ga.MutationProbability > geneticAlgorithmOptions.MaxMutationProbability)
                        ga.MutationProbability = geneticAlgorithmOptions.MaxMutationProbability;
                    m_logger.LogInformation($"Mutation probability increased to {ga.MutationProbability}");
                }
                else
                {
                    ga.MutationProbability = geneticAlgorithmOptions.MutationProbability;
                    m_logger.LogInformation($"Mutation probability reset to {ga.MutationProbability}");
                }
                var bestChromosome = ga.Population.BestChromosome;
                ITradingBotChromosome tradingBotChromosome = (ITradingBotChromosome)bestChromosome;
                var clonedOptions = Options.Create(m_options.Value.Clone());
                tradingBotChromosome.ApplyGenesToTradingBotOptions(clonedOptions.Value);
                int generationDecimalNumbers = (int)Math.Log10(geneticAlgorithmOptions.GenerationCount) + 1;
                var generationDir = Path.Combine(targetDir,
                    $"Generation_{ga.GenerationsNumber.ToString($"D{generationDecimalNumbers}")}");
                if (!Directory.Exists(generationDir))
                    Directory.CreateDirectory(generationDir);
                string generationPopulationFile = Path.Combine(generationDir, "population.json");
                var serializablePopulation = (SerializablePopulation)ga.Population;
                serializablePopulation.SerializePopulation(generationPopulationFile);
                var md5Options = clonedOptions.Value.CalculateMd5();
                var backtestResults = Path.Combine(ConfigPaths.GetBackTestResultDirectory(m_options.Value.StrategyName), md5Options);
                var backtestFiles = Directory.GetFiles(backtestResults);
                foreach (var backtestFile in backtestFiles)
                {
                    var fileName = Path.GetFileName(backtestFile);
                    string targetFile = Path.Combine(generationDir, fileName);
                    if (File.Exists(targetFile))
                        File.Delete(targetFile);
                    File.Copy(backtestFile, targetFile);
                }

                m_logger.LogInformation($"Generation: {ga.GenerationsNumber} - Fitness: {bestChromosome.Fitness}");
            };

            cancel.Register(() => ga.Stop());
            ga.Start();
            m_applicationLifetime.StopApplication();
        }

        public async Task StopAsync(CancellationToken cancel)
        {
            if (m_cancellationTokenSource != null)
            {
                m_cancellationTokenSource.Cancel();
                if (m_executionTask != null)
                {
                    try
                    {
                        await m_executionTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex, "Error while stopping optimizer");
                    }
                }

                m_cancellationTokenSource.Dispose();
            }
        }

        private async Task DownloadDataAsync(CancellationToken cancel)
        {
            IOptions<ProtoHistoricalDataStorageOptions> protoHistoricalDataStorageOptions = Options.Create(
                new ProtoHistoricalDataStorageOptions
                {
                    Directory = ConfigPaths.DefaultHistoricalDataDirectory,
                });
            ProtoHistoricalDataStorage historicalDataStorage = new(protoHistoricalDataStorageOptions);
            BinanceCbFuturesRestClient binanceCbFuturesRestClient = new(
                ApplicationLogging.CreateLogger<BinanceCbFuturesRestClient>(),
                new BinanceRestClient());
            BinanceHistoricalDataDownloader binanceHistoricalDataDownloader = new(
                historicalDataStorage,
                ApplicationLogging.CreateLogger<BinanceHistoricalDataDownloader>(),
                binanceCbFuturesRestClient);
            BackTestDataDownloader backTestDataDownloader = new(binanceHistoricalDataDownloader);

            var start = m_options.Value.BackTest.Start;
            var end = m_options.Value.BackTest.End;
            start -= m_options.Value.BackTest.StartupCandleData;
            start = start.Date;
            end = end.Date;
            var symbols = m_options.Value.Whitelist;
            await backTestDataDownloader.DownloadDataForBackTestAsync(symbols, start, end, cancel);
        }

        private IOptions<TOptions> CreateChromosomeOptions<TOptions>(TradingBotOptions config,
            Action<TOptions> optionsSetup)
            where TOptions : TradingBotChromosomeOptions, new()
        {
            var options = new TOptions
            {
                WalletExposureLong = config.Optimizer.TradingBot.WalletExposureLong,
                WalletExposureShort = config.Optimizer.TradingBot.WalletExposureShort,
                QtyFactorLong = config.Optimizer.TradingBot.QtyFactorLong,
                QtyFactorShort = config.Optimizer.TradingBot.QtyFactorShort,
                EnableRecursiveQtyFactorLong = config.Optimizer.TradingBot.EnableRecursiveQtyFactorLong,
                EnableRecursiveQtyFactorShort = config.Optimizer.TradingBot.EnableRecursiveQtyFactorShort,
                DcaOrdersCount = config.Optimizer.TradingBot.DcaOrdersCount,
                UnstuckingEnabled = config.Optimizer.TradingBot.UnstuckingEnabled,
                SlowUnstuckThresholdPercent = config.Optimizer.TradingBot.SlowUnstuckThresholdPercent,
                SlowUnstuckPositionThresholdPercent = config.Optimizer.TradingBot.SlowUnstuckPositionThresholdPercent,
                SlowUnstuckPercentStep = config.Optimizer.TradingBot.SlowUnstuckPercentStep,
                ForceUnstuckThresholdPercent = config.Optimizer.TradingBot.ForceUnstuckThresholdPercent,
                ForceUnstuckPositionThresholdPercent = config.Optimizer.TradingBot.ForceUnstuckPositionThresholdPercent,
                ForceUnstuckPercentStep = config.Optimizer.TradingBot.ForceUnstuckPercentStep,
                ForceKillTheWorst = config.Optimizer.TradingBot.ForceKillTheWorst,
                MinimumVolume = config.Optimizer.TradingBot.MinimumVolume,
                MinimumPriceDistance = config.Optimizer.TradingBot.MinimumPriceDistance,
                MinProfitRate = config.Optimizer.TradingBot.MinProfitRate,
                TargetLongExposure = config.Optimizer.TradingBot.TargetLongExposure,
                TargetShortExposure = config.Optimizer.TradingBot.TargetShortExposure,
                MaxLongStrategies = config.Optimizer.TradingBot.MaxLongStrategies,
                MaxShortStrategies = config.Optimizer.TradingBot.MaxShortStrategies,
                EnableCriticalModeLong = config.Optimizer.TradingBot.EnableCriticalModeLong,
                EnableCriticalModeShort = config.Optimizer.TradingBot.EnableCriticalModeShort,
                CriticalModelWalletExposureThresholdLong =
                    config.Optimizer.TradingBot.CriticalModelWalletExposureThresholdLong,
                CriticalModelWalletExposureThresholdShort =
                    config.Optimizer.TradingBot.CriticalModelWalletExposureThresholdShort,
                SpotRebalancingRatio = config.Optimizer.TradingBot.SpotRebalancingRatio,
            };
            optionsSetup(options);
            return Options.Create(options);
        }

        private IOptions<TOptions> CreateRecursiveGridChromosomeOptions<TOptions>(TradingBotOptions config,
            Action<TOptions> optionsSetup)
            where TOptions : RecursiveGridTradingBotChromosomeOptions, new()
        {
            var options = new TOptions
            {
                WalletExposureLong = config.Optimizer.TradingBot.WalletExposureLong,
                WalletExposureShort = config.Optimizer.TradingBot.WalletExposureShort,
                UnstuckingEnabled = config.Optimizer.TradingBot.UnstuckingEnabled,
                SlowUnstuckThresholdPercent = config.Optimizer.TradingBot.SlowUnstuckThresholdPercent,
                SlowUnstuckPositionThresholdPercent = config.Optimizer.TradingBot.SlowUnstuckPositionThresholdPercent,
                SlowUnstuckPercentStep = config.Optimizer.TradingBot.SlowUnstuckPercentStep,
                ForceUnstuckThresholdPercent = config.Optimizer.TradingBot.ForceUnstuckThresholdPercent,
                ForceUnstuckPositionThresholdPercent = config.Optimizer.TradingBot.ForceUnstuckPositionThresholdPercent,
                ForceUnstuckPercentStep = config.Optimizer.TradingBot.ForceUnstuckPercentStep,
                ForceKillTheWorst = config.Optimizer.TradingBot.ForceKillTheWorst,
                MinimumVolume = config.Optimizer.TradingBot.MinimumVolume,
                TargetLongExposure = config.Optimizer.TradingBot.TargetLongExposure,
                TargetShortExposure = config.Optimizer.TradingBot.TargetShortExposure,
                MaxLongStrategies = config.Optimizer.TradingBot.MaxLongStrategies,
                MaxShortStrategies = config.Optimizer.TradingBot.MaxShortStrategies,
                EnableCriticalModeLong = config.Optimizer.TradingBot.EnableCriticalModeLong,
                EnableCriticalModeShort = config.Optimizer.TradingBot.EnableCriticalModeShort,
                CriticalModelWalletExposureThresholdLong =
                    config.Optimizer.TradingBot.CriticalModelWalletExposureThresholdLong,
                CriticalModelWalletExposureThresholdShort =
                    config.Optimizer.TradingBot.CriticalModelWalletExposureThresholdShort,
                SpotRebalancingRatio = config.Optimizer.TradingBot.SpotRebalancingRatio,
                DDownFactorLong = config.Optimizer.RecursiveStrategy.DDownFactorLong,
                InitialQtyPctLong = config.Optimizer.RecursiveStrategy.InitialQtyPctLong,
                ReentryPositionPriceDistanceLong = config.Optimizer.RecursiveStrategy.ReentryPositionPriceDistanceLong,
                ReentryPositionPriceDistanceWalletExposureWeightingLong =
                    config.Optimizer.RecursiveStrategy.ReentryPositionPriceDistanceWalletExposureWeightingLong,
                DDownFactorShort = config.Optimizer.RecursiveStrategy.DDownFactorShort,
                InitialQtyPctShort = config.Optimizer.RecursiveStrategy.InitialQtyPctShort,
                ReentryPositionPriceDistanceShort = config.Optimizer.RecursiveStrategy.ReentryPositionPriceDistanceShort,
                ReentryPositionPriceDistanceWalletExposureWeightingShort =
                    config.Optimizer.RecursiveStrategy.ReentryPositionPriceDistanceWalletExposureWeightingShort,
            };
            optionsSetup(options);
            return Options.Create(options);
        }
    }
}