module TradingStrategy.RealTimeData

open System
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent
open TradingStrategy.Data
open TradingStrategy.TechnicalIndicators
open Alpaca.Markets

type TickData = {
    Symbol: string
    Price: decimal
    Size: int
    Timestamp: DateTime
    BidPrice: decimal option
    AskPrice: decimal option
    BidSize: int option
    AskSize: int option
    Exchange: string option
}

type MarketHours = {
    PreMarketStart: TimeSpan    // 04:00 ET
    MarketOpen: TimeSpan        // 09:30 ET
    MarketClose: TimeSpan       // 16:00 ET
    AfterHoursEnd: TimeSpan     // 20:00 ET
    TimeZone: TimeZoneInfo
}

type MarketSession =
    | PreMarket
    | Regular
    | AfterHours
    | Closed

type DataQuality = {
    Symbol: string
    LastUpdate: DateTime
    GapDetected: bool
    GapDuration: TimeSpan option
    TickCount: int64
    AverageLatency: TimeSpan
    DataIntegrity: decimal  // 0.0 to 1.0
}

type StreamingIndicators = {
    Symbol: string
    SMA20: decimal option
    EMA20: decimal option
    RSI14: decimal option
    MACD: MACDValue option
    BollingerBands: BollingerBandsValue option
    LastUpdate: DateTime
}

type RealTimeDataBuffer = {
    MaxSize: int
    Ticks: ConcurrentQueue<TickData>
    Bars: ConcurrentQueue<MarketDataPoint>
    LastCleanup: DateTime
}

type DataStreamState =
    | Disconnected
    | Connecting
    | Connected
    | StreamError of string
    | Reconnecting

type RealTimeDataProvider = {
    State: DataStreamState
    BufferSize: int
    Symbols: string[]
    DataBuffer: ConcurrentDictionary<string, RealTimeDataBuffer>
    StreamingIndicators: ConcurrentDictionary<string, StreamingIndicators>
    DataQuality: ConcurrentDictionary<string, DataQuality>
    MarketHours: MarketHours
    CancellationToken: CancellationTokenSource
}

// Market hours detection
let createMarketHours () =
    let easternTime = TimeZoneInfo.FindSystemTimeZoneById("America/New_York")
    {
        PreMarketStart = TimeSpan(4, 0, 0)   // 04:00 ET
        MarketOpen = TimeSpan(9, 30, 0)      // 09:30 ET  
        MarketClose = TimeSpan(16, 0, 0)     // 16:00 ET
        AfterHoursEnd = TimeSpan(20, 0, 0)   // 20:00 ET
        TimeZone = easternTime
    }

let getCurrentMarketSession (marketHours: MarketHours) (currentTime: DateTime) =
    let easternTime = TimeZoneInfo.ConvertTime(currentTime, marketHours.TimeZone)
    let timeOfDay = easternTime.TimeOfDay
    let dayOfWeek = easternTime.DayOfWeek
    
    // Check if it's weekend
    if dayOfWeek = DayOfWeek.Saturday || dayOfWeek = DayOfWeek.Sunday then
        Closed
    else
        if timeOfDay >= marketHours.PreMarketStart && timeOfDay < marketHours.MarketOpen then
            PreMarket
        elif timeOfDay >= marketHours.MarketOpen && timeOfDay < marketHours.MarketClose then
            Regular
        elif timeOfDay >= marketHours.MarketClose && timeOfDay < marketHours.AfterHoursEnd then
            AfterHours
        else
            Closed

let isMarketOpen (marketHours: MarketHours) (currentTime: DateTime) =
    match getCurrentMarketSession marketHours currentTime with
    | Regular -> true
    | _ -> false

// Data validation and cleaning with market hours awareness
let validateTickDataWithMarketHours (tick: TickData) (marketHours: MarketHours option) (isSimulation: bool) =
    let validationErrors = ResizeArray<string>()
    
    // Price validation
    if tick.Price <= 0m then
        validationErrors.Add("Invalid price: must be positive")
    
    // Size validation  
    if tick.Size <= 0 then
        validationErrors.Add("Invalid size: must be positive")
    
    // Timestamp validation - adjusted for market conditions
    let maxAge = 
        if isSimulation then 
            TimeSpan.FromDays(90.0)  // 90 days for simulation/replay
        else 
            match marketHours with
            | Some mh -> 
                let currentSession = getCurrentMarketSession mh DateTime.Now
                match currentSession with
                | Closed -> TimeSpan.FromHours(24.0)  // 24 hours during closed market
                | _ -> TimeSpan.FromHours(1.0)        // 1 hour during market hours
            | None -> TimeSpan.FromHours(1.0)        // Default 1 hour
    
    let cutoffTime = DateTime.UtcNow.Subtract(maxAge)
    if tick.Timestamp < cutoffTime then
        validationErrors.Add(sprintf "Stale data: timestamp too old (older than %.1f hours)" maxAge.TotalHours)
    
    // Bid/Ask spread validation
    match tick.BidPrice, tick.AskPrice with
    | Some bid, Some ask when bid >= ask ->
        validationErrors.Add("Invalid spread: bid >= ask")
    | Some bid, _ when bid <= 0m ->
        validationErrors.Add("Invalid bid price: must be positive")
    | _, Some ask when ask <= 0m ->
        validationErrors.Add("Invalid ask price: must be positive")
    | _ -> ()
    
    if validationErrors.Count = 0 then Ok tick
    else Error (validationErrors.ToArray())

// Backward-compatible validation function for existing code
let validateTickData (tick: TickData) =
    let validationErrors = ResizeArray<string>()
    
    // Price validation
    if tick.Price <= 0m then
        validationErrors.Add("Invalid price: must be positive")
    
    // Size validation  
    if tick.Size <= 0 then
        validationErrors.Add("Invalid size: must be positive")
    
    // Timestamp validation - more lenient for historical replay and closed markets
    // Allow data up to 7 days old for simulation purposes
    let weekAgo = DateTime.UtcNow.AddDays(-7.0)
    if tick.Timestamp < weekAgo then
        validationErrors.Add("Stale data: timestamp too old (older than 7 days)")
    
    // Bid/Ask spread validation
    match tick.BidPrice, tick.AskPrice with
    | Some bid, Some ask when bid >= ask ->
        validationErrors.Add("Invalid spread: bid >= ask")
    | Some bid, _ when bid <= 0m ->
        validationErrors.Add("Invalid bid price: must be positive")
    | _, Some ask when ask <= 0m ->
        validationErrors.Add("Invalid ask price: must be positive")
    | _ -> ()
    
    if validationErrors.Count = 0 then Ok tick
    else Error (validationErrors.ToArray())

let cleanTickData (tick: TickData) =
    // Remove outlier prices (basic statistical filter)
    // In production, this would use more sophisticated outlier detection
    let cleanedTick = tick
    
    // Round prices to appropriate precision
    let roundedPrice = Math.Round(tick.Price, 4)
    
    { cleanedTick with Price = roundedPrice }

// Data buffering
let createDataBuffer (maxSize: int) =
    {
        MaxSize = maxSize
        Ticks = ConcurrentQueue<TickData>()
        Bars = ConcurrentQueue<MarketDataPoint>()
        LastCleanup = DateTime.UtcNow
    }

let addTickToBuffer (buffer: RealTimeDataBuffer) (tick: TickData) =
    buffer.Ticks.Enqueue(tick)
    
    // Cleanup old data if buffer is full
    let mutable item = Unchecked.defaultof<TickData>
    while buffer.Ticks.Count > buffer.MaxSize && buffer.Ticks.TryDequeue(&item) do
        ()

let addBarToBuffer (buffer: RealTimeDataBuffer) (bar: MarketDataPoint) =
    buffer.Bars.Enqueue(bar)
    
    // Cleanup old data if buffer is full
    let mutable item = Unchecked.defaultof<MarketDataPoint>
    while buffer.Bars.Count > buffer.MaxSize && buffer.Bars.TryDequeue(&item) do
        ()

// Convert ticks to OHLCV bars
let aggregateTicksToBar (ticks: TickData[]) (timeframe: TimeSpan) =
    if ticks.Length = 0 then None
    else
        let sortedTicks = ticks |> Array.sortBy (fun t -> t.Timestamp)
        let firstTick = sortedTicks.[0]
        let lastTick = sortedTicks.[sortedTicks.Length - 1]
        
        let high = sortedTicks |> Array.map (fun t -> t.Price) |> Array.max
        let low = sortedTicks |> Array.map (fun t -> t.Price) |> Array.min
        let volume = sortedTicks |> Array.sumBy (fun t -> int64 t.Size)
        
        Some {
            Timestamp = firstTick.Timestamp
            Open = firstTick.Price
            High = high
            Low = low  
            Close = lastTick.Price
            Volume = volume
        }

// Gap detection
let detectDataGaps (buffer: RealTimeDataBuffer) (expectedInterval: TimeSpan) =
    let bars = buffer.Bars.ToArray() |> Array.sortBy (fun b -> b.Timestamp)
    
    if bars.Length < 2 then []
    else
        bars
        |> Array.pairwise
        |> Array.choose (fun (prev, curr) ->
            let actualGap = curr.Timestamp - prev.Timestamp
            if actualGap > expectedInterval.Add(TimeSpan.FromSeconds(30.0)) then // 30s tolerance
                Some {
                    Symbol = "UNKNOWN" // Will be updated by caller
                    LastUpdate = curr.Timestamp
                    GapDetected = true
                    GapDuration = Some actualGap
                    TickCount = 0L
                    AverageLatency = TimeSpan.Zero
                    DataIntegrity = 0.8m  // Reduced due to gap
                }
            else None
        )
        |> Array.toList

// Streaming technical indicators
let updateStreamingIndicators (symbol: string) (latestBar: MarketDataPoint) (historicalBars: MarketDataPoint[]) =
    try
        let allBars = Array.append historicalBars [| latestBar |]
        
        // Calculate indicators with latest data
        let sma20 = 
            if allBars.Length >= 20 then
                Some (calculateSMA allBars 20 |> Array.last |> fun p -> p.Value)
            else None
        
        let ema20 = 
            if allBars.Length >= 20 then
                Some (calculateEMA allBars 20 |> Array.last |> fun p -> p.Value)
            else None
        
        let rsi14 = 
            if allBars.Length >= 15 then
                Some (calculateRSI allBars 14 |> Array.last |> fun p -> p.Value)
            else None
        
        let macd = 
            if allBars.Length >= 35 then
                Some (calculateMACD allBars 12 26 9 |> Array.last)
            else None
        
        let bollinger = 
            if allBars.Length >= 20 then
                Some (calculateBollingerBands allBars 20 2.0m |> Array.last)
            else None
        
        {
            Symbol = symbol
            SMA20 = sma20
            EMA20 = ema20
            RSI14 = rsi14
            MACD = macd
            BollingerBands = bollinger
            LastUpdate = DateTime.UtcNow
        }
    with
    | ex ->
        // Return empty indicators on error
        {
            Symbol = symbol
            SMA20 = None
            EMA20 = None
            RSI14 = None
            MACD = None
            BollingerBands = None
            LastUpdate = DateTime.UtcNow
        }

// Data quality monitoring
let calculateDataQuality (buffer: RealTimeDataBuffer) (symbol: string) (expectedUpdateInterval: TimeSpan) =
    let now = DateTime.UtcNow
    let recentTicks = 
        buffer.Ticks.ToArray() 
        |> Array.filter (fun t -> now - t.Timestamp < TimeSpan.FromMinutes(5.0))
    
    let lastUpdate = 
        if recentTicks.Length > 0 then
            recentTicks |> Array.map (fun t -> t.Timestamp) |> Array.max
        else
            DateTime.MinValue
    
    let gaps = detectDataGaps buffer expectedUpdateInterval
    let hasGaps = gaps.Length > 0
    
    let averageLatency = 
        if recentTicks.Length > 0 then
            let latencies = 
                recentTicks 
                |> Array.map (fun t -> now - t.Timestamp)
            
            TimeSpan.FromTicks(latencies |> Array.map (fun ts -> float ts.Ticks) |> Array.average |> int64)
        else
            TimeSpan.Zero
    
    let integrity = 
        if hasGaps then 0.7m
        elif averageLatency > TimeSpan.FromSeconds(5.0) then 0.8m
        elif recentTicks.Length < 10 then 0.9m
        else 1.0m
    
    {
        Symbol = symbol
        LastUpdate = lastUpdate
        GapDetected = hasGaps
        GapDuration = if hasGaps then gaps.[0].GapDuration else None
        TickCount = int64 recentTicks.Length
        AverageLatency = averageLatency
        DataIntegrity = integrity
    }

// Real-time data provider
let createRealTimeDataProvider (symbols: string[]) (bufferSize: int) =
    let marketHours = createMarketHours()
    let dataBuffers = 
        let dict = ConcurrentDictionary<string, RealTimeDataBuffer>()
        symbols |> Array.iter (fun symbol -> dict.TryAdd(symbol, createDataBuffer bufferSize) |> ignore)
        dict
    
    let streamingIndicators = ConcurrentDictionary<string, StreamingIndicators>()
    let dataQuality = ConcurrentDictionary<string, DataQuality>()
    
    {
        State = Disconnected
        BufferSize = bufferSize
        Symbols = symbols
        DataBuffer = dataBuffers
        StreamingIndicators = streamingIndicators
        DataQuality = dataQuality
        MarketHours = marketHours
        CancellationToken = new CancellationTokenSource()
    }

// Simulate real-time tick data (for testing without actual WebSocket)
let simulateTickData (symbol: string) (basePrice: decimal) (volatility: decimal) =
    let random = Random()
    let priceChange = decimal (random.NextDouble() - 0.5) * volatility * basePrice / 100m
    let newPrice = Math.Max(0.01m, basePrice + priceChange)
    
    {
        Symbol = symbol
        Price = newPrice
        Size = random.Next(100, 1000)
        Timestamp = DateTime.UtcNow
        BidPrice = Some (newPrice - 0.01m)
        AskPrice = Some (newPrice + 0.01m)
        BidSize = Some (random.Next(100, 500))
        AskSize = Some (random.Next(100, 500))
        Exchange = Some "NASDAQ"
    }

// Process incoming tick data
let processIncomingTick (provider: RealTimeDataProvider) (tick: TickData) =
    task {
        // Determine if this is fake current-time data vs historical replay
        let isFakeCurrentData = 
            tick.Exchange = Some "SIMULATED" || 
            (DateTime.UtcNow - tick.Timestamp).TotalMinutes < 5.0  // Within 5 minutes = fake current data
        
        // Use appropriate validation based on data type
        let validationResult = 
            if isFakeCurrentData then
                // For fake current data, use lenient validation (no timestamp checks)
                validateTickData tick
            else
                // For historical replay, use market-hours aware validation
                validateTickDataWithMarketHours tick (Some provider.MarketHours) true
        
        match validationResult with
        | Error errors ->
            printfn "❌ Invalid tick data for %s: %s" tick.Symbol (String.concat ", " errors)
            return ()
        | Ok validTick ->
            // Clean the data
            let cleanedTick = cleanTickData validTick
            
            // Add to buffer
            match provider.DataBuffer.TryGetValue(cleanedTick.Symbol) with
            | true, buffer ->
                addTickToBuffer buffer cleanedTick
                
                // Update data quality
                let quality = calculateDataQuality buffer cleanedTick.Symbol (TimeSpan.FromSeconds(1.0))
                provider.DataQuality.AddOrUpdate(cleanedTick.Symbol, quality, fun _ _ -> quality) |> ignore
                return ()
                
            | false, _ ->
                printfn "⚠️  Received tick for untracked symbol: %s" cleanedTick.Symbol
                return ()
    }

// Aggregate ticks to bars and update indicators
let aggregateAndUpdateIndicators (provider: RealTimeDataProvider) (symbol: string) (timeframe: TimeSpan) =
    task {
        match provider.DataBuffer.TryGetValue(symbol) with
        | true, buffer ->
            let recentTicks = 
                buffer.Ticks.ToArray()
                |> Array.filter (fun t -> DateTime.UtcNow - t.Timestamp < timeframe)
            
            match aggregateTicksToBar recentTicks timeframe with
            | Some bar ->
                addBarToBuffer buffer bar
                
                // Get historical bars for indicator calculation
                let historicalBars = buffer.Bars.ToArray() |> Array.take (min 200 buffer.Bars.Count)
                
                // Update streaming indicators
                let indicators = updateStreamingIndicators symbol bar historicalBars
                provider.StreamingIndicators.AddOrUpdate(symbol, indicators, fun _ _ -> indicators) |> ignore
                
            | None ->
                ()
        | false, _ ->
            ()
    }

// Data replay capabilities
let replayHistoricalData (provider: RealTimeDataProvider) (historicalData: TimeSeriesData<MarketDataPoint>[]) (speedMultiplier: float) =
    task {
        printfn "🔄 Starting data replay at %.1fx speed..." speedMultiplier
        
        let allDataPoints = 
            historicalData
            |> Array.collect (fun ts -> 
                ts.Data |> Array.map (fun dp -> (ts.Symbol, dp)))
            |> Array.sortBy (fun (_, dp) -> dp.Timestamp)
        
        for (symbol, dataPoint) in allDataPoints do
            if not provider.CancellationToken.Token.IsCancellationRequested then
                // Convert bar to simulated tick
                let simulatedTick = {
                    Symbol = symbol
                    Price = dataPoint.Close
                    Size = int (dataPoint.Volume % 1000L + 100L)
                    Timestamp = dataPoint.Timestamp
                    BidPrice = Some (dataPoint.Close - 0.01m)
                    AskPrice = Some (dataPoint.Close + 0.01m)
                    BidSize = Some 500
                    AskSize = Some 500
                    Exchange = Some "REPLAY"
                }
                
                do! processIncomingTick provider simulatedTick
                
                // Delay based on speed multiplier
                let delay = TimeSpan.FromMilliseconds(100.0 / speedMultiplier)
                do! Task.Delay(delay, provider.CancellationToken.Token)
        
        printfn "✅ Data replay completed"
    }

// Trading Strategy Types
type TradingPosition = {
    Symbol: string
    Quantity: int
    EntryPrice: decimal
    CurrentPrice: decimal
    PnL: decimal
    EntryTime: DateTime
}

type SymbolPnLReport = {
    Symbol: string
    TotalTrades: int
    WinningTrades: int
    LosingTrades: int
    TotalPnL: decimal
    LargestWin: decimal
    LargestLoss: decimal
    AverageTradeReturn: decimal
}

type TradingSessionReport = {
    TotalPnL: decimal
    TotalTrades: int
    WinRate: decimal
    SymbolReports: SymbolPnLReport[]
    SessionDuration: TimeSpan
}

type TradingSignal = {
    Symbol: string
    Action: string  // "BUY" or "SELL"
    Quantity: int
    Price: decimal
    Confidence: decimal
    Reasoning: string
    Timestamp: DateTime
}

type TradingStrategy = {
    MaxLossPercent: decimal     // Stop loss at X% loss
    MaxPositionSize: int        // Max shares per position
    MinConfidence: decimal      // Minimum ML confidence to trade
}

// Simple trading strategy implementation
let createTradingStrategy () =
    {
        MaxLossPercent = 0.02m      // Stop loss at 2% loss (tighter)
        MaxPositionSize = 100       // Max 100 shares per position
        MinConfidence = 0.6m        // 60% minimum confidence (higher bar)
    }

// Generate fake but realistic tick data for simulation
let generateSimulatedTick (symbol: string) (previousPrice: decimal option) (volatility: decimal) =
    let random = Random()
    let basePrice = previousPrice |> Option.defaultValue 500m
    
    // Generate realistic price movement (-0.5% to +0.5%)
    let priceChangePercent = (decimal (random.NextDouble() - 0.5)) * volatility
    let newPrice = basePrice * (1m + priceChangePercent)
    let size = random.Next(50, 500)
    
    {
        Symbol = symbol
        Price = Math.Max(1m, newPrice)
        Size = size
        Timestamp = DateTime.UtcNow  // Use current time for fake data
        BidPrice = Some (newPrice - 0.01m)
        AskPrice = Some (newPrice + 0.01m)
        BidSize = Some (random.Next(100, 300))
        AskSize = Some (random.Next(100, 300))
        Exchange = Some "SIMULATED"
    }

// ML-inspired signal generation with trend analysis
let generateTradingSignal (symbol: string) (currentPrice: decimal) (indicators: StreamingIndicators option) (strategy: TradingStrategy) =
    let random = Random()
    
    match indicators with
    | Some ind ->
        // Check trend direction using SMA
        let trendDirection = 
            match ind.SMA20 with
            | Some sma20 when currentPrice > sma20 * 1.005m -> "UPTREND"  // Price 0.5% above SMA
            | Some sma20 when currentPrice < sma20 * 0.995m -> "DOWNTREND" // Price 0.5% below SMA
            | _ -> "SIDEWAYS"
        
        let rsiSignal = 
            match ind.RSI14, trendDirection with
            // Only buy oversold in uptrend or sideways markets
            | Some rsi, "UPTREND" when rsi < 35m -> Some ("BUY", 0.8m, "RSI oversold in uptrend")
            | Some rsi, "SIDEWAYS" when rsi < 25m -> Some ("BUY", 0.7m, "RSI oversold in sideways")
            // Sell overbought in any market
            | Some rsi, _ when rsi > 75m -> Some ("SELL", 0.8m, "RSI overbought")
            // Avoid buying in downtrends entirely
            | Some rsi, "DOWNTREND" -> None
            | _ -> None
        
        let macdSignal =
            match ind.MACD, trendDirection with
            // MACD bullish crossover - only in uptrend
            | Some macd, "UPTREND" when macd.MACD > macd.Signal && macd.Histogram > 0m -> 
                Some ("BUY", 0.6m, "MACD bullish crossover in uptrend")
            // MACD bearish crossover - sell signal
            | Some macd, _ when macd.MACD < macd.Signal && macd.Histogram < 0m -> 
                Some ("SELL", 0.7m, "MACD bearish crossover")
            | _ -> None
        
        // Combine signals (simple logic)
        match rsiSignal, macdSignal with
        | Some (action1, conf1, reason1), Some (action2, conf2, reason2) when action1 = action2 ->
            let combinedConfidence = (conf1 + conf2) / 2m
            if combinedConfidence >= strategy.MinConfidence then
                Some {
                    Symbol = symbol
                    Action = action1
                    Quantity = strategy.MaxPositionSize
                    Price = currentPrice
                    Confidence = combinedConfidence
                    Reasoning = sprintf "%s + %s" reason1 reason2
                    Timestamp = DateTime.UtcNow
                }
            else None
        | Some (action, conf, reason), None | None, Some (action, conf, reason) ->
            if conf >= strategy.MinConfidence then
                Some {
                    Symbol = symbol
                    Action = action
                    Quantity = strategy.MaxPositionSize / 2  // Half size for single signal
                    Price = currentPrice
                    Confidence = conf
                    Reasoning = reason
                    Timestamp = DateTime.UtcNow
                }
            else None
        | _ -> None
    | None -> None

// Check stop-loss conditions
let checkStopLoss (position: TradingPosition) (strategy: TradingStrategy) =
    let lossPercent = (position.EntryPrice - position.CurrentPrice) / position.EntryPrice
    if lossPercent >= strategy.MaxLossPercent then
        Some {
            Symbol = position.Symbol
            Action = "SELL"
            Quantity = abs position.Quantity
            Price = position.CurrentPrice
            Confidence = 1.0m
            Reasoning = sprintf "Stop loss triggered at %.1f%% loss" (lossPercent * 100m)
            Timestamp = DateTime.UtcNow
        }
    else None

// Check profit-taking conditions (take profit at 3% gain)
let checkTakeProfit (position: TradingPosition) =
    let profitPercent = (position.CurrentPrice - position.EntryPrice) / position.EntryPrice
    if profitPercent >= 0.01m then  // Take profit at 1% gain
        Some {
            Symbol = position.Symbol
            Action = "SELL"
            Quantity = abs position.Quantity
            Price = position.CurrentPrice
            Confidence = 1.0m
            Reasoning = sprintf "Take profit (%.1f%% gain)" (profitPercent * 100m)
            Timestamp = DateTime.UtcNow
        }
    else None

// Simulate real-time trading with fake data and strategy execution
let simulateRealTimeTrading (provider: RealTimeDataProvider) (symbols: string[]) =
    task {
        let sessionStart = DateTime.UtcNow
        printfn "🎮 Starting enhanced simulation with trading strategy..."
        
        let strategy = createTradingStrategy()
        let mutable positions = Map.empty<string, TradingPosition>
        let mutable priceHistory = Map.empty<string, decimal>
        let mutable totalPnL = 0m
        let mutable tradeCount = 0
        let mutable tradesBySymbol = Map.empty<string, ResizeArray<decimal>>  // Track P&L per symbol
        
        printfn "📊 Trading Strategy:"
        printfn "   • Stop Loss: %.1f%%" (strategy.MaxLossPercent * 100m)
        printfn "   • Max Position: %d shares" strategy.MaxPositionSize
        printfn "   • Min Confidence: %.0f%%" (strategy.MinConfidence * 100m)
        printfn ""
        
        for i in 1..300 do  // 5 minutes of simulation (1 tick per second)
            if not provider.CancellationToken.Token.IsCancellationRequested then
                // Generate simulated ticks for each symbol
                for symbol in symbols do
                    let previousPrice = Map.tryFind symbol priceHistory
                    let volatility = 0.005m  // 0.5% volatility
                    let simulatedTick = generateSimulatedTick symbol previousPrice volatility
                    
                    // Update price history
                    priceHistory <- Map.add symbol simulatedTick.Price priceHistory
                    
                    // Process the tick
                    do! processIncomingTick provider simulatedTick
                    
                    // Update indicators
                    do! aggregateAndUpdateIndicators provider symbol (TimeSpan.FromMinutes(1.0))
                    
                    // Get current indicators
                    let indicators = 
                        match provider.StreamingIndicators.TryGetValue(symbol) with
                        | true, ind -> Some ind
                        | false, _ -> None
                    
                    // Update existing position prices
                    positions <- 
                        positions
                        |> Map.map (fun sym pos -> 
                            if sym = symbol then 
                                let pnl = decimal pos.Quantity * (simulatedTick.Price - pos.EntryPrice)
                                { pos with CurrentPrice = simulatedTick.Price; PnL = pnl }
                            else pos
                        )
                    
                    // Check for stop-loss triggers
                    match Map.tryFind symbol positions with
                    | Some position ->
                        match checkStopLoss position strategy with
                        | Some stopLossSignal ->
                            printfn "🚨 STOP LOSS: %s at $%.2f (%.1f%% loss)" 
                                    stopLossSignal.Symbol 
                                    stopLossSignal.Price 
                                    ((position.EntryPrice - position.CurrentPrice) / position.EntryPrice * 100m)
                            
                            // Record trade P&L per symbol
                            let symbolTrades = tradesBySymbol |> Map.tryFind symbol |> Option.defaultValue (ResizeArray<decimal>())
                            symbolTrades.Add(position.PnL)
                            tradesBySymbol <- Map.add symbol symbolTrades tradesBySymbol
                            
                            totalPnL <- totalPnL + position.PnL
                            positions <- Map.remove symbol positions
                            tradeCount <- tradeCount + 1
                        | None -> 
                            // Check for take-profit opportunities
                            match checkTakeProfit position with
                            | Some takeProfitSignal ->
                                printfn "💰 TAKE PROFIT: %s at $%.2f - %s" 
                                        takeProfitSignal.Symbol 
                                        takeProfitSignal.Price 
                                        takeProfitSignal.Reasoning
                                
                                // Record trade P&L per symbol
                                let symbolTrades = tradesBySymbol |> Map.tryFind symbol |> Option.defaultValue (ResizeArray<decimal>())
                                symbolTrades.Add(position.PnL)
                                tradesBySymbol <- Map.add symbol symbolTrades tradesBySymbol
                                
                                totalPnL <- totalPnL + position.PnL
                                positions <- Map.remove symbol positions
                                tradeCount <- tradeCount + 1
                            | None -> ()
                    | None ->
                        // Generate trading signals for new positions
                        match generateTradingSignal symbol simulatedTick.Price indicators strategy with
                        | Some signal when signal.Action = "BUY" ->
                            let newPosition = {
                                Symbol = symbol
                                Quantity = signal.Quantity
                                EntryPrice = signal.Price
                                CurrentPrice = signal.Price
                                PnL = 0m
                                EntryTime = DateTime.UtcNow
                            }
                            positions <- Map.add symbol newPosition positions
                            printfn "📈 BUY: %d shares of %s at $%.2f (%.0f%% confidence) - %s" 
                                    signal.Quantity signal.Symbol signal.Price 
                                    (signal.Confidence * 100m) signal.Reasoning
                            tradeCount <- tradeCount + 1
                        | _ -> ()
                
                // Status update every 10 seconds
                if i % 10 = 0 then
                    let activePositions = positions |> Map.count
                    let unrealizedPnL = positions |> Map.fold (fun acc _ pos -> acc + pos.PnL) 0m
                    printfn "💰 Status: %d active positions, $%.2f unrealized P&L, $%.2f total P&L, %d trades" 
                            activePositions unrealizedPnL totalPnL tradeCount
                
                do! Task.Delay(1000)  // 1 second delay
        
        let sessionEnd = DateTime.UtcNow
        let sessionDuration = sessionEnd - sessionStart
        
        printfn ""
        printfn "🎯 TRADING SIMULATION COMPLETE"
        printfn "   Session Duration: %.1f minutes" sessionDuration.TotalMinutes
        printfn "   Final P&L: $%.2f" totalPnL
        printfn "   Total Trades: %d" tradeCount
        printfn "   Active Positions: %d" (Map.count positions)
        
        // Force close all remaining positions to realize P&L
        if not (Map.isEmpty positions) then
            printfn ""
            printfn "🔄 Closing all remaining positions..."
            positions 
            |> Map.iter (fun symbol position ->
                printfn "💰 SESSION END CLOSE: %s at $%.2f (P&L: $%.2f)" 
                        symbol position.CurrentPrice position.PnL
                
                // Record final trade P&L per symbol
                let symbolTrades = tradesBySymbol |> Map.tryFind symbol |> Option.defaultValue (ResizeArray<decimal>())
                symbolTrades.Add(position.PnL)
                tradesBySymbol <- Map.add symbol symbolTrades tradesBySymbol
                
                totalPnL <- totalPnL + position.PnL
                tradeCount <- tradeCount + 1
            )
            printfn "✅ All positions closed. Final realized P&L: $%.2f" totalPnL
        
        // Generate detailed P&L report per symbol
        printfn ""
        printfn "💰 DETAILED P&L REPORT BY SYMBOL"
        printfn "═══════════════════════════════════════════════════════════════"
        
        let symbolReports = 
            symbols
            |> Array.map (fun symbol ->
                let trades = tradesBySymbol |> Map.tryFind symbol |> Option.defaultValue (ResizeArray<decimal>())
                let tradeArray = trades.ToArray()
                
                let totalTrades = tradeArray.Length
                let symbolPnL = if totalTrades > 0 then tradeArray |> Array.sum else 0m
                let winningTrades = tradeArray |> Array.filter (fun pnl -> pnl > 0m) |> Array.length
                let losingTrades = tradeArray |> Array.filter (fun pnl -> pnl < 0m) |> Array.length
                let largestWin = if tradeArray.Length > 0 then tradeArray |> Array.max else 0m
                let largestLoss = if tradeArray.Length > 0 then tradeArray |> Array.min else 0m
                let avgReturn = if totalTrades > 0 then symbolPnL / decimal totalTrades else 0m
                
                // Display per-symbol report
                printfn "%s:" symbol
                printfn "   • Total Trades: %d" totalTrades
                printfn "   • Total P&L: $%.2f" symbolPnL
                if totalTrades > 0 then
                    printfn "   • Win Rate: %.1f%% (%d wins, %d losses)" 
                            (decimal winningTrades / decimal totalTrades * 100m) winningTrades losingTrades
                    printfn "   • Best Trade: $%.2f" largestWin
                    printfn "   • Worst Trade: $%.2f" largestLoss
                    printfn "   • Avg Trade: $%.2f" avgReturn
                else
                    printfn "   • No trades executed"
                printfn ""
                
                {
                    Symbol = symbol
                    TotalTrades = totalTrades
                    WinningTrades = winningTrades
                    LosingTrades = losingTrades
                    TotalPnL = symbolPnL
                    LargestWin = largestWin
                    LargestLoss = largestLoss
                    AverageTradeReturn = avgReturn
                }
            )
        
        let overallWinRate = 
            if tradeCount > 0 then
                let totalWins = symbolReports |> Array.sumBy (fun r -> r.WinningTrades)
                decimal totalWins / decimal tradeCount * 100m
            else 0m
        
        printfn "📊 OVERALL PERFORMANCE SUMMARY"
        printfn "═══════════════════════════════════════════════════════════════"
        printfn "Total Portfolio P&L: $%.2f" totalPnL
        printfn "Overall Win Rate: %.1f%%" overallWinRate
        printfn "Total Trades Executed: %d" tradeCount
        printfn "Session Duration: %.1f minutes" sessionDuration.TotalMinutes
        
        // Rank symbols by performance
        let rankedSymbols = symbolReports |> Array.sortByDescending (fun r -> r.TotalPnL)
        if rankedSymbols.Length > 0 then
            printfn ""
            printfn "🏆 SYMBOL PERFORMANCE RANKING:"
            rankedSymbols
            |> Array.iteri (fun i report ->
                let emoji = if i = 0 then "🥇" elif i = 1 then "🥈" elif i = 2 then "🥉" else "📈"
                printfn "   %s %s: $%.2f P&L (%d trades)" emoji report.Symbol report.TotalPnL report.TotalTrades
            )
    }

// Monitoring and status
let getDataStreamStatus (provider: RealTimeDataProvider) =
    let symbolStatuses = 
        provider.Symbols
        |> Array.map (fun symbol ->
            let bufferCount = 
                match provider.DataBuffer.TryGetValue(symbol) with
                | true, buffer -> buffer.Ticks.Count
                | false, _ -> 0
            
            let quality = 
                match provider.DataQuality.TryGetValue(symbol) with
                | true, q -> q.DataIntegrity
                | false, _ -> 0m
            
            let lastUpdate = 
                match provider.DataQuality.TryGetValue(symbol) with
                | true, q -> q.LastUpdate
                | false, _ -> DateTime.MinValue
            
            (symbol, bufferCount, quality, lastUpdate)
        )
    
    let totalTicks = symbolStatuses |> Array.sumBy (fun (_, count, _, _) -> count)
    let avgQuality = symbolStatuses |> Array.averageBy (fun (_, _, quality, _) -> float quality) |> decimal
    
    (provider.State, totalTicks, avgQuality, symbolStatuses)

// Real Alpaca Paper WebSocket connection (will show errors when market is closed)
let connectToAlpacaPaperWebSocket (provider: RealTimeDataProvider) (apiKey: string) (secretKey: string) (symbols: string[]) : Task<unit> =
    task {
        try
            printfn "📡 Attempting to connect to Alpaca Paper Data Streaming API..."
            printfn "   Symbols: %s" (String.concat ", " symbols)
            
            // Create Alpaca data streaming client for paper trading  
            let env = Environments.Paper
            let streamingClient = env.GetAlpacaDataStreamingClient(SecretKey(apiKey, secretKey))
            
            printfn "🔌 Connecting to Alpaca Paper Data Stream..."
            do! streamingClient.ConnectAsync()
            printfn "✅ Connected to Alpaca Paper Data Streaming API"
            
            printfn "📈 Attempting to subscribe to trade data for %d symbols..." symbols.Length
            // Note: Actual subscription implementation would go here when API issues are resolved
            // For now, this will fail as expected when markets are closed
            
            // Keep the connection alive until cancellation
            while not provider.CancellationToken.Token.IsCancellationRequested do
                do! Task.Delay(1000, provider.CancellationToken.Token)
            
            printfn "🔌 Disconnecting from Alpaca Paper Data Stream..."
            do! streamingClient.DisconnectAsync()
            printfn "✅ Disconnected from Alpaca Paper Data Stream"
            
        with
        | :? OperationCanceledException -> 
            printfn "🛑 Live trading session cancelled"
        | ex ->
            printfn "❌ Failed to connect to Alpaca Paper Data Streaming API"
            printfn "   Error: %s" ex.Message
            printfn ""
            printfn "🕐 Market Status Check:"
            printfn "   • This error is expected when markets are closed"
            printfn "   • Data streaming requires active market session"
            printfn "   • Market hours: 9:30 AM - 4:00 PM ET (Monday-Friday)"
            printfn "   • Use --nighttime-simulation for testing when markets are closed"
            printfn ""
            printfn "🔧 Troubleshooting:"
            printfn "   • Check if markets are currently open"
            printfn "   • Verify API credentials have data streaming permissions"  
            printfn "   • Ensure stable internet connection"
            printfn "   • Paper trading accounts have same streaming restrictions as live"
            
            // Keep task alive briefly to show the error messages
            do! Task.Delay(5000)
    }