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

#region Namespaces
using System;
using System.ComponentModel;
using System.Drawing;

using TickZoom.Api;
using TickZoom.Common;
using TickZoom.Examples.Indicators;
using TickZoom.Statistics;

#endregion

namespace TickZoom.Examples
{
    public class LimitChangeStrategy : Strategy
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
		
		public LimitChangeStrategy () {
		}
		
		public override void OnInitialize()
		{
			Performance.Equity.GraphEquity = true;
			
			minimumTick = Data.SymbolInfo.MinimumTick;
			lotSize = Data.SymbolInfo.Level2LotSize;
			spread = 5 * minimumTick;
			
			bidLine = Formula.Indicator();
			bidLine.Drawing.IsVisible = true;
			
			askLine = Formula.Indicator();
			askLine.Drawing.IsVisible = true;

			position = Formula.Indicator();
			position.Drawing.PaneType = PaneType.Secondary;
			position.Drawing.IsVisible = true;
		}

        private int changeCount = 0;
		public override bool OnProcessTick(Tick tick)
		{
			if( isFirstTick) {
				isFirstTick = false;
				ask = tick.Ask + spread;
				bid = tick.Bid - spread;
			}
			
			if( tick.Ask < ask) {
				ask = tick.Ask + spread;
			}
			
			if( tick.Bid > bid) {
				bid = tick.Bid - spread;
			}

		    var trades = Performance.ComboTrades;
			if( Position.IsFlat && (trades.Count == 0 || trades.Tail.Completed)) {
				Orders.Enter.ActiveNow.SellLimit(ask, lotSize);
				Orders.Enter.ActiveNow.BuyLimit(bid, lotSize);
			}
            else if( Position.HasPosition)
			{
                if( Position.IsLong)
                {
                    Orders.Exit.ActiveNow.SellLimit(ask);
                    Orders.Change.ActiveNow.BuyLimit(bid, lotSize);
                } else
                {
                    Orders.Exit.ActiveNow.BuyLimit(bid);
                    Orders.Change.ActiveNow.SellLimit(ask, lotSize);
                }
			} else {
		        Orders.Change.ActiveNow.SellLimit(ask, lotSize);
				Orders.Change.ActiveNow.BuyLimit(bid, lotSize);
			}
			
			bidLine[0] = bid;
			askLine[0] = ask;
			position[0] = Position.Current;
			return true;
		}
		
		public override void OnEnterTrade()
		{
            var trades = Performance.ComboTrades;
            var trade = trades.Tail;
            Log.Info("OnEnterTrade() completed=" + trade.Completed);
            ask = Ticks[0].Ask + spread;
			bid = Ticks[0].Bid - spread;
		}
		
		public override void OnChangeTrade()
		{
            var trades = Performance.ComboTrades;
            var trade = trades.Tail;
            Log.Info("OnChangeTrade() completed=" + trade.Completed);
            ask = Ticks[0].Ask + spread;
			bid = Ticks[0].Bid - spread;
		    changeCount++;
		}
		public override void OnExitTrade()
		{
            var trades = Performance.ComboTrades;
		    var trade = trades.Tail;
		    Log.Info("OnExitTrade completed=" + trade.Completed);
			ask = Ticks[0].Ask + spread;
			bid = Ticks[0].Bid - spread;
		    changeCount = 0;
		}
	}
}
