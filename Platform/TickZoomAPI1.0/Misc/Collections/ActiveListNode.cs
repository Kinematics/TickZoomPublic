using System;
using System.Threading;

namespace TickZoom.Api
{
    public sealed class ActiveListNode<T>
    {
        // Fields
        internal T item;
        internal ActiveList<T> list;
        internal ActiveListNode<T> next;
        internal ActiveListNode<T> prev;
        private SimpleLock locker = new SimpleLock();

        // Methods
        public ActiveListNode(T value)
        {
            this.item = value;
        }

        internal ActiveListNode(ActiveList<T> list, T value)
        {
            if( list == null)
            {
                throw new InvalidOperationException("list");
            }
            Interlocked.Exchange(ref this.list, list);
            item = value;
        }

        internal void Invalidate()
        {
            locker.Lock();
            // Keep next or prev unless this is the last or first node, respectively.
            // That way a read loop may continue to loop to the next (or prev) item
            // even if this one got deleted.
            if (this.next == this.list.head)
            {
                Interlocked.Exchange(ref this.next, null);
            }
            if (this.prev == this.list.head)
            {
                Interlocked.Exchange(ref this.prev, null);
            }
            Interlocked.Exchange(ref this.list, null);
            locker.Unlock();
        }

        // Properties
        public ActiveList<T> List
        {
            get
            {
                return this.list;
            }
        }

        public ActiveListNode<T> Next
        {
            get
            {
                locker.Lock();
                if (this.list == null)
                {
                    locker.Unlock();
                    return this.next;
                }
                else if (this.next == this.list.head)
                {
                    locker.Unlock();
                    return null;
                }
                locker.Unlock();
                return this.next;
            }
        }

        public ActiveListNode<T> Previous
        {
            get
            {
                locker.Lock();
                if (this.list == null)
                {
                    locker.Unlock();
                    return this.prev;
                } else if(this == this.list.head)
                {
                    locker.Unlock();
                    return null;
                }
                locker.Unlock();
                return this.prev;
            }
        }

        public T Value
        {
            get
            {
                return this.item;
            }
            set
            {
                this.item = value;
            }
        }
    }
}