open System
open System.Threading.Tasks
open TradingStrategy.Data
open TradingStrategy.AlpacaApi
open TradingStrategy.Configuration
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
                
                printfn "📋 Generating market data report..."
                let report = generateMarketDataOnlyReport marketDataResults
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
