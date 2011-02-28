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
using System.Text;
using System.Threading;

namespace TickZoom.Api
{
	public struct LatencyLogEntry {
		public int Id;
		public int TickTime;
		public int UtcTime;
	}
	public class LatencyManager : IDisposable {
		private static readonly Log log = Factory.Log.GetLogger(typeof(LatencyManager));
		private static readonly bool debug = log.IsDebugEnabled;
		private static int count;
		private object listMapLocker = new object();
		private Dictionary<long,LinkedList<LatencyMetric>> listMap = new Dictionary<long,LinkedList<LatencyMetric>>();
		private static LatencyManager instance;
		private DataSeries<LatencyLogEntry> latencyLog = Factory.Engine.Series<LatencyLogEntry>();
		private int logIndex = 0;
		private long latencyLogStartTime = TimeStamp.UtcNow.Internal;
		private TaskLock latencyLogLocker = new TaskLock();
		
		public static LatencyManager GetInstance() {
			if( instance == null) {
				instance = new LatencyManager();
			}
			return instance;
		}
		
		public static LatencyManager Register(LatencyMetric metric, out int id, out int count, out LatencyMetric previous) {
			GetInstance().Register(metric, out previous);
			id = instance.GetNextId();
			count = instance.Count;
			return instance;
		}
		
		public void Log( int id, long utcTickTime) {
			var entry = new LatencyLogEntry {
				Id = id,
				TickTime = (int) (utcTickTime - latencyLogStartTime) / 100,
				UtcTime = (int) (TimeStamp.UtcNow.Internal - latencyLogStartTime) / 100,
			};
			using( latencyLogLocker.Using()) {
				latencyLog.Add(entry);
			}
		}
		
		public string WriteLog(int entries) {
			using( latencyLogLocker.Using()) {
				var begin = Math.Max(0,Math.Min(entries,latencyLog.Count)-1);
				var startTime = latencyLog[begin].UtcTime;
				var sb = new StringBuilder();
				for( int i=begin; i>=0; i--) {
					var entry = latencyLog[i];
					sb.AppendLine( entry.Id + " => " + entry.TickTime + " at " + entry.UtcTime + " latency " + (entry.UtcTime - entry.TickTime) + ")");
				}
				return sb.ToString();
			}
		}
		
		private void Register( LatencyMetric metric, out LatencyMetric previous) {
			LinkedList<LatencyMetric> list;
			lock( listMapLocker) {
				if( !listMap.TryGetValue(metric.Symbol, out list)) {
					list = new LinkedList<LatencyMetric>();
					listMap[metric.Symbol] = list;
				}
				previous = list.Count > 0 ? list.Last.Value : null;
				list.AddLast( metric);
			}
		}
		
		public int GetNextId() {
			return Interlocked.Increment(ref count) - 1;
		}
		
		public int Count {
			get { return count; }
		}
		
	 	private volatile bool isDisposed = false;
	 	private object disposeLocker = new object();
	    public void Dispose() 
	    {
	        Dispose(true);
	        GC.SuppressFinalize(this);      
	    }
	
	    protected virtual void Dispose(bool disposing)
	    {
	       	if( !isDisposed) {
	    		lock( disposeLocker) {
		            isDisposed = true;   
		            if (disposing) {
//						if( debug) log.Debug("Dispose()");
		            	instance = new LatencyManager();
		            }
	    		}
	    	}
	    }
	    
	    public string GetStats() {
	    	var sb = new StringBuilder();
	    	lock( listMapLocker) {
		    	foreach( var kvp in listMap) {
		        	var symbol = kvp.Key;
		        	var list = kvp.Value;
		        	var previous = list.First.Value;
		        	foreach( var metric in list) {
		        		sb.Append( metric.GetStats(previous));
		        		sb.AppendLine();
		        		previous = metric;	
		        	}
		    	}
	    	}
	    	return sb.ToString();
	    }
	}
}
