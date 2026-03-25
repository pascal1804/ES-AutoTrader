# ES-AutoTrader

Automated trading project for ES Futures.

This project connects TradingView with NinjaTrader using a simple file-based workflow. Trade signals are generated in TradingView (Pine Script), sent to a Python script, and stored as JSON files. A custom C# strategy in NinjaTrader reads these files, processes the signals, and handles order execution including entry, stop loss, and take profit.

The system runs fully automatically and was mainly built to understand how to connect different platforms and automate a trading setup across multiple technologies.

The focus is on the technical implementation rather than the strategy itself. Due to fees and slippage, the strategy is not profitable in live trading.

## Tech Stack

* TradingView (Pine Script) – signal generation and alerts
* Python – receiving alerts and writing structured JSON files
* NinjaTrader (C#) – reading signals, executing trades, and managing positions

## Notes

This is a personal learning project and not intended for live trading.
