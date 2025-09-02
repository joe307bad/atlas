module TradingStrategy.TechnicalIndicators

open System
open TradingStrategy.Data

type IndicatorValue = {
    Timestamp: DateTime
    Value: decimal
}

type MACDValue = {
    Timestamp: DateTime
    MACD: decimal
    Signal: decimal
    Histogram: decimal
}

type BollingerBandsValue = {
    Timestamp: DateTime
    Upper: decimal
    Middle: decimal
    Lower: decimal
}

let calculateSMA (data: MarketDataPoint[]) (period: int) =
    if data.Length < period then [||]
    else
        data
        |> Array.windowed period
        |> Array.map (fun window ->
            let timestamp = window.[window.Length - 1].Timestamp
            let average = window |> Array.map (_.Close) |> Array.average
            { Timestamp = timestamp; Value = average }
        )

let calculateEMA (data: MarketDataPoint[]) (period: int) =
    if data.Length < period then [||]
    else
        let multiplier = 2.0m / (decimal period + 1.0m)
        let firstSMA = data.[0..period-1] |> Array.map (_.Close) |> Array.average
        
        let mutable ema = firstSMA
        let results = Array.zeroCreate data.Length
        
        results.[period - 1] <- { Timestamp = data.[period - 1].Timestamp; Value = ema }
        
        for i = period to data.Length - 1 do
            ema <- (data.[i].Close * multiplier) + (ema * (1.0m - multiplier))
            results.[i] <- { Timestamp = data.[i].Timestamp; Value = ema }
        
        results.[period - 1..]

let calculateEMAFromValues (values: IndicatorValue[]) (period: int) =
    if values.Length < period then [||]
    else
        let multiplier = 2.0m / (decimal period + 1.0m)
        let firstSMA = values.[0..period-1] |> Array.map (_.Value) |> Array.average
        
        let mutable ema = firstSMA
        let results = Array.zeroCreate values.Length
        
        results.[period - 1] <- { Timestamp = values.[period - 1].Timestamp; Value = ema }
        
        for i = period to values.Length - 1 do
            ema <- (values.[i].Value * multiplier) + (ema * (1.0m - multiplier))
            results.[i] <- { Timestamp = values.[i].Timestamp; Value = ema }
        
        results.[period - 1..]

let calculateRSI (data: MarketDataPoint[]) (period: int) =
    if data.Length < period + 1 then [||]
    else
        let priceChanges = 
            data
            |> Array.pairwise
            |> Array.map (fun (prev, curr) -> (curr.Timestamp, curr.Close - prev.Close))
        
        let gains = priceChanges |> Array.map (fun (ts, change) -> (ts, max 0m change))
        let losses = priceChanges |> Array.map (fun (ts, change) -> (ts, max 0m -change))
        
        gains
        |> Array.windowed period
        |> Array.mapi (fun i window ->
            let timestamp = window.[window.Length - 1] |> fst
            let avgGain = window |> Array.map snd |> Array.average
            let avgLoss = losses.[i..i + period - 1] |> Array.map snd |> Array.average
            
            let rsi = 
                if avgLoss = 0m then 100m
                else 100m - (100m / (1m + (avgGain / avgLoss)))
            
            { Timestamp = timestamp; Value = rsi }
        )

let calculateMACD (data: MarketDataPoint[]) (fastPeriod: int) (slowPeriod: int) (signalPeriod: int) =
    if data.Length < slowPeriod then [||]
    else
        let fastEMA = calculateEMA data fastPeriod
        let slowEMA = calculateEMA data slowPeriod
        
        // Find the common length (both arrays should start from slowPeriod-1)
        let minLength = min fastEMA.Length slowEMA.Length
        let alignedFastEMA = fastEMA.[fastEMA.Length - minLength..]
        let alignedSlowEMA = slowEMA.[slowEMA.Length - minLength..]
        
        let macdLine = 
            Array.zip alignedFastEMA alignedSlowEMA
            |> Array.map (fun (fast, slow) -> 
                { Timestamp = fast.Timestamp; Value = fast.Value - slow.Value }
            )
        
        if macdLine.Length < signalPeriod then [||]
        else
            let signalLine = calculateEMAFromValues macdLine signalPeriod
            
            // Align MACD line with signal line
            let alignedMACDLine = macdLine.[macdLine.Length - signalLine.Length..]
            
            Array.zip alignedMACDLine signalLine
            |> Array.map (fun (macd, signal) ->
                {
                    Timestamp = macd.Timestamp
                    MACD = macd.Value
                    Signal = signal.Value
                    Histogram = macd.Value - signal.Value
                }
            )

let calculateBollingerBands (data: MarketDataPoint[]) (period: int) (standardDeviations: decimal) =
    if data.Length < period then [||]
    else
        data
        |> Array.windowed period
        |> Array.map (fun window ->
            let timestamp = window.[window.Length - 1].Timestamp
            let prices = window |> Array.map (_.Close)
            let sma = prices |> Array.average
            let variance = prices |> Array.map (fun p -> (p - sma) * (p - sma)) |> Array.average
            let stdDev = sqrt (float variance) |> decimal
            
            {
                Timestamp = timestamp
                Upper = sma + (standardDeviations * stdDev)
                Middle = sma
                Lower = sma - (standardDeviations * stdDev)
            }
        )

let normalizeValues (values: IndicatorValue[]) =
    if values.Length = 0 then [||]
    else
        let minVal = values |> Array.map (_.Value) |> Array.min
        let maxVal = values |> Array.map (_.Value) |> Array.max
        let range = maxVal - minVal
        
        if range = 0m then 
            values |> Array.map (fun iv -> { iv with Value = 0.5m })
        else
            values |> Array.map (fun iv -> 
                { iv with Value = (iv.Value - minVal) / range }
            )

let scaleValues (values: IndicatorValue[]) (targetMin: decimal) (targetMax: decimal) =
    let normalized = normalizeValues values
    let targetRange = targetMax - targetMin
    
    normalized |> Array.map (fun iv ->
        { iv with Value = targetMin + (iv.Value * targetRange) }
    )

type TechnicalIndicatorSet = {
    Symbol: string
    SMA20: IndicatorValue[]
    SMA50: IndicatorValue[]
    EMA12: IndicatorValue[]
    EMA26: IndicatorValue[]
    RSI14: IndicatorValue[]
    MACD: MACDValue[]
    BollingerBands: BollingerBandsValue[]
}

let calculateAllIndicators (timeSeriesData: TimeSeriesData<MarketDataPoint>) =
    let data = timeSeriesData.Data
    
    {
        Symbol = timeSeriesData.Symbol
        SMA20 = calculateSMA data 20
        SMA50 = calculateSMA data 50
        EMA12 = calculateEMA data 12
        EMA26 = calculateEMA data 26
        RSI14 = calculateRSI data 14
        MACD = calculateMACD data 12 26 9
        BollingerBands = calculateBollingerBands data 20 2.0m
    }

type RealtimeIndicatorValues = {
    SMA20: IndicatorValue
    SMA50: IndicatorValue
    EMA12: IndicatorValue
    EMA26: IndicatorValue
    RSI14: IndicatorValue
    BollingerBands: BollingerBandsValue
}

let calculateRealtimeIndicators (historicalData: MarketDataPoint[]) (newDataPoint: MarketDataPoint) =
    let updatedData = Array.append historicalData [| newDataPoint |]
    
    {
        SMA20 = calculateSMA updatedData 20 |> Array.last
        SMA50 = calculateSMA updatedData 50 |> Array.last
        EMA12 = calculateEMA updatedData 12 |> Array.last
        EMA26 = calculateEMA updatedData 26 |> Array.last
        RSI14 = calculateRSI updatedData 14 |> Array.last
        BollingerBands = calculateBollingerBands updatedData 20 2.0m |> Array.last
    }