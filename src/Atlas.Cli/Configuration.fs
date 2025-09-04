module TradingStrategy.Configuration

open System
open System.IO

type TradingConfig = {
    AlpacaApiKey: string
    AlpacaSecretKey: string
    UsePaperTrading: bool
    DefaultSymbols: string[]
    DataStartDate: DateTime
    DataEndDate: DateTime
    DefaultResolution: TimeSpan
    // Trading Rules
    BuyTriggerPercent: decimal      // Buy when stock goes up by this %
    TakeProfitPercent: decimal      // Sell for profit when stock goes up by this %
    StopLossPercent: decimal        // Sell for loss when stock goes down by this %
    MaxPositionSize: int            // Maximum shares per position
}

let private loadEnvFile() =
    // Try multiple locations for .env file
    let possiblePaths = [
        Path.Combine(Directory.GetCurrentDirectory(), "src", "Atlas.Cli", ".env")
        Path.Combine(Directory.GetCurrentDirectory(), ".env")
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env")
    ]
    
    let envFile = possiblePaths |> List.tryFind File.Exists
    
    match envFile with
    | Some path ->
        printfn "📁 Loading configuration from: %s" path
        File.ReadAllLines(path)
        |> Array.iter (fun line ->
            let trimmed = line.Trim()
            if not (trimmed.StartsWith("#") || String.IsNullOrEmpty(trimmed)) then
                match trimmed.Split('=', 2) with
                | [| key; value |] -> Environment.SetEnvironmentVariable(key, value)
                | _ -> ())
    | None -> ()

let private getEnvironmentVariable (name: string) (defaultValue: string) =
    match Environment.GetEnvironmentVariable(name) with
    | null | "" -> defaultValue
    | value -> value

let loadConfiguration() : TradingConfig =
    // Try to load .env file first
    loadEnvFile()

    {
        AlpacaApiKey = getEnvironmentVariable "ALPACA_API_KEY" "your-api-key-here"
        AlpacaSecretKey = getEnvironmentVariable "ALPACA_SECRET_KEY" "your-secret-key-here"
        UsePaperTrading = getEnvironmentVariable "ALPACA_USE_PAPER" "true" = "true"
        DefaultSymbols = [| "SPY"; "QQQ"; "IWM" |] // S&P 500, NASDAQ, Russell 2000 ETFs
        DataStartDate = DateTime.Now.AddDays(-60.0) // Last 60 days
        DataEndDate = DateTime.Now.AddDays(-1.0) // Yesterday (avoid partial day data)
        DefaultResolution = TimeSpan.FromMinutes(5.0) // 5-minute bars
        // Trading Rules from environment variables
        BuyTriggerPercent = decimal (getEnvironmentVariable "BUY_TRIGGER_PERCENT" "0.0001") / 100m  // Default: 0.0001%
        TakeProfitPercent = decimal (getEnvironmentVariable "TAKE_PROFIT_PERCENT" "0.0001") / 100m   // Default: 0.0001%
        StopLossPercent = decimal (getEnvironmentVariable "STOP_LOSS_PERCENT" "1.0") / 100m          // Default: 1.0%
        MaxPositionSize = int (getEnvironmentVariable "MAX_POSITION_SIZE" "100")                     // Default: 100 shares
    }

let validateConfiguration (config: TradingConfig) : Result<TradingConfig, string[]> =
    let errors = ResizeArray<string>()

    if config.AlpacaApiKey = "your-api-key-here" then
        errors.Add("❌ ALPACA_API_KEY environment variable not set")

    if config.AlpacaSecretKey = "your-secret-key-here" then
        errors.Add("❌ ALPACA_SECRET_KEY environment variable not set")

    if config.DefaultSymbols.Length = 0 then
        errors.Add("❌ No default symbols configured")

    if config.DataStartDate >= config.DataEndDate then
        errors.Add("❌ Start date must be before end date")

    // Validate trading rule parameters
    if config.BuyTriggerPercent < 0m then
        errors.Add("❌ BUY_TRIGGER_PERCENT must be positive")
    
    if config.TakeProfitPercent <= 0m then
        errors.Add("❌ TAKE_PROFIT_PERCENT must be greater than 0")
        
    if config.StopLossPercent <= 0m then
        errors.Add("❌ STOP_LOSS_PERCENT must be greater than 0")
        
    if config.MaxPositionSize <= 0 then
        errors.Add("❌ MAX_POSITION_SIZE must be greater than 0")

    if errors.Count = 0 then
        Ok config
    else
        Error (errors.ToArray())

let printConfigurationSummary (config: TradingConfig) =
    printfn "⚙️  CONFIGURATION SUMMARY"
    printfn "═══════════════════════════════════════════════════════════════"
    printfn "🔑 API Configuration:"
    printfn "   Alpaca API Key: ✅ Configured"
    printfn "   Alpaca Secret Key: ✅ Configured"
    printfn "   Paper Trading: %s" (if config.UsePaperTrading then "✅ Enabled (Safe)" else "⚠️  LIVE TRADING")
    printfn "📈 Trading Rules:"
    printfn "   Buy Trigger: %.4f%% price increase" (config.BuyTriggerPercent * 100m)
    printfn "   Take Profit: %.4f%% price increase" (config.TakeProfitPercent * 100m)
    printfn "   Stop Loss: %.2f%% price decrease" (config.StopLossPercent * 100m)
    printfn "   Max Position Size: %d shares" config.MaxPositionSize
    printfn "═══════════════════════════════════════════════════════════════"

let printConfigurationInstructions() =
    printfn """
🔧 ALPACA API CONFIGURATION REQUIRED
═══════════════════════════════════════════════════════════════

To use real Alpaca market data, you need to set up your API credentials:

1️⃣  Create an Alpaca account at https://alpaca.markets/
2️⃣  Navigate to your account dashboard to generate API credentials

3️⃣  OPTION A - Using .env file (Recommended):
   • Edit the .env file in the project root
   • Replace placeholder values with your actual credentials
   • The system will automatically load them

3️⃣  OPTION B - Set environment variables manually:

   macOS/Linux:
   export ALPACA_API_KEY="your-actual-api-key"
   export ALPACA_SECRET_KEY="your-actual-secret-key"
   export ALPACA_USE_PAPER="true"  # Use paper trading (recommended for testing)

   Windows:
   set ALPACA_API_KEY=your-actual-api-key
   set ALPACA_SECRET_KEY=your-actual-secret-key
   set ALPACA_USE_PAPER=true

4️⃣  Run the command again: dotnet run execute-trading

═══════════════════════════════════════════════════════════════

🔄 ALTERNATIVE: Demo Mode
   If you want to test without real data, use:
   dotnet run test

═══════════════════════════════════════════════════════════════
    """
