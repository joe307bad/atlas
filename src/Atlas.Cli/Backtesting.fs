module TradingStrategy.Backtesting

open System
open TradingStrategy.Data
open TradingStrategy.TechnicalIndicators

type OrderType =
    | Market
    | Limit of decimal

type OrderSide =
    | Buy
    | Sell

type Order = {
    Id: string
    Symbol: string
    OrderType: OrderType
    Side: OrderSide
    Quantity: int
    Timestamp: DateTime
    Price: decimal option  // Filled price
    Status: OrderStatus
}

and OrderStatus =
    | Pending
    | Filled of decimal * DateTime
    | PartiallyFilled of int * decimal * DateTime
    | Rejected of string
    | Cancelled

type Position = {
    Symbol: string
    Quantity: int
    AveragePrice: decimal
    CurrentPrice: decimal
    UnrealizedPnL: decimal
    RealizedPnL: decimal
}

type Portfolio = {
    Cash: decimal
    Positions: Map<string, Position>
    Orders: Order list
    TotalValue: decimal
    StartingCapital: decimal
}

type TradingSignal = {
    Symbol: string
    Action: OrderSide
    Confidence: decimal  // 0.0 to 1.0
    Timestamp: DateTime
    Price: decimal
    Reasoning: string
}

type BacktestConfig = {
    StartingCapital: decimal
    CommissionPerTrade: decimal
    CommissionPercentage: decimal
    SlippageBasisPoints: decimal  // Basis points (e.g., 5 = 0.05%)
    MaxPositionSize: decimal      // Percentage of portfolio per position
    RiskPerTrade: decimal         // Maximum risk per trade as % of portfolio
}

type BacktestResult = {
    Portfolio: Portfolio
    TotalReturn: decimal
    AnnualizedReturn: decimal
    SharpeRatio: decimal
    MaxDrawdown: decimal
    WinRate: decimal
    ProfitFactor: decimal
    TotalTrades: int
    TradingDays: int
    Orders: Order list
    EquityCurve: (DateTime * decimal) list
}

let createPortfolio (startingCapital: decimal) =
    {
        Cash = startingCapital
        Positions = Map.empty
        Orders = []
        TotalValue = startingCapital
        StartingCapital = startingCapital
    }

let calculateSlippage (config: BacktestConfig) (price: decimal) (side: OrderSide) =
    let slippageAmount = price * (config.SlippageBasisPoints / 10000m)
    match side with
    | Buy -> price + slippageAmount
    | Sell -> price - slippageAmount

let calculateCommission (config: BacktestConfig) (quantity: int) (price: decimal) =
    let tradeValue = decimal quantity * price
    let percentageCommission = tradeValue * (config.CommissionPercentage / 100m)
    config.CommissionPerTrade + percentageCommission

let calculatePositionSize (config: BacktestConfig) (portfolio: Portfolio) (price: decimal) (confidence: decimal) (side: OrderSide) =
    match side with
    | Buy ->
        // Risk-based position sizing for buys
        let safePortfolioValue = max 1000m portfolio.TotalValue  // Prevent negative calculations
        let riskAmount = safePortfolioValue * config.RiskPerTrade
        let maxPositionValue = safePortfolioValue * config.MaxPositionSize
        let confidenceAdjustedRisk = riskAmount * confidence
        
        let positionValue = min maxPositionValue confidenceAdjustedRisk
        let quantity = int (positionValue / price)
        max 1 quantity  // Minimum 1 share
    | Sell ->
        // For sells, only sell what we own
        1  // Placeholder - will be adjusted based on actual position

let updatePosition (portfolio: Portfolio) (symbol: string) (quantity: int) (price: decimal) (side: OrderSide) =
    match Map.tryFind symbol portfolio.Positions with
    | Some existingPosition ->
        let orderQuantity = match side with Buy -> quantity | Sell -> -quantity
        let newQuantity = existingPosition.Quantity + orderQuantity
        
        if newQuantity = 0 then
            // Position closed
            let realizedPnL = existingPosition.RealizedPnL + (decimal -orderQuantity * (price - existingPosition.AveragePrice))
            let newPositions = Map.remove symbol portfolio.Positions
            { portfolio with Positions = newPositions }
        elif newQuantity > 0 then
            // Update long position
            let totalCost = 
                if orderQuantity > 0 then
                    // Adding to position
                    (existingPosition.AveragePrice * decimal existingPosition.Quantity) + (price * decimal orderQuantity)
                else
                    // Reducing position - keep same average cost
                    existingPosition.AveragePrice * decimal newQuantity
                
            let newAveragePrice = 
                if orderQuantity > 0 then totalCost / decimal newQuantity 
                else existingPosition.AveragePrice
            let updatedPosition = {
                existingPosition with
                    Quantity = newQuantity
                    AveragePrice = newAveragePrice
                    CurrentPrice = price
            }
            let newPositions = Map.add symbol updatedPosition portfolio.Positions
            { portfolio with Positions = newPositions }
        else
            // Don't allow short positions - reject the order
            portfolio
    | None when side = Buy ->
        // New long position
        let newPosition = {
            Symbol = symbol
            Quantity = quantity
            AveragePrice = price
            CurrentPrice = price
            UnrealizedPnL = 0m
            RealizedPnL = 0m
        }
        let newPositions = Map.add symbol newPosition portfolio.Positions
        { portfolio with Positions = newPositions }
    | None ->
        // Can't sell what we don't own
        portfolio

let fillOrder (config: BacktestConfig) (portfolio: Portfolio) (order: Order) (marketPrice: decimal) =
    let executionPrice = calculateSlippage config marketPrice order.Side
    let commission = calculateCommission config order.Quantity executionPrice
    
    match order.Side with
    | Buy ->
        let totalCost = (decimal order.Quantity * executionPrice) + commission
        if portfolio.Cash < totalCost then
            // Insufficient funds
            let rejectedOrder = { order with Status = Rejected "Insufficient funds" }
            (portfolio, rejectedOrder)
        else
            // Execute buy order
            let newCash = portfolio.Cash - totalCost
            let updatedPortfolio = updatePosition portfolio order.Symbol order.Quantity executionPrice order.Side
            let finalPortfolio = { updatedPortfolio with Cash = newCash }
            let filledOrder = { order with Status = Filled (executionPrice, DateTime.Now); Price = Some executionPrice }
            (finalPortfolio, filledOrder)
    
    | Sell ->
        // Check if we own enough shares
        match Map.tryFind order.Symbol portfolio.Positions with
        | Some position when position.Quantity >= order.Quantity ->
            // Execute sell order
            let saleProceeds = (decimal order.Quantity * executionPrice) - commission
            let newCash = portfolio.Cash + saleProceeds
            let updatedPortfolio = updatePosition portfolio order.Symbol order.Quantity executionPrice order.Side
            let finalPortfolio = { updatedPortfolio with Cash = newCash }
            let filledOrder = { order with Status = Filled (executionPrice, DateTime.Now); Price = Some executionPrice }
            (finalPortfolio, filledOrder)
        | _ ->
            // Don't own enough shares
            let rejectedOrder = { order with Status = Rejected "Insufficient shares" }
            (portfolio, rejectedOrder)

let generateTradingSignals (indicators: TechnicalIndicatorSet) (currentPrice: decimal) (timestamp: DateTime) =
    let signals = ResizeArray<TradingSignal>()
    
    // Simple RSI-based signals
    if indicators.RSI14.Length > 0 then
        let latestRSI = indicators.RSI14.[indicators.RSI14.Length - 1].Value
        
        if latestRSI < 30m then
            signals.Add({
                Symbol = indicators.Symbol
                Action = Buy
                Confidence = (30m - latestRSI) / 30m  // More oversold = higher confidence
                Timestamp = timestamp
                Price = currentPrice
                Reasoning = sprintf "RSI oversold at %.1f" latestRSI
            })
        elif latestRSI > 70m then
            signals.Add({
                Symbol = indicators.Symbol
                Action = Sell
                Confidence = (latestRSI - 70m) / 30m  // More overbought = higher confidence
                Timestamp = timestamp
                Price = currentPrice
                Reasoning = sprintf "RSI overbought at %.1f" latestRSI
            })
    
    // MACD-based signals
    if indicators.MACD.Length > 0 then
        let latestMACD = indicators.MACD.[indicators.MACD.Length - 1]
        
        if latestMACD.Histogram > 0m && latestMACD.MACD > latestMACD.Signal then
            signals.Add({
                Symbol = indicators.Symbol
                Action = Buy
                Confidence = min 1.0m (abs latestMACD.Histogram * 100m)  // Scale histogram to confidence
                Timestamp = timestamp
                Price = currentPrice
                Reasoning = sprintf "MACD bullish crossover (%.3f > %.3f)" latestMACD.MACD latestMACD.Signal
            })
        elif latestMACD.Histogram < 0m && latestMACD.MACD < latestMACD.Signal then
            signals.Add({
                Symbol = indicators.Symbol
                Action = Sell
                Confidence = min 1.0m (abs latestMACD.Histogram * 100m)
                Timestamp = timestamp
                Price = currentPrice
                Reasoning = sprintf "MACD bearish crossover (%.3f < %.3f)" latestMACD.MACD latestMACD.Signal
            })
    
    signals.ToArray()

let updatePortfolioValue (portfolio: Portfolio) (currentPrices: Map<string, decimal>) =
    let positionsValue = 
        portfolio.Positions
        |> Map.fold (fun acc symbol position ->
            match Map.tryFind symbol currentPrices with
            | Some currentPrice ->
                let positionValue = decimal position.Quantity * currentPrice
                let updatedPosition = { position with CurrentPrice = currentPrice; UnrealizedPnL = (currentPrice - position.AveragePrice) * decimal position.Quantity }
                acc + positionValue
            | None -> acc + (decimal position.Quantity * position.CurrentPrice)
        ) 0m
    
    { portfolio with TotalValue = portfolio.Cash + positionsValue }

let runBacktest (config: BacktestConfig) (marketData: TimeSeriesData<MarketDataPoint>[]) (indicators: TechnicalIndicatorSet[]) =
    let mutable portfolio = createPortfolio config.StartingCapital
    let allOrders = ResizeArray<Order>()
    let equityCurve = ResizeArray<DateTime * decimal>()
    
    // Get all unique timestamps across all symbols
    let allTimestamps = 
        marketData
        |> Array.collect (fun ts -> ts.Data |> Array.map (fun dp -> dp.Timestamp))
        |> Array.distinct
        |> Array.sort
    
    // Process each timestamp
    for timestamp in allTimestamps do
        // Get current prices for all symbols
        let currentPrices = 
            marketData
            |> Array.choose (fun ts ->
                ts.Data
                |> Array.tryFind (fun dp -> dp.Timestamp = timestamp)
                |> Option.map (fun dp -> (ts.Symbol, dp.Close))
            )
            |> Map.ofArray
        
        // Generate trading signals for each symbol
        let allSignals = 
            indicators
            |> Array.collect (fun indicator ->
                match Map.tryFind indicator.Symbol currentPrices with
                | Some currentPrice -> generateTradingSignals indicator currentPrice timestamp
                | None -> [||]
            )
        
        // Convert signals to orders
        for signal in allSignals do
            let quantity = 
                match signal.Action with
                | Sell ->
                    // For sells, determine how much we can sell
                    match Map.tryFind signal.Symbol portfolio.Positions with
                    | Some position when position.Quantity > 0 ->
                        min position.Quantity (max 1 (position.Quantity / 4))  // Sell up to 25% of position
                    | _ -> 0  // Can't sell if we don't own any
                | Buy ->
                    calculatePositionSize config portfolio signal.Price signal.Confidence signal.Action
            
            if quantity > 0 then
                let order = {
                    Id = Guid.NewGuid().ToString()
                    Symbol = signal.Symbol
                    OrderType = Market
                    Side = signal.Action
                    Quantity = quantity
                    Timestamp = timestamp
                    Price = None
                    Status = Pending
                }
                
                // Execute order immediately (market order)
                let (updatedPortfolio, filledOrder) = fillOrder config portfolio order signal.Price
                portfolio <- updatedPortfolio
                allOrders.Add(filledOrder)
        
        // Update portfolio value with current prices
        portfolio <- updatePortfolioValue portfolio currentPrices
        
        // Record equity curve point
        equityCurve.Add((timestamp, portfolio.TotalValue))
    
    // Calculate backtest metrics
    let finalValue = portfolio.TotalValue
    let totalReturn = (finalValue - config.StartingCapital) / config.StartingCapital
    let tradingDays = allTimestamps.Length
    let annualizedReturn = totalReturn * (252m / decimal tradingDays) // 252 trading days per year
    
    // Calculate max drawdown
    let maxDrawdown = 
        if equityCurve.Count = 0 then 0m
        else
            let mutable peak = equityCurve.[0] |> snd
            let mutable maxDD = 0m
            for (_, value) in equityCurve do
                if value > peak then peak <- value
                let drawdown = (peak - value) / peak
                if drawdown > maxDD then maxDD <- drawdown
            maxDD
    
    // Calculate win rate
    let filledOrders = allOrders.ToArray() |> Array.filter (fun o -> match o.Status with Filled _ -> true | _ -> false)
    let totalTrades = filledOrders.Length
    let winRate = 
        if totalTrades = 0 then 0m
        else
            // This is simplified - in reality we'd track P&L per trade
            let winners = totalTrades / 2  // Placeholder
            decimal winners / decimal totalTrades
    
    // Simple Sharpe ratio calculation (simplified)
    let sharpeRatio = 
        if maxDrawdown = 0m then 0m
        else annualizedReturn / (maxDrawdown * 4m)  // Simplified calculation
    
    {
        Portfolio = portfolio
        TotalReturn = totalReturn * 100m  // Convert to percentage
        AnnualizedReturn = annualizedReturn * 100m
        SharpeRatio = sharpeRatio
        MaxDrawdown = maxDrawdown * 100m
        WinRate = winRate * 100m
        ProfitFactor = if totalReturn > 0m then abs totalReturn else 1m
        TotalTrades = totalTrades
        TradingDays = tradingDays
        Orders = allOrders.ToArray() |> List.ofArray
        EquityCurve = equityCurve.ToArray() |> List.ofArray
    }