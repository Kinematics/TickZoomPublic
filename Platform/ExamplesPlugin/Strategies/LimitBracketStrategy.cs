using System;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class LimitBracketStrategy : Strategy
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

        public override void OnInitialize()
        {
            Performance.Equity.GraphEquity = true;

            minimumTick = Data.SymbolInfo.MinimumTick;
            lotSize = 1000;
            ResetSpread();

            bidLine = Formula.Indicator();
            bidLine.Drawing.IsVisible = true;

            askLine = Formula.Indicator();
            askLine.Drawing.IsVisible = true;

            position = Formula.Indicator();
            position.Drawing.PaneType = PaneType.Secondary;
            position.Drawing.IsVisible = true;
        }

        private void ResetSpread()
        {
            spread = 5 * minimumTick;
        }

        private double askPrice;
        private double bidPrice;
        private double midPoint;
        private double lastBidPrice;
        private double lastAskPrice;
        private double entryPrice;

        public void SetPrices(Tick tick)
        {
            if (tick.IsQuote)
            {
                askPrice = tick.Ask;
                bidPrice = tick.Bid;
            }
            else if (tick.IsTrade)
            {
                askPrice = bidPrice = tick.Price;
            }
            else
            {
                throw new InvalidOperationException("Tick must have either trade or quote data.");
            }
            midPoint = (askPrice + bidPrice)/2;
        }

        private void ResetBidAsk()
        {
            ResetBid();
            ResetAsk();
        }

        private void ResetBid()
        {
            bid = midPoint - spread;
        }

        private void ResetAsk()
        {
            ask = midPoint + spread;
        }

        public override bool OnProcessTick(Tick tick)
        {
            SetPrices(tick);
            if (Position.IsFlat)
            {
                OnProcessFlat();
            }
            else if( Position.IsLong)
            {
                OnProcessLong();
            }
            else if( Position.IsShort)
            {
                OnProcessShort();
            }

            if( bidLine.Count > 0 )
            {
                bidLine[0] = bid;
                askLine[0] = ask;
                position[0] = Position.Current;
            }
            return true;
        }

        private void OnProcessFlat()
        {
            if (isFirstTick)
            {
                isFirstTick = false;
                lastAskPrice = askPrice;
                lastBidPrice = bidPrice;
                ResetBidAsk();
            }

            if (askPrice > lastAskPrice)
            {
                lastAskPrice = askPrice;
                ResetBid();
            }

            if (bidPrice < lastBidPrice)
            {
                lastBidPrice = bidPrice;
                ResetAsk();
            }
            Orders.Enter.ActiveNow.SellLimit(ask, lotSize);
            Orders.Enter.ActiveNow.BuyLimit(bid, lotSize);
        }

        private void OnProcessLong()
        {
            //if( askPrice < entryPrice)
            //{
            //    TighterSpread();
            //}
            if (bidPrice < lastBidPrice)
            {
                lastBidPrice = bidPrice;
                ResetBidAsk();
            }
            Orders.Reverse.ActiveNow.SellLimit(ask, lotSize);
        }

        private void OnProcessShort()
        {
            //if (bidPrice > entryPrice)
            //{
            //    TighterSpread();
            //}
            if (askPrice > lastAskPrice)
            {
                lastAskPrice = askPrice;
                ResetBidAsk();
            }
            Orders.Reverse.ActiveNow.BuyLimit(bid, lotSize);
        }

        public override void OnEnterTrade()
        {
            entryPrice = Performance.ComboTrades[Performance.ComboTrades.Current].EntryPrice;
            SetPrices(Ticks[0]);
            ResetBidAsk();
            ResetSpread();
            lastAskPrice = askPrice;
            lastBidPrice = bidPrice;
        }

        public override void OnChangeTrade()
        {
            SetPrices(Ticks[0]);
            ResetBidAsk();
            lastAskPrice = askPrice;
            lastBidPrice = bidPrice;
        }

        public override void OnExitTrade()
        {
        }

    }
}