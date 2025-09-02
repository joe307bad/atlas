open System
open System.Threading.Tasks
open TradingStrategy.Data
open TradingStrategy.AlpacaApi
open TradingStrategy.Configuration
open TradingStrategy.TechnicalIndicators
open TradingStrategy.Backtesting
open TradingStrategy.DataReporter
open TradingStrategy.RiskManagement
open TradingStrategy.RealTimeData

let executeTrading () =
    task {
        printfn "🚀 ATLAS TRADING STRATEGY - EXECUTING DATA PULL"
        printfn "═══════════════════════════════════════════════════════════════"
        
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
                
                // Fetch market data for all symbols
                let! marketDataResults = 
                    fetchMultipleSymbols 
                        dataProvider 
                        validConfig.DefaultSymbols 
                        validConfig.DataStartDate 
                        validConfig.DataEndDate 
                        validConfig.DefaultResolution
                
                printfn "📊 Calculating technical indicators..."
                let indicatorsResults = 
                    marketDataResults
                    |> Array.map calculateAllIndicators
                
                printfn "🔄 Phase 4-5: Running risk-managed backtesting engine..."
                
                // Create backtesting configuration
                let backtestConfig = {
                    StartingCapital = 100000m  // $100,000 starting capital
                    CommissionPerTrade = 1m    // $1 per trade
                    CommissionPercentage = 0.005m  // 0.005% per trade
                    SlippageBasisPoints = 5m   // 5 basis points slippage
                    MaxPositionSize = 0.20m    // Max 20% of portfolio per position
                    RiskPerTrade = 0.02m       // Risk 2% per trade
                }
                
                // Create risk management limits
                let riskLimits = createRiskLimits()
                
                printfn "   • Starting capital: $%s" (backtestConfig.StartingCapital.ToString("N0"))
                printfn "   • Commission: $%.2f + %.3f%% per trade" backtestConfig.CommissionPerTrade backtestConfig.CommissionPercentage
                printfn "   • Max position size: %.0f%% of portfolio" (backtestConfig.MaxPositionSize * 100m)
                printfn "   • Risk limits: %.0f%% max daily loss, %.0f%% max drawdown" (riskLimits.MaxDailyLoss * 100m) (riskLimits.MaxDrawdown * 100m)
                
                // Run risk-managed backtest
                let riskManagedResult = runRiskManagedBacktest backtestConfig riskLimits marketDataResults indicatorsResults
                let backtestResult = riskManagedResult.BacktestResult
                
                printfn "📋 Generating comprehensive risk-managed backtesting report..."
                let report = generateMarketDataWithBacktestReport marketDataResults indicatorsResults (Some backtestResult)
                printfn "%s" report
                
                // Display risk management summary
                printfn ""
                printfn "🛡️ RISK MANAGEMENT SUMMARY"
                printfn "══════════════════════════════════════════════════════"
                printfn "Risk Alerts Generated: %d" riskManagedResult.RiskAlerts.Length
                printfn "Stop-Loss Orders: %d" riskManagedResult.StopLossOrders.Length
                printfn "Volatility Measures: %d symbols" riskManagedResult.VolatilityMeasures.Length
                
                if riskManagedResult.RiskAlerts.Length > 0 then
                    printfn ""
                    printfn "🚨 Risk Alerts:"
                    riskManagedResult.RiskAlerts
                    |> Array.take (min 5 riskManagedResult.RiskAlerts.Length)
                    |> Array.iter (fun alert ->
                        let severityIcon = match alert.Severity with | Critical -> "🔴" | Warning -> "🟡" | Info -> "🔵"
                        printfn "   %s %s: %s" severityIcon (alert.AlertType.ToString()) alert.Message
                    )
                    
                    if riskManagedResult.RiskAlerts.Length > 5 then
                        printfn "   ... and %d more alerts" (riskManagedResult.RiskAlerts.Length - 5)
                
                if riskManagedResult.VolatilityMeasures.Length > 0 then
                    printfn ""
                    printfn "📊 Volatility Overview:"
                    riskManagedResult.VolatilityMeasures
                    |> Array.iter (fun vm ->
                        printfn "   %s: ATR=%.3f, RealizedVol=%.1f%%, VolPercentile=%.1f%%" 
                                vm.Symbol vm.ATR (vm.RealizedVolatility * 100m) (vm.VolatilityPercentile * 100m)
                    )
                
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

let executeLiveTrading (useNightTimeSimulation: bool) =
    task {
        printfn "🚀 ATLAS LIVE TRADING STRATEGY - REAL-TIME EXECUTION"
        printfn "═══════════════════════════════════════════════════════════════"
        
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
                printfn "🔄 Initializing real-time data streaming..."
                let provider = createRealTimeDataProvider validConfig.DefaultSymbols 1000
                
                printfn "📡 Market Hours Status:"
                let marketSession = getCurrentMarketSession provider.MarketHours DateTime.Now
                let isOpen = isMarketOpen provider.MarketHours DateTime.Now
                printfn "   • Current Session: %A" marketSession
                printfn "   • Market Open: %b" isOpen
                printfn ""
                
                printfn "🔄 Fetching initial historical data for indicator baseline..."
                use dataProvider = createDataProvider validConfig.AlpacaApiKey validConfig.AlpacaSecretKey validConfig.UsePaperTrading
                
                let! initialData = 
                    fetchMultipleSymbols 
                        dataProvider 
                        validConfig.DefaultSymbols
                        (DateTime.Now.AddDays(-60.0))  // Get 60 days of historical data
                        (DateTime.Now.AddDays(-1.0))   // End 1 day ago to avoid subscription limits
                        validConfig.DefaultResolution
                
                printfn "✅ Loaded %d symbols with historical data for indicator baseline" initialData.Length
                printfn ""
                
                if useNightTimeSimulation then
                    printfn "🌙 Starting NIGHTTIME simulation mode..."
                    printfn "   Using fake tick data with trading strategy"
                    printfn "   Safe for testing when markets are closed"
                    printfn "   Press Ctrl+C to stop simulation"
                else
                    printfn "📡 Starting LIVE paper trading mode..."
                    printfn "   ⚠️  Using Alpaca Paper API - real market hours required!"
                    printfn "   Will show errors if market is closed"
                    printfn "   Press Ctrl+C to stop live trading"
                
                printfn ""
                
                // Choose data source based on mode
                let (dataStreamTask, isSimulationMode) = 
                    if useNightTimeSimulation then
                        // Enhanced simulation with fake real-time data
                        (simulateRealTimeTrading provider validConfig.DefaultSymbols, true)
                    else
                        // Attempt to connect to actual Alpaca Paper WebSocket API
                        // This will show real errors when market is closed
                        (connectToAlpacaPaperWebSocket provider validConfig.AlpacaApiKey validConfig.AlpacaSecretKey validConfig.DefaultSymbols, false)
                
                // Monitor live data stream
                let monitoringTask = task {
                    let mutable iteration = 0
                    while not provider.CancellationToken.Token.IsCancellationRequested do
                        do! Task.Delay(2000) // Update every 2 seconds
                        iteration <- iteration + 1
                        
                        let (state, totalTicks, avgQuality, symbolStatuses) = getDataStreamStatus provider
                        
                        // Clear screen area for live updates
                        printfn "\n" 
                        printfn "📊 LIVE DATA STREAM STATUS (Update #%d)" iteration
                        printfn "══════════════════════════════════════════════════════"
                        printfn "Stream State: %A" state
                        printfn "Total Ticks Received: %d" totalTicks
                        printfn "Average Data Quality: %.1f%%" (avgQuality * 100m)
                        printfn ""
                        
                        printfn "📈 SYMBOL STATUS:"
                        symbolStatuses
                        |> Array.iter (fun (symbol, tickCount, quality, lastUpdate) ->
                            let freshness = 
                                if lastUpdate = DateTime.MinValue then "No Data"
                                else sprintf "%.1fs ago" (DateTime.UtcNow - lastUpdate).TotalSeconds
                            
                            let qualityIcon = 
                                if quality >= 0.9m then "🟢"
                                elif quality >= 0.7m then "🟡" 
                                else "🔴"
                            
                            printfn "   %s %s: %d ticks, %.0f%% quality, %s" 
                                    qualityIcon symbol tickCount (quality * 100m) freshness
                        )
                        
                        printfn ""
                        printfn "🔄 STREAMING INDICATORS:"
                        provider.StreamingIndicators.ToArray()
                        |> Array.iter (fun kvp ->
                            let indicators = kvp.Value
                            let rsiStr = match indicators.RSI14 with Some rsi -> sprintf "RSI: %.1f" rsi | None -> "RSI: --"
                            let smaStr = match indicators.SMA20 with Some sma -> sprintf "SMA20: %.2f" sma | None -> "SMA20: --"
                            let emaStr = match indicators.EMA20 with Some ema -> sprintf "EMA20: %.2f" ema | None -> "EMA20: --"
                            
                            printfn "   📊 %s: %s | %s | %s" indicators.Symbol rsiStr smaStr emaStr
                            
                            match indicators.MACD with
                            | Some macd -> printfn "      MACD: %.3f, Signal: %.3f, Histogram: %.3f" macd.MACD macd.Signal macd.Histogram
                            | None -> printfn "      MACD: --"
                        )
                        
                        printfn ""
                        printfn "🛡️  RISK MONITORING:"
                        provider.DataQuality.ToArray()
                        |> Array.iter (fun kvp ->
                            let quality = kvp.Value
                            if quality.GapDetected then
                                printfn "   ⚠️  %s: Data gap detected (%.1fs)" quality.Symbol (quality.GapDuration |> Option.map (fun ts -> ts.TotalSeconds) |> Option.defaultValue 0.0)
                            
                            if quality.AverageLatency.TotalSeconds > 5.0 then
                                printfn "   🐌 %s: High latency (%.1fs)" quality.Symbol quality.AverageLatency.TotalSeconds
                        )
                        
                        if iteration >= 30 then // Stop after 30 iterations (60 seconds)
                            printfn ""
                            printfn "⏱️  Demo time limit reached. Stopping live trading simulation..."
                            provider.CancellationToken.Cancel()
                }
                
                // Wait for both tasks to complete
                let! _ = Task.WhenAll([| dataStreamTask :> Task; monitoringTask :> Task |])
                
                printfn ""
                if useNightTimeSimulation then
                    printfn "✅ NIGHTTIME SIMULATION COMPLETED"
                else
                    printfn "✅ LIVE PAPER TRADING SESSION COMPLETED"
                printfn "═══════════════════════════════════════════════════════════════"
                
                let (_, finalTicks, finalQuality, _) = getDataStreamStatus provider
                printfn "Session Statistics:"
                printfn "   • Total ticks processed: %d" finalTicks
                printfn "   • Final data quality: %.1f%%" (finalQuality * 100m)
                printfn "   • Streaming indicators updated: %d symbols" provider.StreamingIndicators.Count
                
                // Generate detailed P&L report by symbol
                printfn ""
                printfn "💰 TRADING PERFORMANCE REPORT"
                printfn "═══════════════════════════════════════════════════════════════"
                
                if isSimulationMode then
                    // For simulation mode, we need to get the final results from the simulation
                    printfn "Mode: Nighttime Simulation"
                    printfn "Note: Detailed P&L per symbol available in simulation output above"
                else
                    printfn "Mode: Live Paper Trading"
                    
                // Basic statistics available for both modes
                let finalIndicators = provider.StreamingIndicators.ToArray()
                if finalIndicators.Length > 0 then
                    printfn ""
                    printfn "📊 Final Technical Indicators:"
                    finalIndicators
                    |> Array.iter (fun kvp ->
                        let ind = kvp.Value
                        let rsi = match ind.RSI14 with Some r -> sprintf "%.1f" r | None -> "--"
                        let sma = match ind.SMA20 with Some s -> sprintf "%.2f" s | None -> "--"
                        printfn "   %s: RSI=%s, SMA20=%s" ind.Symbol rsi sma
                    )
                
                printfn ""
                if useNightTimeSimulation then
                    printfn "🎯 Simulation complete - ready for live trading when markets open!"
                else
                    printfn "🎯 Paper trading session complete - check Alpaca dashboard for official records!"
                
                return 0
                
            with
            | ex ->
                printfn "❌ ERROR: Failed to execute live trading simulation"
                printfn "   Details: %s" ex.Message
                printfn ""
                printfn "🔧 Troubleshooting:"
                printfn "   • Verify your API credentials are correct"
                printfn "   • Check your internet connectivity"
                printfn "   • Ensure market data permissions are active"
                return 1
    }

[<EntryPoint>]
let main args =
    match args with
    | [| "execute-trading" |] -> 
        executeTrading().Result
    | [| "execute-live-trading" |] ->
        executeLiveTrading(false).Result  // Default to live paper API
    | [| "execute-live-trading"; "--nighttime-simulation" |] ->
        executeLiveTrading(true).Result   // Use nighttime simulation
    | _ -> 
        printfn "Usage:"
        printfn "  Atlas.Cli execute-trading                            - Pull real data from Alpaca and generate report"
        printfn "  Atlas.Cli execute-live-trading                       - Live paper trading (requires market open)"
        printfn "  Atlas.Cli execute-live-trading --nighttime-simulation - Nighttime simulation with fake data"
        1
