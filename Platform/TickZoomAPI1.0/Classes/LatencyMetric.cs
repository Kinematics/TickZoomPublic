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
		private LatencyMetric previous;
		private Log log;
		private bool debug;
		private bool trace;
		private int id = int.MinValue;
		private long symbol;
		private const int averageLength = 10;
		private Longs latencies = Factory.Engine.Longs(100);
		private long lastSelf;
		private long total;
		private long totalSelf;
		private long count;
		private long tickCount;
		private int metricCount;
		private LatencyManager manager;
		private string name;
		private TaskLock locker = new TaskLock();
		
		public LatencyMetric(string name) {
			this.name = name;
			this.log = Factory.SysLog.GetLogger(typeof(LatencyMetric).FullName + "." + name);
			this.debug = log.IsDebugEnabled;
			this.trace = log.IsTraceEnabled;
		}
		
		public void TryUpdate( long symbol, long timeStamp) {
            if( timeStamp == long.MaxValue || timeStamp == 0L) return;
		    if( id == int.MinValue) {
				this.symbol = symbol;
				this.manager = LatencyManager.Register(this, out id, out metricCount, out previous);
				if( trace) log.Trace( "Registered " + name + " metric (" + id + ") on tick " + new TimeStamp( timeStamp) + ")");
			}
            if (debug)
            {
                manager.Log(id, timeStamp);
                Update(timeStamp);
                tickCount++;
            }
		}
		
		private void Update( long timeStamp) {
			using( locker.Using()) {
				var current = TimeStamp.UtcNow.Internal;
				var latency = current - timeStamp;
				metricCount = manager.Count;
				
				latencies.Add(latency);
				if( previous != null) {
					using( previous.locker.Using()) {
						var prevIndex = (int) (previous.count - count);
						if( prevIndex >= 0 && prevIndex < previous.latencies.Count) {
							lastSelf = latency - previous.latencies[prevIndex];
						}
					}
				}
				total += latency;
				if( averageLength >=0 && latencies.Count > averageLength) {
					total -= latencies[averageLength];
				} else {
					count ++;
				}
				if( previous != null) {
					totalSelf = total - previous.total;
				}
			}
		}
		
		public long Average {
			get {
				return count == 0 ? 0 : total / Math.Min(latencies.Count,averageLength);
			}
		}
		
		public long AverageSelf {
			get {
				return count == 0 || previous == null ? 0 : totalSelf / Math.Min(latencies.Count,averageLength);
			}
		}
		
		public string GetStats(LatencyMetric previous)
		{
			using( locker.Using()) {
				var sb = new StringBuilder();
				sb.Append( name);
				sb.Append( " (");
				sb.Append( id);
				sb.Append( "): ");
				sb.Append( "tick count " );
				sb.Append( tickCount);
				sb.Append( ", count " );
				sb.Append( count);
				sb.Append( ", total " );
				sb.Append( total);
				if( previous != null) {
					sb.Append( " (self ");
					sb.Append( totalSelf);
					sb.Append( ")");
				}
				sb.Append( ", last " );
				sb.Append( latencies[0]);
				if( previous != null) {
					sb.Append( " (self ");
					sb.Append( lastSelf);
					sb.Append( ")");
				}
				sb.Append( ", average " );
				var average = Average;
				if( average != 0 && previous.Average != 0) {
					sb.Append( Average);
					sb.Append( " (self ");
					sb.Append( AverageSelf);
					sb.Append( ")");
				} else {
					sb.Append( "Initializing...");
				}
				if( lastSelf > 1000 || AverageSelf > 1000 || (id == 0 && Average > 1000) ) {
					sb.AppendLine();
					sb.Append("Latencies: ");
					for( var i = 0; i<latencies.Count; i++) {
						if( i!=0) sb.Append(", ");
					    var prevIndex = (int) (previous.count - count) + i;
					    if( prevIndex >= 0 && prevIndex < previous.latencies.Count) {
							var latencySelf = id == 0 ? latencies[i] : latencies[i] - previous.latencies[prevIndex];
							sb.Append( latencySelf );
					    }
					}
				}
				
				return sb.ToString();
			}
		}
		
		public long Symbol {
			get {
				return symbol;
			}
		}
	}
}
