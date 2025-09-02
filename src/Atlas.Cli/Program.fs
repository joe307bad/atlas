open System
open System.Threading.Tasks
open TradingStrategy.Data
open TradingStrategy.AlpacaApi
open TradingStrategy.Configuration
open TradingStrategy.TechnicalIndicators
open TradingStrategy.Backtesting
open TradingStrategy.DataReporter

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
                
                printfn "🔄 Phase 4: Running backtesting engine..."
                
                // Create backtesting configuration
                let backtestConfig = {
                    StartingCapital = 100000m  // $100,000 starting capital
                    CommissionPerTrade = 1m    // $1 per trade
                    CommissionPercentage = 0.005m  // 0.005% per trade
                    SlippageBasisPoints = 5m   // 5 basis points slippage
                    MaxPositionSize = 0.20m    // Max 20% of portfolio per position
                    RiskPerTrade = 0.02m       // Risk 2% per trade
                }
                
                printfn "   • Starting capital: $%s" (backtestConfig.StartingCapital.ToString("N0"))
                printfn "   • Commission: $%.2f + %.3f%% per trade" backtestConfig.CommissionPerTrade backtestConfig.CommissionPercentage
                printfn "   • Max position size: %.0f%% of portfolio" (backtestConfig.MaxPositionSize * 100m)
                
                // Run backtest
                let backtestResult = runBacktest backtestConfig marketDataResults indicatorsResults
                
                printfn "📋 Generating comprehensive backtesting report..."
                let report = generateMarketDataWithBacktestReport marketDataResults indicatorsResults (Some backtestResult)
                printfn "%s" report
                
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

[<EntryPoint>]
let main args =
    match args with
    | [| "execute-trading" |] -> 
        executeTrading().Result
    | _ -> 
        printfn "Usage:"
        printfn "  Atlas.Cli execute-trading    - Pull real data from Alpaca and generate report"
        1
