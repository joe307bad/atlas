module TradingStrategy.TradingEngine

open System
open System.Threading.Tasks
open TradingStrategy.Data
open TradingStrategy.RealTimeData
open TradingStrategy.OrderExecutor

type TradeAction =
    | Buy of shares: int * price: decimal
    | Sell of shares: int * price: decimal
    | Hold

// Reuse existing TradingPosition type from RealTimeData
type Trade = {
    Symbol: string
    Action: string  // "BUY" or "SELL"
    Shares: int
    Price: decimal
    Timestamp: DateTime
    PnL: decimal option  // Only for sells
    Reasoning: string   // Why this trade was made
}

type TradingState = {
    Positions: Map<string, TradingPosition>  // Use existing TradingPosition type
    Trades: Trade list
    Cash: decimal
    TotalPnL: decimal
    MaxDrawdown: decimal
    PeakValue: decimal
    PriceHistory: Map<string, decimal list>  // Track price history for trend analysis
}

open TradingStrategy.Configuration

// Enhanced trading rules using configuration
type EnhancedTradingRules = {
    Config: TradingConfig           // Configuration with all trading rules
    MaxPositionsPerSymbol: int      // Max concurrent positions per symbol
    MinCash: decimal               // Minimum cash to maintain
    PriceHistoryLength: int        // How many prices to keep for trend analysis
}

let createRulesFromConfig (config: TradingConfig) = {
    Config = config
    MaxPositionsPerSymbol = 1            // One position per symbol
    MinCash = 1000m                      // Keep $1000 cash
    PriceHistoryLength = 5               // Keep 5 price points for faster reactions
}

let createInitialState (startingCash: decimal) = {
    Positions = Map.empty
    Trades = []
    Cash = startingCash
    TotalPnL = 0m
    MaxDrawdown = 0m
    PeakValue = startingCash
    PriceHistory = Map.empty
}

let updatePosition (position: TradingPosition) (newPrice: decimal) =
    let unrealizedPnL = decimal position.Quantity * (newPrice - position.EntryPrice)
    { position with 
        CurrentPrice = newPrice
        PnL = unrealizedPnL }

let shouldBuy (tick: TickData) (priceHistory: decimal list) (rules: EnhancedTradingRules) (state: TradingState) : bool =
    // Check if we have enough cash (calculate max shares we can actually afford)
    let maxAffordableShares = int (state.Cash / tick.Price)
    let hasEnoughCash = maxAffordableShares > 0
    
    // Check if we already have a position in this symbol
    let hasPosition = Map.containsKey tick.Symbol state.Positions
    
    // Buy if price increased more than configured threshold
    let simpleUptrend = 
        if priceHistory.Length >= 2 then
            let recent = priceHistory.Head
            let older = priceHistory.Tail.Head
            let priceChange = if older > 0m then (recent - older) / older else 0m
            let percentChange = priceChange * 100m
            
            // Debug output - let's see what's happening
            if abs percentChange > 0.01m then
                printfn "TRADE ANALYSIS: %s %.2f→%.2f (%.3f%%) | Cash: $%.0f | HasCash: %b | Pos: %b | Trend: %b | Threshold: %.4f%%" 
                        tick.Symbol older recent percentChange state.Cash hasEnoughCash hasPosition (priceChange >= rules.Config.BuyTriggerPercent) (rules.Config.BuyTriggerPercent * 100m)
            
            priceChange >= rules.Config.BuyTriggerPercent
        else false
    
    let result = hasEnoughCash && not hasPosition && simpleUptrend
    
    // Additional debug for buy decision
    if priceHistory.Length >= 2 && abs (priceHistory.Head - priceHistory.Tail.Head) > 0.01m then
        printfn "BUY DECISION: HasCash: %b, NoPosition: %b, Uptrend: %b -> RESULT: %b" 
                hasEnoughCash (not hasPosition) simpleUptrend result
    
    result

let shouldSell (position: TradingPosition) (tick: TickData) (rules: EnhancedTradingRules) : bool =
    // Check stop loss condition using config
    let lossPercent = (position.EntryPrice - position.CurrentPrice) / position.EntryPrice
    let stopLossTriggered = lossPercent >= rules.Config.StopLossPercent
    
    // Check take profit condition using config  
    let profitPercent = (position.CurrentPrice - position.EntryPrice) / position.EntryPrice
    let takeProfitTriggered = profitPercent >= rules.Config.TakeProfitPercent
    
    stopLossTriggered || takeProfitTriggered

let executeBuy (tick: TickData) (shares: int) (rules: EnhancedTradingRules) (state: TradingState) (orderExecutor: IOrderExecutor) : Task<TradingState * string> =
    task {
        let totalCost = tick.Price * decimal shares
        
        if state.Cash >= totalCost then
            // Execute the order via the order executor
            let! orderResult = orderExecutor.ExecuteBuyOrder tick.Symbol shares tick.Price
            
            match orderResult with
            | Success (executionPrice, executionTime) ->
                let actualTotalCost = executionPrice * decimal shares
                
                let position = {
                    Symbol = tick.Symbol
                    Quantity = shares
                    EntryPrice = executionPrice  // Use actual execution price
                    EntryTime = executionTime
                    CurrentPrice = executionPrice
                    PnL = 0m
                }
                
                let trade = {
                    Symbol = tick.Symbol
                    Action = "BUY"
                    Shares = shares
                    Price = executionPrice  // Use actual execution price
                    Timestamp = executionTime
                    PnL = None
                    Reasoning = "Upward momentum detected"
                }
                
                let newState = { state with
                                  Positions = Map.add tick.Symbol position state.Positions
                                  Trades = trade :: state.Trades
                                  Cash = state.Cash - actualTotalCost }
                
                return (newState, "Upward momentum detected")
            
            | Failed reason ->
                return (state, sprintf "Order failed: %s" reason)
        else
            return (state, "Insufficient cash")
    }

let executeSell (position: TradingPosition) (tick: TickData) (reason: string) (state: TradingState) (orderExecutor: IOrderExecutor) : Task<TradingState> =
    task {
        // Execute the order via the order executor
        let! orderResult = orderExecutor.ExecuteSellOrder tick.Symbol position.Quantity tick.Price
        
        match orderResult with
        | Success (executionPrice, executionTime) ->
            let saleProceeds = executionPrice * decimal position.Quantity
            let pnl = saleProceeds - (position.EntryPrice * decimal position.Quantity)
            
            let trade = {
                Symbol = tick.Symbol
                Action = "SELL"
                Shares = position.Quantity
                Price = executionPrice  // Use actual execution price
                Timestamp = executionTime
                PnL = Some pnl
                Reasoning = reason
            }
            
            let newTotalPnL = state.TotalPnL + pnl
            let newCash = state.Cash + saleProceeds
            let totalValue = newCash + (state.Positions |> Map.toSeq |> Seq.sumBy (fun (_, pos) -> pos.CurrentPrice * decimal pos.Quantity))
            
            // Update drawdown tracking
            let newPeakValue = max state.PeakValue totalValue
            let drawdown = if newPeakValue > 0m then (newPeakValue - totalValue) / newPeakValue else 0m
            let newMaxDrawdown = max state.MaxDrawdown drawdown
            
            return 
                { state with
                    Positions = Map.remove tick.Symbol state.Positions
                    Trades = trade :: state.Trades
                    Cash = newCash
                    TotalPnL = newTotalPnL
                    MaxDrawdown = newMaxDrawdown
                    PeakValue = newPeakValue }
        
        | Failed reason ->
            printfn "❌ SELL ORDER FAILED: %s - %s" tick.Symbol reason
            // Return unchanged state if sell fails
            return state
    }

// Main function to analyze tick and make trading decisions
let analyzeTickAndTrade (tick: TickData) (rules: EnhancedTradingRules) (state: TradingState) (orderExecutor: IOrderExecutor) : Task<TradingState * TradeAction> =
    task {
    // Update price history for this symbol
    let currentHistory = 
        match Map.tryFind tick.Symbol state.PriceHistory with
        | Some history -> history
        | None -> []
    
    let updatedHistory = 
        (tick.Price :: currentHistory) 
        |> List.take (min rules.PriceHistoryLength (currentHistory.Length + 1))
    
    let updatedState = { state with PriceHistory = Map.add tick.Symbol updatedHistory state.PriceHistory }
    
    // Check if we have an existing position
    match Map.tryFind tick.Symbol updatedState.Positions with
    | Some position ->
        // Update position with current price
        let updatedPosition = updatePosition position tick.Price
        let stateWithUpdatedPosition = { updatedState with Positions = Map.add tick.Symbol updatedPosition updatedState.Positions }
        
        // Check if we should sell
        if shouldSell updatedPosition tick rules then
            let lossPercent = (updatedPosition.EntryPrice - updatedPosition.CurrentPrice) / updatedPosition.EntryPrice
            let profitPercent = (updatedPosition.CurrentPrice - updatedPosition.EntryPrice) / updatedPosition.EntryPrice
            
            let reason = 
                if lossPercent >= rules.Config.StopLossPercent then 
                    sprintf "Stop loss triggered (%.2f%% loss)" (lossPercent * 100m)
                elif profitPercent >= rules.Config.TakeProfitPercent then 
                    sprintf "Take profit triggered (%.4f%% profit)" (profitPercent * 100m)
                else "Other exit condition"
            
            let! finalState = executeSell updatedPosition tick reason stateWithUpdatedPosition orderExecutor
            return (finalState, Sell (position.Quantity, tick.Price))
        else
            return (stateWithUpdatedPosition, Hold)
    
    | None ->
        // No position, check if we should buy
        let shouldBuyResult = shouldBuy tick updatedHistory rules updatedState
        if updatedHistory.Length >= 2 then
            let recent = updatedHistory.Head
            let older = updatedHistory.Tail.Head
            let percentChange = (recent - older) / older * 100m
            if abs percentChange > 0.01m then
                printfn "SHOULD BUY CHECK: %s %.2f→%.2f (%.3f%%) -> %b" tick.Symbol older recent percentChange shouldBuyResult
        
        if shouldBuyResult then
            let sharesToBuy = min rules.Config.MaxPositionSize (int (updatedState.Cash / tick.Price))
            printfn "BUY SIGNAL: Should buy %d shares at $%.2f (total: $%.2f)" sharesToBuy tick.Price (decimal sharesToBuy * tick.Price)
            if sharesToBuy > 0 then
                let! (finalState, reason) = executeBuy tick sharesToBuy rules updatedState orderExecutor
                return (finalState, Buy (sharesToBuy, tick.Price))
            else
                printfn "ERROR: sharesToBuy = 0, Cash: $%.2f, Price: $%.2f" updatedState.Cash tick.Price
                return (updatedState, Hold)
        else
            return (updatedState, Hold)
    }

let printTradeAction (action: TradeAction) (symbol: string) =
    match action with
    | Buy (shares, price) ->
        printfn "🟢 BUY  %s: %d shares @ $%.2f (Total: $%.2f)" symbol shares price (decimal shares * price)
    | Sell (shares, price) ->
        printfn "🔴 SELL %s: %d shares @ $%.2f (Total: $%.2f)" symbol shares price (decimal shares * price)
    | Hold -> 
        () // Don't print holds to reduce noise

let printTradingSummary (state: TradingState) =
    printfn "\n💰 TRADING SUMMARY"
    printfn "═══════════════════════════════════════════════════════════════"
    printfn "Current Cash: $%.2f" state.Cash
    printfn "Total Realized P&L: $%.2f" state.TotalPnL
    printfn "Max Drawdown: %.2f%%" (state.MaxDrawdown * 100m)
    printfn "Total Trades: %d" state.Trades.Length
    
    if not (Map.isEmpty state.Positions) then
        printfn "\n🏦 OPEN POSITIONS:"
        state.Positions
        |> Map.iter (fun symbol position ->
            let pnlPercent = if position.EntryPrice > 0m then 
                                (position.CurrentPrice - position.EntryPrice) / position.EntryPrice * 100m 
                             else 0m
            printfn "   %s: %d shares @ $%.2f → $%.2f (P&L: $%.2f, %.2f%%)" 
                    symbol position.Quantity position.EntryPrice position.CurrentPrice 
                    position.PnL pnlPercent
        )
    
    if not (List.isEmpty state.Trades) then
        printfn "\n📊 RECENT TRADES:"
        state.Trades
        |> List.take (min 5 state.Trades.Length)
        |> List.iter (fun trade ->
            let pnlStr = match trade.PnL with
                         | Some pnl -> sprintf " (P&L: $%.2f)" pnl
                         | None -> ""
            printfn "   %s %s: %d @ $%.2f%s" 
                    trade.Action trade.Symbol trade.Shares trade.Price pnlStr
        )
        
        // Add detailed trade analysis
        printfn "\n📈 COMPLETE TRADE ANALYSIS:"
        printfn "─────────────────────────────────────────────────"
        
        // Separate buy and sell trades
        let buys = state.Trades |> List.filter (fun t -> t.Action = "BUY")
        let sells = state.Trades |> List.filter (fun t -> t.Action = "SELL")
        
        // Calculate trade statistics
        let profitableTrades = sells |> List.filter (fun t -> t.PnL.IsSome && t.PnL.Value > 0m)
        let losingTrades = sells |> List.filter (fun t -> t.PnL.IsSome && t.PnL.Value < 0m)
        let totalProfit = profitableTrades |> List.sumBy (fun t -> t.PnL.Value)
        let totalLoss = losingTrades |> List.sumBy (fun t -> t.PnL.Value)
        let avgWin = if profitableTrades.Length > 0 then totalProfit / decimal profitableTrades.Length else 0m
        let avgLoss = if losingTrades.Length > 0 then totalLoss / decimal losingTrades.Length else 0m
        let winRate = if sells.Length > 0 then decimal profitableTrades.Length / decimal sells.Length * 100m else 0m
        
        printfn "Total Buys: %d | Total Sells: %d" buys.Length sells.Length
        printfn "Winning Trades: %d | Losing Trades: %d" profitableTrades.Length losingTrades.Length
        printfn "Win Rate: %.1f%%" winRate
        printfn "Total Profit from Winners: $%.2f" totalProfit
        printfn "Total Loss from Losers: $%.2f" totalLoss
        printfn "Average Win: $%.2f | Average Loss: $%.2f" avgWin avgLoss
        
        printfn "\n📝 ALL TRADES (chronological):"
        state.Trades
        |> List.rev  // Show in chronological order
        |> List.iteri (fun i trade ->
            let pnlStr = match trade.PnL with
                         | Some pnl -> 
                             let pnlPercent = 
                                 if trade.Action = "SELL" && trade.Shares > 0 then
                                     // Find the most recent BUY before this SELL (looking backwards from position i)
                                     let reversedTrades = state.Trades |> List.rev
                                     let tradesBeforeThis = reversedTrades |> List.take i
                                     let mostRecentBuy = 
                                         tradesBeforeThis 
                                         |> List.tryFindBack (fun t -> t.Symbol = trade.Symbol && t.Action = "BUY")
                                     match mostRecentBuy with
                                     | Some buy -> 
                                         // Percentage return = (Sell Price - Buy Price) / Buy Price * 100
                                         (trade.Price - buy.Price) / buy.Price * 100m
                                     | None -> 0m
                                 else 0m
                             sprintf " → P&L: $%.2f (%.2f%%) - %s" pnl pnlPercent trade.Reasoning
                         | None -> sprintf " - %s" trade.Reasoning
            printfn "%d. %s %s: %d shares @ $%.2f%s" 
                    (i + 1) trade.Action trade.Symbol trade.Shares trade.Price pnlStr
        )