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
	public class LatencyManager : IDisposable {
		private static int count;
		private Dictionary<long,LinkedList<LatencyMetric>> listMap = new Dictionary<long,LinkedList<LatencyMetric>>();
		private static LatencyManager instance;
		
		public static LatencyManager GetInstance() {
			if( instance == null) {
				instance = new LatencyManager();
			}
			return instance;
		}
		public static LatencyManager Register(LatencyMetric metric, out int id, out int count) {
			GetInstance().Register(metric);
			id = instance.GetNextId();
			count = instance.Count;
			return instance;
		}
		
		private void Register( LatencyMetric metric) {
			LinkedList<LatencyMetric> list;
			if( !listMap.TryGetValue(metric.Symbol, out list)) {
				list = new LinkedList<LatencyMetric>();
				listMap[metric.Symbol] = list;
			}
			list.AddLast( metric);
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
	    	foreach( var kvp in listMap) {
	        	var symbol = kvp.Key;
	        	var list = kvp.Value;
	        	var previous = list.First.Value.Average;
	        	foreach( var metric in list) {
	        		sb.Append( metric.GetStats());
	        		sb.Append( ", self ");
	        		if( metric.Average != 0) {
		        		sb.Append( metric.Average - previous);
		        		sb.AppendLine();
	        		}
	        		previous = metric.Average;	
	        	}
	    	}
	    	return sb.ToString();
	    }
	}
}
