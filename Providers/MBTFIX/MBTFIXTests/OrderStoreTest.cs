using NUnit.Framework;
using TickZoom.Api;
using TickZoom.MBTFIX;

namespace Test
{
    [TestFixture]
    public class OrderStoreTest 
    {
        public static readonly Log log = Factory.SysLog.GetLogger(typeof(OrderStoreTest));
        private PhysicalOrderStore store;

        [SetUp]
        public void Setup()
        {
            store = new PhysicalOrderStore("OrderStoreTest");
            store.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            store.Dispose();
        }

        [Test]
        public void WriteAndReadByIdTest()
        {
            var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
            var clientId = "TestString";
            var order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, 100000334, clientId, null);
            store.AssignById(clientId,order);
            var result = store.GetOrderById(clientId);
            Assert.AreEqual(order.LogicalSerialNumber,result.LogicalSerialNumber);

        }

        [Test]
        public void WriteAndReadBySerialTest()
        {
            var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
            var clientId = "TestString";
            var logicalSerial = 100000335;
            var order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, logicalSerial, clientId, null);
            store.AssignById(clientId, order);
            var result = store.GetOrderBySerial(logicalSerial);
            Assert.AreEqual(order.BrokerOrder, result.BrokerOrder);

        }

        [Test]
        public void ReplaceAndReadIdTest()
        {
            var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
            var clientId = "TestString";
            var logicalSerial = 100000335;
            var order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, logicalSerial, clientId, null);
            store.AssignById(clientId, order);
            order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, logicalSerial, clientId, null);
            store.AssignById(clientId, order);
            var result = store.GetOrderBySerial(logicalSerial);
            Assert.AreEqual(order.BrokerOrder, result.BrokerOrder);

        }

        [Test]
        public void DumpDataBase()
        {
            var store = new PhysicalOrderStore("MBTFIXProvider");
            var list = store.GetOrders((x) => true);
            foreach (var order in list)
            {
                log.Info(order.ToString());
            }
        }

        [Test]
        public void ReplaceAndReadSerialTest()
        {
            var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
            var clientId = "TestString";
            var logicalSerial = 100000335;
            var order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, logicalSerial, clientId, null);
            store.AssignById(clientId, order);
            clientId = "TestString2";
            order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, logicalSerial, clientId, null);
            store.AssignById(clientId, order);
            var result = store.GetOrderBySerial(logicalSerial);
            Assert.AreEqual(clientId, result.BrokerOrder);

        }

        [Test]
        public void SelectBySymbolTest()
        {
            var symbolInfo = Factory.Symbol.LookupSymbol("EUR/USD");
            var clientId = "TestString";
            var logicalSerial = 100000335;
            var order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, logicalSerial, clientId, null);
            store.AssignById(clientId, order);
            order = Factory.Utility.PhysicalOrder(OrderState.Active, symbolInfo, OrderSide.Sell, OrderType.BuyLimit,
                                                      124.34, 1234, 14, logicalSerial+1, clientId, null);
            store.AssignById(clientId, order);
            var list = store.GetOrders((o) => o.Symbol.Symbol == "EUR/USD");
            Assert.AreEqual(1,list.Count);
            Assert.AreEqual(order.BrokerOrder, list[0].BrokerOrder);
            Assert.AreEqual(logicalSerial+1, list[0].LogicalSerialNumber);

        }

        [Test]
        public void ReadOrders()
        {
            var list = store.GetOrders( (x) => true );
            foreach( var order in list)
            {
                log.Info(order.ToString());
            }
        }
    }
}