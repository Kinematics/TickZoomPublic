using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using TickZoom.Api;
using TickZoom.Common;
using TickZoom.Interceptors;

namespace TickZoom.Examples
{
    public class SimpleStrategy: Strategy
    {
        IndicatorCommon bidLine;
        IndicatorCommon askLine;
        IndicatorCommon position;
        IndicatorCommon averagePrice;
        bool isFirstTick = true;
        double minimumTick;
        double spread;
        int lotSize;
        double ask;
        double bid;
        private int addDelaySeconds = 15;

        public SimpleStrategy()
        {
        }

        public override void OnConfigure()
        {
            base.OnConfigure();
        }

        public override void OnInitialize()
        {

            Performance.Equity.GraphEquity = true;

            minimumTick = Data.SymbolInfo.MinimumTick;
            lotSize = 1000;
            spread = 15*minimumTick;

            askLine = Formula.Indicator();
            askLine.Name = "Ask";
            askLine.Drawing.IsVisible = true;

            bidLine = Formula.Indicator();
            bidLine.Name = "Bid";
            bidLine.Drawing.IsVisible = true;

            averagePrice = Formula.Indicator();
            averagePrice.Name = "BE";
            averagePrice.Drawing.IsVisible = true;
            averagePrice.Drawing.Color = Color.Black;

            position = Formula.Indicator();
            position.Name = "Position";
            position.Drawing.PaneType = PaneType.Secondary;
            position.Drawing.IsVisible = true;
        }

        public override bool OnProcessTick(Tick tick)
        {
            Orders.SetAutoCancel();
            if (!tick.IsQuote)
            {
                throw new InvalidOperationException("This strategy requires bid/ask quote data to operate.");
            }

            var comboTrades = Performance.ComboTrades;
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

            if( comboTrades.Count > 0)
            {
                var comboTrade = comboTrades.Tail;
                averagePrice[0] = CalcAveragePrice(comboTrade);
            }
            else
            {
                averagePrice[0] = double.NaN;
            }

            if (bidLine.Count > 0)
            {
                position[0] = Position.Current / lotSize;
            }
            return true;
        }

        private void OnProcessFlat(Tick tick)
        {
            if (isFirstTick)
            {
                isFirstTick = false;
                SetFlatBidAsk();
            }
            var comboTrades = Performance.ComboTrades;
            if(comboTrades.Count == 0 || comboTrades.Tail.Completed)
            {
                Orders.Enter.ActiveNow.SellLimit(ask, lotSize);
                Orders.Enter.ActiveNow.BuyLimit(bid, lotSize);
            } else
            {
                Orders.Change.ActiveNow.SellLimit(ask, lotSize);
                Orders.Change.ActiveNow.BuyLimit(bid, lotSize);
            }
        }

        private double CalcAveragePrice(TransactionPairBinary comboTrade)
        {
            var sign = Math.Sign(comboTrade.CurrentPosition);
            var position = comboTrade.CurrentPosition;
            return comboTrade.AverageEntryPrice; //(comboTrade.AverageEntryPrice * position + sign * comboTrade.ClosedPoints) / position;
        }

        private void OnProcessLong(Tick tick)
        {
            var comboTrade = Performance.ComboTrades.Tail;
            var averageEntry = CalcAveragePrice(comboTrade);
            bid = fills.First.Value.Price - CalcIncreaseSpread(tick);
            bidLine[0] = bid;
            if (Math.Abs(comboTrade.CurrentPosition) <= lotSize)
            {
                Orders.Reverse.ActiveNow.SellLimit(ask, lotSize);
            }
            else if( averageEntry < ask)
            {
                Orders.Reverse.ActiveNow.SellLimit(ask, lotSize);
            }
            else
            {
                Orders.Change.ActiveNow.SellLimit(ask, lotSize);
            }
            Orders.Change.ActiveNow.BuyLimit(bid, lotSize);
        }

        private void OnProcessShort(Tick tick)
        {
            var comboTrade = Performance.ComboTrades.Tail;
            var averageEntry = CalcAveragePrice(comboTrade);
            ask = fills.First.Value.Price + CalcIncreaseSpread(tick);
            askLine[0] = ask;
            Orders.Change.ActiveNow.SellLimit(ask, lotSize);
            if (Math.Abs(comboTrade.CurrentPosition) <= lotSize)
            {
                Orders.Reverse.ActiveNow.BuyLimit(bid, lotSize);
            }
            else if( averageEntry > bid)
            {
                Orders.Reverse.ActiveNow.BuyLimit(bid, lotSize);
            }
            else
            {
                Orders.Change.ActiveNow.BuyLimit(bid, lotSize);
            }
        }

        private double CalcIncreaseSpread(Tick tick)
        {
            var lastFill = fills.First.Value;
            var lots = Position.Size / lotSize;
            var elapsed = tick.Time - lastFill.Time;
            var scale = elapsed.TotalSeconds;
            var spread = fills.Count == 1 ? this.spread : Math.Abs(fills.First.Value.Price - fills.First.Next.Value.Price) * 2;
            if (scale < 10)
            {
                spread = (spread / 10) * (10 - scale);
            }
            else
            {
                spread = this.spread;
            }
            return spread;
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
        private int lastSize = 0;
        private ActiveList<LocalFill> fills = new ActiveList<LocalFill>();

        private void SetupBidAsk()
        {
            var tick = Ticks[0];
            var currentFill = fills.First.Value;
            var myAsk = currentFill.Price + spread / 2;
            var myBid = currentFill.Price - spread / 2;
            var marketAsk = Math.Max(tick.Ask, tick.Bid);
            var marketBid = Math.Min(tick.Ask, tick.Bid);
            ask = Math.Max(myAsk, marketAsk);
            bid = Math.Min(myBid, marketBid);
            bidLine[0] = bid;
            askLine[0] = ask;
        }

        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            lastSize = Math.Abs(comboTrade.CurrentPosition);
            fills.AddFirst(new LocalFill( fill));
            SetupBidAsk();
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
                    SetupBidAsk();
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
                            SetupBidAsk();
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
                                SetupBidAsk();
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

        private long totalVolume = 0;
        public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            fills.Clear();
            var tick = Ticks[0];
            if( !comboTrade.Completed)
            {
                throw new InvalidOperationException("Trade must be completed.");
            }
            totalVolume += comboTrade.Volume;
        }
    }
}
