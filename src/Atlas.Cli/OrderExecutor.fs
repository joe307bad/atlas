module TradingStrategy.OrderExecutor

open System
open System.Threading.Tasks

// Order execution result
type OrderResult =
    | Success of executionPrice: decimal * timestamp: DateTime
    | Failed of reason: string

// Abstract interface for order execution
type IOrderExecutor =
    abstract member ExecuteBuyOrder : symbol: string -> shares: int -> price: decimal -> Task<OrderResult>
    abstract member ExecuteSellOrder : symbol: string -> shares: int -> price: decimal -> Task<OrderResult>
    abstract member GetExecutorType : unit -> string

// Mock order executor for simulations - just succeeds immediately
type MockOrderExecutor() =
    interface IOrderExecutor with
        member _.ExecuteBuyOrder symbol shares price =
            task {
                // Simulate slight delay and small slippage
                do! Task.Delay(50) // 50ms delay to simulate order processing
                let slippage = 0.001m // 0.1% slippage
                let executionPrice = price * (1m + slippage)
                printfn "🎯 MOCK BUY ORDER: %s %d shares @ $%.2f (requested: $%.2f)" symbol shares executionPrice price
                return Success (executionPrice, DateTime.UtcNow)
            }

        member _.ExecuteSellOrder symbol shares price =
            task {
                // Simulate slight delay and small slippage
                do! Task.Delay(50) // 50ms delay to simulate order processing
                let slippage = 0.001m // 0.1% slippage
                let executionPrice = price * (1m - slippage)
                printfn "🎯 MOCK SELL ORDER: %s %d shares @ $%.2f (requested: $%.2f)" symbol shares executionPrice price
                return Success (executionPrice, DateTime.UtcNow)
            }

        member _.GetExecutorType() = "MOCK_SIMULATION"

// Alpaca order executor for live trading
type AlpacaOrderExecutor(alpacaClient: Alpaca.Markets.IAlpacaTradingClient) =
    interface IOrderExecutor with
        member _.ExecuteBuyOrder symbol shares price =
            task {
                try
                    // TODO: Implement actual Alpaca API calls
                    // For now, just simulate the order
                    printfn "📡 ALPACA BUY ORDER: Would submit %s %d shares @ $%.2f (market order)" symbol shares price
                    do! Task.Delay(100) // Simulate order processing delay
                    printfn "✅ ALPACA BUY SIMULATED: %s %d shares @ $%.2f" symbol shares price
                    return Success (price, DateTime.UtcNow)

                with
                | ex ->
                    printfn "❌ ALPACA BUY ERROR: %s - %s" symbol ex.Message
                    return Failed ex.Message
            }

        member _.ExecuteSellOrder symbol shares price =
            task {
                try
                    // TODO: Implement actual Alpaca API calls
                    // For now, just simulate the order
                    printfn "📡 ALPACA SELL ORDER: Would submit %s %d shares @ $%.2f (market order)" symbol shares price
                    do! Task.Delay(100) // Simulate order processing delay
                    printfn "✅ ALPACA SELL SIMULATED: %s %d shares @ $%.2f" symbol shares price
                    return Success (price, DateTime.UtcNow)

                with
                | ex ->
                    printfn "❌ ALPACA SELL ERROR: %s - %s" symbol ex.Message
                    return Failed ex.Message
            }

        member _.GetExecutorType() = "ALPACA_LIVE"

// Factory function to create appropriate executor
let createOrderExecutor (isSimulation: bool) (alpacaClientOpt: Alpaca.Markets.IAlpacaTradingClient option) : IOrderExecutor =
    if isSimulation then
        MockOrderExecutor() :> IOrderExecutor
    else
        match alpacaClientOpt with
        | Some client -> AlpacaOrderExecutor(client) :> IOrderExecutor
        | None -> failwith "Alpaca client required for live trading"
