open System
open System.Threading.Tasks
open Alpaca.Markets
open TradingStrategy.AlpacaApi
open TradingStrategy.Configuration
open TradingStrategy.FakeDataGenerator
open TradingStrategy.DataStream
open TradingStrategy.TradingEngine
open TradingStrategy.OrderExecutor
open TradingStrategy.RealTimeData

let executeTrading () =
    task {
        let config = loadConfiguration()

        match validateConfiguration config with
        | Error errors ->
            printfn ""
            errors |> Array.iter (printfn "%s")
            printfn ""
            printConfigurationInstructions()
            return 1

        | Ok validConfig ->
            printConfigurationSummary validConfig

            try
                printfn "🔄 Initializing Alpaca data provider..."
                use dataProvider = createDataProvider validConfig.AlpacaApiKey validConfig.AlpacaSecretKey validConfig.UsePaperTrading

                printfn "📊 Fetching market data for symbols: %s" (String.concat ", " validConfig.DefaultSymbols)


                return 0

            with
            | ex ->
                printfn "❌ ERROR: Failed to fetch data from Alpaca"
                printfn "   Details: %s" ex.Message
                printfn ""
                printfn "🔧 Troubleshooting:"
                printfn "   • Verify your API credentials are correct"
                printfn "   • Check your Alpaca account is active and has market data permissions"
                printfn "   • Ensure you have internet connectivity"
                return 1
    }

let rec executeTradingWithDataStream (simulationMode: string option) =
    executeTradingWithDataStreamAndDuration(simulationMode, 60, None)  // Default 60 seconds, no symbol override

and executeTradingWithDataStreamAndDuration (simulationMode: string option, durationSeconds: int, symbolOverride: string option) =
    task {
        match simulationMode with
        | Some csvPath ->
            printfn "🚀 ATLAS TRADING STRATEGY - CSV SIMULATION MODE"
            printfn "═══════════════════════════════════════════════════════════════"

            // Check if file exists
            if not (System.IO.File.Exists(csvPath)) then
                printfn "❌ ERROR: CSV file not found: %s" csvPath
                return 1
            else
                printfn "📁 Loading simulation data from: %s" csvPath

                let config = loadConfiguration()

                match validateConfiguration config with
                | Error errors ->
                    printfn ""
                    errors |> Array.iter (printfn "%s")
                    printfn ""
                    printConfigurationInstructions()
                    return 1

                | Ok validConfig ->
                    printConfigurationSummary validConfig

                    try
                        // Try to detect symbol from CSV filename or use first column
                        let symbol =
                            if csvPath.Contains("AAPL") then "AAPL"
                            elif csvPath.Contains("TSLA") then "TSLA"
                            elif csvPath.Contains("MSFT") then "MSFT"
                            elif csvPath.Contains("META") then "META"
                            else
                                // Try to read symbol from CSV
                                let lines = System.IO.File.ReadAllLines(csvPath)
                                if lines.Length > 1 then
                                    let firstDataLine = lines.[1].Split(',')
                                    if firstDataLine.Length >= 2 then firstDataLine.[1] else "UNKNOWN"
                                else "UNKNOWN"

                        printfn "📊 Detected symbol: %s" symbol

                        // Create real-time data provider with detected symbol
                        let provider = createRealTimeDataProvider [| symbol |] 1000

                        // Initialize trading engine with config
                        let tradingRules = createRulesFromConfig validConfig
                        let mutable tradingState = createInitialState 10000m  // Start with $10,000

                        // Create order executor (mock for simulation)
                        let orderExecutor = createOrderExecutor true None // true = simulation mode

                        printfn "💰 Trading Configuration:"
                        printfn "   • Starting Cash: $%.2f" tradingState.Cash
                        printfn "   • Max Position Size: %d shares" tradingRules.Config.MaxPositionSize
                        printfn "   • Buy Trigger: %.4f%%" (tradingRules.Config.BuyTriggerPercent * 100m)
                        printfn "   • Take Profit: %.4f%%" (tradingRules.Config.TakeProfitPercent * 100m)
                        printfn "   • Stop Loss: %.2f%%" (tradingRules.Config.StopLossPercent * 100m)
                        printfn "   • Session Duration: %d seconds" durationSeconds
                        printfn ""

                        // Create CSV simulation stream
                        let streamConfig =
                            Map.ofList [
                                "csvPath", csvPath
                                "interval", "1000"  // 1 second intervals
                            ]

                        let dataStream = createDataStream "csv" streamConfig

                        // Connect to stream
                        do! dataStream.ConnectAsync()

                        // Subscribe to events with trading logic
                        dataStream.OnTick.Add(fun tick ->
                            // Process incoming tick through the provider
                            processIncomingTick provider tick |> ignore

                            // Analyze tick and make trading decisions (async)
                            task {
                                let! (newState, tradeAction) = analyzeTickAndTrade tick tradingRules tradingState orderExecutor
                                tradingState <- newState

                                // Print trade actions and basic price info
                                match tradeAction with
                                | Hold -> () // Don't spam with holds
                                | _ -> printTradeAction tradeAction symbol
                            } |> ignore
                        )

                        dataStream.OnBar.Add(fun bar ->
                            // Process bar data
                            match provider.DataBuffer.TryGetValue(symbol) with
                            | true, buffer -> addBarToBuffer buffer bar
                            | false, _ -> ()
                        )

                        // Subscribe to symbols
                        do! dataStream.SubscribeToSymbols([| symbol |])

                        // Monitor the stream
                        let mutable iteration = 0
                        while not provider.CancellationToken.Token.IsCancellationRequested && iteration < durationSeconds do
                            do! Task.Delay(1000)
                            iteration <- iteration + 1

                            let (state, totalTicks, avgQuality, symbolStatuses) = getDataStreamStatus provider

                            printfn "\n📊 SIMULATION STATUS (Second %d/%d)" iteration durationSeconds
                            printfn "══════════════════════════════════════════════════════"
                            printfn "Stream Type: CSV_SIMULATION"
                            printfn "Total Ticks Processed: %d" totalTicks

                            symbolStatuses
                            |> Array.iter (fun (symbol, tickCount, quality, lastUpdate) ->
                                let freshness =
                                    if lastUpdate = DateTime.MinValue then "No Data"
                                    elif (DateTime.UtcNow - lastUpdate).TotalMinutes > 30.0 then
                                        // This is simulation data with historical timestamps
                                        sprintf "Sim Data (%s)" (lastUpdate.ToString("HH:mm:ss"))
                                    else
                                        sprintf "%.1fs ago" (DateTime.UtcNow - lastUpdate).TotalSeconds

                                printfn "   %s: %d ticks, %.0f%% quality, %s"
                                        symbol tickCount (quality * 100m) freshness
                            )

                            // Display trading status every 5 seconds
                            if iteration % 5 = 0 then
                                printfn ""
                                printfn "💰 TRADING STATUS:"
                                printfn "   Cash: $%.2f | P&L: $%.2f | Trades: %d | Positions: %d"
                                        tradingState.Cash tradingState.TotalPnL tradingState.Trades.Length tradingState.Positions.Count

                        // Disconnect
                        do! dataStream.DisconnectAsync()

                        printfn "\n✅ CSV SIMULATION COMPLETED"
                        printfn "═══════════════════════════════════════════════════════════════"
                        printfn "Simulation ran for %d seconds emitting data from: %s" durationSeconds csvPath

                        // Force liquidation of all open positions at session end
                        if not (Map.isEmpty tradingState.Positions) then
                            printfn "\n⚠️  FORCED LIQUIDATION AT SESSION END:"
                            printfn "─────────────────────────────────────────────────"
                            printfn "Positions to liquidate: %d" tradingState.Positions.Count

                            let positionsToLiquidate = tradingState.Positions |> Map.toList
                            let mutable currentState = tradingState

                            // Execute liquidation for each position sequentially
                            for (symbol, position) in positionsToLiquidate do
                                let exitPrice = position.CurrentPrice  // Use last known price

                                printfn "   🔄 LIQUIDATING %s: %d shares @ $%.2f (Entry: $%.2f)"
                                        symbol position.Quantity exitPrice position.EntryPrice

                                // Create a tick for the forced liquidation
                                let forcedLiquidationTick = {
                                    Symbol = symbol
                                    Price = exitPrice
                                    Size = position.Quantity
                                    Timestamp = DateTime.UtcNow
                                    BidPrice = Some (exitPrice - 0.01m)
                                    AskPrice = Some (exitPrice + 0.01m)
                                    BidSize = Some 100
                                    AskSize = Some 100
                                    Exchange = Some "FORCED_LIQUIDATION"
                                }

                                // Execute the sell order properly with await
                                try
                                    let! newState = executeSell position forcedLiquidationTick "Forced liquidation at session end" currentState orderExecutor
                                    currentState <- newState
                                    printfn "   ✅ LIQUIDATED %s successfully" symbol
                                with
                                | ex ->
                                    printfn "   ⚠️  LIQUIDATION FAILED for %s: %s" symbol ex.Message
                                    printfn "   🔧 FORCE CLOSING POSITION (removing from state)" 
                                    // If order fails, force remove the position to prevent portfolio inconsistency
                                    let forcedState = { currentState with Positions = Map.remove symbol currentState.Positions }
                                    currentState <- forcedState

                            tradingState <- currentState
                            printfn "   📊 All positions liquidated. Final cash: $%.2f" tradingState.Cash

                        // Final trading summary
                        printTradingSummary tradingState

                        return 0

                    with
                    | ex ->
                        printfn "❌ ERROR: Failed to run CSV simulation"
                        printfn "   Details: %s" ex.Message
                        return 1

        | None ->
            // Live trading mode with real Alpaca data
            printfn "🚀 ATLAS TRADING STRATEGY - PAPER TRADING MODE (ALPACA)"
            printfn "═══════════════════════════════════════════════════════════════"

            let config = loadConfiguration()

            match validateConfiguration config with
            | Error errors ->
                printfn ""
                errors |> Array.iter (printfn "%s")
                printfn ""
                printfn "Please check your configuration and try again."
                return 1
            | Ok validConfig ->
                try
                    printConfigurationSummary validConfig
                    
                    // Determine which symbol to use for live trading
                    let tradingSymbol = 
                        match symbolOverride with
                        | Some symbol -> symbol
                        | None -> validConfig.DefaultSymbols.[0] // Use first default symbol

                    printfn "📊 Trading symbol: %s" tradingSymbol

                    // Create real-time data provider
                    let provider = createRealTimeDataProvider [| tradingSymbol |] 1000

                    // Initialize trading engine with config
                    let tradingRules = createRulesFromConfig validConfig
                    let mutable tradingState = createInitialState 10000m  // Start with $10,000

                    // Create Alpaca trading client for order execution
                    let env = if validConfig.UsePaperTrading then Alpaca.Markets.Environments.Paper else Alpaca.Markets.Environments.Live
                    let alpacaTradingClient = env.GetAlpacaTradingClient(Alpaca.Markets.SecretKey(validConfig.AlpacaApiKey, validConfig.AlpacaSecretKey))
                    let orderExecutor = createOrderExecutor false (Some alpacaTradingClient) // false = not simulation, use real Alpaca

                    printfn "💰 Trading Configuration:"
                    printfn "   • Trading Mode: %s" (if validConfig.UsePaperTrading then "PAPER" else "LIVE")
                    printfn "   • Trading Symbol: %s" tradingSymbol
                    printfn "   • Starting Cash: $%.2f" tradingState.Cash
                    printfn "   • Max Position Size: %d shares" tradingRules.Config.MaxPositionSize
                    printfn "   • Buy Trigger: %.4f%%" (tradingRules.Config.BuyTriggerPercent * 100m)
                    printfn "   • Take Profit: %.4f%%" (tradingRules.Config.TakeProfitPercent * 100m)
                    printfn "   • Stop Loss: %.2f%%" (tradingRules.Config.StopLossPercent * 100m)
                    printfn "   • Target Ticks: %d ticks (will process each one individually)" durationSeconds
                    printfn ""

                    // Create Alpaca WebSocket stream configuration
                    let streamConfig =
                        Map.ofList [
                            "apiKey", validConfig.AlpacaApiKey
                            "secretKey", validConfig.AlpacaSecretKey
                            "usePaper", validConfig.UsePaperTrading.ToString()
                        ]

                    let dataStream = createDataStream "alpaca" streamConfig

                    // Connect to stream
                    do! dataStream.ConnectAsync()

                    // Track tick processing
                    let mutable processedTicks = 0
                    let mutable sessionComplete = false
                    let sessionCompletionSource = new TaskCompletionSource<int>()

                    // Subscribe to events with trading logic - process every single tick
                    dataStream.OnTick.Add(fun tick ->
                        if not sessionComplete then
                            // Process each tick synchronously to ensure we don't miss any trading opportunities
                            async {
                                try
                                    // Process incoming tick through the provider
                                    do! processIncomingTick provider tick |> Async.AwaitTask
                                    
                                    // Analyze tick and make trading decisions immediately
                                    let! (newState, tradeAction) = analyzeTickAndTrade tick tradingRules tradingState orderExecutor |> Async.AwaitTask
                                    tradingState <- newState
                                    
                                    // Increment processed tick count
                                    processedTicks <- processedTicks + 1
                                    
                                    // Output analysis for EVERY tick
                                    printfn "\n🔄 TICK #%d: %s @ $%.2f (Size: %d) [%s]" 
                                            processedTicks tick.Symbol tick.Price tick.Size 
                                            (tick.Timestamp.ToString("HH:mm:ss.fff"))
                                    
                                    // Print trade action and analysis
                                    match tradeAction with
                                    | Hold -> 
                                        printfn "   📊 ANALYSIS: HOLD - No action taken"
                                        printfn "   💰 Cash: $%.2f | P&L: $%.2f | Positions: %d" 
                                                tradingState.Cash tradingState.TotalPnL tradingState.Positions.Count
                                    | _ -> 
                                        printTradeAction tradeAction tradingSymbol
                                        printfn "   💰 Cash: $%.2f | P&L: $%.2f | Positions: %d" 
                                                tradingState.Cash tradingState.TotalPnL tradingState.Positions.Count
                                    
                                    // Check if we've processed the requested number of ticks
                                    if processedTicks >= durationSeconds then
                                        sessionComplete <- true
                                        sessionCompletionSource.SetResult(0)
                                        
                                with ex ->
                                    printfn "❌ Error processing tick: %s" ex.Message
                            } |> Async.StartImmediate
                    )

                    dataStream.OnBar.Add(fun bar ->
                        // Process bar data
                        match provider.DataBuffer.TryGetValue(tradingSymbol) with
                        | true, buffer -> addBarToBuffer buffer bar
                        | false, _ -> ()
                    )

                    // Subscribe to the trading symbol
                    do! dataStream.SubscribeToSymbols([| tradingSymbol |])

                    // Print session start info
                    printfn "\n🚀 TICK-DRIVEN TRADING SESSION STARTED"
                    printfn "══════════════════════════════════════════════════════"
                    printfn "Target: %d ticks | Symbol: %s | Mode: REAL-TIME" durationSeconds tradingSymbol
                    printfn "Each tick will be analyzed and processed immediately as it arrives from Alpaca WebSocket"
                    printfn "══════════════════════════════════════════════════════"

                    // Wait for session completion (driven by tick count, not time)
                    let! _ = sessionCompletionSource.Task

                    // Disconnect
                    do! dataStream.DisconnectAsync()

                    printfn "\n✅ PAPER TRADING SESSION COMPLETED"
                    printfn "═══════════════════════════════════════════════════════════════"

                    // Force liquidation of all open positions at session end
                    if not (Map.isEmpty tradingState.Positions) then
                        printfn "\n⚠️  FORCED LIQUIDATION AT SESSION END:"
                        printfn "─────────────────────────────────────────────────"
                        printfn "Positions to liquidate: %d" tradingState.Positions.Count

                        let positionsToLiquidate = tradingState.Positions |> Map.toList
                        let mutable currentState = tradingState

                        // Execute liquidation for each position sequentially to avoid state conflicts
                        for (symbol, position) in positionsToLiquidate do
                            let exitPrice = position.CurrentPrice  // Use last known price

                            printfn "   🔄 LIQUIDATING %s: %d shares @ $%.2f (Entry: $%.2f)"
                                    symbol position.Quantity exitPrice position.EntryPrice

                            // Wait a moment to avoid wash trade detection
                            do! Task.Delay(2000)  // 2 second delay

                            // Create a tick for the forced liquidation using current market price
                            let forcedLiquidationTick = {
                                Symbol = symbol
                                Price = exitPrice
                                Size = position.Quantity
                                Timestamp = DateTime.UtcNow
                                BidPrice = Some (exitPrice - 0.01m)
                                AskPrice = Some (exitPrice + 0.01m)
                                BidSize = Some 100
                                AskSize = Some 100
                                Exchange = Some "FORCED_LIQUIDATION"
                            }

                            // Execute the sell order properly with await  
                            try
                                let! newState = executeSell position forcedLiquidationTick "Forced liquidation at session end" currentState orderExecutor
                                currentState <- newState
                                printfn "   ✅ LIQUIDATED %s successfully" symbol
                            with
                            | ex ->
                                printfn "   ⚠️  LIQUIDATION FAILED for %s: %s" symbol ex.Message
                                printfn "   🔧 FORCE CLOSING POSITION (removing from state)" 
                                // If Alpaca order fails, force remove the position to prevent portfolio inconsistency
                                let forcedState = { currentState with Positions = Map.remove symbol currentState.Positions }
                                currentState <- forcedState

                        tradingState <- currentState
                        printfn "   📊 All positions liquidated. Final cash: $%.2f" tradingState.Cash

                    // Final trading summary
                    printTradingSummary tradingState

                    return 0

                with
                | ex ->
                    printfn "❌ ERROR: Failed to run paper trading session"
                    printfn "   Details: %s" ex.Message
                    return 1
    }

[<EntryPoint>]
let main args =
    match args with
    | [| "execute-trading" |] ->
        // Live trading mode (no --simulation argument)
        executeTradingWithDataStreamAndDuration(None, 60, None).Result
    | [| "execute-trading"; durationArg |] when durationArg.StartsWith("--duration=") ->
        // Live trading mode with custom tick count
        let durationStr = durationArg.Substring("--duration=".Length)
        let tickCount =
            if durationStr.EndsWith("t") then
                int (durationStr.Substring(0, durationStr.Length - 1))
            elif durationStr.EndsWith("ticks") then
                int (durationStr.Substring(0, durationStr.Length - 5))
            else
                int durationStr  // Default to treating as tick count
        executeTradingWithDataStreamAndDuration(None, tickCount, None).Result
    | [| "execute-trading"; symbolArg |] when symbolArg.StartsWith("--symbol=") ->
        // Live trading mode with specific symbol
        let symbol = symbolArg.Substring("--symbol=".Length)
        executeTradingWithDataStreamAndDuration(None, 60, Some symbol).Result
    | [| "execute-trading"; symbolArg; durationArg |] when symbolArg.StartsWith("--symbol=") && durationArg.StartsWith("--duration=") ->
        // Live trading mode with specific symbol and tick count
        let symbol = symbolArg.Substring("--symbol=".Length)
        let durationStr = durationArg.Substring("--duration=".Length)
        let tickCount =
            if durationStr.EndsWith("t") then
                int (durationStr.Substring(0, durationStr.Length - 1))
            elif durationStr.EndsWith("ticks") then
                int (durationStr.Substring(0, durationStr.Length - 5))
            else
                int durationStr
        executeTradingWithDataStreamAndDuration(None, tickCount, Some symbol).Result
    | [| "execute-trading"; durationArg; symbolArg |] when durationArg.StartsWith("--duration=") && symbolArg.StartsWith("--symbol=") ->
        // Live trading mode with tick count and symbol (order doesn't matter)
        let symbol = symbolArg.Substring("--symbol=".Length)
        let durationStr = durationArg.Substring("--duration=".Length)
        let tickCount =
            if durationStr.EndsWith("t") then
                int (durationStr.Substring(0, durationStr.Length - 1))
            elif durationStr.EndsWith("ticks") then
                int (durationStr.Substring(0, durationStr.Length - 5))
            else
                int durationStr
        executeTradingWithDataStreamAndDuration(None, tickCount, Some symbol).Result
    | [| "execute-trading"; arg |] when arg.StartsWith("--simulation=") ->
        let csvPath = arg.Substring("--simulation=".Length)
        executeTradingWithDataStreamAndDuration(Some csvPath, 60, None).Result  // CSV simulation mode with default 60s
    | [| "execute-trading"; simulationArg; durationArg |] when simulationArg.StartsWith("--simulation=") && durationArg.StartsWith("--duration=") ->
        let csvPath = simulationArg.Substring("--simulation=".Length)
        let durationStr = durationArg.Substring("--duration=".Length)
        let duration =
            if durationStr.EndsWith("s") then
                int (durationStr.Substring(0, durationStr.Length - 1))
            else
                int durationStr
        executeTradingWithDataStreamAndDuration(Some csvPath, duration, None).Result
    | [| "generate-fake-data" |] ->
        printfn "🎲 ATLAS FAKE DATA GENERATOR"
        printfn "═══════════════════════════════════════════════════════════════"
        let outputDir = "fake_data"
        generateMultipleScenarios outputDir
        0
    | _ ->
        printfn "Usage:"
        printfn "  Atlas.Cli execute-trading                                            - Live trading with real Alpaca data (default 60 ticks)"
        printfn "  Atlas.Cli execute-trading --symbol=<SYMBOL>                          - Live trading with specific symbol"
        printfn "  Atlas.Cli execute-trading --duration=<ticks>                         - Live trading with custom tick count"
        printfn "  Atlas.Cli execute-trading --symbol=<SYMBOL> --duration=<ticks>       - Live trading with symbol and tick count"
        printfn "  Atlas.Cli execute-trading --simulation=<csv_file>                    - Use CSV simulation with default 60s duration"
        printfn "  Atlas.Cli execute-trading --simulation=<csv_file> --duration=<sec>   - Use CSV simulation with custom duration"
        printfn "  Atlas.Cli generate-fake-data                                         - Generate CSV files with fake market data scenarios"
        printfn ""
        printfn "Trading Modes:"
        printfn "  • LIVE MODE: Tick-driven real-time trading - processes each WebSocket tick individually"
        printfn "  • SIMULATION MODE: Time-driven CSV replay - processes historical data at specified intervals"
        printfn ""
        printfn "Arguments:"
        printfn "  --symbol=<SYMBOL>     Specify trading symbol for live mode (e.g., AAPL, TSLA, SPY)"
        printfn "  --duration=<N>        LIVE MODE: Number of ticks to process (default: 60)"
        printfn "                        SIMULATION MODE: Duration in seconds (supports 30s, 5m, etc.)"
        printfn "  --simulation=<file>   Use CSV file for simulation mode"
        printfn ""
        printfn "Examples:"
        printfn "  Atlas.Cli execute-trading                                            # Process 60 real-time ticks (default symbol)"
        printfn "  Atlas.Cli execute-trading --symbol=AAPL                              # Process 60 AAPL ticks"
        printfn "  Atlas.Cli execute-trading --symbol=TSLA --duration=20                # Process 20 TSLA ticks"
        printfn "  Atlas.Cli execute-trading --duration=100t --symbol=MSFT              # Process 100 MSFT ticks (explicit 't' suffix)"
        printfn "  Atlas.Cli execute-trading --duration=50ticks --symbol=NVDA           # Process 50 NVDA ticks (explicit 'ticks' suffix)"
        printfn "  Atlas.Cli execute-trading --simulation=fake_data/fake_data_1min_updown.csv"
        printfn "  Atlas.Cli execute-trading --simulation=fake_data/fake_data_1min_updown.csv --duration=30s"
        printfn ""
        printfn "Note: In live mode, each tick from Alpaca WebSocket is processed individually with full analysis."
        1
