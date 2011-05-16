using TickZoom.Api;

namespace TickZoom.Common
{
    public class PhysicalOrderCache
    {
        private static readonly Log staticLog = Factory.SysLog.GetLogger(typeof(PhysicalOrderCache));
        private readonly bool trace = staticLog.IsTraceEnabled;
        private readonly bool debug = staticLog.IsDebugEnabled;
        private Log log;
        private ActiveList<PhysicalOrder> createOrderQueue = new ActiveList<PhysicalOrder>();
        private ActiveList<string> cancelOrderQueue = new ActiveList<string>();
        private ActiveList<ChangeOrderEntry> changeOrderQueue = new ActiveList<ChangeOrderEntry>();

        public PhysicalOrderCache(string name, SymbolInfo symbol)
        {
            this.log = Factory.SysLog.GetLogger(typeof(PhysicalOrderCache).FullName + "." + symbol.Symbol.StripInvalidPathChars() + "." + name);
        }

        public struct ChangeOrderEntry
        {
            public PhysicalOrder Order;
            public string OrigOrderId;
        }

        public Iterable<PhysicalOrder> CreateOrderQueue
        {
            get { return createOrderQueue; }
        }

        private bool HasCreateOrder(PhysicalOrder order)
        {
            for (var current = CreateOrderQueue.First; current != null; current = current.Next)
            {
                var queueOrder = current.Value;
                if (order.LogicalSerialNumber == queueOrder.LogicalSerialNumber)
                {
                    if (debug) log.Debug("Create ignored because order was already on create order queue: " + queueOrder);
                    return true;
                }
            }
            return false;
        }

        public bool AddCreateOrder(PhysicalOrder order)
        {
            var result = !HasCreateOrder(order);
            if( !result)
            {
                createOrderQueue.AddLast(order);
            }
            return result;
        }

        public void Clear()
        {
            createOrderQueue.Clear();
            cancelOrderQueue.Clear();
            changeOrderQueue.Clear();
        }
    }
}