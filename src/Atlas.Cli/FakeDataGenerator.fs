module TradingStrategy.FakeDataGenerator

open System
open System.IO
open TradingStrategy.Data

type PriceMovement = 
    | Uptrend
    | Downtrend
    | Sideways
    | Volatile

type FakeDataScenario = {
    Symbol: string
    BasePrice: decimal
    Volatility: decimal
    TrendStrength: decimal
    Duration: TimeSpan
    Movement: PriceMovement
}

let generateFakeMarketDataPoint (scenario: FakeDataScenario) (timestamp: DateTime) (previousClose: decimal) (random: Random) : MarketDataPoint =
    let volatilityFactor = scenario.Volatility * (decimal (random.NextDouble() * 2.0 - 1.0))
    
    // Calculate trend based on movement type
    let trendFactor = 
        match scenario.Movement with
        | Uptrend -> scenario.TrendStrength
        | Downtrend -> -scenario.TrendStrength
        | Sideways -> decimal (random.NextDouble() * 0.002 - 0.001) // Small random walk
        | Volatile -> decimal (random.NextDouble() * 0.02 - 0.01) * scenario.TrendStrength
    
    // Calculate new price with trend and volatility
    let baseChange = previousClose * (trendFactor + volatilityFactor)
    let newPrice = previousClose + baseChange
    
    // Generate OHLC with realistic constraints
    let open' = previousClose + (baseChange * decimal (random.NextDouble() * 0.3))
    let close = newPrice
    
    // High and low with some randomness but maintaining OHLC rules
    let priceRange = abs(baseChange) * (1m + decimal (random.NextDouble()))
    let high = max open' close + (priceRange * decimal (random.NextDouble() * 0.5))
    let low = min open' close - (priceRange * decimal (random.NextDouble() * 0.5))
    
    // Generate realistic volume
    let baseVolume = 1000000L
    let volumeVariation = int64 (random.NextDouble() * 500000.0)
    let volume = baseVolume + volumeVariation
    
    {
        Timestamp = timestamp
        Open = Math.Round(open', 2)
        High = Math.Round(high, 2)
        Low = Math.Round(low, 2)
        Close = Math.Round(close, 2)
        Volume = volume
    }

let generateScenarioData (scenario: FakeDataScenario) (startTime: DateTime) (intervalSeconds: int) : MarketDataPoint[] =
    let random = Random()
    let intervalCount = int (scenario.Duration.TotalSeconds / float intervalSeconds)
    
    // For the specific 1-minute scenario: up for 30 seconds, down for 30 seconds
    let halfPoint = intervalCount / 2
    
    let mutable dataPoints = Array.zeroCreate intervalCount
    let mutable previousClose = scenario.BasePrice
    
    for i in 0 .. intervalCount - 1 do
        let timestamp = startTime.AddSeconds(float (i * intervalSeconds))
        
        // Modify scenario based on position in timeline for split movement
        let currentScenario = 
            if scenario.Movement = Uptrend && i >= halfPoint then
                { scenario with Movement = Downtrend }
            elif scenario.Movement = Downtrend && i >= halfPoint then
                { scenario with Movement = Uptrend }
            else
                scenario
        
        let dataPoint = generateFakeMarketDataPoint currentScenario timestamp previousClose random
        dataPoints.[i] <- dataPoint
        previousClose <- dataPoint.Close
    
    dataPoints

let generateMinuteWithUpDownPattern (symbol: string) (basePrice: decimal) (startTime: DateTime) : MarketDataPoint[] =
    // Generate data points every second for 1 minute
    let scenario = {
        Symbol = symbol
        BasePrice = basePrice
        Volatility = 0.001m  // 0.1% volatility
        TrendStrength = 0.002m  // 0.2% trend per interval
        Duration = TimeSpan.FromMinutes(1.0)
        Movement = Uptrend  // Will switch to Downtrend at halfway point
    }
    
    generateScenarioData scenario startTime 1  // 1-second intervals

let exportToCsv (data: MarketDataPoint[]) (symbol: string) (filePath: string) =
    use writer = new StreamWriter(filePath)
    
    // Write CSV header matching Alpaca data format
    writer.WriteLine("timestamp,symbol,open,high,low,close,volume")
    
    // Write data rows
    data
    |> Array.iter (fun point ->
        let timestamp = point.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")
        let row = sprintf "%s,%s,%.2f,%.2f,%.2f,%.2f,%d" 
                         timestamp symbol point.Open point.High point.Low point.Close point.Volume
        writer.WriteLine(row)
    )
    
    printfn "✅ Generated fake data CSV: %s" filePath
    printfn "   • Symbol: %s" symbol
    printfn "   • Data points: %d" data.Length
    printfn "   • Time range: %s to %s" 
            (data.[0].Timestamp.ToString("HH:mm:ss")) 
            (data.[data.Length - 1].Timestamp.ToString("HH:mm:ss"))

let generateMultipleScenarios (outputDir: string) =
    // Ensure output directory exists
    if not (Directory.Exists(outputDir)) then
        Directory.CreateDirectory(outputDir) |> ignore
    
    let startTime = DateTime.Now.Date.AddHours(9.5) // Market open at 9:30 AM
    
    // Scenario 1: 1 minute up/down pattern (as requested)
    let minuteData = generateMinuteWithUpDownPattern "AAPL" 150.00m startTime
    let minuteFile = Path.Combine(outputDir, "fake_data_1min_updown.csv")
    exportToCsv minuteData "AAPL" minuteFile
    
    // Scenario 2: 5-minute volatile pattern
    let volatileScenario = {
        Symbol = "TSLA"
        BasePrice = 250.00m
        Volatility = 0.005m  // 0.5% volatility (higher)
        TrendStrength = 0.001m
        Duration = TimeSpan.FromMinutes(5.0)
        Movement = Volatile
    }
    let volatileData = generateScenarioData volatileScenario startTime 5  // 5-second intervals
    let volatileFile = Path.Combine(outputDir, "fake_data_5min_volatile.csv")
    exportToCsv volatileData "TSLA" volatileFile
    
    // Scenario 3: 10-minute steady uptrend
    let uptrendScenario = {
        Symbol = "MSFT"
        BasePrice = 400.00m
        Volatility = 0.0005m  // 0.05% volatility (low)
        TrendStrength = 0.0015m  // Steady upward trend
        Duration = TimeSpan.FromMinutes(10.0)
        Movement = Uptrend
    }
    let uptrendData = generateScenarioData uptrendScenario startTime 10  // 10-second intervals
    let uptrendFile = Path.Combine(outputDir, "fake_data_10min_uptrend.csv")
    exportToCsv uptrendData "MSFT" uptrendFile
    
    // Scenario 4: 15-minute crash scenario
    let crashScenario = {
        Symbol = "META"
        BasePrice = 500.00m
        Volatility = 0.002m
        TrendStrength = 0.003m  // Strong downward trend
        Duration = TimeSpan.FromMinutes(15.0)
        Movement = Downtrend
    }
    let crashData = generateScenarioData crashScenario startTime 15  // 15-second intervals
    let crashFile = Path.Combine(outputDir, "fake_data_15min_crash.csv")
    exportToCsv crashData "META" crashFile
    
    printfn ""
    printfn "📊 Generated Multiple Test Scenarios:"
    printfn "   1. 1-minute up/down pattern (AAPL) - Tests rapid reversal"
    printfn "   2. 5-minute volatile pattern (TSLA) - Tests high volatility handling"
    printfn "   3. 10-minute uptrend (MSFT) - Tests trend following"
    printfn "   4. 15-minute crash (META) - Tests stop-loss and risk management"
    printfn ""
    printfn "📁 All files saved to: %s" outputDir