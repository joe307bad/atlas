module TradingStrategy.RiskManagement

open System
open TradingStrategy.Data
open TradingStrategy.TechnicalIndicators
open TradingStrategy.Backtesting

type StopLossType =
    | Fixed of decimal        // Fixed percentage stop loss
    | Trailing of decimal     // Trailing stop loss percentage
    | ATR of decimal * int    // ATR-based stop loss (multiplier * ATR period)
    | Technical of string     // Technical level (e.g., support/resistance)

type RiskLimit = {
    MaxPositionSize: decimal     // Max % of portfolio per position
    MaxDailyLoss: decimal        // Max daily loss as % of portfolio
    MaxDrawdown: decimal         // Max total drawdown as % of portfolio
    MaxConcentration: decimal    // Max % in single sector/symbol
    VaRLimit: decimal           // Value at Risk limit
}

type VolatilityMeasure = {
    Symbol: string
    ATR: decimal               // Average True Range
    RealizedVolatility: decimal // Historical volatility
    ImpliedVolatility: decimal option // Options-based volatility
    VolatilityPercentile: decimal // Current vol vs historical range
}

type RiskMetrics = {
    Portfolio: Portfolio
    Timestamp: DateTime
    TotalValue: decimal
    DailyPnL: decimal
    UnrealizedPnL: decimal
    RealizedPnL: decimal
    MaxDrawdown: decimal
    CurrentDrawdown: decimal
    VaR95: decimal            // 95% Value at Risk
    ConcentrationRisk: decimal
    LeverageRatio: decimal
    Beta: decimal
    Volatility: decimal
    SharpeRatio: decimal
}

type RiskAlert = {
    Id: string
    AlertType: RiskAlertType
    Severity: Severity
    Symbol: string option
    Message: string
    Timestamp: DateTime
    Value: decimal
    Threshold: decimal
}

and RiskAlertType =
    | DrawdownAlert
    | PositionSizeAlert
    | DailyLossAlert
    | VolatilityAlert
    | ConcentrationAlert
    | StopLossTriggered

and Severity =
    | Info
    | Warning
    | Critical

let calculateATR (data: MarketDataPoint[]) (period: int) =
    if data.Length < period + 1 then 0m
    else
        let trueRanges = 
            data
            |> Array.pairwise
            |> Array.map (fun (prev, curr) ->
                let tr1 = curr.High - curr.Low
                let tr2 = abs (curr.High - prev.Close)
                let tr3 = abs (curr.Low - prev.Close)
                max tr1 (max tr2 tr3)
            )
        
        trueRanges
        |> Array.skip (max 0 (trueRanges.Length - period))
        |> Array.average

let calculateVolatilityMeasure (marketData: TimeSeriesData<MarketDataPoint>) (period: int) =
    let data = marketData.Data
    let atr = calculateATR data 20
    
    // Calculate realized volatility (daily returns standard deviation)
    let returns = 
        data
        |> Array.pairwise
        |> Array.map (fun (prev, curr) -> 
            log (float (curr.Close / prev.Close))
        )
    
    let meanReturn = returns |> Array.average
    let variance = 
        returns 
        |> Array.map (fun r -> (r - meanReturn) * (r - meanReturn))
        |> Array.average
    
    let realizedVol = sqrt variance * sqrt 252.0 |> decimal // Annualized
    
    // Calculate volatility percentile (simplified)
    let recentVol = 
        returns
        |> Array.skip (max 0 (returns.Length - period))
        |> Array.map (fun r -> (r - meanReturn) * (r - meanReturn))
        |> Array.average
        |> sqrt
        |> decimal
    
    let volPercentile = min 1.0m (recentVol / (realizedVol / 16m)) // Simplified percentile
    
    {
        Symbol = marketData.Symbol
        ATR = atr
        RealizedVolatility = realizedVol
        ImpliedVolatility = None
        VolatilityPercentile = volPercentile
    }

let calculateStopLossPrice (position: Position) (stopLossType: StopLossType) (currentPrice: decimal) (volatilityMeasure: VolatilityMeasure) =
    match stopLossType with
    | Fixed percentage ->
        if position.Quantity > 0 then
            position.AveragePrice * (1m - percentage)
        else
            position.AveragePrice * (1m + percentage)
    
    | Trailing percentage ->
        if position.Quantity > 0 then
            currentPrice * (1m - percentage)
        else
            currentPrice * (1m + percentage)
    
    | ATR (multiplier, _) ->
        if position.Quantity > 0 then
            currentPrice - (volatilityMeasure.ATR * multiplier)
        else
            currentPrice + (volatilityMeasure.ATR * multiplier)
    
    | Technical level ->
        // Placeholder - would use technical analysis
        currentPrice * 0.95m

let checkStopLoss (position: Position) (currentPrice: decimal) (stopLossType: StopLossType) (volatilityMeasure: VolatilityMeasure) =
    let stopPrice = calculateStopLossPrice position stopLossType currentPrice volatilityMeasure
    
    if position.Quantity > 0 then
        currentPrice <= stopPrice
    else
        currentPrice >= stopPrice

let calculateDynamicPositionSize (portfolio: Portfolio) (price: decimal) (volatilityMeasure: VolatilityMeasure) (riskLimits: RiskLimit) (confidence: decimal) =
    // Kelly Criterion-inspired position sizing
    let baseRiskAmount = portfolio.TotalValue * riskLimits.MaxPositionSize
    
    // Adjust for volatility
    let volatilityAdjustment = 
        if volatilityMeasure.VolatilityPercentile > 0.8m then 0.5m // High vol = reduce size
        elif volatilityMeasure.VolatilityPercentile < 0.2m then 1.2m // Low vol = increase size  
        else 1.0m
    
    // Adjust for confidence
    let confidenceAdjustment = confidence
    
    let adjustedRiskAmount = baseRiskAmount * volatilityAdjustment * confidenceAdjustment
    let quantity = int (adjustedRiskAmount / price)
    
    max 1 quantity

let calculatePortfolioRisk (portfolio: Portfolio) (currentPrices: Map<string, decimal>) (benchmarkReturn: decimal) =
    let totalValue = portfolio.TotalValue
    let cashWeight = portfolio.Cash / totalValue
    
    // Calculate position weights and returns
    let positionMetrics = 
        portfolio.Positions
        |> Map.toArray
        |> Array.choose (fun (symbol, position) ->
            match Map.tryFind symbol currentPrices with
            | Some currentPrice ->
                let weight = (decimal position.Quantity * currentPrice) / totalValue
                let dayReturn = (currentPrice - position.CurrentPrice) / position.CurrentPrice
                Some (symbol, weight, dayReturn, position)
            | None -> None
        )
    
    // Calculate portfolio daily return
    let portfolioReturn = 
        positionMetrics
        |> Array.sumBy (fun (_, weight, dayReturn, _) -> weight * dayReturn)
    
    // Calculate unrealized P&L
    let unrealizedPnL = 
        positionMetrics
        |> Array.sumBy (fun (_, _, _, position) ->
            match Map.tryFind position.Symbol currentPrices with
            | Some currentPrice -> 
                decimal position.Quantity * (currentPrice - position.AveragePrice)
            | None -> 0m
        )
    
    // Calculate concentration risk (max weight)
    let concentrationRisk = 
        if positionMetrics.Length = 0 then 0m
        else positionMetrics |> Array.map (fun (_, weight, _, _) -> weight) |> Array.max
    
    // Simplified VaR calculation (assume normal distribution)
    let portfolioVolatility = 0.15m // Placeholder - would calculate from returns
    let var95 = totalValue * portfolioVolatility * 1.65m // 95% confidence
    
    // Beta calculation (simplified)
    let beta = portfolioReturn / benchmarkReturn |> fun b -> if Double.IsNaN(float b) then 1.0m else b
    
    {
        Portfolio = portfolio
        Timestamp = DateTime.Now
        TotalValue = totalValue
        DailyPnL = portfolioReturn * totalValue
        UnrealizedPnL = unrealizedPnL
        RealizedPnL = 0m // Would track from trade history
        MaxDrawdown = 0m // Would track from equity curve
        CurrentDrawdown = 0m
        VaR95 = var95
        ConcentrationRisk = concentrationRisk
        LeverageRatio = (totalValue - portfolio.Cash) / totalValue
        Beta = beta
        Volatility = portfolioVolatility
        SharpeRatio = 0m // Would calculate from return history
    }

let checkRiskLimits (riskMetrics: RiskMetrics) (riskLimits: RiskLimit) =
    let alerts = ResizeArray<RiskAlert>()
    
    // Check daily loss limit
    if riskMetrics.DailyPnL < -riskLimits.MaxDailyLoss * riskMetrics.TotalValue then
        alerts.Add({
            Id = Guid.NewGuid().ToString()
            AlertType = DailyLossAlert
            Severity = Critical
            Symbol = None
            Message = sprintf "Daily loss limit breached: %.2f%% (limit: %.2f%%)" 
                        (riskMetrics.DailyPnL / riskMetrics.TotalValue * 100m)
                        (riskLimits.MaxDailyLoss * 100m)
            Timestamp = DateTime.Now
            Value = riskMetrics.DailyPnL / riskMetrics.TotalValue
            Threshold = -riskLimits.MaxDailyLoss
        })
    
    // Check concentration risk
    if riskMetrics.ConcentrationRisk > riskLimits.MaxConcentration then
        alerts.Add({
            Id = Guid.NewGuid().ToString()
            AlertType = ConcentrationAlert
            Severity = Warning
            Symbol = None
            Message = sprintf "Concentration risk high: %.1f%% (limit: %.1f%%)" 
                        (riskMetrics.ConcentrationRisk * 100m)
                        (riskLimits.MaxConcentration * 100m)
            Timestamp = DateTime.Now
            Value = riskMetrics.ConcentrationRisk
            Threshold = riskLimits.MaxConcentration
        })
    
    // Check VaR limit
    if riskMetrics.VaR95 > riskLimits.VaRLimit * riskMetrics.TotalValue then
        alerts.Add({
            Id = Guid.NewGuid().ToString()
            AlertType = VolatilityAlert
            Severity = Warning
            Symbol = None
            Message = sprintf "VaR limit exceeded: $%.0f (limit: $%.0f)" 
                        riskMetrics.VaR95
                        (riskLimits.VaRLimit * riskMetrics.TotalValue)
            Timestamp = DateTime.Now
            Value = riskMetrics.VaR95
            Threshold = riskLimits.VaRLimit * riskMetrics.TotalValue
        })
    
    alerts.ToArray()

let generateStopLossOrders (portfolio: Portfolio) (currentPrices: Map<string, decimal>) (volatilityMeasures: Map<string, VolatilityMeasure>) (stopLossType: StopLossType) =
    let stopLossOrders = ResizeArray<Order>()
    
    portfolio.Positions
    |> Map.iter (fun symbol position ->
        match Map.tryFind symbol currentPrices, Map.tryFind symbol volatilityMeasures with
        | Some currentPrice, Some volMeasure ->
            if checkStopLoss position currentPrice stopLossType volMeasure then
                let stopOrder = {
                    Id = Guid.NewGuid().ToString()
                    Symbol = symbol
                    OrderType = Market
                    Side = if position.Quantity > 0 then Sell else Buy
                    Quantity = abs position.Quantity
                    Timestamp = DateTime.Now
                    Price = Some currentPrice
                    Status = Pending
                }
                stopLossOrders.Add(stopOrder)
        | _ -> ()
    )
    
    stopLossOrders.ToArray()

let createRiskLimits () =
    {
        MaxPositionSize = 0.15m      // Max 15% per position
        MaxDailyLoss = 0.02m         // Max 2% daily loss
        MaxDrawdown = 0.10m          // Max 10% drawdown
        MaxConcentration = 0.25m     // Max 25% in any position
        VaRLimit = 0.05m             // 5% VaR limit
    }

type RiskManagedBacktestResult = {
    BacktestResult: BacktestResult
    RiskMetrics: RiskMetrics[]
    RiskAlerts: RiskAlert[]
    StopLossOrders: Order[]
    VolatilityMeasures: VolatilityMeasure[]
}

let runRiskManagedBacktest (config: BacktestConfig) (riskLimits: RiskLimit) (marketData: TimeSeriesData<MarketDataPoint>[]) (indicators: TechnicalIndicatorSet[]) =
    // Calculate volatility measures
    let volatilityMeasures = 
        marketData
        |> Array.map (fun md -> calculateVolatilityMeasure md 20)
        |> Array.map (fun vm -> (vm.Symbol, vm))
        |> Map.ofArray
    
    // Run standard backtest first
    let standardBacktest = runBacktest config marketData indicators
    
    // Apply risk management overlay
    let mutable portfolio = standardBacktest.Portfolio
    let allRiskAlerts = ResizeArray<RiskAlert>()
    let allStopLossOrders = ResizeArray<Order>()
    let allRiskMetrics = ResizeArray<RiskMetrics>()
    
    // Simulate risk management checks at key points
    let sampleTimestamps = 
        marketData.[0].Data
        |> Array.mapi (fun i dp -> (i, dp.Timestamp))
        |> Array.filter (fun (i, _) -> i % 100 = 0) // Sample every 100 periods
        |> Array.map snd
    
    for timestamp in sampleTimestamps do
        // Get current prices
        let currentPrices = 
            marketData
            |> Array.choose (fun ts ->
                ts.Data
                |> Array.tryFind (fun dp -> dp.Timestamp = timestamp)
                |> Option.map (fun dp -> (ts.Symbol, dp.Close))
            )
            |> Map.ofArray
        
        // Calculate risk metrics
        let riskMetrics = calculatePortfolioRisk portfolio currentPrices 0.01m // 1% benchmark return
        allRiskMetrics.Add(riskMetrics)
        
        // Check risk limits
        let alerts = checkRiskLimits riskMetrics riskLimits
        allRiskAlerts.AddRange(alerts)
        
        // Generate stop loss orders
        let stopOrders = generateStopLossOrders portfolio currentPrices volatilityMeasures (Fixed 0.05m) // 5% stop loss
        allStopLossOrders.AddRange(stopOrders)
    
    {
        BacktestResult = standardBacktest
        RiskMetrics = allRiskMetrics.ToArray()
        RiskAlerts = allRiskAlerts.ToArray()
        StopLossOrders = allStopLossOrders.ToArray()
        VolatilityMeasures = volatilityMeasures |> Map.toArray |> Array.map snd
    }