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

// Alpaca order executor for live/paper trading
type AlpacaOrderExecutor(alpacaClient: Alpaca.Markets.IAlpacaTradingClient) =
    interface IOrderExecutor with
        member _.ExecuteBuyOrder symbol shares price =
            task {
                try
                    // Create market order request
                    let orderRequest = 
                        Alpaca.Markets.MarketOrder.Buy(symbol, int64 shares)
                    orderRequest.Duration <- Alpaca.Markets.TimeInForce.Day
                    
                    printfn "📡 ALPACA BUY ORDER: Submitting %s %d shares (market order)" symbol shares
                    
                    // Submit the order
                    let! order = alpacaClient.PostOrderAsync(orderRequest)
                    
                    // Wait for order to be filled (with timeout)
                    let mutable filledOrder: Alpaca.Markets.IOrder = order
                    let mutable attempts = 0
                    let maxAttempts = 30  // 30 seconds timeout
                    
                    while filledOrder.OrderStatus <> Alpaca.Markets.OrderStatus.Filled && 
                          filledOrder.OrderStatus <> Alpaca.Markets.OrderStatus.PartiallyFilled &&
                          attempts < maxAttempts do
                        do! Task.Delay(1000)  // Wait 1 second
                        let! updatedOrder = alpacaClient.GetOrderAsync(order.OrderId)
                        filledOrder <- updatedOrder
                        attempts <- attempts + 1
                    
                    match filledOrder.OrderStatus with
                    | Alpaca.Markets.OrderStatus.Filled ->
                        let executionPrice = 
                            if filledOrder.AverageFillPrice.HasValue then 
                                decimal filledOrder.AverageFillPrice.Value 
                            else 
                                price
                        printfn "✅ ALPACA BUY FILLED: %s %d shares @ $%.2f" symbol shares executionPrice
                        return Success (executionPrice, filledOrder.FilledAtUtc.GetValueOrDefault())
                    
                    | Alpaca.Markets.OrderStatus.PartiallyFilled ->
                        let executionPrice = 
                            if filledOrder.AverageFillPrice.HasValue then 
                                decimal filledOrder.AverageFillPrice.Value 
                            else 
                                price
                        printfn "⚠️  ALPACA BUY PARTIALLY FILLED: %s %d/%d shares @ $%.2f" 
                                symbol (int filledOrder.FilledQuantity) shares executionPrice
                        return Success (executionPrice, filledOrder.FilledAtUtc.GetValueOrDefault())
                    
                    | _ ->
                        printfn "❌ ALPACA BUY FAILED: Order status is %A" filledOrder.OrderStatus
                        // Try to cancel the order if it's still open
                        if filledOrder.OrderStatus = Alpaca.Markets.OrderStatus.New || 
                           filledOrder.OrderStatus = Alpaca.Markets.OrderStatus.Accepted then
                            let! cancelResp = alpacaClient.CancelOrderAsync(filledOrder.OrderId)
                            ()
                        return Failed (sprintf "Order not filled after %d seconds (Status: %A)" maxAttempts filledOrder.OrderStatus)

                with
                | ex ->
                    printfn "❌ ALPACA BUY ERROR: %s - %s" symbol ex.Message
                    return Failed ex.Message
            }

        member _.ExecuteSellOrder symbol shares price =
            task {
                try
                    // Create market order request
                    let orderRequest = 
                        Alpaca.Markets.MarketOrder.Sell(symbol, int64 shares)
                    orderRequest.Duration <- Alpaca.Markets.TimeInForce.Day
                    
                    printfn "📡 ALPACA SELL ORDER: Submitting %s %d shares (market order)" symbol shares
                    
                    // Submit the order
                    let! order = alpacaClient.PostOrderAsync(orderRequest)
                    
                    // Wait for order to be filled (with timeout)
                    let mutable filledOrder: Alpaca.Markets.IOrder = order
                    let mutable attempts = 0
                    let maxAttempts = 30  // 30 seconds timeout
                    
                    while filledOrder.OrderStatus <> Alpaca.Markets.OrderStatus.Filled && 
                          filledOrder.OrderStatus <> Alpaca.Markets.OrderStatus.PartiallyFilled &&
                          attempts < maxAttempts do
                        do! Task.Delay(1000)  // Wait 1 second
                        let! updatedOrder = alpacaClient.GetOrderAsync(order.OrderId)
                        filledOrder <- updatedOrder
                        attempts <- attempts + 1
                    
                    match filledOrder.OrderStatus with
                    | Alpaca.Markets.OrderStatus.Filled ->
                        let executionPrice = 
                            if filledOrder.AverageFillPrice.HasValue then 
                                decimal filledOrder.AverageFillPrice.Value 
                            else 
                                price
                        printfn "✅ ALPACA SELL FILLED: %s %d shares @ $%.2f" symbol shares executionPrice
                        return Success (executionPrice, filledOrder.FilledAtUtc.GetValueOrDefault())
                    
                    | Alpaca.Markets.OrderStatus.PartiallyFilled ->
                        let executionPrice = 
                            if filledOrder.AverageFillPrice.HasValue then 
                                decimal filledOrder.AverageFillPrice.Value 
                            else 
                                price
                        printfn "⚠️  ALPACA SELL PARTIALLY FILLED: %s %d/%d shares @ $%.2f" 
                                symbol (int filledOrder.FilledQuantity) shares executionPrice
                        return Success (executionPrice, filledOrder.FilledAtUtc.GetValueOrDefault())
                    
                    | _ ->
                        printfn "❌ ALPACA SELL FAILED: Order status is %A" filledOrder.OrderStatus
                        // Try to cancel the order if it's still open
                        if filledOrder.OrderStatus = Alpaca.Markets.OrderStatus.New || 
                           filledOrder.OrderStatus = Alpaca.Markets.OrderStatus.Accepted then
                            let! cancelResp = alpacaClient.CancelOrderAsync(filledOrder.OrderId)
                            ()
                        return Failed (sprintf "Order not filled after %d seconds (Status: %A)" maxAttempts filledOrder.OrderStatus)

                with
                | ex ->
                    printfn "❌ ALPACA SELL ERROR: %s - %s" symbol ex.Message
                    return Failed ex.Message
            }

        member _.GetExecutorType() = "ALPACA_PAPER"  // Always paper for safety

// Factory function to create appropriate executor
let createOrderExecutor (isSimulation: bool) (alpacaClientOpt: Alpaca.Markets.IAlpacaTradingClient option) : IOrderExecutor =
    if isSimulation then
        MockOrderExecutor() :> IOrderExecutor
    else
        match alpacaClientOpt with
        | Some client -> AlpacaOrderExecutor(client) :> IOrderExecutor
        | None -> failwith "Alpaca client required for live trading"
