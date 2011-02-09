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
using System.Threading;

using log4net.Core;
using TickZoom.Api;

namespace TickZoom.Logging
{
	/// <summary>
	/// Description of Class1.
	/// </summary>
	public class LoggingQueue
	{
	    System.Collections.Generic.Queue<LoggingEvent> queue =
	    	new System.Collections.Generic.Queue<LoggingEvent>();
	    int maxSize = int.MaxValue;
	    TaskLock locker = new TaskLock();
	    
	    public LoggingQueue() {
	    }
	    
	    public LoggingQueue(int maxSize) {
	    	this.maxSize = maxSize;
	    }
	    
	    public void EnQueue(LoggingEvent o)
	    {
	    	using( locker.Using()) {
	            // If the queue is full, wait for an item to be removed
	            if( queue.Count > maxSize) {
	            	throw new ApplicationException("GUI Log queue is full.");
	            }
	            
	            queue.Enqueue(o);
	    	}
	    }
	    
	    public bool Dequeue(out LoggingEvent loggingEvent)
	    {
	    	using( locker.Using()) {
	            // If the queue is empty, wait for an item to be added
	            // Note that this is a while loop, as we may be pulsed
	            // but not wake up before another thread has come in and
	            // consumed the newly added object. In that case, we'll
	            // have to wait for another pulse.
	            if(queue.Count==0)
	            {
	            	loggingEvent = null;
	            	return false;
	            } else {
		            loggingEvent = queue.Dequeue();
	            	return true;
	            }
	    	}
	    }
	    
	    public void Clear() {
	    	using( locker.Using()) {
	        	queue.Clear();
	    	}
	    }
	    
	    public int Count {
	    	get { return queue.Count; }
	    }
	
	}

}
