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
            lotSize = Data.SymbolInfo.Level2LotSize;
            spread = 2 * minimumTick;

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

        public override bool OnProcessTick(Tick tick)
        {
            SetPrices(tick);
            if (isFirstTick)
            {
                isFirstTick = false;
                lastAskPrice = askPrice;
                lastBidPrice = bidPrice;
                ask = askPrice + spread;
                bid = askPrice - spread;
            }

            if (askPrice < lastAskPrice)
            {
                lastAskPrice = askPrice;
                ask = askPrice + spread;
            }

            if (bidPrice > lastBidPrice)
            {
                lastBidPrice = bidPrice;
                bid = askPrice - spread;
            }

            if (Position.IsFlat)
            {
                Orders.Enter.ActiveNow.SellLimit(ask, lotSize);
                Orders.Enter.ActiveNow.BuyLimit(bid, lotSize);
            }
            else if( Position.IsLong)
            {
                Orders.Exit.ActiveNow.SellLimit(ask);
                Orders.Exit.ActiveNow.SellStop(bid - spread);
            }
            else if( Position.IsShort)
            {
                Orders.Exit.ActiveNow.BuyStop(ask + spread);
                Orders.Exit.ActiveNow.BuyLimit(bid);
            }

            bidLine[0] = bid;
            askLine[0] = ask;
            position[0] = Position.Current;
            return true;
        }

        public override void OnEnterTrade()
        {
            SetPrices(Ticks[0]);
            ask = askPrice + spread;
            bid = askPrice - spread;
        }

        public override void OnChangeTrade()
        {
            SetPrices(Ticks[0]);
            ask = askPrice + spread;
            bid = askPrice - spread;
        }
        public override void OnExitTrade()
        {
            SetPrices(Ticks[0]);
            ask = askPrice + spread;
            bid = askPrice - spread;
        }
    }
}