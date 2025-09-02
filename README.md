# Atlas Trading Strategy

A machine learning trading strategy implementation in F#, ported from Python. This project demonstrates systematic trading using quantitative analysis, technical indicators, and ML models.

## 🚀 Quick Start

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Alpaca Account](https://alpaca.markets/) (for real market data)

### Setup

1. **Clone and build the project:**
   ```bash
   git clone <repository-url>
   cd atlas
   dotnet build
   ```

2. **Configure Alpaca API (for real data):**
   ```bash
   # Copy the environment template
   cp .env.example .env
   
   # Edit .env with your Alpaca credentials
   # Get these from https://alpaca.markets/
   ```

3. **Run the application:**
   ```bash
   # Pull real market data and generate report
   dotnet run --project src/Atlas.Cli execute-trading
   
   # Run data infrastructure tests
   dotnet run --project src/Atlas.Cli test
   ```

## 📊 What It Does

The `execute-trading` command pulls real financial data from Alpaca and generates comprehensive reports including:

- **Market Data**: OHLCV data for SPY, QQQ, IWM with price analysis (last 60 days at 5-minute intervals)
- **Data Quality Metrics**: Validation status and completeness checks
- **Price Analysis**: Opening, closing, high, low prices with percentage changes
- **Volume Analysis**: Total, average, and maximum volume statistics

## 🏗️ Project Structure

```
atlas/
├── src/Atlas.Cli/           # Main console application
│   ├── Data.fs             # Core data types and validation
│   ├── AlpacaApi.fs        # API integration for market data
│   ├── Configuration.fs    # Environment and config management
│   ├── DataReporter.fs     # Report generation
│   └── Program.fs          # Main entry point
├── .env.example            # Environment variables template
└── ml-trading-strategy-fsharp-port.md  # Implementation roadmap
```

## 📋 Implementation Progress

### ✅ Phase 0: Project Setup
- [x] F# solution structure with console application
- [x] Package dependencies (FSharp.Data, ML.NET, MathNet.Numerics, etc.)
- [x] Build configuration

### ✅ Phase 1: Data Infrastructure  
- [x] Alpaca API integration for market data
- [x] Time series data structures using F# records
- [x] Data validation and cleaning functions
- [x] Support for OHLCV data with 60-day historical lookback at 5-minute intervals

### 🔄 Phase 2: Feature Engineering (Next)
- [ ] Technical indicators (SMA, EMA, RSI, MACD, Bollinger Bands)
- [ ] Feature scaling and normalization
- [ ] Feature selection utilities

### 🔄 Phase 3: Machine Learning Models
- [ ] ML.NET Random Forest implementation
- [ ] LSTM networks integration
- [ ] Ensemble model development

## 🔧 Configuration

### Environment Variables

Create a `.env` file with your Alpaca credentials:

```bash
# Required for real data access
ALPACA_API_KEY=your-api-key-here
ALPACA_SECRET_KEY=your-secret-key-here
ALPACA_USE_PAPER=true

# Optional configuration
DEFAULT_SYMBOLS=SPY,QQQ,IWM
DATA_START_DAYS_AGO=60
DATA_RESOLUTION_MINUTES=5
```

### Default Settings
- **Symbols**: SPY, QQQ, IWM (major ETFs)
- **Time Range**: Last 60 days  
- **Resolution**: 5-minute bars
- **Data Sources**: OHLCV market data from Alpaca

## 📈 Sample Output

```
🚀 ATLAS TRADING STRATEGY - ALPACA DATA REPORT
Generated: 2024-09-01 19:30:00 UTC

📊 DATA SUMMARY
   Market Instruments: 3
   Total Data Points: 17,280
   Volatility Records: 0
   Economic Indicators: 0

📊 MARKET DATA REPORT - SPY
═══════════════════════════════════════════
📅 Period: 2024-07-02 to 2024-09-01
📊 Data Points: 5,760
💰 Opening: $525.32 | Closing: $548.73
📈 Change: $23.41 (4.46%)
🔍 Validation: ✅ PASSED
```

## 🎯 Next Steps

1. **Set up your Alpaca account** to access real market data
2. **Configure your .env file** with API credentials  
3. **Run the data pull** to verify everything works
4. **Proceed to Phase 2** for technical indicator implementation

## 📚 Resources

- [Alpaca Markets API Documentation](https://alpaca.markets/docs/)
- [F# for Fun and Profit](https://fsharpforfunandprofit.com/)
- [ML.NET Documentation](https://docs.microsoft.com/en-us/dotnet/machine-learning/)

## 🤝 Contributing

This project follows the implementation plan outlined in `ml-trading-strategy-fsharp-port.md`. Contributions should focus on completing the remaining phases while maintaining F# functional programming best practices.