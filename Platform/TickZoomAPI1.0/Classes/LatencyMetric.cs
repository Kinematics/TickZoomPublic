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
using System.Text;

namespace TickZoom.Api
{
	public class LatencyMetric {
		private Log log;
		private bool debug;
		private bool trace;
		private int id = int.MinValue;
		private long symbol;
		private long totalMicroseconds;
		private long count;
		private long tickCount;
		private int metricCount;
		private LatencyManager manager;
		private string name;
		
		public LatencyMetric(string name) {
			this.name = name;
			this.log = Factory.SysLog.GetLogger(typeof(LatencyMetric).FullName + "." + name);
			this.debug = log.IsDebugEnabled;
			this.trace = log.IsTraceEnabled;
		}
		
		public void TryUpdate( long symbol, long timeStamp) {
			if( debug) {
			    if( id == int.MinValue) {
					this.symbol = symbol;
					this.manager = LatencyManager.Register(this, out id, out metricCount);
					if( trace) log.Trace( "Registered " + name + " metric (" + id + ") on tick " + new TimeStamp( timeStamp) + ")");
				}
				Update( timeStamp);
				tickCount++;
			}
		}
		
		private void Update( long timeStamp) {
			var current = TimeStamp.UtcNow.Internal;
			var latency = current - timeStamp;
			metricCount = manager.Count;
			
			if( count > 10) {
				var average = totalMicroseconds / count;
				totalMicroseconds -= average;
				totalMicroseconds += latency;
			} else {
				totalMicroseconds += latency;
				count++;
			}
		}
		
		public long Average {
			get {
				return count == 0 ? 0 : totalMicroseconds / count;
			}
		}
		
		public string GetStats()
		{
			var sb = new StringBuilder();
			sb.Append( name);
			sb.Append( " (");
			sb.Append( id);
			sb.Append( "): ");
			sb.Append( ", tick count " );
			sb.Append( tickCount);
			sb.Append( ", count " );
			sb.Append( count);
			sb.Append( ", total " );
			sb.Append( totalMicroseconds);
			sb.Append( ", average " );
			var average = Average;
			if( average != 0) {
				sb.Append( Average);
			} else {
				sb.Append( "Initializing...");
			}
			
			return sb.ToString();
		}
		
		public long Symbol {
			get {
				return symbol;
			}
		}
	}
}
