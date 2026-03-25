#region Using declarations
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class FileWatcherJsonStrategy : Strategy
    {
        private FileSystemWatcher fsw;
        private TimeZoneInfo estZone;

        // Zum Verhindern mehrfacher Flatten-Aufrufe
        private bool flattenOrderSubmitted = false; 

        // Unmanaged-Order-Referenzen
        private Order entryOrder  = null;
        private Order stopOrder   = null;
        private Order targetOrder = null;

        // JSON-Felder (werden durch das Parsen gesetzt)
        private string  jsonDirection = "";
        private double  jsonPrice     = 0;
        private int     jsonQuantity  = 0;
        private double  jsonTakeProfit= 0;
        private double  jsonStopLoss  = 0;

        // Tickgröße (wird für die Umrechnung verwendet)
        private double tickSize = 0.25;  // Beispielwert: 0.25

        // *** Fester Pfad zum orders.json ***
        public string SignalFilePath { get; set; } = @"C:\Users\admin\Desktop\Tradingbot\orders.json";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description        = "Unmanaged-Strategie. Überwacht C:\\Users\\admin\\Desktop\\Tradingbot\\orders.json und platziert Orders aus JSON.";
                Name               = "FileWatcherJsonStrategy";
                IsUnmanaged        = true;
                Calculate          = Calculate.OnEachTick;
                EntriesPerDirection= 1;
                EntryHandling      = EntryHandling.UniqueEntries;
            }
            else if (State == State.DataLoaded)
            {
                // Eastern Standard Time laden
                try
                {
                    estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                }
                catch (Exception exTz)
                {
                    Print("[Init] Zeitzone 'Eastern Standard Time' nicht gefunden, nutze Local. " + exTz.Message);
                    estZone = TimeZoneInfo.Local;
                }

                Print("[Init] SignalFilePath='" + SignalFilePath + "'");
                try
                {
                    fsw = new FileSystemWatcher();
                    string dir = Path.GetDirectoryName(SignalFilePath);
                    string file = Path.GetFileName(SignalFilePath);
                    Print("[Init] FileSystemWatcher: dir=" + dir + ", file=" + file);

                    if (!Directory.Exists(dir))
                    {
                        Print("[WARNING] Ordner existiert nicht: " + dir);
                    }
                    else
                    {
                        fsw.Path = dir;
                        fsw.Filter = file;
                        fsw.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime;
                        fsw.Changed += OnFileChanged;
                        fsw.Created += OnFileChanged;
                        fsw.Renamed += OnFileRenamed;
                        fsw.EnableRaisingEvents = false; 
                        Print("[Init] FileSystemWatcher eingerichtet fuer " + SignalFilePath);
                    }
                }
                catch (Exception ex)
                {
                    Print("[Init] Fehler beim Einrichten des FileSystemWatchers: " + ex.ToString());
                }
            }
            else if (State == State.Realtime)
            {
                if (fsw != null)
                {
                    fsw.EnableRaisingEvents = true;
                    Print("[Realtime] FileSystemWatcher aktiviert.");
                }
            }
            else if (State == State.Terminated)
            {
                if (fsw != null)
                {
                    fsw.EnableRaisingEvents = false;
                    fsw.Changed -= OnFileChanged;
                    fsw.Created -= OnFileChanged;
                    fsw.Renamed -= OnFileRenamed;
                    fsw.Dispose();
                    fsw = null;
                    Print("[Terminated] FileSystemWatcher freigegeben.");
                }
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            Print("[OnFileChanged] File=" + e.FullPath + ", ChangeType=" + e.ChangeType);
            TriggerCustomEvent(o => ReadAndProcessJson(), null);
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            Print("[OnFileRenamed] OldName=" + e.OldFullPath + ", NewName=" + e.FullPath);
            TriggerCustomEvent(o => ReadAndProcessJson(), null);
        }

        private void ReadAndProcessJson()
        {
            try
            {
                if (!File.Exists(SignalFilePath))
                {
                    Print("[ReadAndProcessJson] Datei existiert nicht: " + SignalFilePath);
                    return;
                }

                string content = File.ReadAllText(SignalFilePath);
                Print("[ReadAndProcessJson] Gelesener Inhalt:\n" + content);

                if (!TryParseJson(content))
                {
                    Print("[ReadAndProcessJson] Konnte JSON-Werte nicht extrahieren.");
                    return;
                }

                if (Position.MarketPosition != MarketPosition.Flat 
                    || (entryOrder != null && entryOrder.OrderState != OrderState.Filled 
                        && entryOrder.OrderState != OrderState.Cancelled && entryOrder.OrderState != OrderState.Rejected))
                {
                    Print("[ReadAndProcessJson] Position/Order aktiv -> Abbruch.");
                    return;
                }

                OrderAction entryAction = (jsonDirection.Equals("buy", StringComparison.OrdinalIgnoreCase))
                                          ? OrderAction.Buy : OrderAction.SellShort;
                
                Print(string.Format("[ReadAndProcessJson] Platziere Entry: {0} {1} Kontr. @Limit={2}", 
                                    entryAction, jsonQuantity, jsonPrice));
                entryOrder = SubmitOrderUnmanaged(0, entryAction, OrderType.Limit, jsonQuantity, jsonPrice, 0, "", "Entry");
                if (entryOrder == null)
                    Print("[ReadAndProcessJson] FEHLER: EntryOrder ist null. Check Parameter.");
            }
            catch (Exception ex)
            {
                Print("[ReadAndProcessJson] Fehler: " + ex.ToString());
            }
        }

        private bool TryParseJson(string content)
        {
            try
            {
                Match mAction = Regex.Match(content, "\"action\"\\s*:\\s*\"(buy|sell)\"", RegexOptions.IgnoreCase);
                if (!mAction.Success) return false;
                jsonDirection = mAction.Groups[1].Value;

                Match mPrice = Regex.Match(content, "\"limit_price\"\\s*:\\s*([-0-9.]+)", RegexOptions.IgnoreCase);
                if (!mPrice.Success) return false;
                jsonPrice = double.Parse(mPrice.Groups[1].Value, CultureInfo.InvariantCulture);

                Match mQty = Regex.Match(content, "\"qty\"\\s*:\\s*([0-9]+)", RegexOptions.IgnoreCase);
                if (!mQty.Success) return false;
                jsonQuantity = int.Parse(mQty.Groups[1].Value);

                Match mTp = Regex.Match(content, "\"take_profit\"\\s*:\\s*([-0-9.]+)", RegexOptions.IgnoreCase);
                if (!mTp.Success) return false;
                jsonTakeProfit = double.Parse(mTp.Groups[1].Value, CultureInfo.InvariantCulture);

                Match mSl = Regex.Match(content, "\"stop_loss\"\\s*:\\s*([-0-9.]+)", RegexOptions.IgnoreCase);
                if (!mSl.Success) return false;
                jsonStopLoss = double.Parse(mSl.Groups[1].Value, CultureInfo.InvariantCulture);

                Print(string.Format("[TryParseJson] OK. action={0}, limit_price={1}, qty={2}, take_profit={3}, stop_loss={4}",
                    jsonDirection, jsonPrice, jsonQuantity, jsonTakeProfit, jsonStopLoss));
                return true;
            }
            catch
            {
                return false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (State != State.Realtime)
                return;

            if (!File.Exists(SignalFilePath))
            {
                Print("[OnBarUpdate] Datei nicht vorhanden: " + SignalFilePath);
            }

            DateTime currentEst = TimeZoneInfo.ConvertTime(Time[0], TimeZoneInfo.Local, estZone);
            TimeSpan estTime = currentEst.TimeOfDay;
            Print(string.Format("[OnBarUpdate] Chart-Zeit: {0}, EST: {1}", Time[0], estTime));

            if (Position.MarketPosition != MarketPosition.Flat &&
                estTime >= new TimeSpan(15, 50, 0) &&
                estTime < new TimeSpan(16, 15, 0))
            {
                if (stopOrder != null && stopOrder.OrderState == OrderState.Working)
                {
                    Print("[OnBarUpdate] 15:50 EST -> StopLoss wird storniert.");
                    CancelOrder(stopOrder);
                }
                if (targetOrder != null && targetOrder.OrderState == OrderState.Working)
                {
                    Print("[OnBarUpdate] 15:50 EST -> TakeProfit wird storniert.");
                    CancelOrder(targetOrder);
                }
            }

            if (Position.MarketPosition != MarketPosition.Flat && estTime >= new TimeSpan(16, 15, 0))
            {
                if (!flattenOrderSubmitted)
                {
                    Print("[OnBarUpdate] 16:15 EST -> Position wird abgeflattet.");
                    if (Position.MarketPosition == MarketPosition.Long)
                        SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, Position.Quantity, 0, 0, "", "EOD_Flat");
                    else
                        SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, Position.Quantity, 0, 0, "", "EOD_Flat");
                    flattenOrderSubmitted = true;
                }
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity,
                                             int filled, double averageFillPrice, OrderState orderState,
                                             DateTime time, ErrorCode error, string nativeError)
        {
            if (error != ErrorCode.NoError)
                Print(string.Format("[OnOrderUpdate] Fehler: {0}, {1}", error, nativeError));

            if (entryOrder != null && order.OrderId == entryOrder.OrderId)
            {
                switch (orderState)
                {
                    case OrderState.Filled:
                        Print("[OnOrderUpdate] Entry gefüllt: " + filled + " @ " + averageFillPrice);
                        string oco = "OCO_" + Guid.NewGuid().ToString("N");
                        double fillPx = averageFillPrice;
                        if (entryOrder.OrderAction == OrderAction.Buy)
                        {
                            // LONG: TP = fillPrice + (jsonTakeProfit * tickSize), SL = fillPrice - (jsonStopLoss * tickSize)
                            double tpLong = fillPx + (jsonTakeProfit * tickSize);
                            double slLong = fillPx + (jsonStopLoss * tickSize);
                            targetOrder = SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Limit, filled, tpLong, 0, oco, "TP");
                            stopOrder   = SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.StopMarket, filled, 0, slLong, oco, "SL");
                            Print(string.Format("[OnOrderUpdate] LONG: SL={0}, TP={1}, OCO={2}", slLong, tpLong, oco));
                        }
                        else
                        {
                            // SHORT: TP = fillPrice - (jsonTakeProfit * tickSize), SL = fillPrice + (jsonStopLoss * tickSize)
                            double tpShort = fillPx + (jsonTakeProfit * tickSize);
                            double slShort = fillPx + (jsonStopLoss * tickSize);
                            targetOrder = SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Limit, filled, tpShort, 0, oco, "TP");
                            stopOrder   = SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.StopMarket, filled, 0, slShort, oco, "SL");
                            Print(string.Format("[OnOrderUpdate] SHORT: SL={0}, TP={1}, OCO={2}", slShort, tpShort, oco));
                        }
                        flattenOrderSubmitted = false;
                        break;

                    case OrderState.PartFilled:
                        Print(string.Format("[OnOrderUpdate] Entry Teilfüllung: {0}/{1}", filled, quantity));
                        CancelOrder(order);
                        break;

                    case OrderState.Cancelled:
                        if (filled > 0)
                        {
                            Print("[OnOrderUpdate] Entry abgebrochen nach Teilfüllung -> Flatten " + filled);
                            if (entryOrder.OrderAction == OrderAction.Buy && Position.MarketPosition == MarketPosition.Long)
                                SubmitOrderUnmanaged(0, OrderAction.Sell, OrderType.Market, filled, 0, 0, "", "PartialFlat");
                            else if (entryOrder.OrderAction == OrderAction.SellShort && Position.MarketPosition == MarketPosition.Short)
                                SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, filled, 0, 0, "", "PartialFlat");
                        }
                        else
                            Print("[OnOrderUpdate] Entry cancelled ohne Fill.");
                        entryOrder = null;
                        break;

                    case OrderState.Rejected:
                        Print("[OnOrderUpdate] Entry REJECTED!");
                        entryOrder = null;
                        break;
                }
            }

            if (stopOrder != null && order.OrderId == stopOrder.OrderId)
            {
                if (orderState == OrderState.Filled)
                {
                    Print("[OnOrderUpdate] StopLoss gefüllt: Position ausgestoppt.");
                    entryOrder = null;
                    stopOrder = null;
                    targetOrder = null;
                }
                else if (orderState == OrderState.Cancelled)
                {
                    Print("[OnOrderUpdate] StopLoss Cancelled");
                    stopOrder = null;
                }
                else if (orderState == OrderState.Rejected)
                {
                    Print("[OnOrderUpdate] StopLoss Rejected");
                    stopOrder = null;
                }
            }

            if (targetOrder != null && order.OrderId == targetOrder.OrderId)
            {
                if (orderState == OrderState.Filled)
                {
                    Print("[OnOrderUpdate] TakeProfit gefüllt: Gewinnziel erreicht.");
                    entryOrder = null;
                    stopOrder = null;
                    targetOrder = null;
                }
                else if (orderState == OrderState.Cancelled)
                {
                    Print("[OnOrderUpdate] TakeProfit Cancelled");
                    targetOrder = null;
                }
                else if (orderState == OrderState.Rejected)
                {
                    Print("[OnOrderUpdate] TakeProfit Rejected");
                    targetOrder = null;
                }
            }

            if (order.Name == "EOD_Flat" || order.Name == "PartialFlat")
            {
                if (orderState == OrderState.Filled)
                {
                    Print("[OnOrderUpdate] Flatten-Order '" + order.Name + "' ausgeführt => Position geschlossen.");
                    entryOrder = null;
                    stopOrder = null;
                    targetOrder = null;
                }
                else if (orderState == OrderState.Cancelled)
                {
                    Print("[OnOrderUpdate] Flatten-Order '" + order.Name + "' Cancelled.");
                }
                else if (orderState == OrderState.Rejected)
                {
                    Print("[OnOrderUpdate] Flatten-Order '" + order.Name + "' Rejected! " + nativeError);
                }
            }
        }
    }
}
