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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;

namespace TickZoom.Api
{
	public interface Iterable<T> {
		int Count {
			get;
		}
		ActiveListNode<T> First {
			get;
		}
	}

	/// <summary>
	/// 
	///			var next = list.First;
	///			for( var node = next; node != null; node = next) {
	///				next = node.Next;
	///				var other = node.Value;
	/// 
	/// </summary>
	public class ActiveList<T> : Iterable<T> {
        internal ActiveListNode<T> head;
        internal int count;
        private SimpleLock locker = new SimpleLock();
        private static int nextListId = 0;
	    private int id;

        // Methods
        public ActiveList()
        {
            id = Interlocked.Increment(ref nextListId);
        }

        public ActiveListNode<T> AddAfter(ActiveListNode<T> node, T value)
        {
            locker.Lock();
            this.AssertNode(node);
            var newNode = new ActiveListNode<T>(node.list, value);
            this.InterlockedInsertNodeBefore(node.next, newNode);
            locker.Unlock();
            return newNode;
        }

        public void AddAfter(ActiveListNode<T> node, ActiveListNode<T> newNode)
        {
            locker.Lock();
            this.AssertNode(node);
            this.AssertNewNode(newNode);
            this.InterlockedInsertNodeBefore(node.next, newNode);
            locker.Unlock();
        }

        public void AddBefore(ActiveListNode<T> node, ActiveListNode<T> newNode)
        {
            locker.Lock();
            this.AssertNode(node);
            this.AssertNewNode(newNode);
            this.InterlockedInsertNodeBefore(node, newNode);
            if (node == this.head)
            {
                Interlocked.Exchange(ref this.head, newNode);
            }
            locker.Unlock();
        }

        public ActiveListNode<T> AddBefore(ActiveListNode<T> node, T value)
        {
            locker.Lock();
            this.AssertNode(node);
            var newNode = new ActiveListNode<T>(node.list, value);
            this.InterlockedInsertNodeBefore(node, newNode);
            if (node == this.head)
            {
                Interlocked.Exchange(ref this.head, newNode);
            }
            locker.Unlock();
            return newNode;
        }

        public void AddFirst(ActiveListNode<T> node)
        {
            locker.Lock();
            this.AssertNewNode(node);
            if (this.head == null)
            {
                this.InterlockedInsertNodeToEmptyList(node);
            }
            else
            {
                this.InterlockedInsertNodeBefore(this.head, node);
                Interlocked.Exchange(ref this.head, node);
            }
            locker.Unlock();
        }

        public ActiveListNode<T> AddFirst(T value)
        {
            locker.Lock();
            var newNode = new ActiveListNode<T>((ActiveList<T>)this, value);
            if (this.head == null)
            {
                this.InterlockedInsertNodeToEmptyList(newNode);
            } else
            {
                this.InterlockedInsertNodeBefore(this.head, newNode);
                Interlocked.Exchange(ref this.head, newNode);
            }
            locker.Unlock();
            return newNode;
        }

        public ActiveListNode<T> SortFirst(T value, Func<T, T, int> comparator)
        {
            var newNode = new ActiveListNode<T>((ActiveList<T>)this, value);
            locker.Lock();
            if (this.head == null)
            {
                this.InterlockedInsertNodeToEmptyList(newNode);
                locker.Unlock();
                return newNode;
            }

            var node = this.head;
            do
            {
                if (comparator(node.Value, value) > 0)
                {
                    this.InterlockedInsertNodeBefore(node, newNode);
                    if (node == this.head)
                    {
                        Interlocked.Exchange(ref this.head, newNode);
                    }
                    locker.Unlock();
                    return newNode;
                }
                node = node.Next;
            } while (node != null);
            this.InterlockedInsertNodeBefore(this.head, newNode);
            locker.Unlock();
            return newNode;
        }

        public void ResortFirst(ActiveListNode<T> newNode, Func<T, T, int> comparator)
        {
            if( newNode == null)
            {
                throw new ArgumentNullException("newNode");
            }
            locker.Lock();
            if (newNode.list == this)
            {
                this.InterlockedRemoveNode(newNode);
            }
            if( newNode.list != null)
            {
                throw new InvalidOperationException("newNode belongs to a different list");
            }
            if (this.head == null)
            {
                this.InterlockedInsertNodeToEmptyList(newNode);
                locker.Unlock();
                return;
            }

            var node = this.head;
            do
            {
                if (comparator(node.Value, newNode.Value) > 0)
                {
                    this.InterlockedInsertNodeBefore(node, newNode);
                    if (node == this.head)
                    {
                        Interlocked.Exchange(ref this.head, newNode);
                    }
                    locker.Unlock();
                    return;
                }
                node = node.Next;
            } while (node != null);
            this.InterlockedInsertNodeBefore(this.head, newNode);
            locker.Unlock();
            return;
        }

        public ActiveListNode<T> AddLast(T value)
        {
            locker.Lock();
            var newNode = new ActiveListNode<T>((ActiveList<T>)this, value);
            if (this.head == null)
            {
                this.InterlockedInsertNodeToEmptyList(newNode);
                locker.Unlock();
                return newNode;
            }
            this.InterlockedInsertNodeBefore(this.head, newNode);
            locker.Unlock();
            return newNode;
        }

        public void AddLast(Iterable<T> list2)
        {
            locker.Lock();
            if (list2 != null)
            {
                for (var current = list2.First; current != null; current = current.Next)
                {
                    var newNode = new ActiveListNode<T>((ActiveList<T>)this, current.Value);
                    if (this.head == null)
                    {
                        this.InterlockedInsertNodeToEmptyList(newNode);
                    }
                    else
                    {
                        this.InterlockedInsertNodeBefore(this.head, newNode);
                    }
                }
            }
            locker.Unlock();
        }

        public void AddLast(ActiveListNode<T> node)
        {
            locker.Lock();
            this.AssertNewNode(node);
            if (this.head == null)
            {
                this.InterlockedInsertNodeToEmptyList(node);
            }
            else
            {
                this.InterlockedInsertNodeBefore(this.head, node);
            }
            locker.Unlock();
        }

        public void Clear()
        {
            locker.Lock();
            var head = this.head;
            while (head != null)
            {
                ActiveListNode<T> node2 = head;
                node2.Invalidate();
                Interlocked.Exchange(ref head, head.Next);
            }
            Interlocked.Exchange(ref this.head, null);
            this.count = 0;
            locker.Unlock();
        }

        public bool Contains(T value)
        {
            return (this.Find(value) != null);
        }

        public void CopyTo(T[] array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException("array");
            }
            if ((index < 0) || (index > array.Length))
            {
                throw new ArgumentOutOfRangeException("bad index " + index);
            }
            if ((array.Length - index) < this.Count)
            {
                throw new ArgumentException("Not enough space");
            }
            var head = this.head;
            if (head != null)
            {
                do
                {
                    array[index++] = head.item;
                    head = head.next;
                }
                while (head != this.head);
            }
        }

        public ActiveListNode<T> Find(T value)
        {
            var head = this.head;
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            if (head != null)
            {
                if (value != null)
                {
                    do
                    {
                        if (comparer.Equals(head.item, value))
                        {
                            return head;
                        }
                        head = head.next;
                    }
                    while (head != this.head);
                }
                else
                {
                    do
                    {
                        if (head.item == null)
                        {
                            return head;
                        }
                        head = head.next;
                    }
                    while (head != this.head);
                }
            }
            return null;
        }

        private void InterlockedInsertNodeBefore(ActiveListNode<T> node, ActiveListNode<T> newNode)
        {
            Interlocked.Exchange(ref newNode.next, node);
            Interlocked.Exchange(ref newNode.prev, node.prev);
            Interlocked.Exchange(ref node.prev.next, newNode);
            Interlocked.Exchange(ref node.prev, newNode);
            Interlocked.Exchange(ref newNode.list, (ActiveList<T>)this);
            Interlocked.Increment(ref this.count);
        }

        private void InterlockedInsertNodeToEmptyList(ActiveListNode<T> newNode)
        {
            Interlocked.Exchange(ref newNode.next, newNode);
            Interlocked.Exchange(ref newNode.prev, newNode);
            Interlocked.Exchange(ref this.head, newNode);
            Interlocked.Exchange(ref newNode.list, (ActiveList<T>)this);
            Interlocked.Increment(ref this.count);
        }

        internal void InterlockedRemoveNode(ActiveListNode<T> node)
        {
            if (node.next == node)
            {
                node.Invalidate();
                Interlocked.Exchange(ref this.head, null);
            }
            else
            {
                Interlocked.Exchange(ref node.next.prev, node.prev);
                Interlocked.Exchange(ref node.prev.next, node.next);
                node.Invalidate();
                if (this.head == node)
                {
                    Interlocked.Exchange(ref this.head, node.next);
                }
            }
            Interlocked.Decrement(ref this.count);
        }

        public bool Remove(T value)
        {
            locker.Lock();
            var node = this.Find(value);
            if (node != null)
            {
                this.InterlockedRemoveNode(node);
                locker.Unlock();
                return true;
            }
            locker.Unlock();
            return false;
        }

        public bool Remove(ActiveListNode<T> node)
        {
            if( node == null) {
                throw new ArgumentNullException("node");
            }
            locker.Lock();
            if (node.list == null)
            {
                locker.Unlock();
                return true;
            }
            if (node.list != this)
            {
                locker.Unlock();
                throw new InvalidOperationException("node belongs to a different list. null? " + (node.list == null));
            }
            this.InterlockedRemoveNode(node);
            locker.Unlock();
            return true;
        }

        public ActiveListNode<T> RemoveFirst()
        {
            locker.Lock();
            if (this.head == null)
            {
                locker.Unlock();
                throw new InvalidOperationException("empty list");
            }
            var first = this.head;
            this.InterlockedRemoveNode(first);
            locker.Unlock();
            return first;
        }

        public ActiveListNode<T> RemoveLast()
        {
            locker.Lock();
            if (this.head == null)
            {
                locker.Unlock();
                throw new InvalidOperationException("empty list");
            }
            var last = this.head.prev;
            this.InterlockedRemoveNode(last);
            locker.Unlock();
            return last;
        }

        internal void AssertNewNode(ActiveListNode<T> node)
        {
            if (node == null)
            {
                locker.Unlock();
                throw new ArgumentNullException("node");
            }
            if (node.list == this)
            {
                locker.Unlock();
                throw new InvalidOperationException("already in this list for " + node.Value);
            }
            if (node.list != null)
            {
                locker.Unlock();
                throw new InvalidOperationException("already in a different list for " + node.Value);
            }
        }

        internal void AssertNode(ActiveListNode<T> node)
        {
            if (node == null)
            {
                locker.Unlock();
                throw new ArgumentNullException("active list node");
            }
            if (node.list == null)
            {
                if( head == node)
                {
                    locker.Unlock();
                    throw new InvalidOperationException("node removed but head points to node " + node.Value);
                }
                if( count != 0)
                {
                    var current = head;
                    do
                    {
                        if( current.next == node)
                        {
                            locker.Unlock();
                            throw new InvalidOperationException("node removed but a different node next still points to node for " + node.Value);
                        }
                        if( current.prev == node)
                        {
                            locker.Unlock();
                            throw new InvalidOperationException("node removed but a different node prev still points to node for " + node.Value);
                        }
                        current = current.Next;
                    } while (current != null);
                }
                locker.Unlock();
                throw new InvalidOperationException("node not in the list for " + node.Value);
            }
            if (node.list != this)
            {
                if( node.list == null)
                {
                    locker.Unlock();
                    throw new InvalidOperationException("wrong list. node.list is null for " + node.Value);
                }
                if (node.list != null)
                {
                    locker.Unlock();
                    throw new InvalidOperationException("wrong list. mismatch \n node.list " + node.list.id + " " + node.list + "\n this " + this.id + " " + this);
                }
            }
        }

        // Properties
        public int Count
        {
            get
            {
                return this.count;
            }
        }

        public ActiveListNode<T> First
        {
            get
            {
                return this.head;
            }
        }

        public ActiveListNode<T> Last
        {
            get
            {
                ActiveListNode<T> result = null;
                locker.Lock();
                if (this.head != null)
                {
                    result = this.head.prev;
                }
                locker.Unlock();
                return result;
            }
        }
    }
}
