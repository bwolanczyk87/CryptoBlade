# CryptoBlade Strategies Documentation

## Table of Contents
- [1. Introduction](#1-introduction)
- [2. Detailed Description of Each Strategy](#2-detailed-description-of-each-strategy)
  - [2.1. AutoHedge Strategy](#21-autohedge-strategy)
  - [2.2. Linear Regression Strategy](#22-linear-regression-strategy)
  - [2.3. Tartaglia Strategy](#23-tartaglia-strategy)
  - [2.4. Mona Strategy](#24-mona-strategy)
  - [2.5. MfiRsiCandlePrecise Trading Strategy](#25-mfirsicandleprecise-trading-strategy)
  - [2.6. MfiRsiEriTrend Trading Strategy](#26-mfirsieritrend-trading-strategy)
  - [2.7. Qiqi Strategy](#27-qiqi-strategy)
  - [2.8. Recursive Strategy (Generalized DCA/Re-entry)](#28-recursive-strategy-generalized-dcare-entry)
- [3. Suggestions for Strategy Improvements](#3-suggestions-for-strategy-improvements)

---

## 1. Introduction

This document provides a comprehensive look at each trading strategy available in CryptoBlade, discussing the underlying logic, notable parameters, and the ways to improve or expand these approaches. Each strategy leverages the `.NET` environment, real-time data feeds, and historical backtesting to refine parameters. While many strategies share certain configuration fields (e.g. wallet exposure, re-entry distances, or minimum volume thresholds), each approach has unique signals, logic, or re-entry patterns.

---

## 2. Detailed Description of Each Strategy

### 2.1. AutoHedge Strategy
- **Class:** `AutoHedgeStrategy`
- **Purpose:**  
  Automatically hedges positions by dynamically opening or adding to hedged trades based on volume, spread, and short/long signals. The core idea is to balance or offset risk—especially in volatile markets—by scaling positions in the opposite direction of an existing bias, or adding to winning trades under certain conditions.

- **Key Logic Details:**  
  1. **Data and Indicators**:  
     - Primarily uses one-minute candles to compute short-term moving averages, volume indicators, and spread.  
     - May incorporate an optional oscillator or a threshold (like `MinReentryPositionDistanceLong/Short`) to decide whether the price has moved “enough” to justify adding another portion of the position.
  2. **Long and Short Re-entry**:  
     - Supports partial additions (dogrywki) to both long and short positions.  
     - `QtyFactorLong`/`QtyFactorShort` and `InitialQtyPctLong`/`InitialQtyPctShort` help define the magnitude of each new partial entry, preventing overly small or large top-ups.
  3. **Risk and Wallet Exposure**:  
     - Respects global wallet-exposure parameters (e.g. `WalletExposureLong`, `WalletExposureShort`) to cap risk on each side.  
     - The re-entry logic uses `DDownFactorLong/Short` if it’s coded similarly to a recursive or grid approach, meaning the next partial entry can be bigger or smaller, depending on the factor.

- **Key Configuration Options:**  
  - `MinimumVolume`, `MinimumPriceDistance`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`  
  - `QtyFactorLong`, `QtyFactorShort`  
  - `InitialQtyPctLong`, `InitialQtyPctShort`  
  - `DDownFactorLong`, `DDownFactorShort`

- **Potential Improvements:**  
  1. **Adaptive Re-entry**: Dynamically adjust the re-entry distance or multiplier based on market volatility or average true range (ATR), so in high-volatility periods, the strategy gives more distance.  
  2. **Expanded Hedging Logic**: Incorporate an advanced check if the strategy is nearing a major support/resistance (from e.g. Mona or LinearRegression signals) to reduce or intensify hedging.  
  3. **Profit-taking Overhaul**: Instead of a single `MinProfitRate`, implement multiple partial TPs.  
  4. **Code Suggestions**: Where small second positions appear, ensure that `InitialQtyPctShort` (or Long) is not too low, and that the code checks relevant environment variables or config fields consistently. Possibly expose these fields in `appsettings.json` if they are not already.

---

### 2.2. Linear Regression Strategy
- **Class:** `LinearRegressionStrategy`
- **Purpose:**  
  Uses a regression line over recent candle data to estimate price channels. Signals appear when the price deviates sufficiently from the regression line, providing buy-low and sell-high opportunities.

- **Key Logic Details:**  
  1. **Regression Calculation**:  
     - Typically uses a fixed window size (`ChannelLength`) of one-minute candles, and applies something like Accord.NET’s `OrdinaryLeastSquares` to fit a linear model.  
     - `StandardDeviation` determines how wide the channel extends around the regression line.
  2. **Entry/Exit**:  
     - Buys if price crosses below the lower channel boundary (perceived undervaluation).  
     - Sells if price crosses above the upper channel boundary (perceived overvaluation).
  3. **Volume Filter**:  
     - Typically requires `MinimumVolume` or other checks to ensure the symbol is sufficiently liquid.

- **Configuration:**  
  - `ChannelLength`, `StandardDeviation`  
  - `MinimumVolume`, `MinimumPriceDistance`

- **Potential Improvements:**  
  1. **Adaptive Window**: Dynamically adjust `ChannelLength` based on volatility or average daily range.  
  2. **Augmented Regression**: Incorporate additional data (like time-of-day effects or a polynomial fit) to catch more nuanced patterns.  
  3. **Multiple Time Frame Confirmation**: Combine signals from a 1-min regression with a 5-min or 15-min slope check.

---

### 2.3. Tartaglia Strategy
- **Class:** `TartagliaStrategy`
- **Purpose:**  
  Implements multiple channel-based logic for long and short entries, partially inspired by advanced geometric or historical references (Tartaglia was a mathematician historically known for ballistic and polynomial solutions).

- **Key Logic Details:**  
  1. **Separate Channels**:  
     - Uses a different regression (or channel) length for longs (`ChannelLengthLong`) vs. shorts (`ChannelLengthShort`).  
     - Possibly allows independent standard deviation thresholds (`StandardDeviationLong`, `StandardDeviationShort`).
  2. **Entry/Exit**:  
     - Signals appear when the ticker price breaks above or below each respective channel.  
     - May also rely on re-entry logic (`MinReentryPositionDistanceLong`/`Short`) to add partial positions if the price continues to move in the same direction.
  3. **Multi-lane Regression**:  
     - Potentially uses multiple polynomials or linear fits to produce a combined “channel.”

- **Configuration:**  
  - `ChannelLengthLong`, `ChannelLengthShort`  
  - `StandardDeviationLong`, `StandardDeviationShort`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`

- **Potential Improvements:**  
  1. **Adaptive Standard Deviations**: Let the strategy widen or shrink each channel automatically in fast- vs. slow-moving markets.  
  2. **Volume or Volatility Overlay**: Incorporate volume thresholds to reduce false positives.  
  3. **Shared Gains**: Possibly unify partial logic if the short and long channels are triggered in opposite directions simultaneously.

---

### 2.4. Mona Strategy
- **Class:** `MonaStrategy`
- **Purpose:**  
  Clusters recent price data (Mean Shift with a Gaussian kernel) to detect key support/resistance “modes,” entering trades when price crosses below or above these modes.

- **Key Logic Details:**  
  1. **Mean Shift Clustering**:  
     - Collects a specified number of 1-min candles (`ClusteringLength`).  
     - Applies a Mean Shift algorithm (Accord.Statistics, for instance) with a `BandwidthCoefficient`.  
     - Identifies cluster centers (modes) that can act as pivot levels.
  2. **Crossing Logic**:  
     - If the price crosses below a cluster level, that often signals an undervalued region → potential buy.  
     - Crossing above might signal overvaluation → potential short.
  3. **Oscillator Filter**:  
     - Often uses MFI/RSI (`MfiRsiLookback`) or a basic spread/volume check to avoid entering in illiquid or narrow conditions.

- **Configuration:**  
  - `ClusteringLength`, `BandwidthCoefficient`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`  
  - `MfiRsiLookback`, `MinimumVolume`, `MinimumPriceDistance`

- **Potential Improvements:**  
  1. **Cluster Counting**: Adjust the bandwidth or add noise handling if too many trivial clusters appear.  
  2. **Adaptive Bandwidth**: Possibly recalculate the kernel bandwidth based on real-time volatility.  
  3. **Re-entry Criteria**: Evaluate whether a second or third re-entry at the same cluster level is beneficial.

---

### 2.5. MfiRsiCandlePrecise Trading Strategy
- **Class:** `MfiRsiCandlePreciseTradingStrategy`
- **Purpose:**  
  Leverages precise oscillator thresholds (MFI + RSI) on one-minute candles to trigger buys/sells.

- **Key Logic Details:**  
  1. **Indicators**:  
     - Calculates Money Flow Index (MFI) and Relative Strength Index (RSI).  
     - Typically checks if they’re below some threshold (e.g., MFI < 20 and RSI < 30) for a buy signal, or above thresholds (MFI > 80 and RSI > 70) for a sell signal.
  2. **Volume & Spread**:  
     - Uses `MinimumVolume` and `MinimumPriceDistance` to skip trades in poor liquidity or insufficient spread conditions.
  3. **Partial Adds**:  
     - May add extra positions if the MFI/RSI remain deeply oversold or overbought for multiple consecutive candles.

- **Configuration:**  
  - `MinimumVolume`, `MinimumPriceDistance`  
  - Potential hidden fields for MFI or RSI thresholds (like `MfiThresholdBuy = 20`, `RsiThresholdBuy = 30`, etc.)

- **Potential Improvements:**  
  1. **Dynamic Thresholding**: If volatility spikes, shift thresholds so that fewer false signals occur.  
  2. **Time-based Exit**: Combine typical oscillator-based exit with a “max time in trade” limit.  
  3. **Integration**: Confirm buy signals with a short SMA cross or a momentum factor.

---

### 2.6. MfiRsiEriTrend Trading Strategy
- **Class:** `MfiRsiEriTrendTradingStrategy`
- **Purpose:**  
  Merges MFI/RSI oscillators with a separate trend detection system—often an ERI (Ehlers or a custom “ERI indicator”)—to confirm bullish/bearish signals.

- **Key Logic Details:**  
  1. **Trend Confirmation**:  
     - Compares an “ERI trend” or modified average with the MFI/RSI oscillator.  
     - If both are bullish, it triggers a buy signal; if both are bearish, it triggers a short.
  2. **Re-entry**:  
     - `MinReentryPositionDistanceLong/Short` sets how far price must move from the average entry to allow adding more.  
     - `MfiRsiLookbackPeriod` can drastically change how quickly the strategy reacts.
  3. **Optional ERI-Only**:  
     - If `UseEriOnly` is set true, the strategy relies more heavily on the ERI’s indicated momentum.

- **Configuration:**  
  - `MinimumVolume`, `MinimumPriceDistance`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`  
  - `MfiRsiLookbackPeriod`, `UseEriOnly`

- **Potential Improvements:**  
  1. **Adaptive Lookback**: Let the strategy adjust `MfiRsiLookbackPeriod` in real-time if volatility changes.  
  2. **Combining Multi Time Frames**: Evaluate a short-term ERI plus a mid-term RSI to mitigate choppy signals.  
  3. **Advanced Exits**: Possibly define trailing stops based on the same indicators.

---

### 2.7. Qiqi Strategy
- **Class:** `QiqiStrategy`
- **Purpose:**  
  Merges RSI-based take-profit triggers and QFL fractal analysis to identify strong reversal points for entry.

- **Key Logic Details:**  
  1. **Fractal (QFL) Levels**:  
     - Identifies local fractal “bases” below which the price rarely dips; if it does, the strategy sees a potential undervaluation.  
     - For short signals, does the inverse with fractal levels above current price.
  2. **RSI Validation**:  
     - If RSI is under a threshold (e.g. 30) when the price is near or below a fractal base, Qiqi triggers a buy.  
     - `RsiTakeProfitLong` can define if the position closes once RSI crosses some upper boundary.
  3. **Timed Exits**:  
     - Includes `MaxTimeStuck` to exit losing or stuck positions after a certain period.  
     - `TakeProfitPercentLong/Short` sets partial or total profit-taking triggers.

- **Configuration:**  
  - `RsiTakeProfitLong`, `RsiTakeProfitShort`  
  - `QflBellowPercentEnterLong`, `QflAbovePercentEnterShort`  
  - `MaxTimeStuck`, `TakeProfitPercentLong`, `TakeProfitPercentShort`

- **Potential Improvements:**  
  1. **Adaptive RSI**: Let the threshold vary if the strategy is missing big rebounds.  
  2. **Multi-lot Exits**: Add partial sells as RSI climbs, instead of a single threshold.  
  3. **Fractal Smoothing**: Filter fractal levels with a moving average to reduce noise.

---

### 2.8. Recursive Strategy (Generalized DCA/Re-entry)
While not listed as a separate class in the user docs, many of these strategies can incorporate or do incorporate a “recursive” or “grid” approach to partial entries. For example:

- **DDownFactorLong/Short**: If set to 2.0, each subsequent position after the first might be double the size of the previous one.  
- **ReentryPositionPriceDistanceLong/Short**: The price must diverge from the average position by this fraction/percentage to add a new partial.  
- **InitialQtyPctLong/Short**: The first chunk of capital used for a new position, which can be crucial in preventing extremely small or large second entries.

**Why it matters**:  
- Adjusting these parameters is critical for scaling in or out of trades. A modest initial entry followed by heavier re-entries might drastically improve or worsen performance depending on volatility.  
- Example fix: If the second short entry is too small (~0.06 USDT), raise `InitialQtyPctShort` or `QtyFactorShort` to approach ~0.5 USDT for subsequent partials.

---

## 3. Suggestions for Strategy Improvements

1. **Adaptive Param Tuning**  
   - Many strategies rely on fixed thresholds (`MinReentryPositionDistance`, `StandardDeviation`, etc.). Making them adapt based on real-time volatility, e.g. ATR-based gating, could reduce both overtrading in sideways markets and undertrading in fast markets.

2. **Multi-indicator Confirmation**  
   - Combining signals from multiple strategies might reduce false positives. For instance, AutoHedge can detect volume peaks, while Mona might supply pivot cluster levels.

3. **Partial Take-profits**  
   - Instead of a single `MinProfitRate` or one RSI threshold, allow for partial profit captures in stages. That approach can lock in gains while letting the rest of the position run.

4. **Stop-loss / Unstucking Enhancements**  
   - Evaluate the “unstucking” code across all strategies (e.g., forcibly closing losing positions if they exceed a certain drawdown or time limit). Add more nuanced logic—like trailing stops, or forced kills if correlated market movements turn strongly against a strategy.

5. **Enhanced Logging and Live Analysis**  
   - Provide real-time logs of which signals triggered and why, to speed up debugging.  
   - Possibly implement a real-time chart overlay (outside .NET or with a library) to see the channels, fractal bases, regression lines, and cluster modes.

6. **Expanded Dockerization**  
   - For multi-strategy or multi-instance setups, confirm that each strategy can run in an isolated container or environment with an appropriate container_name or project name.  
   - This ensures no naming conflicts and can allow multiple strategies or multiple versions of the same strategy to run side by side for live A/B testing.

In all cases, thorough backtesting on relevant pairs (e.g. BTC/USDT, ETH/USDT) plus forward testing (demo environment or small real capital) is recommended before scaling up.
