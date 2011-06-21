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
    public class EnterCommon : StrategySupport
	{
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(EnterCommon));
		private readonly bool debug = log.IsDebugEnabled;
        private InternalOrders orders;
		
		private bool enableWrongSideOrders = false;
		private bool isNextBar = false;
		
		public EnterCommon(Strategy strategy) : base(strategy) {
            orders = new InternalOrders(strategy,TradeDirection.Entry);
		}
		
		public void OnInitialize()
		{
			if( IsDebug) Log.Debug("OnInitialize()");
			Strategy.Drawing.Color = Color.Black;
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
        	if( Strategy.Position.HasPosition) {
        		throw new TickZoomException("Strategy must be flat before a short market entry.");
        	}
            var trades = Strategy.Performance.ComboTrades;
            if (trades.Count > 0 && !trades.Tail.Completed)
            {
                throw new TickZoomException("Combo trade must be completed before setting a sell market entry.");
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
        	if( Strategy.Position.HasPosition) {
        		throw new TickZoomException("Strategy cannot enter long market when position is short. Set AllowReversal to true to allow this.");
        	}
            var trades = Strategy.Performance.ComboTrades;
            if (trades.Count > 0 && !trades.Tail.Completed)
            {
                throw new TickZoomException("Combo trade must be completed before setting a buy market entry.");
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
            BuyLimit(price, lots, 1, 1);
		}

        /// <summary>
        /// Create a multi-level active buy limit order.
        /// </summary>
        /// <param name="price">Order price.</param>
        /// <param name="positions">Number of positions as in 1, 2, 3, etc. To set the size of a single position, 
        ///  use PositionSize.Size.</param>

        public void BuyLimit(double price, double lots, int levels, int levelIncrement)
        {
            if (Strategy.Position.HasPosition)
            {
                throw new TickZoomException("Strategy must be flat before setting a long limit entry.");
            }
            var trades = Strategy.Performance.ComboTrades;
            if (trades.Count > 0 && !trades.Tail.Completed)
            {
                throw new TickZoomException("Combo trade must be completed before setting a long limit entry.");
            }
            orders.BuyLimit.Price = price;
            orders.BuyLimit.Position = (int)lots;
            if (isNextBar && !orders.BuyLimit.IsActive)
            {
                orders.BuyLimit.Status = OrderStatus.NextBar;
            }
            else
            {
                orders.BuyLimit.Status = OrderStatus.Active;
            }
        }

        public void SellLimit(double price)
        {
        	SellLimit( price, 1);
        }

        public void SellLimit(double price, double lots)
        {
            SellLimit(price, lots, 1, 1);
        }

        /// <summary>
        /// Create a active sell limit order.
        /// </summary>
        /// <param name="price">Order price.</param>
        /// <param name="positions">Number of positions as in 1, 2, 3, etc. To set the size of a single position, 
        ///  use PositionSize.Size.</param>

        public void SellLimit( double price, double lots, int levels, int increment) {
        	if( Strategy.Position.HasPosition) {
        		throw new TickZoomException("Strategy must be flat before setting a short limit entry.");
        	}
            var trades = Strategy.Performance.ComboTrades;
            if (trades.Count > 0 && !trades.Tail.Completed)
            {
                throw new TickZoomException("Combo trade must be completed before setting a short limit entry.");
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
            var trades = Strategy.Performance.ComboTrades;
            if (trades.Count > 0 && !trades.Tail.Completed)
            {
                throw new TickZoomException("Combo trade must be completed before setting a long stop entry.");
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
            var trades = Strategy.Performance.ComboTrades;
            if (trades.Count > 0 && !trades.Tail.Completed)
            {
                throw new TickZoomException("Combo trade must be completed before setting a short stop entry.");
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
