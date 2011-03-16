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
using System.Threading;

namespace TickZoom.Api
{
	public class TaskLock : IDisposable {
	    private int isLocked = UNLOCKED;
	    private int lockCount = 0;
	    private const int UNLOCKED = int.MinValue;
	    
		public bool AlreadyLocked {
			get { return isLocked != UNLOCKED && isLocked == Thread.CurrentThread.ManagedThreadId; }
		}
	    
		public bool WillBlock {
			get { return isLocked != UNLOCKED && isLocked != Thread.CurrentThread.ManagedThreadId; }
		}

        public bool TryLock(int threadId)
        {
            if (isLocked == threadId)
            {
                Interlocked.Increment(ref lockCount);
                return true;
            }
            else if (isLocked == UNLOCKED && Interlocked.CompareExchange(ref isLocked, threadId, UNLOCKED) == UNLOCKED)
            {
                Interlocked.Increment(ref lockCount);
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool TryLock()
        {
	    	return TryLock(Thread.CurrentThread.ManagedThreadId);
	    }
	    
		public void Lock() {
			while( !TryLock());
	    }

        public void Lock(int threadId)
        {
            while (!TryLock(threadId)) ;
        }

        public TaskLock Using(int threadId)
        {
            Lock(threadId);
            return this;
        }
        public TaskLock Using()
        {
	    	Lock();
	    	return this;
	    }

        public void Unlock()
        {
            Unlock( Thread.CurrentThread.ManagedThreadId);
        }
	    
	    public void Unlock(int threadId) {
	    	if( isLocked == threadId) {
	    		var count = Interlocked.Decrement(ref lockCount);
	    		if( count <= 0) {
	    			ForceUnlock();
	    		}
	    	} else if( isLocked == UNLOCKED) {
	    		string message = "Attempt to unlock when already unlocked on thread id: " + threadId;
	    		throw new ApplicationException( message);
	    	} else {
	    		string message = "Attempt to unlock from a different managed thread. Expected: " + isLocked + " but was " + threadId;
	    		throw new ApplicationException( message);
	    	}
	    }
	    
	    public void ForceUnlock() {
	    	Interlocked.Exchange(ref lockCount, 0);
	    	Interlocked.Exchange(ref isLocked, UNLOCKED);
	    }
		
		public void Dispose()
		{
			Unlock();
		}
	}

}
