module TradingStrategy.DataTests

open System
open TradingStrategy.Data

let createTestMarketData() : MarketDataPoint[] = [|
    { Timestamp = DateTime(2024, 1, 1, 9, 30, 0); Open = 100m; High = 105m; Low = 98m; Close = 103m; Volume = 1000L }
    { Timestamp = DateTime(2024, 1, 1, 9, 35, 0); Open = 103m; High = 107m; Low = 102m; Close = 106m; Volume = 1200L }
    { Timestamp = DateTime(2024, 1, 1, 9, 40, 0); Open = 106m; High = 108m; Low = 104m; Close = 105m; Volume = 900L }
    { Timestamp = DateTime(2024, 1, 1, 9, 45, 0); Open = 105m; High = 109m; Low = 103m; Close = 108m; Volume = 1500L }
|]

let createInvalidMarketData() : MarketDataPoint[] = [|
    { Timestamp = DateTime(2024, 1, 1, 9, 30, 0); Open = 100m; High = 95m; Low = 98m; Close = 103m; Volume = 1000L } // High < Low
    { Timestamp = DateTime(2024, 1, 1, 9, 35, 0); Open = 0m; High = 107m; Low = 102m; Close = 106m; Volume = 1200L } // Zero open
    { Timestamp = DateTime(2024, 1, 1, 9, 40, 0); Open = 106m; High = 108m; Low = 110m; Close = 105m; Volume = 900L } // Low > High
|]

let createTestTickData() : TickData[] = [|
    { Timestamp = DateTime(2024, 1, 1, 9, 30, 0); Price = 100m; Volume = 100L; BidPrice = Some 99.5m; AskPrice = Some 100.5m }
    { Timestamp = DateTime(2024, 1, 1, 9, 30, 15); Price = 101m; Volume = 200L; BidPrice = Some 100.5m; AskPrice = Some 101.5m }
    { Timestamp = DateTime(2024, 1, 1, 9, 30, 30); Price = 99m; Volume = 150L; BidPrice = Some 98.5m; AskPrice = Some 99.5m }
    { Timestamp = DateTime(2024, 1, 1, 9, 30, 45); Price = 102m; Volume = 300L; BidPrice = Some 101.5m; AskPrice = Some 102.5m }
|]

let runValidationTests() =
    printfn "Running Data Validation Tests..."
    
    // Test 1: Valid data should pass validation
    let validData = createTestMarketData()
    let validationResult = validateMarketData validData
    match validationResult with
    | Valid -> printfn "✓ Valid data test passed"
    | _ -> printfn "✗ Valid data test failed"
    
    // Test 2: Invalid data should be detected
    let invalidData = createInvalidMarketData()
    let invalidValidationResult = validateMarketData invalidData
    match invalidValidationResult with
    | InvalidPrices _ -> printfn "✓ Invalid prices detection test passed"
    | MissingData _ -> printfn "✓ Missing data detection test passed"
    | _ -> printfn "✗ Invalid data detection test failed"
    
    // Test 3: Data cleaning should remove invalid entries
    let cleanedData = cleanMarketData invalidData
    if cleanedData.Length < invalidData.Length then
        printfn "✓ Data cleaning test passed"
    else
        printfn "✗ Data cleaning test failed"

let runTimeSeriesTests() =
    printfn "Running Time Series Tests..."
    
    // Test 1: OHLC calculation from tick data
    let tickData = createTestTickData()
    let ohlcData = calculateOHLC tickData (TimeSpan.FromMinutes(1.0))
    
    if ohlcData.Length = 1 then
        let ohlc = ohlcData.[0]
        if ohlc.Open = 100m && ohlc.High = 102m && ohlc.Low = 99m && ohlc.Close = 102m then
            printfn "✓ OHLC calculation test passed"
        else
            printfn "✗ OHLC calculation test failed - incorrect values"
    else
        printfn "✗ OHLC calculation test failed - incorrect length"
    
    // Test 2: Missing data filling
    let dataWithGaps = [|
        { Timestamp = DateTime(2024, 1, 1, 9, 30, 0); Open = 100m; High = 105m; Low = 98m; Close = 103m; Volume = 1000L }
        { Timestamp = DateTime(2024, 1, 1, 9, 40, 0); Open = 106m; High = 108m; Low = 104m; Close = 105m; Volume = 900L } // 10-minute gap
    |]
    
    let filledData = fillMissingData dataWithGaps (TimeSpan.FromMinutes(5.0))
    if filledData.Length > dataWithGaps.Length then
        printfn "✓ Missing data filling test passed"
    else
        printfn "✗ Missing data filling test failed"

let runDataStructureTests() =
    printfn "Running Data Structure Tests..."
    
    // Test 1: TimeSeriesData creation
    let marketData = createTestMarketData()
    let timeSeriesData = {
        Symbol = "SPY"
        Data = marketData
        StartDate = DateTime(2024, 1, 1)
        EndDate = DateTime(2024, 1, 2)
    }
    
    if timeSeriesData.Symbol = "SPY" && timeSeriesData.Data.Length = 4 then
        printfn "✓ TimeSeriesData creation test passed"
    else
        printfn "✗ TimeSeriesData creation test failed"
    
    // Test 2: MarketDataRequest creation
    let request = {
        Symbol = "AAPL"
        StartDate = DateTime(2024, 1, 1)
        EndDate = DateTime(2024, 1, 31)
        Resolution = TimeSpan.FromMinutes(5.0)
        IncludeAfterHours = false
    }
    
    if request.Symbol = "AAPL" && request.Resolution = TimeSpan.FromMinutes(5.0) then
        printfn "✓ MarketDataRequest creation test passed"
    else
        printfn "✗ MarketDataRequest creation test failed"

let runAllTests() =
    printfn "=== Running Phase 1 Data Infrastructure Tests ==="
    printfn ""
    
    runValidationTests()
    printfn ""
    
    runTimeSeriesTests()
    printfn ""
    
    runDataStructureTests()
    printfn ""
    
    printfn "=== Phase 1 Tests Complete ==="