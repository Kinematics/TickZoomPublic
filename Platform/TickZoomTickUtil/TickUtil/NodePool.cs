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
using TickZoom.Api;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace TickZoom.TickUtil
{
	public class NodePool<T>
	{
		private Stack<LinkedListNode<T>> _nodes = new Stack<LinkedListNode<T>>();
        private SimpleLock _sync = new SimpleLock();
		private int count = 0;
	
		public LinkedListNode<T> Create(T item)
		{
			using (_sync.Using()) {
				if (_nodes.Count == 0) {
					Interlocked.Increment(ref count);
					return new LinkedListNode<T>(item);
				} else {
					var node = _nodes.Pop();
					node.Value = item;
					return node;
				}
			}
		}
	
		public void Free(LinkedListNode<T> node)
		{
			using(_sync.Using()) {
//                Debug.Assert(!_nodes.Contains(node));
				_nodes.Push(node);
			}
		}

		public void Clear()
		{
			using(_sync.Using()) {
				_nodes.Clear();
			}
		}
		
		public int Count {
			get { return count; }
		}
	}
}


