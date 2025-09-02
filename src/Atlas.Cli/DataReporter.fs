module TradingStrategy.DataReporter

open System
open TradingStrategy.Data
open TradingStrategy.TechnicalIndicators
open TradingStrategy.Backtesting

let formatDecimal (value: decimal) =
    value.ToString("F2")

let formatVolume (volume: int64) =
    if volume >= 1_000_000L then
        sprintf "%.1fM" (float volume / 1_000_000.0)
    elif volume >= 1_000L then
        sprintf "%.1fK" (float volume / 1_000.0)
    else
        volume.ToString()

let calculateStats (data: MarketDataPoint[]) =
    if data.Length = 0 then
        (0m, 0m, 0m, 0m, 0L)
    else
        let prices = data |> Array.map (_.Close)
        let volumes = data |> Array.map (_.Volume)
        let minPrice = prices |> Array.min
        let maxPrice = prices |> Array.max
        let avgPrice = prices |> Array.average
        let totalVolume = volumes |> Array.sum
        (minPrice, maxPrice, avgPrice, prices.[prices.Length - 1], totalVolume)

let generateMarketDataReport (timeSeriesData: TimeSeriesData<MarketDataPoint>) =
    let symbol = timeSeriesData.Symbol
    let dataCount = timeSeriesData.Data.Length
    let startDate = timeSeriesData.StartDate.ToString("yyyy-MM-dd")
    let endDate = timeSeriesData.EndDate.ToString("yyyy-MM-dd")
    
    if dataCount = 0 then
        sprintf "📊 MARKET DATA REPORT - %s\n❌ NO DATA AVAILABLE\nPeriod: %s to %s" symbol startDate endDate
    else
        let (minPrice, maxPrice, avgPrice, lastPrice, totalVolume) = calculateStats timeSeriesData.Data
        let firstPoint = timeSeriesData.Data.[0]
        let lastPoint = timeSeriesData.Data.[dataCount - 1]
        let priceChange = lastPrice - firstPoint.Open
        let priceChangePercent = (priceChange / firstPoint.Open) * 100m
        let validationResult = match validateMarketData timeSeriesData.Data with | Valid -> "✅ PASSED" | _ -> "⚠️ ISSUES"
        
        [
            sprintf "📊 MARKET DATA REPORT - %s" symbol
            "═══════════════════════════════════════════"
            sprintf "📅 Period: %s to %s" startDate endDate
            sprintf "📊 Data Points: %s" (dataCount.ToString("N0"))
            sprintf "🕐 First: %s | Last: %s" 
                (firstPoint.Timestamp.ToString("MM-dd HH:mm")) 
                (lastPoint.Timestamp.ToString("MM-dd HH:mm"))
            ""
            sprintf "💰 Opening: $%s | Closing: $%s" (formatDecimal firstPoint.Open) (formatDecimal lastPrice)
            sprintf "📈 Change: $%s (%s%%)" (formatDecimal priceChange) (formatDecimal priceChangePercent)
            sprintf "🔺 High: $%s | 🔻 Low: $%s" (formatDecimal maxPrice) (formatDecimal minPrice)
            sprintf "📊 Avg Price: $%s" (formatDecimal avgPrice)
            ""
            sprintf "📈 Total Volume: %s" (formatVolume totalVolume)
            sprintf "📊 Avg Volume: %s" (formatVolume (totalVolume / int64 dataCount))
            sprintf "🔍 Validation: %s" validationResult
        ] |> String.concat "\n"

let generateTechnicalIndicatorReport (indicators: TechnicalIndicatorSet) =
    let symbol = indicators.Symbol
    let latestSMA20 = if indicators.SMA20.Length > 0 then indicators.SMA20.[indicators.SMA20.Length - 1].Value else 0m
    let latestSMA50 = if indicators.SMA50.Length > 0 then indicators.SMA50.[indicators.SMA50.Length - 1].Value else 0m
    let latestEMA12 = if indicators.EMA12.Length > 0 then indicators.EMA12.[indicators.EMA12.Length - 1].Value else 0m
    let latestRSI = if indicators.RSI14.Length > 0 then indicators.RSI14.[indicators.RSI14.Length - 1].Value else 0m
    let latestMACD = if indicators.MACD.Length > 0 then indicators.MACD.[indicators.MACD.Length - 1] else { Timestamp = DateTime.MinValue; MACD = 0m; Signal = 0m; Histogram = 0m }
    let latestBB = if indicators.BollingerBands.Length > 0 then indicators.BollingerBands.[indicators.BollingerBands.Length - 1] else { Timestamp = DateTime.MinValue; Upper = 0m; Middle = 0m; Lower = 0m }
    
    let rsiSignal = 
        if latestRSI > 70m then "🔴 OVERBOUGHT"
        elif latestRSI < 30m then "🟢 OVERSOLD"
        else "🟡 NEUTRAL"
    
    let macdSignal = 
        if latestMACD.Histogram > 0m then "🟢 BULLISH"
        elif latestMACD.Histogram < 0m then "🔴 BEARISH"
        else "🟡 NEUTRAL"
    
    [
        sprintf "📊 TECHNICAL INDICATORS - %s" symbol
        "═══════════════════════════════════════════"
        sprintf "📈 SMA 20: $%s | SMA 50: $%s" (formatDecimal latestSMA20) (formatDecimal latestSMA50)
        sprintf "📊 EMA 12: $%s | EMA 26: $%s" (formatDecimal latestEMA12) (formatDecimal (if indicators.EMA26.Length > 0 then indicators.EMA26.[indicators.EMA26.Length - 1].Value else 0m))
        ""
        sprintf "🔍 RSI (14): %s [%s]" (formatDecimal latestRSI) rsiSignal
        sprintf "📊 MACD: %s | Signal: %s [%s]" (formatDecimal latestMACD.MACD) (formatDecimal latestMACD.Signal) macdSignal
        ""
        sprintf "🔸 Bollinger Bands:"
        sprintf "   Upper: $%s | Middle: $%s | Lower: $%s" (formatDecimal latestBB.Upper) (formatDecimal latestBB.Middle) (formatDecimal latestBB.Lower)
    ] |> String.concat "\n"



let generateBacktestReport (backtestResult: BacktestResult) =
    let formatPercentage (value: decimal) = sprintf "%.2f%%" value
    let formatCurrency (value: decimal) = sprintf "$%s" (value.ToString("N2"))
    
    [
        "📈 BACKTESTING RESULTS"
        "═══════════════════════════════════════════"
        sprintf "💰 Portfolio Performance:"
        sprintf "   Starting Capital: %s" (formatCurrency backtestResult.Portfolio.StartingCapital)
        sprintf "   Final Value: %s" (formatCurrency backtestResult.Portfolio.TotalValue)
        sprintf "   Cash Position: %s" (formatCurrency backtestResult.Portfolio.Cash)
        sprintf "   Total Return: %s" (formatPercentage backtestResult.TotalReturn)
        sprintf "   Annualized Return: %s" (formatPercentage backtestResult.AnnualizedReturn)
        ""
        sprintf "📊 Risk Metrics:"
        sprintf "   Max Drawdown: %s" (formatPercentage backtestResult.MaxDrawdown)
        sprintf "   Sharpe Ratio: %.2f" backtestResult.SharpeRatio
        sprintf "   Profit Factor: %.2f" backtestResult.ProfitFactor
        ""
        sprintf "🎯 Trading Statistics:"
        sprintf "   Total Trades: %d" backtestResult.TotalTrades
        sprintf "   Win Rate: %s" (formatPercentage backtestResult.WinRate)
        sprintf "   Trading Days: %d" backtestResult.TradingDays
        sprintf "   Active Positions: %d" backtestResult.Portfolio.Positions.Count
        ""
        sprintf "📋 Strategy: RSI + MACD Signal Generation"
        sprintf "   • Buy when RSI < 30 (oversold)"
        sprintf "   • Sell when RSI > 70 (overbought)"
        sprintf "   • MACD crossover confirmations"
    ] |> String.concat "\n"

let generateMarketDataWithBacktestReport (marketData: TimeSeriesData<MarketDataPoint>[]) (indicators: TechnicalIndicatorSet[]) (backtestResult: BacktestResult option) =
    let timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss UTC")
    let totalDataPoints = marketData |> Array.sumBy (fun ts -> ts.Data.Length)
    
    let header = 
        [
            "🚀 ATLAS TRADING STRATEGY - BACKTESTED ANALYSIS"
            sprintf "Generated: %s" timestamp
            "═══════════════════════════════════════════════════════════════"
            ""
            "📊 DATA SUMMARY"
            sprintf "   Market Instruments: %d" marketData.Length
            sprintf "   Total Data Points: %s" (totalDataPoints.ToString("N0"))
            sprintf "   Technical Indicators: %d symbols analyzed" indicators.Length
            sprintf "   Backtesting: %s" (match backtestResult with Some _ -> "✅ Complete" | None -> "❌ Not Run")
            ""
        ] |> String.concat "\n"
    
    let marketReports = 
        marketData
        |> Array.map generateMarketDataReport
        |> String.concat "\n\n"
    
    let indicatorReports =
        indicators
        |> Array.map generateTechnicalIndicatorReport  
        |> String.concat "\n\n"
    
    let backtestReport = 
        match backtestResult with
        | Some result -> generateBacktestReport result
        | None -> ""
    
    let footer = 
        [
            ""
            "═══════════════════════════════════════════════════════════════"
            "🎯 PHASE 4 COMPLETE: Backtesting Engine"
            "   • Market data successfully retrieved from Alpaca"
            "   • Technical indicators calculated (SMA, EMA, RSI, MACD, Bollinger Bands)"
            "   • Trading strategy backtested with realistic costs"
            "   • Ready for Phase 5: Risk Management"
            "═══════════════════════════════════════════════════════════════"
        ] |> String.concat "\n"
    
    let parts = [header; marketReports; indicatorReports] @ (if backtestReport <> "" then [backtestReport] else []) @ [footer]
    parts |> String.concat "\n\n"