module TradingStrategy.Data

open System
open MathNet.Numerics.Statistics

type MarketDataPoint = {
    Timestamp: DateTime
    Open: decimal
    High: decimal
    Low: decimal
    Close: decimal
    Volume: int64
}

type TickData = {
    Timestamp: DateTime
    Price: decimal
    Volume: int64
    BidPrice: decimal option
    AskPrice: decimal option
}

type EconomicIndicator = {
    Timestamp: DateTime
    IndicatorType: string
    Value: decimal
    Unit: string
}

type NewsData = {
    Timestamp: DateTime
    Symbol: string
    Headline: string
    SentimentScore: decimal
    Source: string
}

type VolatilityIndex = {
    Timestamp: DateTime
    IndexName: string
    Value: decimal
}

type TimeSeriesData<'T> = {
    Symbol: string
    Data: 'T[]
    StartDate: DateTime
    EndDate: DateTime
}

type MarketDataRequest = {
    Symbol: string
    StartDate: DateTime
    EndDate: DateTime
    Resolution: TimeSpan
    IncludeAfterHours: bool
}

type DataValidationResult = 
    | Valid
    | MissingData of DateTime[]
    | InvalidPrices of (DateTime * string)[]
    | VolumeAnomalies of DateTime[]

let validateMarketData (data: MarketDataPoint[]) : DataValidationResult =
    let missingDataPoints = 
        data
        |> Array.filter (fun point -> 
            point.Open <= 0m || point.High <= 0m || 
            point.Low <= 0m || point.Close <= 0m)
        |> Array.map (_.Timestamp)
    
    let invalidPrices = 
        data
        |> Array.choose (fun point ->
            if point.High < point.Low then
                Some (point.Timestamp, "High price less than low price")
            elif point.High < point.Open || point.High < point.Close then
                Some (point.Timestamp, "High price less than open/close")
            elif point.Low > point.Open || point.Low > point.Close then
                Some (point.Timestamp, "Low price greater than open/close")
            else None)
    
    let volumeAnomalies =
        if data.Length > 1 then
            let volumes = data |> Array.map (fun p -> float p.Volume)
            let mean = volumes |> Array.average
            let stdDev = volumes |> Statistics.StandardDeviation
            let threshold = mean + 3.0 * stdDev
            
            data
            |> Array.filter (fun point -> float point.Volume > threshold)
            |> Array.map (_.Timestamp)
        else [||]
    
    match missingDataPoints, invalidPrices, volumeAnomalies with
    | [||], [||], [||] -> Valid
    | missing, [||], [||] when missing.Length > 0 -> MissingData missing
    | [||], invalid, [||] when invalid.Length > 0 -> InvalidPrices invalid
    | [||], [||], anomalies when anomalies.Length > 0 -> VolumeAnomalies anomalies
    | _, _, _ -> 
        if missingDataPoints.Length > 0 then MissingData missingDataPoints
        elif invalidPrices.Length > 0 then InvalidPrices invalidPrices
        else VolumeAnomalies volumeAnomalies

let cleanMarketData (data: MarketDataPoint[]) : MarketDataPoint[] =
    data
    |> Array.filter (fun point ->
        point.Open > 0m && point.High > 0m && 
        point.Low > 0m && point.Close > 0m &&
        point.High >= point.Low &&
        point.High >= point.Open && point.High >= point.Close &&
        point.Low <= point.Open && point.Low <= point.Close)
    |> Array.sortBy (_.Timestamp)

let fillMissingData (data: MarketDataPoint[]) (resolution: TimeSpan) : MarketDataPoint[] =
    if data.Length < 2 then data
    else
        let sortedData = data |> Array.sortBy (_.Timestamp)
        let result = ResizeArray<MarketDataPoint>()
        
        for i in 0 .. sortedData.Length - 2 do
            result.Add(sortedData.[i])
            let current = sortedData.[i]
            let next = sortedData.[i + 1]
            let timeDiff = next.Timestamp - current.Timestamp
            
            if timeDiff > resolution then
                let missingPeriods = int (timeDiff.Ticks / resolution.Ticks) - 1
                for j in 1 .. missingPeriods do
                    let interpolatedTime = current.Timestamp.Add(TimeSpan(resolution.Ticks * int64 j))
                    let interpolatedPoint = {
                        Timestamp = interpolatedTime
                        Open = current.Close
                        High = current.Close
                        Low = current.Close
                        Close = current.Close
                        Volume = 0L
                    }
                    result.Add(interpolatedPoint)
        
        result.Add(sortedData.[sortedData.Length - 1])
        result.ToArray()

let calculateOHLC (tickData: TickData[]) (timeframe: TimeSpan) : MarketDataPoint[] =
    tickData
    |> Array.sortBy (_.Timestamp)
    |> Array.groupBy (fun tick -> 
        let ticks = tick.Timestamp.Ticks
        DateTime(ticks - (ticks % timeframe.Ticks)))
    |> Array.map (fun (timestamp, ticks) ->
        let prices = ticks |> Array.map (_.Price)
        let volumes = ticks |> Array.map (_.Volume)
        {
            Timestamp = timestamp
            Open = prices.[0]
            High = prices |> Array.max
            Low = prices |> Array.min
            Close = prices.[prices.Length - 1]
            Volume = volumes |> Array.sum
        })
    |> Array.sortBy (_.Timestamp)