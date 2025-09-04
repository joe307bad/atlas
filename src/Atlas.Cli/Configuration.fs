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
}

let private loadEnvFile() =
    let envFile = Path.Combine(Directory.GetCurrentDirectory(), ".env")
    if File.Exists(envFile) then
        File.ReadAllLines(envFile)
        |> Array.iter (fun line ->
            let trimmed = line.Trim()
            if not (trimmed.StartsWith("#") || String.IsNullOrEmpty(trimmed)) then
                match trimmed.Split('=', 2) with
                | [| key; value |] -> Environment.SetEnvironmentVariable(key, value)
                | _ -> ())

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

    if errors.Count = 0 then
        Ok config
    else
        Error (errors.ToArray())

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

let printConfigurationSummary (config: TradingConfig) =
    printfn $"""
⚙️  CONFIGURATION SUMMARY
═══════════════════════════════════════════════════════════════
🔑 API Configuration:
   Alpaca API Key: {if config.AlpacaApiKey <> "your-api-key-here" then "✅ Configured" else "❌ Missing"}
   Alpaca Secret Key: {if config.AlpacaSecretKey <> "your-secret-key-here" then "✅ Configured" else "❌ Missing"}
   Paper Trading: {if config.UsePaperTrading then "✅ Enabled (Safe)" else "⚠️ Live Trading"}
═══════════════════════════════════════════════════════════════
    """
