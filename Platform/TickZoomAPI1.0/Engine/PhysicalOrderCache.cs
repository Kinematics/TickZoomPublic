using System;
using System.Collections.Generic;

namespace TickZoom.Api
{
    public interface PhysicalOrderCache : IDisposable
    {
        void SetOrder(CreateOrChangeOrder order);
        CreateOrChangeOrder RemoveOrder(string clientOrderId);
        Iterable<CreateOrChangeOrder> GetActiveOrders(SymbolInfo symbol);
        bool HasCancelOrder(PhysicalOrder order);
        bool HasCreateOrder(CreateOrChangeOrder order);
    }

    public interface PhysicalOrderStore : PhysicalOrderCache
    {
        void OpenFile();
        string DatabasePath { get; }
        long SnapshotRolloverSize { get; set; }
        int RemoteSequence { get; }
        int LocalSequence { get; }
        void TrySnapshot();
        void ForceSnapShot();
        void WaitForSnapshot();
        IEnumerable<CreateOrChangeOrder> OrderReferences(CreateOrChangeOrder order);
        bool Recover();
        void Clear();
        bool TryGetOrderById(string brokerOrder, out CreateOrChangeOrder order);
        bool TryGetOrderBySequence(int sequence, out CreateOrChangeOrder order);
        CreateOrChangeOrder GetOrderById(string brokerOrder);
        CreateOrChangeOrder RemoveOrder(string clientOrderId);
        bool TryGetOrderBySerial(long logicalSerialNumber, out CreateOrChangeOrder order);
        CreateOrChangeOrder GetOrderBySerial(long logicalSerialNumber);
        void UpdateSequence(int remoteSequence, int localSequence);
        void SetSequences(int remoteSequence, int localSequence);
        void ClearPendingOrders(SymbolInfo symbol);
        List<CreateOrChangeOrder> GetOrders(Func<CreateOrChangeOrder, bool> select);
        string LogOrders();
        void Dispose();
        int Count();
    }
}