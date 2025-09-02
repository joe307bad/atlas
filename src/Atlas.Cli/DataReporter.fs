module TradingStrategy.DataReporter

open System
open TradingStrategy.Data

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



let generateMarketDataOnlyReport (marketData: TimeSeriesData<MarketDataPoint>[]) =
    let timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss UTC")
    let totalDataPoints = marketData |> Array.sumBy (fun ts -> ts.Data.Length)
    
    let header = 
        [
            "🚀 ATLAS TRADING STRATEGY - ALPACA MARKET DATA"
            sprintf "Generated: %s" timestamp
            "═══════════════════════════════════════════════════════════════"
            ""
            "📊 DATA SUMMARY"
            sprintf "   Market Instruments: %d" marketData.Length
            sprintf "   Total Data Points: %s" (totalDataPoints.ToString("N0"))
            ""
        ] |> String.concat "\n"
    
    let marketReports = 
        marketData
        |> Array.map generateMarketDataReport
        |> String.concat "\n\n"
    
    let footer = 
        [
            ""
            "═══════════════════════════════════════════════════════════════"
            "🎯 PHASE 1 COMPLETE: Data Infrastructure"
            "   • Market data successfully retrieved from Alpaca"
            "   • Ready for Phase 2: Technical Indicators"
            "═══════════════════════════════════════════════════════════════"
        ] |> String.concat "\n"
    
    [header; marketReports; footer] |> String.concat "\n\n"