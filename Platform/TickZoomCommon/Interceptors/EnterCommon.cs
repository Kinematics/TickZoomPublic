#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;

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
        public void OnInitialize()
        {
            var order = BuyMarket;
            order = BuyLimit;
            order = BuyStop;
            order = SellLimit;
            order = SellMarket;
            order = SellStop;
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
    public class EnterCommon : StrategySupport
	{
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(EnterCommon));
		private readonly bool debug = log.IsDebugEnabled;
        private InternalOrders orders;
		
		private bool enableWrongSideOrders = false;
		private bool allowReversal = true;
		private bool isNextBar = false;
		
		public EnterCommon(Strategy strategy) : base(strategy) {
            orders = new InternalOrders(strategy,TradeDirection.Entry);
		}
		
		public void OnInitialize()
		{
			if( IsDebug) Log.Debug("OnInitialize()");
			Strategy.Drawing.Color = Color.Black;
            orders.OnInitialize();
		}
		
        public void CancelOrders()
        {
            orders.CancelOrders();
        }
		
		private void LogEntry(string description) {
			if( Strategy.Chart.IsDynamicUpdate) {
        		if( IsNotice) Log.Notice("Bar="+Strategy.Chart.DisplayBars.CurrentBar+", " + description);
			} else {
        		if( IsDebug) Log.Debug("Bar="+Strategy.Chart.DisplayBars.CurrentBar+", " + description);
			}
		}
		
        #region Properties		
        public void SellMarket() {
        	SellMarket(1);
        }
        
        public void SellMarket( double lots) {
        	if( Strategy.Position.IsShort) {
        		string reversal = allowReversal ? "or long " : "";
        		string reversalEnd = allowReversal ? " since AllowReversal is true" : "";
        		throw new TickZoomException("Strategy must be flat "+reversal+"before a sell market entry"+reversalEnd+".");
        	}
        	if( !allowReversal && Strategy.Position.IsLong) {
        		throw new TickZoomException("Strategy cannot enter sell market when position is short. Set AllowReversal to true to allow this.");
        	}
        	if( !allowReversal && Strategy.Position.HasPosition) {
        		throw new TickZoomException("Strategy must be flat before a short market entry.");
        	}
        	/// <summary>
        	/// comment.
        	/// </summary>
        	/// <param name="allowReversal"></param>
            var order = orders.SellMarket;
        	order.Price = 0;
            order.Position = (int)lots;
            if (isNextBar && !order.IsActive)
            {
                order.Status = OrderStatus.NextBar;
        	} else {
                order.Status = OrderStatus.Active;
        	}
        }
        
        [Obsolete("AllowReversals = true is now default until reverse order types.",true)]
        public void SellMarket(bool allowReversal) {
        }
        
        [Obsolete("AllowReversals = true is now default until reverse order types.",true)]
        public void SellMarket( double positions, bool allowReversal) {
		}
        
        public void BuyMarket() {
        	BuyMarket( 1);
        }
        
        public void BuyMarket(double lots) {
        	if( Strategy.Position.IsLong) {
        		string reversal = allowReversal ? "or short " : "";
        		string reversalEnd = allowReversal ? " since AllowReversal is true" : "";
        		throw new TickZoomException("Strategy must be flat "+reversal+"before a long market entry"+reversalEnd+".");
        	}
        	if( !allowReversal && Strategy.Position.IsShort) {
        		throw new TickZoomException("Strategy cannot enter long market when position is short. Set AllowReversal to true to allow this.");
        	}
            var order = orders.BuyMarket;
        	order.Price = 0;
        	order.Position = (int) lots;
        	if( isNextBar && !order.IsActive) {
                order.Status = OrderStatus.NextBar;
        	} else {
                order.Status = OrderStatus.Active;
        	}
        }
        
        [Obsolete("AllowReversals = true is now default until reverse order types.",true)]
        public void BuyMarket(bool allowReversal) {
        }
        
        [Obsolete("AllowReversals = true is now default until reverse order types.",true)]
        public void BuyMarket( double positions, bool allowReversal) {
		}
        
        public void BuyLimit( double price) {
        	BuyLimit( price, 1);
        }
        	
        /// <summary>
        /// Create a active buy limit order.
        /// </summary>
        /// <param name="price">Order price.</param>
        /// <param name="positions">Number of positions as in 1, 2, 3, etc. To set the size of a single position, 
        ///  use PositionSize.Size.</param>

        public void BuyLimit( double price, double lots) {
        	if( Strategy.Position.HasPosition) {
        		throw new TickZoomException("Strategy must be flat before setting a long limit entry.");
        	}
        	orders.BuyLimit.Price = price;
        	orders.BuyLimit.Position = (int) lots;
        	if( isNextBar && !orders.BuyLimit.IsActive) {
	        	orders.BuyLimit.Status = OrderStatus.NextBar;
        	} else {
        		orders.BuyLimit.Status = OrderStatus.Active;
        	}
		}
        
        public void SellLimit( double price) {
        	SellLimit( price, 1);
        }
        	
        /// <summary>
        /// Create a active sell limit order.
        /// </summary>
        /// <param name="price">Order price.</param>
        /// <param name="positions">Number of positions as in 1, 2, 3, etc. To set the size of a single position, 
        ///  use PositionSize.Size.</param>

        public void SellLimit( double price, double lots) {
        	if( Strategy.Position.HasPosition) {
        		throw new TickZoomException("Strategy must be flat before setting a short limit entry.");
        	}
        	orders.SellLimit.Price = price;
        	orders.SellLimit.Position = (int) lots;
        	if( isNextBar && !orders.SellLimit.IsActive) {
	        	orders.SellLimit.Status = OrderStatus.NextBar;
        	} else {
        		orders.SellLimit.Status = OrderStatus.Active;
        	}
		}
        
        public void BuyStop( double price) {
        	BuyStop( price, 1);
        }
        
        /// <summary>
        /// Create a active buy stop order.
        /// </summary>
        /// <param name="price">Order price.</param>
        /// <param name="positions">Number of positions as in 1, 2, 3, etc. To set the size of a single position, 
        ///  use PositionSize.Size.</param>

        public void BuyStop( double price, double lots) {
        	if( Strategy.Position.HasPosition) {
        		throw new TickZoomException("Strategy must be flat before setting a long stop entry.");
        	}
        	orders.BuyStop.Price = price;
        	orders.BuyStop.Position = (int) lots;
        	if( isNextBar && !orders.BuyStop.IsActive) {
	        	orders.BuyStop.Status = OrderStatus.NextBar;
        	} else {
        		orders.BuyStop.Status = OrderStatus.Active;
        	}
		}
	
        public void SellStop( double price) {
        	SellStop( price, 1);
        }
        
        /// <summary>
        /// Create a active sell stop order.
        /// </summary>
        /// <param name="price">Order price.</param>
        /// <param name="positions">Number of positions as in 1, 2, 3, etc. To set the size of a single position, 
        ///  use PositionSize.Size.</param>
        
        public void SellStop( double price, double lots) {
        	if( Strategy.Position.HasPosition) {
        		throw new TickZoomException("Strategy must be flat before setting a short stop entry.");
        	}
        	orders.SellStop.Price = price;
        	orders.SellStop.Position = (int) lots;
        	if( isNextBar && !orders.SellStop.IsActive) {
	        	orders.SellStop.Status = OrderStatus.NextBar;
        	} else {
        		orders.SellStop.Status = OrderStatus.Active;
        	}
        }
        
		#endregion
	
		public override string ToString()
		{
			return Strategy.FullName;
		}
		
		public bool EnableWrongSideOrders {
			get { return enableWrongSideOrders; }
			set { enableWrongSideOrders = value; }
		}

		public bool HasBuyOrder {
			get {
				return orders.BuyStop.IsActive || orders.BuyStop.IsNextBar || 
					orders.BuyLimit.IsActive || orders.BuyLimit.IsNextBar ||
					orders.BuyMarket.IsActive || orders.BuyMarket.IsNextBar;
			}
		}
		
		public bool HasSellOrder {
			get {
				return orders.SellStop.IsActive || orders.SellStop.IsNextBar || 
					orders.SellLimit.IsActive || orders.SellLimit.IsNextBar || 
					orders.SellMarket.IsActive || orders.SellMarket.IsNextBar;
			}
		}
		
		internal InternalOrders Orders {
			get { return orders; }
			set { orders = value; }
		}
		
		internal bool IsNextBar {
			get { return isNextBar; }
			set { isNextBar = value; }
		}
	}
}
