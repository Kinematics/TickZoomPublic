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
        int[] factors = new int[] { 0, 0, 0, 0, 0, 1, 2, 3, 5, 10, 15, 20, 40, 60, 100, 150, 200};

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

        private void ResetBid(double tradePrice, Tick tick, int increasePositions, int decreasePositions)
        {
            bid = tick.Bid - (spread)/2 - factors[increasePositions] * 10 * minimumTick + decreasePositions * minimumTick;
            bid = Math.Min(bid, tick.Bid);
            if (bidLine.Count > 0)
            {
                bidLine[0] = bid;
            }
        }

        private void ResetAsk(double tradePrice, Tick tick, int increasePositions, int decreasePositions)
        {
            ask = tick.Ask + (spread) / 2 + factors[increasePositions] * 10 * minimumTick - decreasePositions * minimumTick;
            ask = Math.Max(ask, tick.Ask);
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
                var midPoint = (tick.Bid+tick.Ask)/2;
                ResetBid(midPoint, tick, 0, 0);
                ResetAsk(midPoint, tick, 0, 0);
            }
            Orders.Enter.ActiveNow.SellLimit(ask, lotSize);
            Orders.Enter.ActiveNow.BuyLimit(bid, lotSize);
        }

        private void OnProcessLong(Tick tick)
        {
            //var positions = Position.Size/lotSize;
            //if( positions >= 10)
            //{
            //    Orders.Reverse.ActiveNow.CancelOrders();
            //    Orders.Change.ActiveNow.CancelOrders();
            //    Orders.Enter.ActiveNow.CancelOrders();
            //    Orders.Exit.ActiveNow.CancelOrders();
            //    Orders.Exit.ActiveNow.GoFlat();
            //}
            SetUpSpread(tick);
        }

        private void OnProcessShort(Tick tick)
        {
            //var positions = Position.Size / lotSize;
            //if (positions >= 10)
            //{
            //    Orders.Reverse.ActiveNow.CancelOrders();
            //    Orders.Change.ActiveNow.CancelOrders();
            //    Orders.Enter.ActiveNow.CancelOrders();
            //    Orders.Exit.ActiveNow.CancelOrders();
            //    Orders.Exit.ActiveNow.GoFlat();
            //}
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

        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var tick = Ticks[0];
            ResetBid(fill.Price, tick, 0, 0);
            ResetAsk(fill.Price, tick, 0, 0);
        }

        public override void  OnChangeTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var tick = Ticks[0];
            var positions = Math.Abs(comboTrade.CurrentPosition)/lotSize;
            var lots = Math.Abs(comboTrade.CurrentPosition) / lotSize;
//            if( fill.Position == filledOrder.Position)
//            {
                if (comboTrade.CurrentPosition > 0)
                {
                    ResetAsk(fill.Price, tick, 0, lots);
                    ResetBid(fill.Price, tick, lots, 0);
                }
                else
                {
                    ResetAsk(fill.Price, tick, lots, 0);
                    ResetBid(fill.Price, tick, 0, lots);
                }
//            }
        }

        private long totalVolume = 0;
        public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var tick = Ticks[0];
            ResetBid(fill.Price, tick, 0, 0);
            ResetAsk(fill.Price, tick, 0, 0);
            if( !comboTrade.Completed)
            {
                throw new InvalidOperationException("Trade must be completed.");
            }
            totalVolume += comboTrade.Volume;
        }
    }
}