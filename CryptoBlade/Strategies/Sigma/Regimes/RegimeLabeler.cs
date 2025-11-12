using System;
using System.Collections.Generic;
using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma
{
    public static class RegimeLabeler
    {
        // Heurystyki zgodne z kontraktem:
        // MM: trend „zdrowy” (ATR% ∈ [1.2;4.0], ADX ≥ 18), |Z| <= 0.6, ZSlopeDvwap i DeltaCvd5m ten sam znak
        // MR: ADX <= 18, ATR% ∈ [1.0;3.5], |Z| >= 0.8
        // BO: kompresja (HasInsideOrNr7=true) i wzrost zmienności (BBW pct wzrasta >= +20 pp lub >= p80)
        public static RegimeLabel Label(FeatureSnapshot f, FeatureSnapshot? fPrev = null)
        {
            // BO – eventowy (kompresja + eksplozja zmienności)
            bool compression = f.HasInsideOrNr7;
            bool volJump = false;
            if (fPrev != null)
            {
                volJump = (f.Bbw15mPct - fPrev.Bbw15mPct) >= 20.0 || f.Bbw15mPct >= 80.0;
            }
            else
            {
                volJump = f.Bbw15mPct >= 80.0;
            }
            if (compression && volJump)
                return RegimeLabel.Breakout;

            // MM – trend
            bool atrOkMM = f.AtrPct1h >= 1.2 && f.AtrPct1h <= 4.0;
            bool adxOkMM = f.Adx1h >= 18.0;
            double absZ = f.ZDvwap >= 0 ? f.ZDvwap : -f.ZDvwap;
            bool nearValue = absZ <= 0.6;
            bool dirAgree = (f.ZSlopeDvwap > 0 && f.DeltaCvd5m > 0) || (f.ZSlopeDvwap < 0 && f.DeltaCvd5m < 0);
            if (atrOkMM && adxOkMM && nearValue && dirAgree)
                return RegimeLabel.Momentum;

            // MR – mean reversion
            bool atrOkMR = f.AtrPct1h >= 1.0 && f.AtrPct1h <= 3.5;
            bool adxOkMR = f.Adx1h <= 18.0;
            bool farFromValue = absZ >= 0.8;
            if (atrOkMR && adxOkMR && farFromValue)
                return RegimeLabel.MeanReversion;

            return RegimeLabel.None;
        }

        // Dopuszczamy niewielki lag w ocenie – np. 1–2 kroki
        public static bool MatchWithLag(RegimeLabel expected, RegimeLabel actual, Queue<RegimeLabel> futureActual, int maxLag = 2)
        {
            if (expected == actual) return true;
            if (expected == RegimeLabel.None) return actual == RegimeLabel.None;
            // sprawdź, czy w najbliższych krokach pojawi się oczekiwany reżim (akceptujemy lag przełączenia)
            int i = 0;
            foreach (var a in futureActual)
            {
                if (++i > maxLag) break;
                if (a == expected) return true;
            }
            return false;
        }
    }
}
