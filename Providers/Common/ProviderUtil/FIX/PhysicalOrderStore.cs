using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.FIX
{
    public class LogicalOrderReference
    {
        public long LogicalSerialNumber;
        public CreateOrChangeOrder CreateOrChangeOrder;
    }
    public class PhysicalOrderStore : IDisposable
    {
        private static readonly Log log = Factory.SysLog.GetLogger(typeof(PhysicalOrderStore));
        private static readonly bool info = log.IsDebugEnabled;
        private static readonly bool debug = log.IsDebugEnabled;
        private static readonly bool trace = log.IsTraceEnabled;
        private Dictionary<string, CreateOrChangeOrder> ordersByBrokerId = new Dictionary<string, CreateOrChangeOrder>();
        private Dictionary<long, CreateOrChangeOrder> ordersBySerial = new Dictionary<long, CreateOrChangeOrder>();
        private TaskLock ordersLocker = new TaskLock();
        private int pendingExpireSeconds = 3;
        private string databasePath;
        private FileStream fs;
        private MemoryStream memory = null;
        private BinaryWriter writer = null;
        private BinaryReader reader = null;
        private Dictionary<CreateOrChangeOrder, int> unique = new Dictionary<CreateOrChangeOrder, int>();
        private Dictionary<int,CreateOrChangeOrder> uniqueIds = new Dictionary<int,CreateOrChangeOrder>();
        private Dictionary<int,int> replaceIds = new Dictionary<int,int>();
        private Dictionary<int, int> originalIds = new Dictionary<int, int>();
        private int uniqueId = 0;
        private long snapshotTimer;
        private int snapshotSeconds = 60;
        private Action writeFileAction;
        private IAsyncResult writeFileResult;
        private long snapshotLength = 0;
        private int updateCount = 0;
        private long snapshotRolloverSize = 128*1024;
        private string storeName;
        private string dbFolder;
        private TaskLock snapshotLocker = new TaskLock();
        private int remoteSequence = 0;
        private int localSequence = 0;

        public PhysicalOrderStore(string name)
        {
            storeName = name;
            writeFileAction = SnapShot;
            var appData = Factory.Settings["AppDataFolder"];
            dbFolder = Path.Combine(appData, "DataBase");
            Directory.CreateDirectory(dbFolder);
            databasePath = Path.Combine(dbFolder, name + ".dat");
            OpenSnapShot();
        }

        private void OpenSnapShot()
        {
            fs = new FileStream(databasePath, FileMode.Append, FileAccess.Write, FileShare.Read, 1024, FileOptions.WriteThrough);
            snapshotLength = fs.Length;
            memory = new MemoryStream();
            writer = new BinaryWriter(memory, Encoding.UTF8);
            reader = new BinaryReader(memory, Encoding.UTF8);
        }

        public string DatabasePath
        {
            get { return databasePath; }
        }

        public long SnapshotRolloverSize
        {
            get { return snapshotRolloverSize; }
            set { snapshotRolloverSize = value; }
        }

        public int RemoteSequence
        {
            get { return remoteSequence; }
        }

        public int LocalSequence
        {
            get { return localSequence; }
        }

        private void AddUniqueOrder(CreateOrChangeOrder order)
        {
            int id;
            if( !unique.TryGetValue(order, out id))
            {
                unique.Add(order,++uniqueId);
            }
        }

        public void TrySnapshot()
        {
            if (isDisposed) return;
            updateCount++;
            if (updateCount > 100)
            {
                ForceSnapShot();
            }
        }

        public void ForceSnapShot()
        {
            using( snapshotLocker.Using())
            {
                if( isDisposed) return;
                if (writeFileResult != null)
                {
                    if (writeFileResult.IsCompleted)
                    {
                        writeFileAction.EndInvoke(writeFileResult);
                        writeFileResult = null;
                    }
                }
                if( writeFileResult == null)
                {
                    writeFileResult = writeFileAction.BeginInvoke(null, null);
                    updateCount = 0;
                    snapshotTimer = Factory.TickCount;
                }
            }
        }

        public void WaitForSnapshot()
        {
            while (writeFileResult != null && !writeFileResult.IsCompleted)
            {
                Thread.Sleep(100);
            }
            if (writeFileResult != null && writeFileResult.IsCompleted)
            {
                writeFileAction.EndInvoke(writeFileResult);
                writeFileResult = null;
            }
        }

        public struct SnapshotFile
        {
            public int Order;
            public string Filename;
        }

        private IList<SnapshotFile> FindSnapshotFiles()
        {
            var files = Directory.GetFiles(dbFolder, storeName + ".dat.*", SearchOption.TopDirectoryOnly);
            var fileList = new List<SnapshotFile>();
            foreach (var file in files)
            {
                var parts = file.Split('.');
                if (parts.Length == 3)
                {
                    int count;
                    if (int.TryParse(parts[2], out count) && count > 0)
                    {
                        fileList.Add(new SnapshotFile { Order = count, Filename = file });
                    }
                }
                else
                {
                    fileList.Add(new SnapshotFile { Order = 0, Filename = file });
                }
            }
            fileList.Sort((a,b) => a.Order - b.Order);
            return fileList;
        }

        private void ForceSnapshotRollover()
        {
            if( debug) log.Debug("Creating new snapshot file and rolling older ones to higher number.");
            if (fs != null)
            {
                fs.Close();
            }
            var files = FindSnapshotFiles();
            for (var i = files.Count - 1; i >= 0; i--)
            {
                var count = files[i].Order;
                var source = files[i].Filename;
                if (File.Exists(source))
                {
                    if (count > 9)
                    {
                        File.Delete(source);
                    }
                    else
                    {
                        var replace = Path.Combine(dbFolder, storeName + ".dat." + (count + 1));
                        File.Move(source, replace);
                    }
                }
            }
            OpenSnapShot();
        }

        private void CheckSnapshotRollover()
        {
            if (snapshotLength >= SnapshotRolloverSize || forceSnapShotRollover)
            {
                forceSnapShotRollover = false;
                log.Info("Snapshot length greater than snapshot rollover: " + SnapshotRolloverSize);
                ForceSnapshotRollover();
            }
        }

        private void SnapShot()
        {
            CheckSnapshotRollover();

            memory.SetLength(0);
            uniqueId = 0;
            unique.Clear();

            using (ordersLocker.Using())
            {
                // Save space for length.
                writer.Write((int)memory.Length);
                // Write the current sequence number
                writer.Write(remoteSequence);
                writer.Write(LocalSequence);
                foreach (var kvp in ordersByBrokerId)
                {
                    var order = kvp.Value;
                    AddUniqueOrder(order);
                    if (order.ReplacedBy != null)
                    {
                        AddUniqueOrder(order.ReplacedBy);
                    }
                }

                foreach (var kvp in ordersBySerial)
                {
                    var order = kvp.Value;
                    AddUniqueOrder(order);
                    if (order.ReplacedBy != null)
                    {
                        AddUniqueOrder(order.ReplacedBy);
                    }
                }

                writer.Write(unique.Count);
                foreach (var kvp in unique)
                {
                    var order = kvp.Key;
                    var id = kvp.Value;
                    writer.Write(id);
                    writer.Write((int)order.Action);
                    writer.Write(order.BrokerOrder);
                    writer.Write(order.LogicalOrderId);
                    writer.Write(order.LogicalSerialNumber);
                    writer.Write((int)order.OrderState);
                    writer.Write(order.Price);
                    if (order.ReplacedBy != null)
                    {
                        writer.Write(unique[order.ReplacedBy]);
                    }
                    else
                    {
                        writer.Write((int)0);
                    }
                    if (order.OriginalOrder != null)
                    {
                        writer.Write(unique[order.OriginalOrder]);
                    }
                    else
                    {
                        writer.Write((int)0);
                    }
                    writer.Write((int)order.Side);
                    writer.Write((int)order.Size);
                    writer.Write(order.Symbol.Symbol);
                    if (order.Tag == null)
                    {
                        writer.Write("");
                    }
                    else
                    {
                        writer.Write(order.Tag);
                    }
                    writer.Write((int)order.Type);
                    writer.Write(order.UtcCreateTime.Internal);
                }

                writer.Write(ordersBySerial.Count);
                foreach (var kvp in ordersBySerial)
                {
                    var serial = kvp.Key;
                    var order = kvp.Value;
                    writer.Write(serial);
                    try
                    {
                        writer.Write(unique[order]);
                    }
                    catch (KeyNotFoundException)
                    {
                        Int16 x = 0;
                    }
                }
            }
            memory.Position = 0;
            writer.Write((Int32)memory.Length - sizeof(Int32)); // length excludes the size of the length value.
            fs.Write(memory.GetBuffer(), 0, (int)memory.Length);
            snapshotLength += memory.Length;
            log.Info("Wrote snapshot. Sequence Remote = " + remoteSequence + ", Local = " + localSequence +
                ", Size = " + memory.Length + ". File Size = " + snapshotLength);

        }

        private void SnapshotReadAll(string filePath)
        {
            using (var readFS = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var count = 0;
                memory.SetLength(snapshotRolloverSize<<2);
                memory.Position = 0;
                do
                {
                    count = readFS.Read(memory.GetBuffer(), (int)memory.Position, (int)(memory.Length - count));
                    memory.Position += count;
                } while (count > 0);
                memory.SetLength(memory.Position);
            }
        }

        public struct Snapshot
        {
            public int Offset;
            public int Length;
        }

        private IList<Snapshot> SnapshotScan()
        {
            var snapshots = new List<Snapshot>();
            memory.Position = 0;
            while (memory.Position < memory.Length)
            {
                var snapshot = new Snapshot {Offset = (int) memory.Position, Length = reader.ReadInt32()};
                if (snapshot.Length <= 0 || memory.Position + snapshot.Length > memory.Length)
                {
                    log.Warn("Invalid snapshot length: " + snapshot.Length + ". Probably corrupt snapshot. Ignoring remainder of current snapshot file.");
                    break;
                }
                snapshots.Add(snapshot);
                memory.Position += snapshot.Length;
            }
            return snapshots;
        }

        public bool Recover()
        {
            var files = FindSnapshotFiles();
            var loaded = false;
            foreach (var file in files)
            {
                if (debug) log.Debug("Attempting recovery from snapshot file: " + file.Filename);
                SnapshotReadAll(file.Filename);
                var snapshots = SnapshotScan();
                for (var i = snapshots.Count - 1; i >= 0; i--)
                {
                    var snapshot = snapshots[i];
                    if (debug) log.Debug("Trying snapshot at offset: " + snapshot.Offset + ", length: " + snapshot.Length);
                    if (SnapshotLoadLast(snapshot))
                    {
                        if (debug) log.Debug("Snapshot successfully loaded.");
                        loaded = true;
                        break;
                    }
                }
                if (loaded)
                {
                    break;
                }
            }
            if( loaded)
            {
                forceSnapShotRollover = true;
                ForceSnapShot();
            }
            return loaded;
        }

        private bool forceSnapShotRollover = false;

        private bool SnapshotLoadLast(Snapshot snapshot) {

            try
            {
                uniqueIds.Clear();
                replaceIds.Clear();
                originalIds.Clear();

                memory.Position = snapshot.Offset + sizeof(Int32); // Skip the snapshot length;

                remoteSequence = reader.ReadInt32();
                localSequence = reader.ReadInt32();

                int orderCount = reader.ReadInt32();
                for (var i = 0; i < orderCount; i++)
                {

                    var id = reader.ReadInt32();
                    var action = (OrderAction) reader.ReadInt32();
                    var brokerOrder = reader.ReadString();
                    var logicalOrderId = reader.ReadInt32();
                    var logicalSerialNumber = reader.ReadInt64();
                    var orderState = (OrderState)reader.ReadInt32();
                    var price = reader.ReadDouble();
                    var replaceId = reader.ReadInt32();
                    var originalId = reader.ReadInt32();
                    var side = (OrderSide)reader.ReadInt32();
                    var size = reader.ReadInt32();
                    var symbol = reader.ReadString();
                    var tag = reader.ReadString();
                    if (string.IsNullOrEmpty(tag)) tag = null;
                    var type = (OrderType)reader.ReadInt32();
                    var utcCreateTime= new TimeStamp(reader.ReadInt64());
                    var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
                    var order = Factory.Utility.PhysicalOrder(action, orderState, symbolInfo, side, type, price, size,
                                                              logicalOrderId, logicalSerialNumber, brokerOrder, tag, utcCreateTime);
                    uniqueIds.Add(id, order);
                    if (replaceId != 0)
                    {
                        replaceIds.Add(id, replaceId);
                    }
                    if( originalId != 0)
                    {
                        originalIds.Add(id, originalId);
                    }
                }

                foreach (var kvp in replaceIds)
                {
                    var orderId = kvp.Key;
                    var replaceId = kvp.Value;
                    uniqueIds[orderId].ReplacedBy = uniqueIds[replaceId];
                }

                foreach (var kvp in originalIds)
                {
                    var orderId = kvp.Key;
                    var originalId = kvp.Value;
                    uniqueIds[orderId].OriginalOrder = uniqueIds[originalId];
                }

                using (ordersLocker.Using())
                {
                    ordersByBrokerId.Clear();
                    ordersBySerial.Clear();
                    foreach (var kvp in uniqueIds)
                    {
                        var order = kvp.Value;
                        ordersByBrokerId[order.BrokerOrder] = order;
                    }

                    var bySerialCount = reader.ReadInt32();
                    for (var i = 0; i < bySerialCount; i++)
                    {
                        var logicalSerialNum = reader.ReadInt64();
                        var orderId = reader.ReadInt32();
                        var order = uniqueIds[orderId];
                        ordersBySerial[order.LogicalSerialNumber] = order;
                    }
                }
                return true;
            }
            catch( Exception ex)
            {
                log.Info("Loading snapshot at offset " + snapshot.Offset + " failed due to " + ex.Message + ". Rolling back to previous snapshot.", ex);
                return false;
            }
        }

        public void Clear()
        {
            log.Info("Clearing all orders.");
            using( ordersLocker.Using())
            {
                ordersByBrokerId.Clear();
                ordersBySerial.Clear();
            }
        }

        public bool TryGetOrderById(string brokerOrder, out CreateOrChangeOrder order)
        {
            if( brokerOrder == null)
            {
                order = null;
                return false;
            }
            using (ordersLocker.Using())
            {
                return ordersByBrokerId.TryGetValue((string) brokerOrder, out order);
            }
        }

        public CreateOrChangeOrder GetOrderById(string brokerOrder)
        {
            using (ordersLocker.Using())
            {
                CreateOrChangeOrder order;
                if (!ordersByBrokerId.TryGetValue((string) brokerOrder, out order))
                {
                    throw new ApplicationException("Unable to find order for id: " + brokerOrder);
                }
                return order;
            }
        }

        public CreateOrChangeOrder RemoveOrder(string clientOrderId)
        {
            if (string.IsNullOrEmpty(clientOrderId))
            {
                return null;
            }
            using (ordersLocker.Using())
            {
                TrySnapshot();
                CreateOrChangeOrder order = null;
                if (ordersByBrokerId.TryGetValue(clientOrderId, out order))
                {
                    var result = ordersByBrokerId.Remove(clientOrderId);
                    if( trace) log.Trace("Removed " + clientOrderId);
                    CreateOrChangeOrder orderBySerial;
                    if( ordersBySerial.TryGetValue(order.LogicalSerialNumber, out orderBySerial))
                    {
                        if( orderBySerial.BrokerOrder.Equals(clientOrderId))
                        {
                            ordersBySerial.Remove(order.LogicalSerialNumber);
                            if( trace) log.Trace("Removed " + order.LogicalSerialNumber);
                        }
                    }
                }
                return order;
            }
        }

        public bool TryGetOrderBySerial(long logicalSerialNumber, out CreateOrChangeOrder order)
        {
            using (ordersLocker.Using())
            {
                return ordersBySerial.TryGetValue(logicalSerialNumber, out order);
            }
        }

        public CreateOrChangeOrder GetOrderBySerial(long logicalSerialNumber)
        {
            using (ordersLocker.Using())
            {
                CreateOrChangeOrder order;
                if (!ordersBySerial.TryGetValue(logicalSerialNumber, out order))
                {
                    throw new ApplicationException("Unable to find order by serial for id: " + logicalSerialNumber);
                }
                return order;
            }
        }

        public void UpdateSequence(int remoteSequence, int localSequence)
        {
            using (ordersLocker.Using())
            {
                this.remoteSequence = remoteSequence;
                this.localSequence = localSequence;
                TrySnapshot();
            }
        }

        public void AssignById(CreateOrChangeOrder order, int remoteSequence, int localSequence)
        {
            using (ordersLocker.Using())
            {
                this.remoteSequence = remoteSequence;
                this.localSequence = localSequence;
                if (trace) log.Trace("Assigning order " + order.BrokerOrder + " with " + order.LogicalSerialNumber);
                ordersByBrokerId[order.BrokerOrder] = order;
                if( order.LogicalSerialNumber != 0)
                {
                    ordersBySerial[order.LogicalSerialNumber] = order;
                }
                TrySnapshot();
            }
        }

        public void ClearPendingOrders(SymbolInfo symbol)
        {
            var remove = new List<CreateOrChangeOrder>();
            using (ordersLocker.Using())
            {
                foreach (var kvp in ordersByBrokerId)
                {
                    var order = kvp.Value;
                    if (order.Symbol.BinaryIdentifier == symbol.BinaryIdentifier && order.OrderState == OrderState.Pending)
                    {
                        remove.Add(order);
                    }
                }
            }
            foreach (var order in remove)
            {
                log.Warn("Removing pending order due recovery from snapshot: " + order);
                RemoveOrder(order.BrokerOrder);
            }
        }

        public List<CreateOrChangeOrder> GetOrders(Func<CreateOrChangeOrder,bool> select)
        {
            var list = new List<CreateOrChangeOrder>();
            var remove = new List<CreateOrChangeOrder>();
            using (ordersLocker.Using())
            {
                foreach (var kvp in ordersByBrokerId)
                {
                    var order = kvp.Value;
                    if( order.OrderState == OrderState.Pending)
                    {
                        var elapsed = TimeStamp.UtcNow - order.LastStateChange;
                        if( elapsed.TotalSeconds >= pendingExpireSeconds)
                        {
                            remove.Add(order);
                            continue;
                        }
                    }
                    if (select(order))
                    {
                        list.Add(order);
                    }
                }
            }
            foreach (var order in remove)
            {
                log.Warn("Removing pending order due to " + pendingExpireSeconds + " second expiration: " + order);
                RemoveOrder(order.BrokerOrder);
            }
            return list;
        }

        public string LogOrders()
        {
            using (ordersLocker.Using())
            {
                var sb = new StringBuilder();
                foreach (var kvp in ordersByBrokerId)
                {
                    sb.AppendLine(kvp.Value.ToString());
                }
                return sb.ToString();
            }
        }

        protected volatile bool isDisposed = false;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                isDisposed = true;
                if (disposing)
                {
                    if (debug) log.Debug("Dispose()");
                    ForceSnapShot();
                    WaitForSnapshot();
                    if (fs != null)
                    {
                        fs.Close();
                    }
                }
            }
        }

        public int Count()
        {
            return ordersByBrokerId.Count;
        }
    }
}