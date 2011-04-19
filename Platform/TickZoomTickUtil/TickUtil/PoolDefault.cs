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
using TickZoom.Api;
using System.Diagnostics;

namespace TickZoom.TickUtil
{
    public class PoolDefault<T> : Pool<T> where T : new()
	{
		private Stack<T> _items = new Stack<T>();
        private SimpleLock _sync = new SimpleLock();
		private int count = 0;
        private Queue<T> _freed = new Queue<T>();

		public T Create()
		{
			using (_sync.Using()) {
				if (_items.Count == 0) {
					Interlocked.Increment(ref count);
					return new T();
				} else {
					return _items.Pop();
				}
			}
		}

		public void Free(T item)
		{
            if( item == null)
            {
                throw new InvalidOperationException("Attempt to free null reference.");
            }
			using (_sync.Using()) {
                _freed.Enqueue(item);
                if (_freed.Count > 10)
                {
                    _items.Push(_freed.Dequeue());
                }
			}
		}

		public void Clear()
		{
			using(_sync.Using()) {
				_items.Clear();
			}
		}
		
		public int Count {
			get { return count; }
		}
	}
}
