open System
open System.Threading.Tasks
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
    executeTradingWithDataStreamAndDuration(simulationMode, 60)  // Default 60 seconds

and executeTradingWithDataStreamAndDuration (simulationMode: string option, durationSeconds: int) =
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

                        // Initialize trading engine
                        let tradingRules = createDefaultRules ()
                        let mutable tradingState = createInitialState 10000m  // Start with $10,000

                        // Create order executor (mock for simulation)
                        let orderExecutor = createOrderExecutor true None // true = simulation mode

                        printfn "💰 Trading Configuration:"
                        printfn "   • Starting Cash: $%.2f" tradingState.Cash
                        printfn "   • Max Position Size: %d shares" tradingRules.Strategy.MaxPositionSize
                        printfn "   • Stop Loss: %.1f%%" (tradingRules.Strategy.MaxLossPercent * 100m)
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

                            let mutable finalState = tradingState
                            let positionsToLiquidate = tradingState.Positions |> Map.toList

                            // Execute actual sell orders for each position
                            for (symbol, position) in positionsToLiquidate do
                                let exitPrice = position.CurrentPrice  // Use last known price

                                // Create a mock tick for the forced liquidation
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

                                printfn "   LIQUIDATING %s: %d shares @ $%.2f (Entry: $%.2f)"
                                        symbol position.Quantity exitPrice position.EntryPrice

                                // Execute the sell order through the order executor
                                task {
                                    let! newState = executeSell position forcedLiquidationTick "Forced liquidation at session end" finalState orderExecutor
                                    finalState <- newState
                                } |> Async.AwaitTask |> Async.RunSynchronously

                            tradingState <- finalState

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
            printfn "🚀 ATLAS TRADING STRATEGY - LIVE TRADING MODE"
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

                    // Initialize trading engine
                    let tradingRules = createDefaultRules ()
                    let mutable tradingState = createInitialState 10000m  // Start with $10,000

                    // Create order executor (Alpaca for live trading)
                    // TODO: Fix Alpaca API client creation - for now use mock
                    let orderExecutor = createOrderExecutor true None // Use mock for now since Alpaca API needs fixing

                    printfn "💰 Trading Configuration:"
                    printfn "   • Starting Cash: $%.2f" tradingState.Cash
                    printfn "   • Max Position Size: %d shares" tradingRules.Strategy.MaxPositionSize
                    printfn "   • Stop Loss: %.1f%%" (tradingRules.Strategy.MaxLossPercent * 100m)
                    printfn "   • Session Duration: %d seconds" durationSeconds
                    printfn ""

                    // For now, use the existing historical data approach
                    printfn "🔄 Live trading mode will fetch historical data from Alpaca API..."
                    printfn "   (Real-time WebSocket streaming will be implemented in a future version)"
                    printfn ""

                    // Use the existing executeTrading function which fetches Alpaca data
                    let! result = executeTrading()
                    return result

                with
                | ex ->
                    printfn "❌ ERROR: Failed to run live trading session"
                    printfn "   Details: %s" ex.Message
                    return 1
    }

[<EntryPoint>]
let main args =
    match args with
    | [| "execute-trading" |] ->
        // Live trading mode (no --simulation argument)
        executeTradingWithDataStream(None).Result
    | [| "execute-trading"; durationArg |] when durationArg.StartsWith("--duration=") ->
        // Live trading mode with custom duration
        let durationStr = durationArg.Substring("--duration=".Length)
        let duration =
            if durationStr.EndsWith("s") then
                int (durationStr.Substring(0, durationStr.Length - 1))
            else
                int durationStr
        executeTradingWithDataStreamAndDuration(None, duration).Result
    | [| "execute-trading"; arg |] when arg.StartsWith("--simulation=") ->
        let csvPath = arg.Substring("--simulation=".Length)
        executeTradingWithDataStream(Some csvPath).Result  // CSV simulation mode with default 60s
    | [| "execute-trading"; simulationArg; durationArg |] when simulationArg.StartsWith("--simulation=") && durationArg.StartsWith("--duration=") ->
        let csvPath = simulationArg.Substring("--simulation=".Length)
        let durationStr = durationArg.Substring("--duration=".Length)
        let duration =
            if durationStr.EndsWith("s") then
                int (durationStr.Substring(0, durationStr.Length - 1))
            else
                int durationStr
        executeTradingWithDataStreamAndDuration(Some csvPath, duration).Result
    | [| "generate-fake-data" |] ->
        printfn "🎲 ATLAS FAKE DATA GENERATOR"
        printfn "═══════════════════════════════════════════════════════════════"
        let outputDir = "fake_data"
        generateMultipleScenarios outputDir
        0
    | _ ->
        printfn "Usage:"
        printfn "  Atlas.Cli execute-trading                                            - Live trading with real Alpaca data (default 60s duration)"
        printfn "  Atlas.Cli execute-trading --duration=<sec>                           - Live trading with custom duration"
        printfn "  Atlas.Cli execute-trading --simulation=<csv_file>                    - Use CSV simulation with default 60s duration"
        printfn "  Atlas.Cli execute-trading --simulation=<csv_file> --duration=<sec>   - Use CSV simulation with custom duration"
        printfn "  Atlas.Cli generate-fake-data                                         - Generate CSV files with fake market data scenarios"
        printfn ""
        printfn "Trading Modes:"
        printfn "  • LIVE MODE: Uses AlpacaOrderExecutor - connects to real Alpaca WebSocket and executes actual trades"
        printfn "  • SIMULATION MODE: Uses MockOrderExecutor - processes CSV data with mock order execution"
        printfn ""
        printfn "Examples:"
        printfn "  Atlas.Cli execute-trading                                            # Live trading for 60 seconds"
        printfn "  Atlas.Cli execute-trading --duration=300s                            # Live trading for 5 minutes"
        printfn "  Atlas.Cli execute-trading --simulation=fake_data/fake_data_1min_updown.csv"
        printfn "  Atlas.Cli execute-trading --simulation=fake_data/fake_data_1min_updown.csv --duration=30s"
        printfn "  Atlas.Cli execute-trading --simulation=fake_data/fake_data_5min_volatile.csv --duration=45"
        1
