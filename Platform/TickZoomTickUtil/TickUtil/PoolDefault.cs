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
    public class PoolChecked<T> : Pool<T> where T : new()
    {
        private ActiveList<T> _available = new ActiveList<T>();
        private ActiveList<T> _allocated = new ActiveList<T>();
        private SimpleLock _sync = new SimpleLock();
        private int count = 0;
        private ActiveList<T> _freed = new ActiveList<T>();

        public T Create()
        {
            using (_sync.Using())
            {
                if (_available.Count == 0)
                {
                    Interlocked.Increment(ref count);
                    var value = new T();
                    _allocated.AddLast(value);
                    return value;
                }
                else
                {
                    var node = _available.RemoveFirst();
                    _allocated.AddLast(node);
                    return node.Value;
                }
            }
        }

        public void Free(T item)
        {
            if (item == null)
            {
                throw new InvalidOperationException("Attempt to free null reference.");
            }
            using (_sync.Using())
            {
                for (var current = _allocated.First; current != null; current = current.Next)
                {
                    if( object.ReferenceEquals(current.Value,item))
                    {
                        _allocated.Remove(current);
                        _freed.AddLast(current);
                        if (_freed.Count > 10)
                        {
                            _available.AddLast(_freed.RemoveFirst());
                        }
                        return;
                    }
                }
            }
            // Not on the allocated list. Where is it?
            for( var current = _freed.First; current != null; current = current.Next)
            {
                if( object.ReferenceEquals(item,current.Value))
                {
                    throw new InvalidOperationException("Item is on the pending list already. --> " + item);
                }
            }
            // Okay is it on the already free list?
            for (var current = _available.First; current != null; current = current.Next)
            {
                if (object.ReferenceEquals(item, current.Value))
                {
                    throw new InvalidOperationException("Item is the available list already. --> " + item);
                }
            }
        }

        public void Clear()
        {
            using (_sync.Using())
            {
                _available.Clear();
            }
        }

        public int Count
        {
            get { return count; }
        }

    }

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

        public T[] Freed
        {
            get { return _freed.ToArray(); }
        }
	}
}
