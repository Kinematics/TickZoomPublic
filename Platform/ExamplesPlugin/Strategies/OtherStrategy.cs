using System;
using System.Drawing;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class OtherStrategy : Strategy
    {
        #region Fields
        IndicatorCommon bidLine;
        IndicatorCommon askLine;
        IndicatorCommon position;
        IndicatorCommon averagePrice;
        private double indifferencePrice;
        bool isFirstTick = true;
        double minimumTick;
        private int increaseSpreadInTicks = 3;
        private int reduceSpreadInTicks = 3;
        private int closeProfitInTicks = 20;
        private double reduceSpread;
        private double increaseSpread;
        private int lotSize = 1000;
        double ask, marketAsk;
        double bid, marketBid;
        private double midPoint;
        private ActiveList<LocalFill> fills = new ActiveList<LocalFill>();
        private bool isVisible = false;
        private long totalVolume = 0;
        private int maxLots = int.MaxValue;
        private int lastSize = 0;
        private int buySize = 1;
        private int sellSize = 1;
        private IndicatorCommon retraceLine;
        private IndicatorCommon maxExcursion;

        private bool enableSizing = true;
        private bool enablePegging = true;
        private bool throttleIncreasing = false;
        private bool filterIndifference = false;

        private readonly long commission = -0.0000195D.ToLong();
        private double lastMidPoint;
        private int startSizingLots = 30;
        private int retraceErrorMarginInTicks = 25;
        private int sequentialIncreaseCount;
        private double lastMarketBid;
        private double lastMarketAsk;
        #endregion

        public OtherStrategy()
        {
            RequestUpdate(Intervals.Second1);
        }

        public override void OnInitialize()
        {
            Performance.Equity.GraphEquity = true; // Graphed by portfolio.
            Performance.GraphTrades = isVisible;

            minimumTick = Data.SymbolInfo.MinimumTick;
            increaseSpread = increaseSpreadInTicks * minimumTick;
            reduceSpread = reduceSpreadInTicks * minimumTick;

            askLine = Formula.Indicator();
            askLine.Name = "Ask";
            askLine.Drawing.IsVisible = isVisible;

            bidLine = Formula.Indicator();
            bidLine.Name = "Bid";
            bidLine.Drawing.IsVisible = isVisible;

            averagePrice = Formula.Indicator();
            averagePrice.Name = "BE";
            averagePrice.Drawing.IsVisible = isVisible;
            averagePrice.Drawing.Color = Color.Black;

            retraceLine = Formula.Indicator();
            retraceLine.Name = "Retrace";
            retraceLine.Drawing.IsVisible = enableSizing && isVisible;
            retraceLine.Drawing.Color = Color.Magenta;

            maxExcursion = Formula.Indicator();
            maxExcursion.Name = "Excursion";
            maxExcursion.Drawing.IsVisible = enableSizing && isVisible;
            maxExcursion.Drawing.Color = Color.Magenta;

            position = Formula.Indicator();
            position.Name = "Position";
            position.Drawing.PaneType = PaneType.Secondary;
            position.Drawing.GroupName = "Position";
            position.Drawing.IsVisible = isVisible;

        }

        private void UpdateIndicators(Tick tick)
        {
            var comboTrades = Performance.ComboTrades;
            if (comboTrades.Count > 0)
            {
                var comboTrade = comboTrades.Tail;
                indifferencePrice = CalcIndifferencePrice(comboTrade);
                if( filterIndifference)
                {
                    var avgDivergence = Math.Abs(tick.Bid - indifferencePrice) / minimumTick;
                    if (avgDivergence > 150 || Position.IsFlat)
                    {
                        averagePrice[0] = double.NaN;
                    }
                    else
                    {
                        averagePrice[0] = indifferencePrice;
                    }
                }
                else
                {
                    averagePrice[0] = indifferencePrice;
                }
            }
            else
            {
                indifferencePrice = (tick.Ask + tick.Bid)/2;
                averagePrice[0] = indifferencePrice;
            }

            if( comboTrades.Count > 0 && !comboTrades.Tail.Completed)
            {
                var lots = Position.Size/lotSize;
                var currentTrade = comboTrades.Tail;
                var retracePercent = lots < startSizingLots ? 0.50 : lots < 100 ? 0.40 : lots < 200 ? 0.30 : 0.20;

                if (double.IsNaN(retraceLine[0]))
                {
                    retraceLine[0] = midPoint;
                }

                if (Position.IsLong)
                {
                    var retraceAmount = (currentTrade.EntryPrice - maxExcursion[0]) * retracePercent;
                    var retraceLevel = maxExcursion[0] + retraceAmount;
                    if (retraceLevel < retraceLine[0])
                    {
                        retraceLine[0] = retraceLevel;
                    }
                }
                if (Position.IsShort)
                {
                    var retraceAmount = (currentTrade.EntryPrice - maxExcursion[0])*retracePercent;
                    var retraceLevel = maxExcursion[0] + retraceAmount;
                    if (retraceLevel > retraceLine[0])
                    {
                        retraceLine[0] = retraceLevel;
                    }
                }

            } 
            else
            {
                retraceLine[0] = double.NaN;
            }

            if (bidLine.Count > 0)
            {
                position[0] = Position.Current / lotSize;
            }
        }

        public override bool OnProcessTick(Tick tick)
        {
            if (!tick.IsQuote)
            {
                throw new InvalidOperationException("This strategy requires bid/ask quote data to operate.");
            }

            Orders.SetAutoCancel();

            CalcMarketPrices(tick);

            UpdateIndicators(tick);

            if( enablePegging) HandlePegging(tick);

            if( enableSizing) PerformSizing(tick);

            ProcessSideways(tick);
            return true;
        }

        private void PerformSizing(Tick tick)
        {
            var lots = Position.Size/lotSize;
            sellSize = 1;
            buySize = 1;

            if( Performance.ComboTrades.Count <= 0) return;

            var size = Math.Max(2, lots / 5);
            var currentTrade = Performance.ComboTrades.Tail;
            if (Position.IsShort)
            {
                var buyIndifference = CalcIndifferenceUpdate(indifferencePrice, Position.Size, ask, -buySize * lotSize);
                var retraceDelta = retraceLine[0] - buyIndifference;
                if (retraceDelta > retraceErrorMarginInTicks * minimumTick)
                {
                    buySize = 0;
                }

                if (lots <= 30) return;

                if (ask > maxExcursion[0] &&
                    indifferencePrice < retraceLine[0])
                {
                    sellSize = CalcAdjustmentSize(indifferencePrice, Position.Size, retraceLine[0] + retraceErrorMarginInTicks * minimumTick, ask);
                }
            }

            if (Position.IsLong)
            {
                var sellIndifference = CalcIndifferenceUpdate(indifferencePrice, Position.Size, ask, -buySize * lotSize);
                var retraceDelta = sellIndifference - retraceLine[0];
                if (retraceDelta > retraceErrorMarginInTicks * minimumTick)
                {
                    sellSize = 0;
                }

                if (lots <= 30) return;

                if (bid < maxExcursion[0] &&
                    indifferencePrice > retraceLine[0])
                {
                    buySize = CalcAdjustmentSize(indifferencePrice, Position.Size, retraceLine[0] - retraceErrorMarginInTicks * minimumTick, bid);
                }
            }

        }

        private void HandlePegging(Tick tick)
        {
            if( marketAsk < bid || marketBid > ask)
            {
                SetupBidAsk(midPoint);
            }
            if (marketAsk < lastMarketAsk)
            {
                lastMarketAsk = marketAsk;
                if (marketAsk < ask - increaseSpread && midPoint > lastMidPoint)
                {
                    SetupAsk(midPoint);
                }
            }

            if (marketBid > lastMarketBid)
            {
                lastMarketBid = marketBid;
                if (marketBid > bid + increaseSpread && midPoint < lastMidPoint)
                {
                    SetupBid(midPoint);
                }
            }
        }

        private void ProcessSideways(Tick tick)
        {
            if (Position.IsFlat)
            {
                OnProcessFlat(tick);
            }
            else if (Position.IsLong)
            {
                OnProcessLong(tick);
            }
            else if (Position.IsShort)
            {
                OnProcessShort(tick);
            }

        }

        private void OnProcessFlat(Tick tick)
        {
            if (isFirstTick)
            {
                isFirstTick = false;
                SetFlatBidAsk();
            }
            var comboTrades = Performance.ComboTrades;
            if (comboTrades.Count == 0 || comboTrades.Tail.Completed)
            {
                if( buySize > 0)
                {
                    Orders.Enter.ActiveNow.BuyLimit(bid, buySize * lotSize);
                }

                if( sellSize > 0)
                {
                    Orders.Enter.ActiveNow.SellLimit(ask, sellSize * lotSize);
                }
            }
            else
            {
                if( buySize > 0)
                {
                    Orders.Change.ActiveNow.BuyLimit(bid, buySize * lotSize);
                }
                if( sellSize > 0)
                {
                    Orders.Change.ActiveNow.SellLimit(ask, sellSize * lotSize);
                }
            }
        }

        private double CalcIndifferencePrice(TransactionPairBinary comboTrade)
        {
            var size = Math.Abs(comboTrade.CurrentPosition);
            if (size == 0)
            {
                return midPoint;
            }
            var sign = -Math.Sign(comboTrade.CurrentPosition);
            var openPoints = comboTrade.AverageEntryPrice.ToLong() * size;
            var closedPoints = comboTrade.ClosedPoints.ToLong() * sign;
            var grossProfit = openPoints + closedPoints;
            var transaction = 0; // size * commission * sign;
            var expectedTransaction = 0; // size * commission * sign;
            var result = (grossProfit - transaction - expectedTransaction) / size;
            result = ((result + 5000) / 10000) * 10000;
            return result.ToDouble();
        }

        private int CalcAdjustmentSize(double indifference, int size, double desiredIndifference, double currentPrice)
        {
            var lots = size/lotSize;
            var delta = Math.Abs(desiredIndifference - currentPrice);
            if (delta < minimumTick || lots > 100000)
            {
                return 1;
            }
            var result = (size*indifference - size*desiredIndifference)/(delta);
            result = Math.Abs(result);
            if( result >= int.MaxValue)
            {
                System.Diagnostics.Debugger.Break();
            }
            return Math.Max(1,(int) (result / lotSize));
        }

        private double CalcIndifferenceUpdate(double indifference, int size, double currentPrice, int changeSize)
        {
            var result = (size*indifference + changeSize*currentPrice)/(size + changeSize);
            return result;
        }

        private void OnProcessLong(Tick tick)
        {
            var lots = Position.Size / lotSize;
            if( buySize > 0)
            {
                Orders.Change.ActiveNow.BuyLimit(bid, buySize * lotSize);
            }
            if (lots == 1 && sellSize > 0)
            {
                Orders.Reverse.ActiveNow.SellLimit(ask, sellSize * lotSize);
            }
            else if( sellSize > 0)
            {
                Orders.Change.ActiveNow.SellLimit(ask, sellSize * lotSize);
            }
            Orders.Exit.ActiveNow.SellLimit(indifferencePrice + closeProfitInTicks * minimumTick);
        }

        private void OnProcessShort(Tick tick)
        {
            var lots = Position.Size / lotSize;
            if( sellSize > 0)
            {
                Orders.Change.ActiveNow.SellLimit(ask, sellSize * lotSize);
            }
            if (lots == 1 && buySize > 0)
            {
                Orders.Reverse.ActiveNow.BuyLimit(bid, buySize * lotSize);
            }
            else if( buySize > 0) 
            {
                Orders.Change.ActiveNow.BuyLimit(bid, buySize * lotSize);
            }
            Orders.Exit.ActiveNow.BuyLimit(indifferencePrice - closeProfitInTicks * minimumTick);
        }

        public override void OnEndHistorical()
        {
            if( Performance.ComboTrades.Count > 0)
            {
                var lastTrade = Performance.ComboTrades.Tail;
                totalVolume += lastTrade.Volume;
            }
            Log.Notice("Total volume was " + totalVolume + ". With commission paid of " + ((totalVolume / 1000) * 0.02D));
        }

        public class LocalFill
        {
            public int Size;
            public double Price;
            public TimeStamp Time;
            public LocalFill(LogicalFill fill)
            {
                Size = Math.Abs(fill.Position);
                Price = fill.Price;
                Time = fill.Time;
            }
            public LocalFill(int size, double price, TimeStamp time)
            {
                Size = size;
                Price = price;
                Time = time;
            }
            public override string ToString()
            {
                return Size + " at " + Price;
            }
        }

        private void SetupBidAsk(double price)
        {
            if( Performance.ComboTrades.Count > 0)
            {
                indifferencePrice = CalcIndifferencePrice(Performance.ComboTrades.Tail);
            }
            else
            {
                indifferencePrice = price;
            }
            lastMidPoint = price;
            SetupAsk(price);
            SetupBid(price);
        }

        private void CalcMarketPrices(Tick tick)
        {
            // Calculate market prics.
            marketAsk = Math.Max(tick.Ask, tick.Bid);
            marketBid = Math.Min(tick.Ask, tick.Bid);
            midPoint = (tick.Ask + tick.Bid) / 2;
        }

        private void SetupAsk(double price)
        {
            var sequentialAdjustment = throttleIncreasing ? sequentialIncreaseCount*10*minimumTick : 0;
            var myAsk = Position.IsLong ? price + reduceSpread : price + increaseSpread + sequentialAdjustment;
            ask = Math.Max(myAsk, marketAsk);
            askLine[0] = ask;
        }

        private void SetupBid(double price)
        {
            var sequentialAdjustment = throttleIncreasing ? sequentialIncreaseCount*10*minimumTick : 0;
            var myBid = Position.IsLong ? (price - increaseSpread) - sequentialAdjustment : price - reduceSpread;
            bid = Math.Min(myBid, marketBid);
            bidLine[0] = bid;
        }

        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            lastSize = Math.Abs(comboTrade.CurrentPosition);
            fills.AddFirst(new LocalFill(fill));
            SetupBidAsk(fill.Price);
            maxExcursion[0] = fill.Price;
            sequentialIncreaseCount = 0;
            lastMarketAsk = marketAsk;
            lastMarketBid = marketBid;
            LogFills("OnEnterTrade");
        }

        private void LogFills(string onChange)
        {
            if( IsDebug)
            {
                Log.Debug(onChange + " fills");
                for (var current = fills.First; current != null; current = current.Next)
                {
                    var fill = current.Value;
                    Log.Debug("Fill: " + fill.Size + " at " + fill.Price + " " + fill.Time);
                }
            }
        }

        public override void OnChangeTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var tick = Ticks[0];
            var midpoint = (tick.Ask + tick.Bid)/2;
            if (!fill.IsComplete) return;
            if( comboTrade.CurrentPosition > 0 && fill.Price < maxExcursion[0])
            {
                maxExcursion[0] = fill.Price;
            }
            if (comboTrade.CurrentPosition < 0 && fill.Price > maxExcursion[0])
            {
                maxExcursion[0] = fill.Price;
            }
            var size = Math.Abs(comboTrade.CurrentPosition);
            var change = size - lastSize;
            lastSize = size;
            if (change > 0)
            {
                sequentialIncreaseCount++;
                var changed = false;
                change = Math.Abs(change);
                if (fills.First != null)
                {
                    var firstFill = fills.First.Value;
                    if (firstFill.Size + change <= lotSize)
                    {
                        firstFill.Size += change;
                        changed = true;
                    }
                }
                if( !changed)
                {
                    fills.AddFirst(new LocalFill(change, fill.Price, fill.Time));
                }
                SetupBidAsk(fill.Price);
            }
            else
            {
                sequentialIncreaseCount=0;
                change = Math.Abs(change);
                var prevFill = fills.First.Value;
                if (change <= prevFill.Size)
                {
                    prevFill.Size -= change;
                    if (prevFill.Size == 0)
                    {
                        fills.RemoveFirst();
                        if (fills.Count > 0)
                        {
                            SetupBidAsk(fill.Price);
                        }
                    }
                    return;
                }
                change -= prevFill.Size;
                fills.RemoveFirst();
                SetupBidAsk(fill.Price);
                return;

                for (var current = fills.Last; current != null; current = current.Previous)
                {
                    prevFill = current.Value;
                    if (change > prevFill.Size)
                    {
                        change -= prevFill.Size;
                        fills.Remove(current);
                        if (fills.Count > 0)
                        {
                            SetupBidAsk(fill.Price);
                        }
                    }
                    else
                    {
                        prevFill.Size -= change;
                        if (prevFill.Size == 0)
                        {
                            fills.Remove(current);
                            if (fills.Count > 0)
                            {
                                SetupBidAsk(fill.Price);
                            }
                        }
                        break;
                    }
                }
            }
            LogFills("OnChange");
        }

        private void SetFlatBidAsk()
        {
            var tick = Ticks[0];
            var midPoint = (tick.Bid + tick.Ask) / 2;
            var myAsk = midPoint + increaseSpread / 2;
            var myBid = midPoint - increaseSpread / 2;
            marketAsk = Math.Max(tick.Ask, tick.Bid);
            marketBid = Math.Min(tick.Ask, tick.Bid);
            ask = Math.Max(myAsk, marketAsk);
            bid = Math.Min(myBid, marketBid);
            bidLine[0] = bid;
            askLine[0] = ask;
        }

        public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {            
            fills.Clear();
            SetFlatBidAsk();
            if (!comboTrade.Completed)
            {
                throw new InvalidOperationException("Trade must be completed.");
            }
            totalVolume += comboTrade.Volume;
            maxExcursion[0] = double.NaN;
            LogFills("OnEnterTrade");
        }

        public bool IsVisible
        {
            get { return isVisible; }
            set { isVisible = value; }
        }

    }
}