module TradingStrategy.AlpacaApi

open System
open System.Threading.Tasks
open Alpaca.Markets
open TradingStrategy.Data

type AlpacaConfig = {
    ApiKey: string
    SecretKey: string
    UsePaper: bool
}

type AlpacaDataProvider(config: AlpacaConfig) =
    
    let client = 
        let env = if config.UsePaper then Environments.Paper else Environments.Live
        env.GetAlpacaDataClient(SecretKey(config.ApiKey, config.SecretKey))
    
    // Get Alpaca server time to ensure proper synchronization
    member _.GetServerTimeAsync() : Task<DateTime> =
        task {
            try
                let env = if config.UsePaper then Environments.Paper else Environments.Live
                let tradingClient = env.GetAlpacaTradingClient(SecretKey(config.ApiKey, config.SecretKey))
                let! clock = tradingClient.GetClockAsync()
                return clock.TimestampUtc
            with
            | ex ->
                printfn "Warning: Could not get server time, using local time: %s" ex.Message
                return DateTime.UtcNow
        }
    
    // Adjust end date to comply with subscription limitations (15-minute delay for free accounts)
    member this.GetSafeEndDate(requestedEndDate: DateTime) : Task<DateTime> =
        task {
            let! serverTime = this.GetServerTimeAsync()
            let fifteenMinutesAgo = serverTime.AddMinutes(-15.0)
            
            if requestedEndDate > fifteenMinutesAgo then
                printfn "⏰ Adjusting end date to comply with subscription limits (15-min delay)"
                return fifteenMinutesAgo
            else
                return requestedEndDate
        }
    
    member this.GetHistoricalBarsAsync(symbol: string, startDate: DateTime, endDate: DateTime, timeframe: BarTimeFrame) : Task<MarketDataPoint[]> =
        task {
            try
                let! safeEndDate = this.GetSafeEndDate(endDate)
                
                // For very recent end dates, omit the end parameter entirely (as suggested in forum)
                let! serverTime = this.GetServerTimeAsync()
                let thirtyMinutesAgo = serverTime.AddMinutes(-30.0)
                
                let request = 
                    if endDate > thirtyMinutesAgo then
                        printfn "📅 Using earlier end date for %s to avoid subscription limits" symbol
                        // Use a much earlier end date to avoid subscription issues
                        let conservativeEndDate = serverTime.AddHours(-1.0)  // 1 hour ago
                        HistoricalBarsRequest(symbol, startDate, conservativeEndDate, timeframe)
                            .WithPageSize(10000u)
                    else
                        HistoricalBarsRequest(symbol, startDate, safeEndDate, timeframe)
                            .WithPageSize(10000u)
                
                let! response = client.ListHistoricalBarsAsync(request)
                
                let dataPoints = 
                    response.Items
                    |> Seq.map (fun (bar: IBar) ->
                        {
                            Timestamp = bar.TimeUtc.ToLocalTime()
                            Open = bar.Open
                            High = bar.High
                            Low = bar.Low
                            Close = bar.Close
                            Volume = int64 bar.Volume
                        })
                    |> Seq.toArray
                
                return dataPoints
            with
            | ex -> 
                printfn $"Error fetching data for {symbol}: {ex.Message}"
                return Array.empty
        }
    
    member this.GetMultipleSymbolsAsync(symbols: string[], startDate: DateTime, endDate: DateTime, timeframe: BarTimeFrame) : Task<TimeSeriesData<MarketDataPoint>[]> =
        task {
            let tasks = 
                symbols
                |> Array.map (fun symbol ->
                    task {
                        let! data = this.GetHistoricalBarsAsync(symbol, startDate, endDate, timeframe)
                        let cleanedData = cleanMarketData data
                        return {
                            Symbol = symbol
                            Data = cleanedData
                            StartDate = startDate
                            EndDate = endDate
                        }
                    })
            
            let! results = Task.WhenAll(tasks)
            return results
        }
    
    interface IDisposable with
        member _.Dispose() = client.Dispose()

let createDataProvider (apiKey: string) (secretKey: string) (usePaper: bool) : AlpacaDataProvider =
    let config = {
        ApiKey = apiKey
        SecretKey = secretKey
        UsePaper = usePaper
    }
    new AlpacaDataProvider(config)

let fetchMarketDataSet (provider: AlpacaDataProvider) (request: MarketDataRequest) : Task<TimeSeriesData<MarketDataPoint>> =
    task {
        let timeframe = 
            if request.Resolution = TimeSpan.FromMinutes(1.0) then BarTimeFrame.Minute
            elif request.Resolution = TimeSpan.FromMinutes(5.0) then BarTimeFrame.Minute
            elif request.Resolution = TimeSpan.FromMinutes(15.0) then BarTimeFrame.Minute
            elif request.Resolution = TimeSpan.FromHours(1.0) then BarTimeFrame.Hour
            elif request.Resolution = TimeSpan.FromDays(1.0) then BarTimeFrame.Day
            else BarTimeFrame.Minute
        
        let! data = provider.GetHistoricalBarsAsync(request.Symbol, request.StartDate, request.EndDate, timeframe)
        let cleanedData = cleanMarketData data
        
        return {
            Symbol = request.Symbol
            Data = cleanedData
            StartDate = request.StartDate
            EndDate = request.EndDate
        }
    }

let fetchMultipleSymbols (provider: AlpacaDataProvider) (symbols: string[]) (startDate: DateTime) (endDate: DateTime) (resolution: TimeSpan) : Task<TimeSeriesData<MarketDataPoint>[]> =
    task {
        let timeframe = 
            if resolution = TimeSpan.FromMinutes(1.0) then BarTimeFrame.Minute
            elif resolution = TimeSpan.FromMinutes(5.0) then BarTimeFrame.Minute
            elif resolution = TimeSpan.FromMinutes(15.0) then BarTimeFrame.Minute
            elif resolution = TimeSpan.FromHours(1.0) then BarTimeFrame.Hour
            elif resolution = TimeSpan.FromDays(1.0) then BarTimeFrame.Day
            else BarTimeFrame.Minute
        
        let! results = provider.GetMultipleSymbolsAsync(symbols, startDate, endDate, timeframe)
        return results
    }