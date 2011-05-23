using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Interceptors
{
    public class InternalOrders
    {
        private Strategy strategy;
        private TradeDirection direction;
        public InternalOrders(Strategy strategy, TradeDirection direction)
        {
            this.strategy = strategy;
            this.direction = direction;
        }

        private LogicalOrder buyMarket;
        public LogicalOrder BuyMarket
        {
            get
            {
                if (buyMarket == null)
                {
                    buyMarket = Factory.Engine.LogicalOrder(strategy.Data.SymbolInfo, strategy);
                    buyMarket.TradeDirection = direction;
                    buyMarket.Type = OrderType.BuyMarket;
                    strategy.AddOrder(buyMarket);
                }
                return buyMarket;
            }
        }
        private LogicalOrder sellMarket;
        public LogicalOrder SellMarket
        {
            get
            {
                if (sellMarket == null)
                {
                    sellMarket = Factory.Engine.LogicalOrder(strategy.Data.SymbolInfo, strategy);
                    sellMarket.TradeDirection = direction;
                    sellMarket.Type = OrderType.SellMarket;
                    strategy.AddOrder(sellMarket);
                }
                return sellMarket;
            }
        }
        private LogicalOrder buyStop;
        public LogicalOrder BuyStop
        {
            get
            {
                if (buyStop == null)
                {
                    buyStop = Factory.Engine.LogicalOrder(strategy.Data.SymbolInfo, strategy);
                    buyStop.TradeDirection = direction;
                    buyStop.Type = OrderType.BuyStop;
                    strategy.AddOrder(buyStop);
                }
                return buyStop;
            }
        }

        private LogicalOrder sellStop;
        public LogicalOrder SellStop
        {
            get
            {
                if (sellStop == null)
                {
                    sellStop = Factory.Engine.LogicalOrder(strategy.Data.SymbolInfo, strategy);
                    sellStop.TradeDirection = direction;
                    sellStop.Type = OrderType.SellStop;
                    strategy.AddOrder(sellStop);
                }
                return sellStop;
            }
        }
        private LogicalOrder buyLimit;
        public LogicalOrder BuyLimit
        {
            get
            {
                if (buyLimit == null)
                {
                    buyLimit = Factory.Engine.LogicalOrder(strategy.Data.SymbolInfo, strategy);
                    buyLimit.TradeDirection = direction;
                    buyLimit.Type = OrderType.BuyLimit;
                    strategy.AddOrder(buyLimit);
                }
                return buyLimit;
            }
        }
        private LogicalOrder sellLimit;
        public LogicalOrder SellLimit
        {
            get
            {
                if (sellLimit == null)
                {
                    sellLimit = Factory.Engine.LogicalOrder(strategy.Data.SymbolInfo, strategy);
                    sellLimit.TradeDirection = direction;
                    sellLimit.Type = OrderType.SellLimit;
                    strategy.AddOrder(sellLimit);
                }
                return sellLimit;
            }
        }
        public void CancelOrders()
        {
            if( buyMarket != null) buyMarket.Status = OrderStatus.AutoCancel;
            if (sellMarket != null) sellMarket.Status = OrderStatus.AutoCancel;
            if (buyStop != null) buyStop.Status = OrderStatus.AutoCancel;
            if (sellStop != null) sellStop.Status = OrderStatus.AutoCancel;
            if (buyLimit != null) buyLimit.Status = OrderStatus.AutoCancel;
            if (sellLimit != null) sellLimit.Status = OrderStatus.AutoCancel;

        }
    }
}