using NUnit.Framework;
using TickZoom.Api;
using TickZoom.MBTFIX;
using System.IO;
using System;
using System.Text;
using TickZoom.FIX;

namespace Test
{
    [TestFixture]
    public unsafe class OrderStoreTest 
    {
        public static readonly Log log = Factory.SysLog.GetLogger(typeof(OrderStoreTest));

        [SetUp]
        public void Setup()
        {
        }

        [TearDown]
        public void TearDown()
        {
        }

        [Test]
        public void WriteAndReadByIdTest()
        {
            using( var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
                var clientId = "TestString";
                var order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                          124.34, 1234, 14, 100000334, clientId, null);
                store.AssignById(order,1,1);
                var result = store.GetOrderById(clientId);
                Assert.AreEqual(order.LogicalSerialNumber, result.LogicalSerialNumber);
            }
        }

        [Test]
        public void WriteAndReadBySerialTest()
        {
            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
                var clientId = "TestString";
                var logicalSerial = 100000335;
                var order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell,
                                                          OrderType.BuyLimit,
                                                          124.34, 1234, 14, logicalSerial, clientId, null);
                store.AssignById(order, 1, 1);
                var result = store.GetOrderBySerial(logicalSerial);
                Assert.AreEqual(order.BrokerOrder, result.BrokerOrder);
            }
        }

        [Test]
        public void ReplaceAndReadIdTest()
        {
            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
                var clientId = "TestString";
                var logicalSerial = 100000335;
                var order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell,
                                                          OrderType.BuyLimit,
                                                          124.34, 1234, 14, logicalSerial, clientId, null);
                store.AssignById(order, 1, 1);
                order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, logicalSerial, clientId, null);
                store.AssignById(order, 1, 1);
                var result = store.GetOrderBySerial(logicalSerial);
                Assert.AreEqual(order.BrokerOrder, result.BrokerOrder);
            }
        }

        [Test]
        public void DumpDataBase()
        {
            using (var store = new PhysicalOrderStore("MBTFIXProvider"))
            {
                var list = store.GetOrders((x) => true);
                foreach (var order in list)
                {
                    log.Info(order.ToString());
                }
            }
        }

        [Test]
        public void ReplaceAndReadSerialTest()
        {
            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
                var clientId = "TestString";
                var logicalSerial = 100000335;
                var order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell,
                                                          OrderType.BuyLimit,
                                                          124.34, 1234, 14, logicalSerial, clientId, null);
                store.AssignById(order, 1, 1);
                clientId = "TestString2";
                order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, logicalSerial, clientId, null);
                store.AssignById(order, 1, 1);
                var result = store.GetOrderBySerial(logicalSerial);
                Assert.AreEqual(clientId, result.BrokerOrder);
            }
        }

        [Test]
        public void SelectBySymbolTest()
        {
            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
                var clientId = "TestString";
                var logicalSerial = 100000335;
                var order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell,
                                                          OrderType.BuyLimit,
                                                          124.34, 1234, 14, logicalSerial, clientId, null);
                store.AssignById(order, 1, 1);
                order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, logicalSerial + 1, clientId, null);
                store.AssignById(order, 1, 1);
                var list = store.GetOrders((o) => o.Symbol.Symbol == "EUR/USD");
                Assert.AreEqual(1, list.Count);
                Assert.AreEqual(order.BrokerOrder, list[0].BrokerOrder);
                Assert.AreEqual(logicalSerial + 1, list[0].LogicalSerialNumber);
            }
        }

        [Test]
        public void ReadOrders()
        {
            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                var list = store.GetOrders((x) => true);
                foreach (var order in list)
                {
                    log.Info(order.ToString());
                }
            }
        }

        [Test]
        public void WriteSnapShotTest()
        {
            string dbpath;
            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                dbpath = store.DatabasePath;
            }
            File.Delete(dbpath);
            var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
            var clientId1 = "TestString1";
            var logicalSerial = 100000335;
            var price = 124.34;
            var size = 1234;
            var logicalId = 14;
            var state = OrderState.Active;
            var side = OrderSide.Sell;
            var type = OrderType.BuyLimit;
            var order1 = Factory.Utility.PhysicalOrder(state, symbolInfo, side,
                                                      type,
                                                      price, size, logicalId, logicalSerial, clientId1, null);
            var clientId2 = "TestString2";
            logicalSerial = 100000336;
            price = 432.13;
            size = 4321;
            logicalId = 41;
            state = OrderState.Active;
            side = OrderSide.Sell;
            type = OrderType.BuyLimit;
            var order2 = Factory.Utility.PhysicalOrder(state, symbolInfo, side, type,
                                                      price, size, logicalId, logicalSerial, clientId2, null);
            order1.Replace = order2;

            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                store.AssignById(order1, 1, 1);
                store.ForceSnapShot();
                store.WaitForSnapshot();

                // Replace order in store to make new snapshot.
                store.AssignById(order2, 1, 1);
                store.ForceSnapShot();
                store.WaitForSnapshot();
            }

            using (var fs = new FileStream(dbpath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                Console.WriteLine("File size = " + fs.Length);
            }

            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                store.Recover();

                Assert.AreEqual(2,store.Count());

                var result1 = store.GetOrderById(clientId1);
                Assert.AreEqual(order1.Price, result1.Price);
                Assert.AreEqual(order1.Size, result1.Size);
                Assert.AreEqual(order1.BrokerOrder, result1.BrokerOrder);
                Assert.AreEqual(order1.Symbol, result1.Symbol);
                Assert.AreEqual(order1.OrderState, result1.OrderState);
                Assert.AreEqual(order1.Side, result1.Side);
                Assert.AreEqual(order1.Type, result1.Type);
                Assert.AreEqual(order1.LogicalSerialNumber, result1.LogicalSerialNumber);

                var result2 = store.GetOrderById(clientId2);
                Assert.AreEqual(order2.Price, result2.Price);
                Assert.AreEqual(order2.Size, result2.Size);
                Assert.AreEqual(order2.BrokerOrder, result2.BrokerOrder);
                Assert.AreEqual(order2.Symbol, result2.Symbol);
                Assert.AreEqual(order2.OrderState, result2.OrderState);
                Assert.AreEqual(order2.Side, result2.Side);
                Assert.AreEqual(order2.Type, result2.Type);
                Assert.AreEqual(order2.LogicalSerialNumber, result2.LogicalSerialNumber);
                Assert.IsTrue(object.ReferenceEquals(result1.Replace,result2));
            }
        }

        [Test]
        public void SnapShotRollOverTest()
        {
            string dbpath;
            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                dbpath = store.DatabasePath;
            }
            File.Delete(dbpath);
            var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
            var clientId1 = "TestString1";
            var logicalSerial = 100000335;
            var price = 124.34;
            var size = 1234;
            var logicalId = 14;
            var state = OrderState.Active;
            var side = OrderSide.Sell;
            var type = OrderType.BuyLimit;
            var order1 = Factory.Utility.PhysicalOrder(state, symbolInfo, side,
                                                      type,
                                                      price, size, logicalId, logicalSerial, clientId1, null);
            var clientId2 = "TestString2";
            logicalSerial = 100000336;
            price = 432.13;
            size = 4321;
            logicalId = 41;
            state = OrderState.Active;
            side = OrderSide.Sell;
            type = OrderType.BuyLimit;
            var order2 = Factory.Utility.PhysicalOrder(state, symbolInfo, side, type,
                                                      price, size, logicalId, logicalSerial, clientId2, null);
            order1.Replace = order2;

            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                store.SnapshotRolloverSize = 1000;
                store.AssignById(order1, 1, 1);
                store.ForceSnapShot();
                store.WaitForSnapshot();

                // Replace order in store to make new snapshot.
                store.AssignById(order2, 1, 1);
                store.ForceSnapShot();
                store.WaitForSnapshot();

                for (int i = 0; i < 20; i++ )
                {
                    store.ForceSnapShot();
                    store.WaitForSnapshot();
                }
            }

            File.Delete(dbpath);
            File.WriteAllText(dbpath,"This is a test for corrupt snapshot file.");

            using (var store = new PhysicalOrderStore("OrderStoreTest"))
            {
                store.Recover();

                Assert.AreEqual(2, store.Count());

                var result1 = store.GetOrderById(clientId1);
                Assert.AreEqual(order1.Price, result1.Price);
                Assert.AreEqual(order1.Size, result1.Size);
                Assert.AreEqual(order1.BrokerOrder, result1.BrokerOrder);
                Assert.AreEqual(order1.Symbol, result1.Symbol);
                Assert.AreEqual(order1.OrderState, result1.OrderState);
                Assert.AreEqual(order1.Side, result1.Side);
                Assert.AreEqual(order1.Type, result1.Type);
                Assert.AreEqual(order1.LogicalSerialNumber, result1.LogicalSerialNumber);

                var result2 = store.GetOrderById(clientId2);
                Assert.AreEqual(order2.Price, result2.Price);
                Assert.AreEqual(order2.Size, result2.Size);
                Assert.AreEqual(order2.BrokerOrder, result2.BrokerOrder);
                Assert.AreEqual(order2.Symbol, result2.Symbol);
                Assert.AreEqual(order2.OrderState, result2.OrderState);
                Assert.AreEqual(order2.Side, result2.Side);
                Assert.AreEqual(order2.Type, result2.Type);
                Assert.AreEqual(order2.LogicalSerialNumber, result2.LogicalSerialNumber);
                Assert.IsTrue(object.ReferenceEquals(result1.Replace, result2));
            }
        }
    }
}