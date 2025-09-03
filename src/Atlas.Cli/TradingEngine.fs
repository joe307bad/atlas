module TradingStrategy.TradingEngine

open System
open TradingStrategy.Data
open TradingStrategy.RealTimeData

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

// Extend the existing TradingStrategy type
type EnhancedTradingRules = {
    Strategy: TradingStrategy       // Reuse existing strategy
    TrendThreshold: decimal         // Price move threshold to trigger action
    MaxPositionsPerSymbol: int      // Max concurrent positions per symbol
    MinCash: decimal               // Minimum cash to maintain
    PriceHistoryLength: int        // How many prices to keep for trend analysis
}

let createDefaultRules () = {
    Strategy = createTradingStrategy ()  // Reuse existing strategy creation
    TrendThreshold = 0.0005m             // 0.05% price move to consider (very sensitive)
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
    
    // Simple approach: buy if price increased more than threshold
    let simpleUptrend = 
        if priceHistory.Length >= 2 then
            let recent = priceHistory.Head
            let older = priceHistory.Tail.Head
            let priceChange = if older > 0m then (recent - older) / older else 0m
            let percentChange = priceChange * 100m
            
            // Debug output - let's see what's happening
            if abs percentChange > 0.01m then
                printfn "TRADE ANALYSIS: %s %.2f→%.2f (%.3f%%) | Cash: $%.0f | HasCash: %b | Pos: %b | Trend: %b | Threshold: %.3f%%" 
                        tick.Symbol older recent percentChange state.Cash hasEnoughCash hasPosition (priceChange >= rules.TrendThreshold) (rules.TrendThreshold * 100m)
            
            priceChange >= rules.TrendThreshold
        else false
    
    let result = hasEnoughCash && not hasPosition && simpleUptrend
    
    // Additional debug for buy decision
    if priceHistory.Length >= 2 && abs (priceHistory.Head - priceHistory.Tail.Head) > 0.01m then
        printfn "BUY DECISION: HasCash: %b, NoPosition: %b, Uptrend: %b -> RESULT: %b" 
                hasEnoughCash (not hasPosition) simpleUptrend result
    
    result

let shouldSell (position: TradingPosition) (tick: TickData) (rules: EnhancedTradingRules) : bool =
    // Reuse existing stop loss logic
    let stopLossSignal = checkStopLoss position rules.Strategy
    
    // Reuse existing take profit logic
    let takeProfitSignal = checkTakeProfit position
    
    // Time-based exit (hold for max 10 minutes in simulation)
    let timeBasedExit = 
        (DateTime.UtcNow - position.EntryTime).TotalMinutes > 10.0
    
    stopLossSignal.IsSome || takeProfitSignal.IsSome || timeBasedExit

let executeBuy (tick: TickData) (shares: int) (rules: EnhancedTradingRules) (state: TradingState) : TradingState * string =
    let totalCost = tick.Price * decimal shares
    
    if state.Cash >= totalCost then
        let position = {
            Symbol = tick.Symbol
            Quantity = shares
            EntryPrice = tick.Price
            EntryTime = DateTime.UtcNow
            CurrentPrice = tick.Price
            PnL = 0m
        }
        
        let trade = {
            Symbol = tick.Symbol
            Action = "BUY"
            Shares = shares
            Price = tick.Price
            Timestamp = DateTime.UtcNow
            PnL = None
            Reasoning = "Upward momentum detected"
        }
        
        let newState = { state with
                          Positions = Map.add tick.Symbol position state.Positions
                          Trades = trade :: state.Trades
                          Cash = state.Cash - totalCost }
        
        (newState, "Upward momentum detected")
    else
        (state, "Insufficient cash")

let executeSell (position: TradingPosition) (tick: TickData) (reason: string) (state: TradingState) : TradingState =
    let saleProceeds = tick.Price * decimal position.Quantity
    let pnl = saleProceeds - (position.EntryPrice * decimal position.Quantity)
    
    let trade = {
        Symbol = tick.Symbol
        Action = "SELL"
        Shares = position.Quantity
        Price = tick.Price
        Timestamp = DateTime.UtcNow
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
    
    { state with
        Positions = Map.remove tick.Symbol state.Positions
        Trades = trade :: state.Trades
        Cash = newCash
        TotalPnL = newTotalPnL
        MaxDrawdown = newMaxDrawdown
        PeakValue = newPeakValue }

// Main function to analyze tick and make trading decisions
let analyzeTickAndTrade (tick: TickData) (rules: EnhancedTradingRules) (state: TradingState) : TradingState * TradeAction =
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
            let reason = 
                let stopLoss = checkStopLoss position rules.Strategy
                let takeProfit = checkTakeProfit position
                if stopLoss.IsSome then "Stop loss triggered"
                elif takeProfit.IsSome then "Take profit triggered" 
                else "Time-based exit"
            
            let finalState = executeSell updatedPosition tick reason stateWithUpdatedPosition
            (finalState, Sell (position.Quantity, tick.Price))
        else
            (stateWithUpdatedPosition, Hold)
    
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
            let sharesToBuy = min rules.Strategy.MaxPositionSize (int (updatedState.Cash / tick.Price))
            printfn "BUY SIGNAL: Should buy %d shares at $%.2f (total: $%.2f)" sharesToBuy tick.Price (decimal sharesToBuy * tick.Price)
            if sharesToBuy > 0 then
                let (finalState, reason) = executeBuy tick sharesToBuy rules updatedState
                (finalState, Buy (sharesToBuy, tick.Price))
            else
                printfn "ERROR: sharesToBuy = 0, Cash: $%.2f, Price: $%.2f" updatedState.Cash tick.Price
                (updatedState, Hold)
        else
            (updatedState, Hold)

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