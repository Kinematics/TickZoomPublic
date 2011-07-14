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
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Interceptors
{
	public class FillHandlerDefault : FillHandler, LogAware
	{
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(FillHandlerDefault));
        private volatile bool trace = log.IsTraceEnabled;
        private volatile bool debug = log.IsDebugEnabled;
        public void RefreshLogLevel()
        {
            if (log != null)
            {
                debug = log.IsDebugEnabled;
                trace = log.IsTraceEnabled;
            }
        }
        private static readonly bool notice = log.IsNoticeEnabled;
		private Action<SymbolInfo, LogicalFill> changePosition;
		private Func<LogicalOrder, LogicalFill, int> drawTrade;
		private SymbolInfo symbol;
		private bool doStrategyOrders = true;
		private bool doExitStrategyOrders = false;
	    private Strategy strategy;
		
		public FillHandlerDefault()
		{
            log.Register(this);
		}
	
		public FillHandlerDefault(StrategyInterface strategyInterface)
		{
            this.strategy = (Strategy)strategyInterface;
            log.Register(this);
        }
	
		private void TryDrawTrade(LogicalOrder order, LogicalFill fill) {
            if (drawTrade != null && strategy != null && strategy.Performance.GraphTrades)
            {
				drawTrade(order, fill);
			}
		}
	
		public void ProcessFill(StrategyInterface strategyInterface, LogicalFill fill) {
			if( debug) log.Debug( "ProcessFill: " + fill + " for strategy " + strategyInterface);
			var strategy = (Strategy) strategyInterface;
			int orderId = fill.OrderId;
			LogicalOrder filledOrder = null;
			if( strategyInterface.TryGetOrderById( fill.OrderId, out filledOrder)) {
				if( debug) log.Debug( "Matched fill with orderId: " + orderId);
				if( !doStrategyOrders && filledOrder.TradeDirection != TradeDirection.ExitStrategy ) {
					if( debug) log.Debug( "Skipping fill, strategy order fills disabled.");
					return;
				}
				if( !doExitStrategyOrders && filledOrder.TradeDirection == TradeDirection.ExitStrategy) {
					if( debug) log.Debug( "Skipping fill, exit strategy orders fills disabled.");
					return;
				}
				TryDrawTrade(filledOrder, fill);
				if( debug) log.Debug( "Changed strategy position to " + fill.Position + " because of fill.");
				changePosition(strategy.Data.SymbolInfo,fill);
                if( fill.Recency > strategy.Recency)
                {
                    if( debug) log.Debug("strategy recency now " + fill.Recency);
                    strategy.Recency = fill.Recency+1;
                }
			} else {
				throw new ApplicationException("A fill for order id: " + orderId + " was incorrectly routed to: " + strategyInterface.Name);
			}
		}

		public Func<LogicalOrder, LogicalFill, int> DrawTrade {
			get { return drawTrade; }
			set { drawTrade = value; }
		}
		
		public Action<SymbolInfo, LogicalFill> ChangePosition {
			get { return changePosition; }
			set { changePosition = value; }
		}
		
		public SymbolInfo Symbol {
			get { return symbol; }
			set { symbol = value; }
		}
		
		public bool DoStrategyOrders {
			get { return doStrategyOrders; }
			set { doStrategyOrders = value; }
		}
		
		public bool DoExitStrategyOrders {
			get { return doExitStrategyOrders; }
			set { doExitStrategyOrders = value; }
		}
		
	}
}
