﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using TickZoom.Api;
using TickZoom.Common;
using TickZoom.Interceptors;

namespace TickZoom.Examples
{
    public enum Direction
    {
        Long,
        Short,
        Both
    }

    public class SimpleStrategy: Strategy
    {
        IndicatorCommon bidLine;
        IndicatorCommon askLine;
        IndicatorCommon position;
        IndicatorCommon averagePrice;
        private int displaceSMA = 10;
        bool isFirstTick = true;
        double minimumTick;
        double spread;
        private int lotSize = 1000;
        private int increaseLotSize = 1000;
        double ask, marketAsk;
        double bid, marketBid;
        private int thresholdLots = int.MaxValue;
        private Direction direction = Direction.Both;
        private Action<SimpleStrategy> onDirectionChange;
        private bool isVisible = false;
        private long totalVolume = 0;
        private int maxLots = 30;
        private int lastSize = 0;
        private ActiveList<LocalFill> fills = new ActiveList<LocalFill>();
        private double mantissa = 1.15;
        private SMA sma;
        private IndicatorCommon displacedSMA;

        public SimpleStrategy()
        {
            RequestUpdate(Intervals.Second1);
        }

        public override void OnInitialize()
        {
            Performance.Equity.GraphEquity = false; // Graphed by portfolio.
            Performance.GraphTrades = isVisible;

            var seconds = Data.Get(Intervals.Second1);

            sma = Formula.SMA(seconds.Close, 30);
            sma.Drawing.IsVisible = false;
            sma.IntervalDefault = Intervals.Second1;

            displacedSMA = Formula.Indicator();
            displacedSMA.Name = "DiplacedSMA";
            displacedSMA.Drawing.IsVisible = isVisible;
            displacedSMA.IntervalDefault = Intervals.Second1;

            minimumTick = Data.SymbolInfo.MinimumTick;
            spread = 10*minimumTick;

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
            position.Drawing.IsVisible = isVisible;
        }

        private void UpdateIndicators(Tick tick)
        {
            var comboTrades = Performance.ComboTrades;
            displacedSMA[0] = sma[displaceSMA];
            if (comboTrades.Count > 0)
            {
                var comboTrade = comboTrades.Tail;
                averagePrice[0] = Position.IsFlat ? double.NaN : CalcAveragePrice(comboTrade);
            }
            else
            {
                averagePrice[0] = double.NaN;
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

            return true;
        }

        private double lastMidpoint;
        private void OnProcessFlat(Tick tick)
        {
            var midpoint = (tick.Ask + tick.Bid) / 2;
            if (isFirstTick)
            {
                isFirstTick = false;
                lastMidpoint = midpoint;
                SetFlatBidAsk();
            }
            var comboTrades = Performance.ComboTrades;
            if(comboTrades.Count == 0 || comboTrades.Tail.Completed)
            {
                switch( Direction)
                {
                    case Direction.Long:
                        if( midpoint > lastMidpoint + 5*minimumTick)
                        {
                            lastMidpoint = midpoint;
                            SetFlatBidAsk();
                        }
                        Orders.Enter.ActiveNow.BuyLimit(bid, increaseLotSize);
                        break;
                    case Direction.Short:
                        if (midpoint < lastMidpoint - 5 * minimumTick)
                        {
                            lastMidpoint = midpoint;
                            SetFlatBidAsk();
                        }
                        Orders.Enter.ActiveNow.SellLimit(ask, increaseLotSize);
                        break;
                    case Direction.Both:
                        Orders.Enter.ActiveNow.BuyLimit(bid, increaseLotSize);
                        Orders.Enter.ActiveNow.SellLimit(ask, increaseLotSize);
                        break;
                }
            }
            else
            {
                switch (Direction)
                {
                    case Direction.Long:
                        Orders.Change.ActiveNow.BuyLimit(bid, increaseLotSize);
                        break;
                    case Direction.Short:
                        Orders.Change.ActiveNow.SellLimit(ask, increaseLotSize);
                        break;
                    case Direction.Both:
                        Orders.Change.ActiveNow.BuyLimit(bid, increaseLotSize);
                        Orders.Change.ActiveNow.SellLimit(ask, increaseLotSize);
                        break;
                }
            }
        }

        private double CalcAveragePrice(TransactionPairBinary comboTrade)
        {
            var size = Math.Abs(comboTrade.CurrentPosition);
            var sign = -Math.Sign(comboTrade.CurrentPosition);
            var openPnL = comboTrade.AverageEntryPrice*size;
            var closedPnl = sign * comboTrade.ClosedPoints;

            var result = ( openPnL + closedPnl) / size;
            result = comboTrade.AverageEntryPrice;
            return result;
        }

        private void OnProcessLong(Tick tick)
        {
            var midpoint = (tick.Ask + tick.Bid) / 2;
            if (reduceRisk && midpoint > displacedSMA[0])
            {
                reduceRisk = false;
                SetupBidAsk(lastFillPrice);
            }
            var comboTrade = Performance.ComboTrades.Tail;
            var averageEntry = CalcAveragePrice(comboTrade);
            var lots = Position.Size / lotSize;
            var lastTrade = fills.First.Value;
            //bid = Math.Min(marketBid,lastTrade.Price - CalcIncreaseSpread(tick));
            //bidLine[0] = bid;
            if( lots < MaxLots)
            {
                Orders.Change.ActiveNow.BuyLimit(bid, increaseLotSize);
            }
            if (lots > thresholdLots)
            {
                if (Direction == Direction.Long)
                {
                    Orders.Exit.ActiveNow.SellLimit(averageEntry + spread);
                }
                else
                {
                    Orders.Reverse.ActiveNow.SellLimit(averageEntry + spread, increaseLotSize);
                }
                return;
            }
            if (averageEntry < ask)
            {
                if( Direction == Direction.Long)
                {
                    Orders.Exit.ActiveNow.SellLimit(ask);
                }
                else
                {
                    Orders.Reverse.ActiveNow.SellLimit(ask, increaseLotSize);
                }
            }
            else if( lots <= thresholdLots)
            {
                Orders.Change.ActiveNow.SellLimit(ask, lastTrade.Size);
            }
        }

        private void OnProcessShort(Tick tick)
        {
            var midpoint = (tick.Ask + tick.Bid)/2;
            if (reduceRisk && midpoint < displacedSMA[0])
            {
                reduceRisk = false;
                SetupBidAsk(lastFillPrice);
            }
            var comboTrade = Performance.ComboTrades.Tail;
            var averageEntry = CalcAveragePrice(comboTrade);
            var lots = Position.Size/lotSize;
            var lastTrade = fills.First.Value;
            //ask = Math.Max(marketAsk,lastTrade.Price + CalcIncreaseSpread(tick));
            //askLine[0] = ask;
            if( lots < MaxLots)
            {
                Orders.Change.ActiveNow.SellLimit(ask, increaseLotSize);
            }
            if( lots > thresholdLots)
            {
                if (Direction == Direction.Short)
                {
                    Orders.Exit.ActiveNow.BuyLimit(averageEntry - spread);
                } else {
                    Orders.Reverse.ActiveNow.BuyLimit(averageEntry - spread, increaseLotSize);
                }
                return;
            }
            if (averageEntry > bid)
            {
                if (Direction == Direction.Short)
                {
                    Orders.Exit.ActiveNow.BuyLimit(bid);
                }
                else
                {
                    Orders.Reverse.ActiveNow.BuyLimit(bid, increaseLotSize);
                }
            } else {
                Orders.Change.ActiveNow.BuyLimit(bid, lastTrade.Size);
            }
        }

        public override void OnEndHistorical()
        {
            Log.Notice("Total volume was " + totalVolume + ". With commission paid of " + ((totalVolume / 1000) * 0.02D));
        }

        public class LocalFill
        {
            public int Size;
            public double Price;
            public TimeStamp Time;
            public LocalFill( LogicalFill fill)
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

        private bool reduceRisk = true;
        private double lastFillPrice;
        private void SetupBidAsk(double price)
        {
            lastFillPrice = price;
            var originalTrade = fills.Last.Value.Price;
            var tick = Ticks[0];
            var midpoint = (tick.Ask + tick.Bid) / 2;
            var tradeDivergence = midpoint - originalTrade;
            var priceDivergence = midpoint - displacedSMA[0];
            var lots = Position.Size / lotSize;
            var myAsk = price + spread / 2;
            var myBid = price - spread / 2;
            if( Position.IsShort && tradeDivergence > 50 * minimumTick && midpoint > displacedSMA[0])
            {
                reduceRisk = true;
                myAsk = price + priceDivergence;
                myBid = price - priceDivergence;
            }
            else if( Position.IsLong && tradeDivergence < - 50 * minimumTick && midpoint < displacedSMA[0])
            {
                reduceRisk = true;
                myAsk = price + Math.Abs(priceDivergence);
                myBid = price - Math.Abs(priceDivergence);
            }
            else
            {
                myAsk = price + spread / 2;
                myBid = price - spread / 2;
            }
            marketAsk = Math.Max(tick.Ask, tick.Bid);
            marketBid = Math.Min(tick.Ask, tick.Bid);
            ask = Math.Max(myAsk, marketAsk);
            bid = Math.Min(myBid, marketBid);
            bidLine[0] = bid;
            askLine[0] = ask;
        }

        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            lastSize = Math.Abs(comboTrade.CurrentPosition);
            fills.AddFirst(new LocalFill(fill));
            SetupBidAsk(fill.Price);
        }

        public override void OnChangeTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var size = Math.Abs(comboTrade.CurrentPosition);
            var change = size - lastSize;
            lastSize = size;
            if (change > 0)
            {
                if (fills.First != null && fill.Price == fills.First.Value.Price)
                {
                    fills.First.Value.Size += change;
                }
                else
                {
                    fills.AddFirst(new LocalFill(change, fill.Price, fill.Time));
                    SetupBidAsk(fill.Price);
                }
            }
            else
            {
                change = Math.Abs(change);
                for (var current = fills.First; current != null; current = current.Next)
                {
                    var prevFill = current.Value;
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
                            if( fills.Count > 0)
                            {
                                SetupBidAsk(fill.Price);
                            }
                        }
                        break;
                    }
                }
            }
        }

        private void SetFlatBidAsk()
        {
            var tick = Ticks[0];
            var midPoint = (tick.Bid + tick.Ask)/2;
            var myAsk = midPoint + spread/2;
            var myBid = midPoint - spread/2;
            var marketAsk = Math.Max(tick.Ask, tick.Bid);
            var marketBid = Math.Min(tick.Ask, tick.Bid);
            ask = Math.Max(myAsk, marketAsk);
            bid = Math.Min(myBid, marketBid);
            bidLine[0] = bid;
            askLine[0] = ask;
        }


        public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var tick = Ticks[0];
            lastMidpoint = (tick.Ask + tick.Bid)/2;
            fills.Clear();
            SetFlatBidAsk();
            if (!comboTrade.Completed)
            {
                throw new InvalidOperationException("Trade must be completed.");
            }
            totalVolume += comboTrade.Volume;
        }
        public Action<SimpleStrategy> OnDirectionChange
        {
            get { return onDirectionChange; }
            set { onDirectionChange = value; }
        }

        public Direction Direction
        {
            get { return direction; }
            set { direction = value; }
        }

        public bool IsVisible
        {
            get { return isVisible; }
            set { isVisible = value; }
        }

        public int MaxLots
        {
            get { return maxLots; }
            set { maxLots = value; }
        }

        public int IncreaseLotSize
        {
            get { return increaseLotSize; }
            set { increaseLotSize = value; }
        }

        public override void OnConfigure()
        {
            base.OnConfigure();
        }

    }
}
