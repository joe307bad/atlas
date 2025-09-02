module TradingStrategy.MachineLearning

open System
open Microsoft.ML
open Microsoft.ML.Data
open TradingStrategy.Data
open TradingStrategy.TechnicalIndicators

[<CLIMutable>]
type TradingData = {
    [<LoadColumn(0)>] SMA20: single
    [<LoadColumn(1)>] SMA50: single
    [<LoadColumn(2)>] EMA12: single
    [<LoadColumn(3)>] EMA26: single
    [<LoadColumn(4)>] RSI14: single
    [<LoadColumn(5)>] MACD: single
    [<LoadColumn(6)>] MACDSignal: single
    [<LoadColumn(7)>] MACDHistogram: single
    [<LoadColumn(8)>] BBUpper: single
    [<LoadColumn(9)>] BBMiddle: single
    [<LoadColumn(10)>] BBLower: single
    [<LoadColumn(11)>] Volume: single
    [<LoadColumn(12)>] PriceChange: single
    [<LoadColumn(13)>] Label: bool
}

[<CLIMutable>]
type TradingPrediction = {
    [<ColumnName("PredictedLabel")>] PredictedDirection: bool
    [<ColumnName("Probability")>] Probability: single
    [<ColumnName("Score")>] Score: single
}

type ModelMetrics = {
    Accuracy: double
    AUC: double
    F1Score: double
    LogLoss: double
    PositivePrecision: double
    PositiveRecall: double
    NegativePrecision: double
    NegativeRecall: double
}

let createTrainingData (marketData: TimeSeriesData<MarketDataPoint>) (indicators: TechnicalIndicatorSet) =
    let data = marketData.Data
    
    // Calculate future price movement (label)
    let futureReturns = 
        data 
        |> Array.windowed 6  // Look 5 periods ahead (25 minutes at 5-min intervals)
        |> Array.map (fun window -> 
            let current = window.[0]
            let future = window.[5]
            let returnPct = (future.Close - current.Close) / current.Close
            (current, returnPct > 0.002m) // Profitable if > 0.2% return
        )
    
    // Align indicators with the data points that have labels
    let alignedData = 
        futureReturns
        |> Array.choose (fun (dataPoint, label) ->
            // Find corresponding indicators for this timestamp
            let sma20 = indicators.SMA20 |> Array.tryFind (fun x -> x.Timestamp = dataPoint.Timestamp)
            let sma50 = indicators.SMA50 |> Array.tryFind (fun x -> x.Timestamp = dataPoint.Timestamp)
            let ema12 = indicators.EMA12 |> Array.tryFind (fun x -> x.Timestamp = dataPoint.Timestamp)
            let ema26 = indicators.EMA26 |> Array.tryFind (fun x -> x.Timestamp = dataPoint.Timestamp)
            let rsi14 = indicators.RSI14 |> Array.tryFind (fun x -> x.Timestamp = dataPoint.Timestamp)
            let macd = indicators.MACD |> Array.tryFind (fun x -> x.Timestamp = dataPoint.Timestamp)
            let bb = indicators.BollingerBands |> Array.tryFind (fun x -> x.Timestamp = dataPoint.Timestamp)
            
            match sma20, sma50, ema12, ema26, rsi14, macd, bb with
            | Some s20, Some s50, Some e12, Some e26, Some r14, Some m, Some b ->
                Some {
                    SMA20 = float32 s20.Value
                    SMA50 = float32 s50.Value
                    EMA12 = float32 e12.Value
                    EMA26 = float32 e26.Value
                    RSI14 = float32 r14.Value
                    MACD = float32 m.MACD
                    MACDSignal = float32 m.Signal
                    MACDHistogram = float32 m.Histogram
                    BBUpper = float32 b.Upper
                    BBMiddle = float32 b.Middle
                    BBLower = float32 b.Lower
                    Volume = float32 dataPoint.Volume / 1000.0f  // Scale volume
                    PriceChange = float32 ((dataPoint.Close - dataPoint.Open) / dataPoint.Open)
                    Label = label
                }
            | _ -> None
        )
    
    alignedData

let trainRandomForestModel (trainingData: TradingData[]) =
    let mlContext = MLContext(seed = Nullable(42))
    
    // Load data into ML.NET
    let dataView = mlContext.Data.LoadFromEnumerable(trainingData)
    
    // Split into train/test
    let trainTestSplit = mlContext.Data.TrainTestSplit(dataView, testFraction = 0.2)
    let trainData = trainTestSplit.TrainSet
    let testData = trainTestSplit.TestSet
    
    // Create pipeline with proper ML.NET syntax
    let featureColumns = [| "SMA20"; "SMA50"; "EMA12"; "EMA26"; "RSI14"; "MACD"; "MACDSignal"; "MACDHistogram"; "BBUpper"; "BBMiddle"; "BBLower"; "Volume"; "PriceChange" |]
    let pipeline = 
        mlContext.Transforms.Concatenate("Features", featureColumns)
            .Append(mlContext.BinaryClassification.Trainers.LightGbm())
    
    // Train the model
    printfn "🔄 Training Random Forest model..."
    let model = pipeline.Fit(trainData)
    
    // Evaluate the model
    let predictions = model.Transform(testData)
    let metrics = mlContext.BinaryClassification.Evaluate(predictions, labelColumnName = "Label")
    
    let modelMetrics = {
        Accuracy = metrics.Accuracy
        AUC = metrics.AreaUnderRocCurve
        F1Score = metrics.F1Score
        LogLoss = metrics.LogLoss
        PositivePrecision = metrics.PositivePrecision
        PositiveRecall = metrics.PositiveRecall
        NegativePrecision = metrics.NegativePrecision
        NegativeRecall = metrics.NegativeRecall
    }
    
    (model, modelMetrics, mlContext)

let trainLinearModel (trainingData: TradingData[]) =
    let mlContext = MLContext(seed = Nullable(42))
    
    let dataView = mlContext.Data.LoadFromEnumerable(trainingData)
    let trainTestSplit = mlContext.Data.TrainTestSplit(dataView, testFraction = 0.2)
    
    let featureColumns2 = [| "SMA20"; "EMA12"; "RSI14"; "MACD" |]
    let pipeline = 
        mlContext.Transforms.Concatenate("Features", featureColumns2)
            .Append(mlContext.BinaryClassification.Trainers.SdcaLogisticRegression())
    
    printfn "🔄 Training Linear model (LSTM approximation)..."
    let model = pipeline.Fit(trainTestSplit.TrainSet)
    
    let predictions = model.Transform(trainTestSplit.TestSet)
    let metrics = mlContext.BinaryClassification.Evaluate(predictions, labelColumnName = "Label")
    
    let modelMetrics = {
        Accuracy = metrics.Accuracy
        AUC = metrics.AreaUnderRocCurve
        F1Score = metrics.F1Score
        LogLoss = metrics.LogLoss
        PositivePrecision = metrics.PositivePrecision
        PositiveRecall = metrics.PositiveRecall
        NegativePrecision = metrics.NegativePrecision
        NegativeRecall = metrics.NegativeRecall
    }
    
    (model, modelMetrics, mlContext)

type EnsembleModel = {
    RandomForestModel: ITransformer
    LinearModel: ITransformer
    RandomForestContext: MLContext
    LinearContext: MLContext
    RandomForestWeight: double
    LinearWeight: double
}

let createEnsembleModel (rfModel: ITransformer) (linearModel: ITransformer) (rfContext: MLContext) (linearContext: MLContext) (rfMetrics: ModelMetrics) (linearMetrics: ModelMetrics) =
    // Weight models based on their AUC scores
    let totalAUC = rfMetrics.AUC + linearMetrics.AUC
    let rfWeight = rfMetrics.AUC / totalAUC
    let linearWeight = linearMetrics.AUC / totalAUC
    
    {
        RandomForestModel = rfModel
        LinearModel = linearModel
        RandomForestContext = rfContext
        LinearContext = linearContext
        RandomForestWeight = rfWeight
        LinearWeight = linearWeight
    }

let makePrediction (ensemble: EnsembleModel) (features: TradingData) =
    // Create prediction engines
    let rfEngine = ensemble.RandomForestContext.Model.CreatePredictionEngine<TradingData, TradingPrediction>(ensemble.RandomForestModel)
    let linearEngine = ensemble.LinearContext.Model.CreatePredictionEngine<TradingData, TradingPrediction>(ensemble.LinearModel)
    
    // Get predictions from both models
    let rfPrediction = rfEngine.Predict(features)
    let linearPrediction = linearEngine.Predict(features)
    
    // Ensemble the predictions (weighted average of probabilities)
    let ensembleProbability = 
        (double rfPrediction.Probability * ensemble.RandomForestWeight) +
        (double linearPrediction.Probability * ensemble.LinearWeight)
    
    let ensembleDecision = ensembleProbability > 0.5
    
    {
        PredictedDirection = ensembleDecision
        Probability = float32 ensembleProbability
        Score = float32 (ensembleProbability - 0.5) * 2.0f  // Convert to [-1, 1] range
    }

type MLModelSet = {
    Symbol: string
    RandomForestModel: ITransformer * ModelMetrics * MLContext
    LinearModel: ITransformer * ModelMetrics * MLContext
    EnsembleModel: EnsembleModel
    TrainingDataSize: int
}

let trainAllModels (marketData: TimeSeriesData<MarketDataPoint>) (indicators: TechnicalIndicatorSet) =
    printfn "📊 Preparing training data for %s..." indicators.Symbol
    let trainingData = createTrainingData marketData indicators
    
    if trainingData.Length < 100 then
        printfn "⚠️ Insufficient training data for %s (%d samples). Need at least 100." indicators.Symbol trainingData.Length
        None
    else
        printfn "✅ Created %d training samples for %s" trainingData.Length indicators.Symbol
        
        // Train Random Forest
        let (rfModel, rfMetrics, rfContext) = trainRandomForestModel trainingData
        
        // Train Linear model
        let (linearModel, linearMetrics, linearContext) = trainLinearModel trainingData
        
        // Create ensemble
        let ensemble = createEnsembleModel rfModel linearModel rfContext linearContext rfMetrics linearMetrics
        
        Some {
            Symbol = indicators.Symbol
            RandomForestModel = (rfModel, rfMetrics, rfContext)
            LinearModel = (linearModel, linearMetrics, linearContext)
            EnsembleModel = ensemble
            TrainingDataSize = trainingData.Length
        }