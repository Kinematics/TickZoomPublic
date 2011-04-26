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
using System.Diagnostics;
using System.Drawing;

using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Interceptors
{
	/// <summary>
	/// Description of StrategySupport.
	/// </summary>
	public class ExitCommon : StrategySupport
	{
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(ExitCommon));
		private PositionInterface position;
        private InternalOrders orders;
		
		private bool enableWrongSideOrders = false;
		private bool isNextBar = false;
		
		public ExitCommon(Strategy strategy) : base(strategy) {
            orders = new InternalOrders(strategy, TradeDirection.Exit);
		}
		
		public void OnInitialize()
		{
			if( IsTrace) Log.Trace(Strategy.FullName+".Initialize()");
			Strategy.Drawing.Color = Color.Black;
			position = Strategy.Position;
        }

		private void FlattenSignal(double price) {
			Strategy.Position.Change(0,price,Strategy.Ticks[0].Time);
			CancelOrders();
		}
	
		public void CancelOrders() {
            orders.CancelOrders();
		}
		
        #region Orders

        public void GoFlat() {
            if (Strategy.Position.IsLong)
            {
	        	orders.SellMarket.Price = 0;
	        	orders.SellMarket.Position = 0;
	        	if( isNextBar) {
	    	    	orders.SellMarket.Status = OrderStatus.NextBar;
		       	} else {
		        	orders.SellMarket.Status = OrderStatus.Active;
	        	}
        	}
        	if( Strategy.Position.IsShort) {
	        	orders.BuyMarket.Price = 0;
	        	orders.BuyMarket.Position = 0;
	        	if( isNextBar) {
	    	    	orders.BuyMarket.Status = OrderStatus.NextBar;
		       	} else {
		        	orders.BuyMarket.Status = OrderStatus.Active;
	        	}
        	}
		}
	
        public void BuyStop(double price) {
        	if( Strategy.Position.IsLong) {
        		throw new TickZoomException("Strategy must be short or flat before setting a buy stop to exit.");
        	} else if( Strategy.Position.IsFlat) {
        		if(!Strategy.Orders.Enter.ActiveNow.HasSellOrder) {
        			throw new TickZoomException("When flat, a sell order must be active before creating a buy order to exit.");
        		}
			}
            orders.BuyStop.Price = price;
        	if( isNextBar) {
    	    	orders.BuyStop.Status = OrderStatus.NextBar;
	       	} else {
	        	orders.BuyStop.Status = OrderStatus.Active;
        	}
        }
	
        public void SellStop( double price) {
        	if( Strategy.Position.IsShort) {
        		throw new TickZoomException("Strategy must be long or flat before setting a sell stop to exit.");
        	} else if( Strategy.Position.IsFlat) {
        		if(!Strategy.Orders.Enter.ActiveNow.HasBuyOrder) {
        			throw new TickZoomException("When flat, a buy order must be active before creating a sell stop to exit.");
        		}
        	}
			orders.SellStop.Price = price;
        	if( isNextBar) {
    	    	orders.SellStop.Status = OrderStatus.NextBar;
	       	} else {
	        	orders.SellStop.Status = OrderStatus.Active;
        	}
		}
        
        public void BuyLimit(double price) {
        	if( Strategy.Position.IsLong) {
        		throw new TickZoomException("Strategy must be short or flat before setting a buy limit to exit.");
        	} else if( Strategy.Position.IsFlat) {
        		if(!Strategy.Orders.Enter.ActiveNow.HasSellOrder) {
        			throw new TickZoomException("When flat, a sell order must be active before creating a buy order to exit.");
        		}
			}
            orders.BuyLimit.Price = price;
        	if( isNextBar) {
    	    	orders.BuyLimit.Status = OrderStatus.NextBar;
	       	} else {
	        	orders.BuyLimit.Status = OrderStatus.Active;
        	}
		}
	
        public void SellLimit( double price) {
        	if( Strategy.Position.IsShort) {
        		throw new TickZoomException("Strategy must be long or flat before setting a sell limit to exit.");
        	} else if( Strategy.Position.IsFlat) {
        		if(!Strategy.Orders.Enter.ActiveNow.HasBuyOrder) {
        			throw new TickZoomException("When flat, a buy order must be active before creating a sell order to exit.");
        		}
			}
            orders.SellLimit.Price = price;
        	if( isNextBar) {
    	    	orders.SellLimit.Status = OrderStatus.NextBar;
	       	} else {
	        	orders.SellLimit.Status = OrderStatus.Active;
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
		
		internal bool IsNextBar {
			get { return isNextBar; }
			set { isNextBar = value; }
		}
		
		internal InternalOrders Orders {
			get { return orders; }
			set { orders = value; }
		}
	}
}
