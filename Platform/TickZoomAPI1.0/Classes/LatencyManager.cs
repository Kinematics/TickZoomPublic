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
	    public int Analyze;
	    public int Simulator;
	}
	public class LatencyManager : IDisposable
	{
	    public static long TryReadCounter = 0;
		private static readonly Log log = Factory.Log.GetLogger(typeof(LatencyManager));
		private static readonly bool debug = log.IsDebugEnabled;
		private static int count;
		private object listMapLocker = new object();
		private Dictionary<long,ActiveList<LatencyMetric>> listMap = new Dictionary<long,ActiveList<LatencyMetric>>();
		private static LatencyManager instance;
	    private LatencyLogEntry[] latencyLog;
        //private DataSeries<LatencyLogEntry> latencyLog = Factory.Engine.Series<LatencyLogEntry>();
		private long latencyLogStartTime = TimeStamp.UtcNow.Internal;
		private TaskLock latencyLogLocker = new TaskLock();

        private void AddLatency( ref LatencyLogEntry entry)
        {
            var index = Interlocked.Increment(ref latencyLogIndex);
            latencyLog[index-1] = entry;
        }

		public static LatencyManager GetInstance() {
			if( instance == null) {
				instance = new LatencyManager();
			}
			return instance;
		}

	    private static long simulatorCount = 0;

        public static void IncrementSymbolHandler()
        {
            Interlocked.Increment(ref simulatorCount);
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
        private volatile bool alreadyShowedLog = false;
        private volatile int logCount = 0;
	    private volatile int tickCount = 0;
	    private int lastId = 0;
		public void Log( int id, long utcTickTime)
		{
            if( latencyLog == null)
            {
                latencyLog = new LatencyLogEntry[2000000];
            }
            if( id < lastId)
            {
                Interlocked.Increment(ref tickCount);
            }
            lastId = id;
            var currentTime = TimeStamp.UtcNow.Internal;
		    var latency = currentTime - utcTickTime;
            if( latency < 1000)
            {
                alreadyShowedLog = false;
            }
		    var showLog = tickCount > 100 && latency > 2000;
            var selectCount = Factory.Provider.Manager.SelectCount;
		    var roundRobinCounter = Factory.Parallel.RoundRobinCounter;
		    var earliestCounter = Factory.Parallel.EarliestCounter;
            var sendCounter = Factory.Provider.Manager.SendCounter;
		    var receiveCounter = Factory.Provider.Manager.ReceiveCounter;
		    var tryReadCounter = Interlocked.Read(ref TryReadCounter);
            var analyze = (int) Factory.Parallel.AnalyzePoint;
		    var simulator = simulatorCount;
		    var entry = new LatencyLogEntry
            {
                Count = tickCount,
                Id = id,
                TickTime = utcTickTime,
                UtcTime = currentTime,
                Selects = (int) (selectCount - lastSelectCount),
                TryReceive = (int) (tryReadCounter - lastTryReadCount),
                Receives = (int) (receiveCounter - lastReceiveCount),
                Sends = (int) (sendCounter - lastSendCount),
                RoundRobin = (int) (roundRobinCounter - lastRoundRobinCount),
                Earliest = (int) (earliestCounter - lastEarliestCount),
                // - lastEarliestCount),
                Analyze = analyze,
                Simulator = (int) (simulator - lastSimulatorCount),
			};
		    lastSelectCount = selectCount;
		    lastTryReadCount = tryReadCounter;
		    lastReceiveCount = receiveCounter;
		    lastSendCount = sendCounter;
		    lastRoundRobinCount = roundRobinCounter;
		    lastEarliestCount = earliestCounter;
		    lastSimulatorCount = simulator;
			latencyLogLocker.Lock();
			AddLatency(ref entry);
            latencyLogLocker.Unlock();
            if (showLog && !alreadyShowedLog)
            {
                alreadyShowedLog = true;
                try { throw new ExceededLatencyException(); }
                catch { }
                log.Info("Latency exceed limit at " + latency + "ms.");
                log.Info("Latency log:\n" + LatencyManager.GetInstance().WriteLog(1000));
                log.Info(LatencyManager.GetInstance().GetStats() + "\n" + Factory.Parallel.GetStats());
                System.Diagnostics.Debugger.Break();
                logCount++;
            }
        }

	    private int x = 0;

        public class ExceededLatencyException : Exception { }
		
		public int LogCount {
			get {
				return latencyLog.Length;
			}
		}

        public string WriteLog()
        {
            return WriteLog(0);
        }

	    public string WriteLog(int entries) {
			using( latencyLogLocker.Using()) {
				var sb = new StringBuilder();
				if( LogCount > 0)
				{
				    var begin = latencyLogIndex - entries;
                    begin = begin > 0 ? begin : 0;
					var startTime = latencyLog[0].UtcTime;
					for( int i=begin; i<latencyLogIndex; i++) {
						var entry = latencyLog[i];
                        if (entry.Id == 16) sb.AppendLine();
					    var latency = entry.UtcTime - entry.TickTime;
                        var tickTime = new TimeStamp(entry.TickTime).TimeOfDay;
                        var tickTimeStr = tickTime + "." + tickTime.Microseconds;
                        var time = new TimeStamp(entry.UtcTime).TimeOfDay;
                        var timeStr = time + "." + time.Microseconds;
                        sb.AppendLine(entry.Count + ": " + entry.Id + " => " + tickTimeStr + " at " + timeStr + " latency " + latency + "us, selects " + entry.Selects + ", send " + entry.Sends + ", receive " + entry.Receives + ", (try " + entry.TryReceive + "), roundR " + entry.RoundRobin + ", earliest " + entry.Earliest + ", analyze " + entry.Analyze + ", simulator " + entry.Simulator);
                        if (i % 2000 == 0)
                        {
                            log.Info("Up to " + i + "\n" + sb);
                            sb.Length = 0;
                        }
					}
				}
				return sb.ToString();
			}
		}
		
		private void Register( LatencyMetric metric, out LatencyMetric previous) {
			ActiveList<LatencyMetric> list;
			lock( listMapLocker) {
				if( !listMap.TryGetValue(metric.Symbol, out list)) {
					list = new ActiveList<LatencyMetric>();
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

	    public int TickCount
	    {
	        get { return tickCount; }
	    }

	    private volatile bool isDisposed = false;
	 	private object disposeLocker = new object();
	    private int latencyLogIndex = 0;
	    private decimal lastSimulatorCount;

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
		    	    var next = list.First;
		    	    for (var current = next; current != null; current = next)
		    	    {
		    	        next = current.Next;
		    	        var metric = current.Value;
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
