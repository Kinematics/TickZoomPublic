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
using TickZoom.Common;

namespace TickZoom.Interceptors
{
	public class FillSimulatorPhysical : FillSimulator
	{
		private static readonly Log staticLog = Factory.SysLog.GetLogger(typeof(FillSimulatorPhysical));
		private readonly bool trace = staticLog.IsTraceEnabled;
		private readonly bool debug = staticLog.IsDebugEnabled;
		private static readonly bool notice = staticLog.IsNoticeEnabled;
        private Queue<FillEvent> fillQueue = new Queue<FillEvent>();
        private Log log;

		private Dictionary<string,CreateOrChangeOrder> orderMap = new Dictionary<string, CreateOrChangeOrder>();
		private ActiveList<CreateOrChangeOrder> increaseOrders = new ActiveList<CreateOrChangeOrder>();
		private ActiveList<CreateOrChangeOrder> decreaseOrders = new ActiveList<CreateOrChangeOrder>();
		private ActiveList<CreateOrChangeOrder> marketOrders = new ActiveList<CreateOrChangeOrder>();
        private NodePool<CreateOrChangeOrder> nodePool = new NodePool<CreateOrChangeOrder>();
        private object orderMapLocker = new object();
		private bool isOpenTick = false;
		private TimeStamp openTime;

		private Action<PhysicalFill,int,int,int> onPhysicalFill;
		private Action<CreateOrChangeOrder,string> onRejectOrder;
		private Action<int> onPositionChange;
		private bool useSyntheticMarkets = true;
		private bool useSyntheticStops = true;
		private bool useSyntheticLimits = true;
		private SymbolInfo symbol;
		private int actualPosition = 0;
		private TickSync tickSync;
		private TickIO currentTick = Factory.TickUtil.TickIO();
        private PhysicalOrderConfirm confirmOrders;
		private bool isBarData = false;
		private bool createSimulatedFills = false;
	    private LimitOrderQuoteSimulation limitOrderQuoteSimulation;
	    private LimitOrderTradeSimulation limitOrderTradeSimulation;
        // Randomly rotate the partial fills but using a fixed
		// seed so that test results are reproducable.
		private Random random = new Random(1234);
		private long minimumTick;

        public struct FillEvent
        {
            public PhysicalFill PhysicalFill;
            public int TotalSize;
            public int CumulativeSize;
            public int RemainingSize;
        }

        public FillSimulatorPhysical(string name, SymbolInfo symbol, bool createSimulatedFills)
		{
			this.symbol = symbol;
		    limitOrderQuoteSimulation = symbol.LimitOrderQuoteSimulation;
		    limitOrderTradeSimulation = symbol.LimitOrderTradeSimulation;
			this.minimumTick = symbol.MinimumTick.ToLong();
			this.tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
			this.createSimulatedFills = createSimulatedFills;
			this.log = Factory.SysLog.GetLogger(typeof(FillSimulatorPhysical).FullName + "." + symbol.Symbol.StripInvalidPathChars() + "." + name);
		}
		private bool hasCurrentTick = false;
		public void OnOpen(Tick tick) {
			if( trace) log.Trace("OnOpen("+tick+")");
			isOpenTick = true;
			openTime = tick.Time;
			if( !tick.IsQuote && !tick.IsTrade) {
				throw new ApplicationException("tick w/o either trade or quote data? " + tick);
			}
			currentTick.Inject( tick.Extract());
			hasCurrentTick = true;
		}
		
		public Iterable<CreateOrChangeOrder> GetActiveOrders(SymbolInfo symbol) {
			ActiveList<CreateOrChangeOrder> activeOrders = new ActiveList<CreateOrChangeOrder>();
			activeOrders.AddLast(increaseOrders);
			activeOrders.AddLast(decreaseOrders);
			activeOrders.AddLast(marketOrders);
			return activeOrders;
		}
	
		public void OnChangeBrokerOrder(CreateOrChangeOrder order)
		{
			if( debug) log.Debug("OnChangeBrokerOrder( " + order + ")");
            CancelBrokerOrder((string) order.OriginalOrder.BrokerOrder);
            CreateBrokerOrder( order);
            if (confirmOrders != null) confirmOrders.ConfirmChange(order,true);
		}

        public bool TryGetOrderById(string orderId, out CreateOrChangeOrder createOrChangeOrder)
        {
            LogOpenOrders();
            lock (orderMapLocker)
            {
                return orderMap.TryGetValue(orderId, out createOrChangeOrder);
            }
        }


        public CreateOrChangeOrder GetOrderById(string orderId)
        {
			CreateOrChangeOrder order;
			lock( orderMapLocker) {
				if( !TryGetOrderById( orderId, out order)) {
					throw new ApplicationException( symbol + ": Cannot find physical order by id: " + orderId);
				}
			}
			return order;
		}

		
		private void CreateBrokerOrder(CreateOrChangeOrder order) {
			lock( orderMapLocker) {
				try {
					orderMap.Add((string)order.BrokerOrder,order);
					if( trace) log.Trace("Added order " + order.BrokerOrder);
				} catch( ArgumentException) {
					throw new ApplicationException("An broker order id of " + order.BrokerOrder + " was already added.");
				}
			}
			SortAdjust(order);
			VerifySide(order);
		}

		private void CancelBrokerOrder(string oldOrderId)
		{
            CreateOrChangeOrder createOrChangeOrder;
            if (TryGetOrderById(oldOrderId, out createOrChangeOrder))
            {
                var node = (ActiveListNode<CreateOrChangeOrder>)createOrChangeOrder.Reference;
                if (node.List != null)
                {
                    node.List.Remove(node);
                }
                lock (orderMapLocker)
                {
                    orderMap.Remove(oldOrderId);
                }
                LogOpenOrders();
            }
            else
            {
                log.Info("PhysicalOrder too late to cancel or already canceled, ignoring: " + oldOrderId);
            }
        }

        public bool HasBrokerOrder( CreateOrChangeOrder order)
        {
            var list = increaseOrders;
            switch (order.Type)
            {
                case OrderType.BuyLimit:
                case OrderType.SellStop:
                    list = decreaseOrders;
                    break;
                case OrderType.SellLimit:
                case OrderType.BuyStop:
                    list = increaseOrders;
                    break;
                case OrderType.BuyMarket:
                case OrderType.SellMarket:
                    list = marketOrders;
                    break;
                default:
                    throw new ApplicationException("Unexpected order type: " + order.Type);
            }
            for (var current = list.First; current != null; current = current.Next)
            {
                var queueOrder = current.Value;
                if (order.LogicalSerialNumber == queueOrder.LogicalSerialNumber)
                {
                    if (debug) log.Debug("Create ignored because order was already on active order queue.");
                    return true;
                }
            }
            return false;
        }

		public void OnCreateBrokerOrder(CreateOrChangeOrder order)
		{
			if( debug) log.Debug("OnCreateBrokerOrder( " + order + ")");
			if( order.Size <= 0) {
				throw new ApplicationException("Sorry, Size of order must be greater than zero: " + order);
			}
            CreateBrokerOrder(order);
            if (confirmOrders != null) confirmOrders.ConfirmCreate(order, true);
		}
		
		public void OnCancelBrokerOrder(CreateOrChangeOrder order)
		{
            if (debug) log.Debug("OnCancelBrokerOrder( " + order.OriginalOrder.BrokerOrder + ")");
            CancelBrokerOrder((string)order.OriginalOrder.BrokerOrder);
            if (confirmOrders != null) confirmOrders.ConfirmCancel(order, true);
        }

		public int ProcessOrders() {
			if( hasCurrentTick) {
                ProcessOrdersInternal(currentTick);
            }
		    return 1;
		}
		
		public void StartTick(Tick lastTick)
		{
			if( trace) log.Trace("StartTick("+lastTick+")");
			if( !lastTick.IsQuote && !lastTick.IsTrade) {
				throw new ApplicationException("tick w/o either trade or quote data? " + lastTick);
			}
			currentTick.Inject( lastTick.Extract());
			hasCurrentTick = true;
		}
		
		private void ProcessOrdersInternal(Tick tick) {
			if( isOpenTick && tick.Time > openTime) {
				if( trace) {
					if( isOpenTick) {
						log.Trace( "ProcessOrders( " + symbol + ", " + tick + " ) [OpenTick]") ;
					} else {
						log.Trace( "ProcessOrders( " + symbol + ", " + tick + " )") ;
					}
				}
				isOpenTick = false;
			}
			if( symbol == null) {
				throw new ApplicationException("Please set the Symbol property for the " + GetType().Name + ".");
			}
			for( var node = marketOrders.First; node != null; node = node.Next) {
				var order = node.Value;
				OnProcessOrder(order, tick);
			}
			for( var node = increaseOrders.First; node != null; node = node.Next) {
				var order = node.Value;
				OnProcessOrder(order, tick);
			}
			for( var node = decreaseOrders.First; node != null; node = node.Next) {
				var order = node.Value;
				OnProcessOrder(order, tick);
			}
            if (onPhysicalFill == null)
            {
                throw new ApplicationException("Please set the OnPhysicalFill property.");
            }
            else
            {
                while (fillQueue.Count > 0)
                {
                    var entry = fillQueue.Dequeue();
                    onPhysicalFill(entry.PhysicalFill, entry.TotalSize, entry.CumulativeSize, entry.RemainingSize);
                }
            }
		}
		
		private void LogOpenOrders() {
			if( trace) {
				log.Trace( "Found " + orderMap.Count + " open orders for " + symbol + ":");
				lock( orderMapLocker) {
					foreach( var kvp in orderMap) {
						var order = kvp.Value;
						log.Trace( order.ToString());
					}
				}
			}
		}

		private void SortAdjust(CreateOrChangeOrder order) {
			switch( order.Type) {
				case OrderType.BuyLimit:					
				case OrderType.SellStop:
					SortAdjust( decreaseOrders, order, (x,y) => y.Price - x.Price);
					break;
				case OrderType.SellLimit:
				case OrderType.BuyStop:
					SortAdjust( increaseOrders, order, (x,y) => x.Price - y.Price);
					break;
				case OrderType.BuyMarket:
				case OrderType.SellMarket:
					Adjust( marketOrders, order);
					break;
				default:
					throw new ApplicationException("Unexpected order type: " + order.Type);
			}
		}
		
		private void AssureNode(CreateOrChangeOrder order) {
			if( order.Reference == null) {
				order.Reference = nodePool.Create(order);
			}
		}
		
		private void Adjust(ActiveList<CreateOrChangeOrder> list, CreateOrChangeOrder order) {
			AssureNode(order);
			var node = (ActiveListNode<CreateOrChangeOrder>) order.Reference;
			if( node.List == null ) {
				list.AddLast(node);
			} else if( !node.List.Equals(list)) {
				node.List.Remove(node);
				list.AddLast(node);
			}
		}
		
		private void SortAdjust(ActiveList<CreateOrChangeOrder> list, CreateOrChangeOrder order, Func<CreateOrChangeOrder,CreateOrChangeOrder,double> compare) {
			AssureNode(order);
			var orderNode = (ActiveListNode<CreateOrChangeOrder>) order.Reference;
			if( orderNode.List == null || !orderNode.List.Equals(list)) {
				if( orderNode.List != null) {
					orderNode.List.Remove(orderNode);
				}
				bool found = false;
				var next = list.First;
				for( var node = next; node != null; node = next) {
					next = node.Next;
					var other = node.Value;
					if( object.ReferenceEquals(order,other)) {
						found = true;
						break;
					} else {
						var result = compare(order,other);
						if( result < 0) {
							list.AddBefore(node,orderNode);
							found = true;
							break;
						}
					}
				}
				if( !found) {
					list.AddLast(orderNode);
				}
			}
		}

		private void VerifySide(CreateOrChangeOrder order) {
			switch (order.Type) {
				case OrderType.SellMarket:
				case OrderType.SellStop:
				case OrderType.SellLimit:
					VerifySellSide(order);
					break;
				case OrderType.BuyMarket:
				case OrderType.BuyStop:
				case OrderType.BuyLimit:
					VerifyBuySide(order);
					break;
			}
		}
		
		private void OrderSideWrongReject(CreateOrChangeOrder order) {
			var message = "Sorry, improper setting of a " + order.Side + " order when position is " + actualPosition;
			lock( orderMapLocker) {
				orderMap.Remove((string)order.BrokerOrder);
			}
			var node = (ActiveListNode<CreateOrChangeOrder>) order.Reference;
			if( node.List != null) {
				node.List.Remove(node);
			}
			if( onRejectOrder != null)
			{
			    log.Warn("Rejecting order because position is " + actualPosition + " but order side was " + order.Side + ": " + order);
				onRejectOrder( order, message);
			} else {
				throw new ApplicationException( message + " while handling order: " + order);
			}
		}
		
		private void VerifySellSide( CreateOrChangeOrder order) {
			if( actualPosition > 0) {
				if( order.Side != OrderSide.Sell) {
					OrderSideWrongReject(order);
				}
			} else {
				if( order.Side != OrderSide.SellShort) {
					OrderSideWrongReject(order);
				}
			}
		}
		
		private void VerifyBuySide( CreateOrChangeOrder order) {
			if( order.Side != OrderSide.Buy) {
				OrderSideWrongReject(order);
			}
		}
		
		private void OnProcessOrder(CreateOrChangeOrder order, Tick tick)
		{
            if (tick.UtcTime < order.UtcCreateTime)
            {
                //if (trace) log.Trace
                log.Info("Skipping check of " + order.Type + " on tick UTC time " + tick.UtcTime + "." + order.UtcCreateTime.Microsecond + " because earlier than order create UTC time " + order.UtcCreateTime + "." + order.UtcCreateTime.Microsecond);
                return;
            }
            switch (order.Type)
            {
				case OrderType.SellMarket:
					ProcessSellMarket(order, tick);
					break;
				case OrderType.SellStop:
					ProcessSellStop(order, tick);
					break;
				case OrderType.SellLimit:
                    if (tick.IsTrade && limitOrderTradeSimulation != LimitOrderTradeSimulation.None)
                    {
                        ProcessSellLimitTrade(order, tick);
                    }
                    else if (tick.IsQuote)
                    {
                        ProcessSellLimitQuote(order, tick);
                    }
                    break;
				case OrderType.BuyMarket:
					ProcessBuyMarket(order, tick);
					break;
				case OrderType.BuyStop:
					ProcessBuyStop(order, tick);
					break;
				case OrderType.BuyLimit:
                    if (tick.IsTrade && limitOrderTradeSimulation != LimitOrderTradeSimulation.None)
                    {
                        ProcessBuyLimitTrade(order, tick);
                    }
                    else if (tick.IsQuote)
                    {
                        ProcessBuyLimitQuote(order, tick);
                    }
					break;
			}
		}
		private bool ProcessBuyStop(CreateOrChangeOrder order, Tick tick)
		{
			bool retVal = false;
		    long price = tick.IsQuote ? tick.lAsk : tick.lPrice;
			if (price >= order.Price.ToLong()) {
				CreatePhysicalFillHelper(order.Size, price.ToDouble(), tick.Time, tick.UtcTime, order);
				retVal = true;
			}
			return retVal;
		}

		private bool ProcessSellStop(CreateOrChangeOrder order, Tick tick)
		{
			bool retVal = false;
			long price = tick.IsQuote ? tick.lBid : tick.lPrice;
			if (price <= order.Price.ToLong()) {
				CreatePhysicalFillHelper(-order.Size, price.ToDouble(), tick.Time, tick.UtcTime, order);
				retVal = true;
			}
			return retVal;
		}

		private bool ProcessBuyMarket(CreateOrChangeOrder order, Tick tick)
		{
            if (!tick.IsQuote && !tick.IsTrade)
            {
                throw new ApplicationException("tick w/o either trade or quote data? " + tick);
            }
            var price = tick.IsQuote ? tick.Ask : tick.Price;
            CreatePhysicalFillHelper(order.Size, price, tick.Time, tick.UtcTime, order);
            if (debug) log.Debug("Filling " + order.Type + " at " + price + " created at " + order.UtcCreateTime + "." + order.UtcCreateTime.Microsecond + " using tick UTC time " + tick.UtcTime + "." + tick.UtcTime.Microsecond);
            return true;
		}

        private bool ProcessBuyLimitTrade(CreateOrChangeOrder order, Tick tick)
        {
            var result = false;
            var orderPrice = order.Price.ToLong();
            var fillPrice = 0D;
            switch (limitOrderTradeSimulation)
            {
                case LimitOrderTradeSimulation.TradeTouch:
                    if (tick.lPrice <= orderPrice)
                    {
                        fillPrice = tick.Price;
                        result = true;
                    }
                    break;
                case LimitOrderTradeSimulation.TradeThrough:
                    if (tick.lPrice < orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown limit order trade simulation: " + limitOrderTradeSimulation);
            }
            if (result)
            {
                CreatePhysicalFillHelper(order.Size, fillPrice, tick.Time, tick.UtcTime, order);
            }
            return result;
        }

        private bool ProcessBuyLimitQuote(CreateOrChangeOrder order, Tick tick)
		{
            var orderPrice = order.Price.ToLong();
            var result = false;
            var fillPrice = 0D;
            switch (limitOrderQuoteSimulation)
            {
                case LimitOrderQuoteSimulation.SameSideQuoteTouch:
                    var bid = Math.Min(tick.lBid, tick.lAsk);
                    if (bid <= orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.SameSideQuoteThrough:
                    bid = Math.Min(tick.lBid, tick.lAsk);
                    if (bid < orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.OppositeQuoteTouch:
                    if (tick.lAsk <= orderPrice)
                    {
                        fillPrice = tick.Ask;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.OppositeQuoteThrough:
                    if (tick.lAsk < orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown limit order quote simulation: " + limitOrderQuoteSimulation);
            }
            if (result) {
                if (debug) log.Debug("Filling " + order.Type + " with " + limitOrderQuoteSimulation + " at ask " + tick.Ask + " / bid " + tick.Bid + " at " + tick.Time);
                CreatePhysicalFillHelper(order.Size, fillPrice, tick.Time, tick.UtcTime, order);
			}
			return result;
		}

        private bool ProcessSellLimitTrade(CreateOrChangeOrder order, Tick tick)
        {
            var result = false;
            var orderPrice = order.Price.ToLong();
            var fillPrice = 0D;
            switch (limitOrderTradeSimulation)
            {
                case LimitOrderTradeSimulation.TradeTouch:
                    if (tick.lPrice >= orderPrice)
                    {
                        fillPrice = tick.Price;
                        result = true;
                    }
                    break;
                case LimitOrderTradeSimulation.TradeThrough:
                    if (tick.lPrice > orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown limit order trade simulation: " + limitOrderTradeSimulation);
            }
            if( result)
            {
                CreatePhysicalFillHelper(-order.Size, fillPrice, tick.Time, tick.UtcTime, order);
            }
            return true;
        }

        private bool ProcessSellLimitQuote(CreateOrChangeOrder order, Tick tick)
		{
            var orderPrice = order.Price.ToLong();
            var result = false;
            var fillPrice = 0D;
            switch (limitOrderQuoteSimulation)
            {
                case LimitOrderQuoteSimulation.SameSideQuoteTouch:
                    var ask = Math.Max(tick.lAsk, tick.lBid);
                    if (ask >= orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.SameSideQuoteThrough:
                    ask = Math.Max(tick.lAsk, tick.lBid);
                    if (ask > orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.OppositeQuoteTouch:
                    if (tick.lBid >= orderPrice)
                    {
                        fillPrice = tick.Bid;
                        result = true;
                    }
                    break;
                case LimitOrderQuoteSimulation.OppositeQuoteThrough:
                    if (tick.lBid > orderPrice)
                    {
                        fillPrice = order.Price;
                        result = true;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown limit order quote simulation: " + limitOrderQuoteSimulation);
            }
            if( result) {
                if (debug) log.Debug("Filling " + order.Type + " with " + limitOrderQuoteSimulation + " at ask " + tick.Ask + " / bid " + tick.Bid + " at " + tick.Time);
                CreatePhysicalFillHelper(-order.Size, fillPrice, tick.Time, tick.UtcTime, order);
                result = true;
            }
			return result;
		}
		
		private bool ProcessSellMarket(CreateOrChangeOrder order, Tick tick)
		{
			if( !tick.IsQuote && !tick.IsTrade) {
				throw new ApplicationException("tick w/o either trade or quote data? " + tick);
			}
            double price = tick.IsQuote ? tick.Bid : tick.Price;
            CreatePhysicalFillHelper(-order.Size, price, tick.Time, tick.UtcTime, order);
            if( debug) log.Debug("Filling " + order.Type + " at " + price + " created at " + order.UtcCreateTime + "." + order.UtcCreateTime.Microsecond + " using tick UTC time " + tick.UtcTime + "." + tick.UtcTime.Microsecond);
            return true;
		}

		private int maxPartialFillsPerOrder = 10;
		private void CreatePhysicalFillHelper(int totalSize, double price, TimeStamp time, TimeStamp utcTime, CreateOrChangeOrder order) {
			if( debug) log.Debug("Filling order: " + order );
			var split = random.Next(maxPartialFillsPerOrder)+1;
			var lastSize = totalSize / split;
			var cumulativeQuantity = 0;
			if( lastSize == 0) lastSize = totalSize;
			while( order.Size > 0) {
				order.Size -= Math.Abs(lastSize);
				if( order.Size < Math.Abs(lastSize)) {
					lastSize += Math.Sign(lastSize) * order.Size;
					order.Size = 0;
				}
				cumulativeQuantity += lastSize;
				if( order.Size == 0)
				{
                    CancelBrokerOrder((string)order.BrokerOrder);
				}
				CreateSingleFill( lastSize, totalSize, cumulativeQuantity, order.Size, price, time, utcTime, order);
			}
		}

		private void CreateSingleFill(int size, int totalSize, int cumulativeSize, int remainingSize, double price, TimeStamp time, TimeStamp utcTime, CreateOrChangeOrder order) {
			if( debug) log.Debug("Changing actual position from " + this.actualPosition + " to " + (actualPosition+size) + ". Fill size is " + size);
			this.actualPosition += size;
            //if( onPositionChange != null) {
            //    onPositionChange( actualPosition);
            //}
			var fill = new PhysicalFillDefault(size,price,time,utcTime,order,createSimulatedFills);
			if( debug) log.Debug("Fill: " + fill );
			if( SyncTicks.Enabled) tickSync.AddPhysicalFill(fill);
		    fillQueue.Enqueue(new FillEvent
		                          {
		                              PhysicalFill = fill,
                                      TotalSize = totalSize,
                                      CumulativeSize = cumulativeSize,
                                      RemainingSize = remainingSize
                                  });
		}
		
		public bool UseSyntheticLimits {
			get { return useSyntheticLimits; }
			set { useSyntheticLimits = value; }
		}
		
		public bool UseSyntheticStops {
			get { return useSyntheticStops; }
			set { useSyntheticStops = value; }
		}
		
		public bool UseSyntheticMarkets {
			get { return useSyntheticMarkets; }
			set { useSyntheticMarkets = value; }
		}
		
		public Action<PhysicalFill,int,int,int> OnPhysicalFill {
			get { return onPhysicalFill; }
			set { onPhysicalFill = value; }
		}
		
		public int GetActualPosition(SymbolInfo symbol) {
			return actualPosition;
		}
		
		public int ActualPosition {
			get { return actualPosition; }
			set { if( actualPosition != value) {
					if( debug) log.Debug("Setter: ActualPosition changed from " + actualPosition + " to " + value);
					actualPosition = value;
					if( onPositionChange != null) {
						onPositionChange( actualPosition);
					}
				}
			}
		}
		
		public Action<int> OnPositionChange {
			get { return onPositionChange; }
			set { onPositionChange = value; }
		}
		
		public PhysicalOrderConfirm ConfirmOrders {
			get { return confirmOrders; }
			set { confirmOrders = value;
				if( confirmOrders == this) {
					throw new ApplicationException("Please set ConfirmOrders to an object other than itself to avoid circular loops.");
				}
			}
		}
		
		public bool IsBarData {
			get { return isBarData; }
			set { isBarData = value; }
		}
		
		public Action<CreateOrChangeOrder, string> OnRejectOrder {
			get { return onRejectOrder; }
			set { onRejectOrder = value; }
		}

	    public TimeStamp CurrentTick
	    {
	        get { return currentTick.UtcTime; }
	    }
	}
}