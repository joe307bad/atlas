module TradingStrategy.DataStream

open System
open System.IO
open System.Threading.Tasks
open System.Collections.Concurrent
open TradingStrategy.Data
open TradingStrategy.RealTimeData

type IDataStream =
    abstract member ConnectAsync: unit -> Task<unit>
    abstract member DisconnectAsync: unit -> Task<unit>
    abstract member SubscribeToSymbols: string[] -> Task<unit>
    abstract member OnTick: IEvent<TickData>
    abstract member OnBar: IEvent<MarketDataPoint>
    abstract member IsConnected: bool
    abstract member StreamType: string

type CSVSimulationStream(csvFilePath: string, tickIntervalMs: int) =
    let tickEvent = Event<TickData>()
    let barEvent = Event<MarketDataPoint>()
    let mutable isConnected = false
    let mutable cancellationToken = new System.Threading.CancellationTokenSource()
    let mutable subscribedSymbols = [||]
    
    let parseCSVLine (line: string) : MarketDataPoint option =
        try
            let parts = line.Split(',')
            if parts.Length >= 7 then
                Some {
                    Timestamp = DateTime.Parse(parts.[0])
                    Open = decimal parts.[2]
                    High = decimal parts.[3]
                    Low = decimal parts.[4]
                    Close = decimal parts.[5]
                    Volume = int64 parts.[6]
                }
            else None
        with _ -> None
    
    let convertBarToTick (symbol: string) (bar: MarketDataPoint) : TickData =
        {
            Symbol = symbol
            Price = bar.Close
            Size = int (bar.Volume / 100L) + 100  // Approximate tick size
            Timestamp = bar.Timestamp
            BidPrice = Some (bar.Close - 0.01m)
            AskPrice = Some (bar.Close + 0.01m)
            BidSize = Some (Random().Next(100, 1000))
            AskSize = Some (Random().Next(100, 1000))
            Exchange = Some "SIMULATED"
        }
    
    interface IDataStream with
        member _.ConnectAsync() = 
            task {
                if not isConnected then
                    isConnected <- true
                    printfn "📁 Connected to CSV simulation stream: %s" csvFilePath
                    printfn "   Tick interval: %dms" tickIntervalMs
            }
        
        member _.DisconnectAsync() = 
            task {
                if isConnected then
                    cancellationToken.Cancel()
                    isConnected <- false
                    printfn "🔌 Disconnected from CSV simulation stream"
            }
        
        member _.SubscribeToSymbols(symbols: string[]) = 
            task {
                subscribedSymbols <- symbols
                printfn "📊 Subscribed to symbols: %s" (String.concat ", " symbols)
                
                // Start emitting data
                let emitTask = task {
                    if File.Exists(csvFilePath) then
                        let lines = File.ReadAllLines(csvFilePath)
                        let dataLines = lines |> Array.skip 1  // Skip header
                        
                        // Parse symbol from CSV
                        let symbol = 
                            if dataLines.Length > 0 then
                                let firstLine = dataLines.[0].Split(',')
                                if firstLine.Length >= 2 then firstLine.[1] else "UNKNOWN"
                            else "UNKNOWN"
                        
                        printfn "🔄 Starting data emission for %s (%d data points)" symbol dataLines.Length
                        
                        for line in dataLines do
                            if not cancellationToken.Token.IsCancellationRequested then
                                match parseCSVLine line with
                                | Some bar ->
                                    // Emit bar event
                                    barEvent.Trigger(bar)
                                    
                                    // Convert to tick and emit tick event
                                    let tick = convertBarToTick symbol bar
                                    tickEvent.Trigger(tick)
                                    
                                    // Wait for specified interval
                                    do! Task.Delay(tickIntervalMs, cancellationToken.Token)
                                | None -> ()
                        
                        printfn "✅ CSV simulation completed - all %d data points emitted" dataLines.Length
                    else
                        printfn "❌ CSV file not found: %s" csvFilePath
                }
                
                // Start emission in background
                emitTask |> ignore
            }
        
        member _.OnTick = tickEvent.Publish
        member _.OnBar = barEvent.Publish
        member _.IsConnected = isConnected
        member _.StreamType = "CSV_SIMULATION"

// Placeholder for future Alpaca WebSocket implementation
// For now, we'll just use CSV simulation

// Factory function to create appropriate data stream
let createDataStream (streamType: string) (config: Map<string, string>) : IDataStream =
    match streamType.ToLower() with
    | "csv" | "simulation" ->
        let csvPath = config.["csvPath"]
        let interval = config.TryFind "interval" |> Option.map int |> Option.defaultValue 1000
        CSVSimulationStream(csvPath, interval) :> IDataStream
    
    | _ ->
        failwithf "Unknown stream type: %s. Currently only 'csv' or 'simulation' are supported." streamType