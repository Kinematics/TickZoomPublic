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

        public LimitBracketStrategy()
        {
        }

        public override void OnInitialize()
        {
            Performance.Equity.GraphEquity = true;

            minimumTick = Data.SymbolInfo.MinimumTick;
            lotSize = 1000;
            spread = 15 * minimumTick;

            bidLine = Formula.Indicator();
            bidLine.Drawing.IsVisible = true;

            askLine = Formula.Indicator();
            askLine.Drawing.IsVisible = true;

            position = Formula.Indicator();
            position.Drawing.PaneType = PaneType.Secondary;
            position.Drawing.IsVisible = true;
        }

        private double askPrice;
        private double bidPrice;
        private double lastBidPrice;
        private double lastAskPrice;

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
        }

        private void ResetBidAsk()
        {
            ResetBid();
            ResetAsk();
        }

        private void ResetBid()
        {
            bid = askPrice - spread;
        }

        private void ResetAsk()
        {
            ask = bidPrice + spread;
        }

        public override bool OnProcessTick(Tick tick)
        {
            SetPrices(tick);
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

            if (Position.IsFlat)
            {
                Orders.Enter.ActiveNow.SellLimit(ask, lotSize);
                Orders.Enter.ActiveNow.BuyLimit(bid, lotSize);
            }
            else if( Position.IsLong)
            {
                Orders.Reverse.ActiveNow.SellLimit(ask, lotSize);
            }
            else if( Position.IsShort)
            {
                Orders.Reverse.ActiveNow.BuyLimit(bid, lotSize);
            }

            if( bidLine.Count > 0 )
            {
                bidLine[0] = bid;
                askLine[0] = ask;
                position[0] = Position.Current;
            }
            return true;
        }

        public override void OnEnterTrade()
        {
            SetPrices(Ticks[0]);
            ResetBidAsk();
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
            SetPrices(Ticks[0]);
            ResetBidAsk();
            lastAskPrice = askPrice;
            lastBidPrice = bidPrice;
        }
    }
}