using System;
using System.Drawing;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class OtherStrategy : Strategy
    {
        IndicatorCommon bidLine;
        IndicatorCommon askLine;
        IndicatorCommon position;
        IndicatorCommon averagePrice;
        private double indifferencePrice;
        bool isFirstTick = true;
        double minimumTick;
        private int increaseSpreadInTicks = 3;
        private int reduceSpreadInTicks = 3;
        private double reduceSpread;
        private double increaseSpread;
        private int lotSize = 1000;
        double ask, marketAsk;
        double bid, marketBid;
        private ActiveList<LocalFill> fills = new ActiveList<LocalFill>();
        private bool isVisible = false;
        private long totalVolume = 0;
        private int maxLots = int.MaxValue;
        private int lastSize = 0;
        private int buySize = 1;
        private int sellSize = 1;
        private double lastMidRange;
        private SMA sma;

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

            position = Formula.Indicator();
            position.Name = "Position";
            position.Drawing.PaneType = PaneType.Secondary;
            position.Drawing.GroupName = "Position";
            position.Drawing.IsVisible = isVisible;

            sma = Formula.SMA(Minutes.Close, 20);
            sma.Drawing.IsVisible = isVisible;
            sma.Drawing.Color = Color.Magenta;
        }

        private void UpdateIndicators(Tick tick)
        {
            var comboTrades = Performance.ComboTrades;
            if (comboTrades.Count > 0)
            {
                var comboTrade = comboTrades.Tail;
                indifferencePrice = CalcIndifferencePrice(comboTrade);
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
                indifferencePrice = (tick.Ask + tick.Bid)/2;
                averagePrice[0] = indifferencePrice;
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

            UpdateIndicators(tick);

            HandlePegging(tick);

            DetermineTrend(tick);

            ProcessSideways(tick);
            return true;
        }

        private void DetermineTrend(Tick tick)
        {
        }

        private void HandlePegging(Tick tick)
        {
            var midPoint = (tick.Ask + tick.Bid) / 2;
            var marketAsk = Math.Max(tick.Bid, tick.Ask);
            var marketBid = Math.Min(tick.Ask, tick.Bid);
            if( marketAsk < bid || marketBid > ask)
            {
                lastMidRange = midPoint;
                SetupBidAsk();
            }
            if (midPoint < lastAskMidpoint)
            {
                lastAskMidpoint = midPoint;
                SetupAsk();
            }
            if (midPoint > lastBidMidpoint)
            {
                lastBidMidpoint = midPoint;
                SetupBid();
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

        private double lastAskMidpoint;
        private double lastBidMidpoint;
        private void OnProcessFlat(Tick tick)
        {
            var midpoint = (tick.Ask + tick.Bid) / 2;
            if (isFirstTick)
            {
                isFirstTick = false;
                lastAskMidpoint = midpoint;
                lastMidRange = midpoint;
                SetFlatBidAsk();
            }
            var comboTrades = Performance.ComboTrades;
            var lots = Position.Size / lotSize;
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

        private readonly long commission = -0.0000195D.ToLong();

        private double CalcIndifferencePrice(TransactionPairBinary comboTrade)
        {
            var size = Math.Abs(comboTrade.CurrentPosition);
            if (size == 0)
            {
                var midPoint = (Ticks[0].Bid + Ticks[0].Ask) / 2;
                return midPoint;
            }
            var sign = -Math.Sign(comboTrade.CurrentPosition);
            var openPoints = comboTrade.AverageEntryPrice.ToLong() * size;
            var closedPoints = comboTrade.ClosedPoints.ToLong();
            var grossProfit = openPoints + closedPoints;
            var transaction = 0; // size * commission * sign;
            var expectedTransaction = size * commission * sign;
            var result = (grossProfit - transaction - expectedTransaction) / size;
            result = ((result + 5000) / 10000) * 10000;
            return result.ToDouble();
        }

        private void OnProcessLong(Tick tick)
        {
            var lots = Position.Size / lotSize;
            var reduce = lots > maxLots ? 2 * buySize : buySize;
            if( buySize > 0)
            {
                Orders.Change.ActiveNow.BuyLimit(bid, buySize * lotSize);
            }
            if (lots == 1)
            {
                if( sellSize > 0)
                {
                    Orders.Reverse.ActiveNow.SellLimit(ask, sellSize * lotSize);
                }
                else
                {
                    Orders.Exit.ActiveNow.SellLimit(ask);
                }
            }
            else
            {
                Orders.Change.ActiveNow.SellLimit(ask, reduce * lotSize);
            }
        }

        private void OnProcessShort(Tick tick)
        {
            var lots = Position.Size / lotSize;
            var reduce = lots > maxLots ? 2 * sellSize : sellSize;
            Orders.Change.ActiveNow.SellLimit(ask, sellSize * lotSize);
            if (lots == 1)
            {
                if( buySize > 0)
                {
                    Orders.Reverse.ActiveNow.BuyLimit(bid, buySize * lotSize);
                }
                else
                {
                    Orders.Exit.ActiveNow.BuyLimit(bid);
                }
            }
            else
            {
                Orders.Change.ActiveNow.BuyLimit(bid, reduce * lotSize);
            }
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

        private void SetupBidAsk()
        {
            AdjustForInventory();
            var tick = Ticks[0];
            lastAskMidpoint = lastBidMidpoint = (tick.Ask + tick.Bid)/2;
            if( Performance.ComboTrades.Count > 0)
            {
                indifferencePrice = CalcIndifferencePrice(Performance.ComboTrades.Tail);
            }
            else
            {
                indifferencePrice = lastAskMidpoint;
            }
            SetupAsk();
            SetupBid();
        }

        private void AdjustForInventory()
        {
            // Calculate market prics.
            var tick = Ticks[0];
            marketAsk = Math.Max(tick.Ask, tick.Bid);
            marketBid = Math.Min(tick.Ask, tick.Bid);

        }

        private void AdjustSpread()
        {
            var lots = Position.Size / lotSize;
            increaseSpread = (increaseSpreadInTicks) * minimumTick;
            reduceSpread = reduceSpreadInTicks * minimumTick;
        }

        private void SetupAsk()
        {
            var price = lastMidRange;
            AdjustSpread();
            var myAsk = Position.IsLong ? price + reduceSpread : price + increaseSpread;
            ask = Position.IsLong ? Math.Max(myAsk, lastAskMidpoint) : Math.Max(myAsk, marketAsk);
            askLine[0] = ask;
        }

        private void SetupBid()
        {
            var price = lastMidRange;
            AdjustSpread();
            var myBid = Position.IsLong ? price - increaseSpread : price - reduceSpread;
            bid = Position.IsLong ? Math.Min(myBid, marketBid) : Math.Min(myBid, lastBidMidpoint);
            bidLine[0] = bid;
        }

        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            lastSize = Math.Abs(comboTrade.CurrentPosition);
            fills.AddFirst(new LocalFill(fill));
            SetupBidAsk();
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
            var size = Math.Abs(comboTrade.CurrentPosition);
            var change = size - lastSize;
            lastSize = size;
            if (change > 0)
            {
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
                lastMidRange = fills.First.Value.Price;
                SetupBidAsk();
            }
            else
            {
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
                            lastMidRange = fills.First.Value.Price;
                            SetupBidAsk();
                        }
                    }
                    return;
                }
                change -= prevFill.Size;
                fills.RemoveFirst();
                lastMidRange = fills.First.Value.Price;
                SetupBidAsk();
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
                            SetupBidAsk();
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
                                SetupBidAsk();
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
            LogFills("OnEnterTrade");
        }

        public bool IsVisible
        {
            get { return isVisible; }
            set { isVisible = value; }
        }

    }
}