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
		public int Count;
		public int Id;
		public long TickTime;
		public long UtcTime;
	    public int Selects;
	    public int TryReceive;
	    public int Receives;
	    public int Sends;
	    public int RoundRobin;
	    public int Earliest;
	}
	public class LatencyManager : IDisposable
	{
	    public static long TryReadCounter = 0;
		private static readonly Log log = Factory.Log.GetLogger(typeof(LatencyManager));
		private static readonly bool debug = log.IsDebugEnabled;
		private static int count;
		private object listMapLocker = new object();
		private Dictionary<long,LinkedList<LatencyMetric>> listMap = new Dictionary<long,LinkedList<LatencyMetric>>();
		private static LatencyManager instance;
		private DataSeries<LatencyLogEntry> latencyLog = Factory.Engine.Series<LatencyLogEntry>();
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

	    private long lastSelectCount;
	    private long lastRoundRobinCount;
	    private long lastEarliestCount;
	    private long lastReceiveCount;
	    private long lastTryReadCount;
	    private long lastSendCount;
	    private int previousId = 0;
	    private int idCounter = 0;
		public void Log( int id, long utcTickTime)
		{
		    var showLog = false;
            if (id == previousId)
            {
                idCounter++;
            }
            else
            {
                if (previousId == 0 && idCounter >= 10)
                {
                    showLog = true;
                }
                idCounter = 0;
            }
            var selectCount = Factory.Provider.Manager.SelectCount;
		    var roundRobinCounter = Factory.Parallel.RoundRobinCounter;
		    var earliestCounter = Factory.Parallel.EarliestCounter;
		    var sendCounter = Factory.Provider.Manager.SendCounter;
		    var receiveCounter = Factory.Provider.Manager.ReceiveCounter;
		    var tryReadCounter = Interlocked.Read(ref TryReadCounter);
		    var entry = new LatencyLogEntry {
                Count = latencyLog.BarCount,
                Id = id,
                TickTime = utcTickTime,
                UtcTime = TimeStamp.UtcNow.Internal,
                Selects = (int)(selectCount - lastSelectCount),
                TryReceive = (int) (tryReadCounter - lastTryReadCount),
                Receives = (int) (receiveCounter - lastReceiveCount),
                Sends = (int) (sendCounter - lastSendCount),
                RoundRobin = (int) (roundRobinCounter - lastRoundRobinCount),
                Earliest = (int) (earliestCounter - lastEarliestCount),
			};
		    lastSelectCount = selectCount;
		    lastTryReadCount = tryReadCounter;
		    lastReceiveCount = receiveCounter;
		    lastSendCount = sendCounter;
		    lastRoundRobinCount = roundRobinCounter;
		    lastEarliestCount = earliestCounter;
			latencyLogLocker.Lock();
			latencyLog.Add(entry);
            latencyLogLocker.Unlock();
		    previousId = id;
            //if (latencyLog.BarCount == 500)
            ////if (false && showLog)
            //{
            //    log.Info(LatencyManager.GetInstance().GetStats() + "\n" + Factory.Parallel.GetStats());
            //    log.Info("Latency log:\n" + LatencyManager.GetInstance().WriteLog(600));
            //    System.Diagnostics.Debugger.Break();
            //}
        }
		
		public int LogCount {
			get {
				return latencyLog.Count;
			}
		}
		
		public string WriteLog(int entries) {
			using( latencyLogLocker.Using()) {
				var sb = new StringBuilder();
				if( latencyLog.Count > 0) {
					var begin = Math.Max(0,Math.Min(entries,latencyLog.Count)-1);
					var startTime = latencyLog[begin].UtcTime;
					for( int i=begin; i>=0; i--) {
						var entry = latencyLog[i];
					    var latency = entry.UtcTime - entry.TickTime;
					    var tickTime = new TimeStamp(entry.TickTime).TimeOfDay;
					    var tickTimeStr = tickTime + "." + tickTime.Microseconds;
					    var time = new TimeStamp(entry.UtcTime).TimeOfDay;
					    var timeStr = time + "." + time.Microseconds;
						sb.AppendLine( entry.Count + ": " + entry.Id + " => " + tickTimeStr + " at " + timeStr + " latency " + latency + "us, selects " + entry.Selects + ", send " + entry.Sends + ", receive " + entry.Receives + ", (try " + entry.TryReceive + "), roundR " + entry.RoundRobin + ", earliest " + entry.Earliest);
					}
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
