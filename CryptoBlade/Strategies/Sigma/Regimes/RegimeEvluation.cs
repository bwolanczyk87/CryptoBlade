using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma
{
    public sealed class RegimeEvaluation
    {
        public sealed record Summary(
            double Accuracy,
            double F1_Momentum,
            double F1_MeanReversion,
            double F1_Breakout,
            double AvgSwitchLagBars,
            int N);

        public static Summary EvaluateAndExport(IReadOnlyList<RegimeAuditRecord> recs, string csvPath)
        {
            if (recs.Count == 0) return new Summary(0, 0, 0, 0, 0, 0);
            // Najpierw wylicz orakla na podstawie kolejnych snapshotów
            var labeled = new List<(RegimeAuditRecord rec, RegimeLabel oracle)>(recs.Count);
            for (int i = 0; i < recs.Count; i++)
            {
                var prev = i > 0 ? recs[i - 1] : null;
                var oracle = RegimeLabeler.Label(ToFs(recs[i]), prev != null ? ToFs(prev) : null);
                recs[i].Oracle = oracle;
                labeled.Add((recs[i], oracle));
            }

            // CSV
            using (var sw = new StreamWriter(csvPath))
            {
                sw.WriteLine("TimeUtc,Symbol,Selected,Oracle,ScoreMM,ScoreMR,ScoreBO,Adx1h,AtrPct1h,Bbw15mPct,ZDvwap,ZSlopeDvwap,DeltaCvd5m,OiDelta1hPct,SpreadBps,HasInsideOrNr7");
                foreach (var (r, oracle) in labeled)
                {
                    sw.WriteLine(string.Join(",",
                        r.TimeUtc.ToString("o"),
                        r.Symbol,
                        r.Selected,
                        oracle,
                        r.ScoreMM.ToString(CultureInfo.InvariantCulture),
                        r.ScoreMR.ToString(CultureInfo.InvariantCulture),
                        r.ScoreBO.ToString(CultureInfo.InvariantCulture),
                        r.Adx1h.ToString(CultureInfo.InvariantCulture),
                        r.AtrPct1h.ToString(CultureInfo.InvariantCulture),
                        r.Bbw15mPct.ToString(CultureInfo.InvariantCulture),
                        r.ZDvwap.ToString(CultureInfo.InvariantCulture),
                        r.ZSlopeDvwap.ToString(CultureInfo.InvariantCulture),
                        r.DeltaCvd5m.ToString(CultureInfo.InvariantCulture),
                        r.OiDelta1hPct.ToString(CultureInfo.InvariantCulture),
                        r.SpreadBps.ToString(CultureInfo.InvariantCulture),
                        r.HasInsideOrNr7 ? "1" : "0"));
                }
            }

            // Klasyczne metryki
            var yTrue = labeled.Select(x => x.oracle).ToArray();
            var yPred = labeled.Select(x => x.rec.Selected).ToArray();

            double acc = yTrue.Zip(yPred, (t, p) => t == p ? 1.0 : 0.0).Average();

            double f1(RegimeLabel c)
            {
                double tp = 0, fp = 0, fn = 0;
                for (int i = 0; i < yTrue.Length; i++)
                {
                    bool isCTrue = yTrue[i] == c;
                    bool isCPred = yPred[i] == c;
                    if (isCTrue && isCPred) tp++;
                    else if (!isCTrue && isCPred) fp++;
                    else if (isCTrue && !isCPred) fn++;
                }
                double prec = tp + fp > 0 ? tp / (tp + fp) : 0;
                double rec = tp + fn > 0 ? tp / (tp + fn) : 0;
                if (prec + rec == 0) return 0;
                return 2 * prec * rec / (prec + rec);
            }

            // Lag (średnia liczba barów opóźnienia w trafieniu „w orakla” przy zmianach)
            var lags = new List<int>();
            for (int i = 1; i < yTrue.Length; i++)
            {
                if (yTrue[i] != yTrue[i - 1] && yTrue[i] != RegimeLabel.None)
                {
                    // od tego miejsca licz, po ilu krokach predykcja zrówna się z nowym oraklem
                    int lag = 0, j = i;
                    while (j < yPred.Length && yPred[j] != yTrue[i] && lag <= 10) { lag++; j++; }
                    lags.Add(lag);
                }
            }
            double avgLag = lags.Count > 0 ? lags.Average() : 0;

            return new Summary(
                Accuracy: acc,
                F1_Momentum: f1(RegimeLabel.Momentum),
                F1_MeanReversion: f1(RegimeLabel.MeanReversion),
                F1_Breakout: f1(RegimeLabel.Breakout),
                AvgSwitchLagBars: avgLag,
                N: yTrue.Length);
        }

        private static FeatureSnapshot ToFs(RegimeAuditRecord r) =>
            new FeatureSnapshotProxy(r);

        // Lekki proxy tylko do labelera
        private sealed class FeatureSnapshotProxy : FeatureSnapshot
        {
            public FeatureSnapshotProxy(RegimeAuditRecord r)
            {
                // ustawiamy tylko pola, których używa Labeler
                typeof(FeatureSnapshot).GetProperty(nameof(FeatureSnapshot.Adx1h))!.SetValue(this, r.Adx1h);
                typeof(FeatureSnapshot).GetProperty(nameof(FeatureSnapshot.AtrPct1h))!.SetValue(this, r.AtrPct1h);
                typeof(FeatureSnapshot).GetProperty(nameof(FeatureSnapshot.Bbw15mPct))!.SetValue(this, r.Bbw15mPct);
                typeof(FeatureSnapshot).GetProperty(nameof(FeatureSnapshot.ZDvwap))!.SetValue(this, r.ZDvwap);
                typeof(FeatureSnapshot).GetProperty(nameof(FeatureSnapshot.ZSlopeDvwap))!.SetValue(this, r.ZSlopeDvwap);
                typeof(FeatureSnapshot).GetProperty(nameof(FeatureSnapshot.DeltaCvd5m))!.SetValue(this, r.DeltaCvd5m);
                typeof(FeatureSnapshot).GetProperty(nameof(FeatureSnapshot.HasInsideOrNr7))!.SetValue(this, r.HasInsideOrNr7);
            }
        }
    }
}
