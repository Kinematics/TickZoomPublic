using System;
using System.Threading;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class SimpleStrategy: Strategy
    {
        IndicatorCommon bidLine;
        IndicatorCommon askLine;
        IndicatorCommon position;
        bool isFirstTick = true;
        double minimumTick;
        double spread;
        int lotSize;
        double ask;
        double bid;
        int[] fibonacci = new int[19] { 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };

        public override void OnInitialize()
        {
            Performance.Equity.GraphEquity = true;

            minimumTick = Data.SymbolInfo.MinimumTick;
            lotSize = 1000;
            spread = 10*minimumTick;

            bidLine = Formula.Indicator();
            bidLine.Drawing.IsVisible = true;

            askLine = Formula.Indicator();
            askLine.Drawing.IsVisible = true;

            position = Formula.Indicator();
            position.Drawing.PaneType = PaneType.Secondary;
            position.Drawing.IsVisible = true;
        }

        private void ResetBid(Tick tick, int increaseFactor, int decreaseFactor)
        {
            bid = tick.Bid - (spread*increaseFactor)/2 + minimumTick*decreaseFactor;
            if (bidLine.Count > 0)
            {
                bidLine[0] = bid;
            }
        }

        private void ResetAsk(Tick tick, int increaseFactor, int decreaseFactor)
        {
            ask = tick.Ask + (spread*increaseFactor)/2 - minimumTick*decreaseFactor;
            if (askLine.Count > 0)
            {
                askLine[0] = ask;
            }
        }

        public override bool OnProcessTick(Tick tick)
        {
            if( !tick.IsQuote)
            {
                throw new InvalidOperationException("This strategy requires bid/ask quote data to operate.");
            }
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

            if (bidLine.Count > 0)
            {
                position[0] = Position.Current;
            }
            return true;
        }

        private void OnProcessFlat(Tick tick)
        {
            if (isFirstTick)
            {
                isFirstTick = false;
                ResetBid(tick,1,0);
                ResetAsk(tick,1,0);
            }
            Orders.Enter.ActiveNow.SellLimit(ask, lotSize);
            Orders.Enter.ActiveNow.BuyLimit(bid, lotSize);
        }

        private void OnProcessLong(Tick tick)
        {
            SetUpSpread(tick);
        }

        private void OnProcessShort(Tick tick)
        {
            SetUpSpread(tick);
        }

        private void SetUpSpread(Tick tick)
        {

            var size = Position.Size;
            var increaseSize = lotSize;
            var decreaseSize = lotSize;
            if (Position.IsLong)
            {
                Orders.Change.ActiveNow.BuyLimit(bid, increaseSize);
                if (size <= lotSize)
                {
                    Orders.Reverse.ActiveNow.SellLimit(ask, lotSize);
                }
                else
                {
                    Orders.Change.ActiveNow.SellLimit(ask, decreaseSize);
                }
            }
            else
            {
                Orders.Change.ActiveNow.SellLimit(ask, increaseSize);
                if (size <= lotSize)
                {
                    Orders.Reverse.ActiveNow.BuyLimit(bid, lotSize);
                }
                else
                {
                    Orders.Change.ActiveNow.BuyLimit(bid, decreaseSize);
                }
            }
        }

        public override void OnEndHistorical()
        {
            Log.Notice("Total volume was " + totalVolume + ". With commission paid of " + ((totalVolume / 1000) * 0.02D));
        }

        public override void OnEnterTrade()
        {
            var tick = Ticks[0];
            ResetBid(tick,1,1);
            ResetAsk(tick,1,1);
        }

        public override void  OnChangeTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var tick = Ticks[0];
            var positions = Math.Abs(comboTrade.CurrentPosition)/lotSize;
            if( comboTrade.CurrentPosition > 0)
            {
                ResetBid(tick, fibonacci[positions], 0);
                ResetAsk(tick, 1, fibonacci[positions]);
            } else {
                ResetBid(tick, 1, fibonacci[positions]);
                ResetAsk(tick, fibonacci[positions], 0);
            }
        }

        private long totalVolume = 0;
        public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var tick = Ticks[0];
            ResetBid(tick,1,0);
            ResetAsk(tick,1,0);
            if( !comboTrade.Completed)
            {
                throw new InvalidOperationException("Trade must be completed.");
            }
            totalVolume += comboTrade.Volume;
        }
    }
}