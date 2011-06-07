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

namespace TickZoom.Api
{
	public class StrategyPositionDefault : StrategyPosition
	{
	    private static readonly Log log = Factory.SysLog.GetLogger(typeof (StrategyPositionDefault));
	    private bool debug = log.IsDebugEnabled;
	    private int _id;
	    private SymbolInfo _symbol;
        private int actualPosition;
        private int expectedPosition;
        private long recency;

        public StrategyPositionDefault(int id, SymbolInfo symbol)
        {
            this._id = id;
            this._symbol = symbol;
            if( debug) log.Debug("New StrategyPosition");
        }

	    public int ActualPosition
	    {
	        get { return actualPosition; }
	    }

	    public long Recency
	    {
	        get { return recency; }
	    }

	    public int ExpectedPosition
	    {
	        get { return expectedPosition; }
	    }

	    public SymbolInfo Symbol
	    {
	        get { return _symbol; }
	    }

	    public int Id
	    {
	        get { return _id; }
	    }

	    public void SetExpectedPosition(int position)
        {
            if (debug) log.Debug("SetExpectedPositions() strategy " + Id + " for " + Symbol + " position change from " + expectedPosition + " to " + position + ". Recency " + this.recency + " to " + recency);
            expectedPosition = position;
        }

        public void SetActualPosition(int position)
        {
            if (debug) log.Debug("SetActualPosition() strategy " + Id + " for " + Symbol + " position change from " + actualPosition + " to " + position + ". Recency " + this.recency + " to " + recency);
            actualPosition = position;
        }

	    public void TrySetPosition( int position, long recency)
        {
            if( recency == 0L)
            {
                throw new InvalidOperationException("Recency must be non-zero.");
            }
            if (recency > this.recency)
            {
                if (position != actualPosition)
                {
                    if (debug) log.Debug("Strategy " + Id + " for " + Symbol + " position change from " + actualPosition + " to " + position + ". Recency " + this.recency + " to " + recency);
                    actualPosition = position;
                }
                this.recency = recency;
            }
            else if (position != actualPosition)
            {
                if (debug) log.Debug("Rejected change of strategy " + Id + " for " + Symbol + "position " + actualPosition + " to " + position + ".  Recency " + recency + " wasn't newer than " + this.recency);
            }
        }
	}
	public interface LogicalOrderCache
	{
		StrategyPosition GetStrategyPosition(int id);
		LogicalOrder FindLogicalOrder(int id);
		void SetActiveOrders(Iterable<LogicalOrder> inputOrders);
		Iterable<LogicalOrder> ActiveOrders { get; }
		void RemoveInactive(LogicalOrder order);
        void SyncPositions(Iterable<StrategyPosition> strategyPositions);
	}
}
