using System;
using System.Collections.Generic;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public class PoolTicks : Pool<TickBinaryBox>
    {
        private Stack<TickBinaryBox> _items = new Stack<TickBinaryBox>();
        private SimpleLock _sync = new SimpleLock();
        private int count = 0;
        private ActiveList<TickBinaryBox> _freed = new ActiveList<TickBinaryBox>();
        private int enqueDiagnoseMetric;
        private int pushDiagnoseMetric;
        private static int nextPoolId = 0;
        private long nextTickId = 0;

        public PoolTicks()
        {
            var id = Interlocked.Increment(ref nextPoolId);
            enqueDiagnoseMetric = Diagnose.RegisterMetric("PoolTicks-Enqueue-"+id);
            pushDiagnoseMetric = Diagnose.RegisterMetric("PoolTicks-Push-"+id);
        }

        public TickBinaryBox Create()
        {
            using (_sync.Using()) {
                if (_items.Count == 0) {
                    Interlocked.Increment(ref count);
                    var box = new TickBinaryBox();
                    box.TickBinary.Id = Interlocked.Increment(ref nextTickId);
                    return box;
                } else {
                    return _items.Pop();
                }
            }
        }

        public void Free(TickBinaryBox item)
        {
            if( item.TickBinary.Id == 0)
            {
                throw new InvalidOperationException("TickBinary id must be non-zero to be freed.");
            }
            if (Diagnose.TraceTicks)
            {
                var binary = item.TickBinary;
                Diagnose.AddTick(enqueDiagnoseMetric,ref binary);
            }
            //for( var node = _freed.First; node != null; node = node.Next)
            //{
            //    if( node.Value.TickBinary.Id == item.TickBinary.Id)
            //    {
            //        Diagnose.LogTicks(300);
            //        System.Diagnostics.Debugger.Break();
            //    }
            //}
            _freed.AddFirst(item);
            if (_freed.Count > 10)
            {
                using( _sync.Using())
                {
                    if (_freed.Count > 10)
                    {
                        var freed = _freed.RemoveLast().Value;
                        if (Diagnose.TraceTicks)
                        {
                            var binary = freed.TickBinary;
                            Diagnose.AddTick(pushDiagnoseMetric, ref binary);
                        }
                        _items.Push(freed);
                    }
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