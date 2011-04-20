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
	public class StrategyPosition
	{
	    private static readonly Log log = Factory.SysLog.GetLogger(typeof (StrategyPosition));
	    private bool debug = log.IsDebugEnabled;
		public int Id;
        public SymbolInfo Symbol;
        private int _position;
	    private long recency;

        public StrategyPosition()
        {
            if( debug) log.Debug("New StrategyPosition");
        }

	    public int Position
	    {
	        get { return _position; }
	    }

	    public long Recency
	    {
	        get { return recency; }
	    }

        public void SetPosition( int position)
        {
            if (debug) log.Debug("SetPositions() strategy " + Id + " for " + Symbol + " position change from " + _position + " to " + position + ". Recency " + this.recency + " to " + recency);
            _position = position;
        }

	    public void TrySetPosition( int position, long recency)
        {
            if( recency == 0L)
            {
                throw new InvalidOperationException("Recency must be non-zero.");
            }
            if (position != _position)
            {
                if (recency > this.recency)
                {
                    if( debug) log.Debug("Strategy " + Id + " for " + Symbol + " position change from " + _position + " to " + position + ". Recency " + this.recency + " to " + recency);
                    _position = position;
                    this.recency = recency;
                }
                else
                {
                    if (debug) log.Debug("Rejected change of strategy " + Id + " for " + Symbol + "position " + _position + " to " + position +
                             ".  Recency " + recency + " wasn't newer than " + this.recency);
                }
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
	}
}
