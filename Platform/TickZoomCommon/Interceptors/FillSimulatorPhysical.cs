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
    public enum LimitOrderTradeSimulation
    {
        None,
        TradeTouch,
        TradeThrough,
    }
    public enum LimitOrderQuoteSimulation
    {
        None,
		SameSideQuoteTouch,
        SameSideQuoteThrough,
        OppositeQuoteTouch,
        OppositeQuoteThrough,
	}
	public class FillSimulatorPhysical : FillSimulator
	{
		private static readonly Log staticLog = Factory.SysLog.GetLogger(typeof(FillSimulatorPhysical));
		private readonly bool trace = staticLog.IsTraceEnabled;
		private readonly bool debug = staticLog.IsDebugEnabled;
		private static readonly bool notice = staticLog.IsNoticeEnabled;
		private Log log;

		private Dictionary<string,PhysicalOrder> orderMap = new Dictionary<string, PhysicalOrder>();
		private ActiveList<PhysicalOrder> increaseOrders = new ActiveList<PhysicalOrder>();
		private ActiveList<PhysicalOrder> decreaseOrders = new ActiveList<PhysicalOrder>();
		private ActiveList<PhysicalOrder> marketOrders = new ActiveList<PhysicalOrder>();
		private NodePool<PhysicalOrder> nodePool = new NodePool<PhysicalOrder>();
		private object orderMapLocker = new object();
		private bool isOpenTick = false;
		private TimeStamp openTime;

		private Action<PhysicalFill,int,int,int> onPhysicalFill;
		private Action<PhysicalOrder,string> onRejectOrder;
		private Action<int> onPositionChange;
		private bool useSyntheticMarkets = true;
		private bool useSyntheticStops = true;
		private bool useSyntheticLimits = true;
		private SymbolInfo symbol;
		private int actualPosition = 0;
		private TickSync tickSync;
		private TickIO currentTick = Factory.TickUtil.TickIO();
		private PhysicalOrderHandler confirmOrders;
		private bool isBarData = false;
		private bool createSimulatedFills = false;
		private LimitOrderQuoteSimulation limitOrderQuoteSimulation = LimitOrderQuoteSimulation.OppositeQuoteTouch;
        private LimitOrderTradeSimulation limitOrderTradeSimulation = LimitOrderTradeSimulation.TradeTouch;
        // Randomly rotate the partial fills but using a fixed
		// seed so that test results are reproducable.
		private Random random = new Random(1234);
		private long minimumTick;
		
		public FillSimulatorPhysical(string name, SymbolInfo symbol, bool createSimulatedFills)
		{
			this.symbol = symbol;
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
		
		public Iterable<PhysicalOrder> GetActiveOrders(SymbolInfo symbol) {
			ActiveList<PhysicalOrder> activeOrders = new ActiveList<PhysicalOrder>();
			activeOrders.AddLast(increaseOrders);
			activeOrders.AddLast(decreaseOrders);
			activeOrders.AddLast(marketOrders);
			return activeOrders;
		}
	
		public void OnChangeBrokerOrder(PhysicalOrder order, object origBrokerOrder)
		{
			if( debug) log.Debug("OnChangeBrokerOrder( " + order + ")");
			CancelBrokerOrder( origBrokerOrder);
			CreateBrokerOrder( order);
			ProcessOrders();
			if( confirmOrders != null) confirmOrders.OnChangeBrokerOrder(order, origBrokerOrder);
		}

        public bool TryGetOrderById(string orderId, out PhysicalOrder physicalOrder)
        {
            LogOpenOrders();
            lock (orderMapLocker)
            {
                return orderMap.TryGetValue(orderId, out physicalOrder);
            }
        }


        public PhysicalOrder GetOrderById(string orderId)
        {
			PhysicalOrder order;
			lock( orderMapLocker) {
				if( !TryGetOrderById( orderId, out order)) {
					throw new ApplicationException( symbol + ": Cannot find physical order by id: " + orderId);
				}
			}
			return order;
		}
		
		private PhysicalOrder CancelBrokerOrder(object origBrokerOrder) {
			var oldOrderId = (string) origBrokerOrder;
		    PhysicalOrder physicalOrder;
            if( TryGetOrderById(oldOrderId, out physicalOrder))
		    {
                var node = (ActiveListNode<PhysicalOrder>)physicalOrder.Reference;
		        if (node.List != null)
		        {
		            node.List.Remove(node);
		        }
		        lock (orderMapLocker)
		        {
		            orderMap.Remove(oldOrderId);
		        }
		        LogOpenOrders();
		    } else
		    {
		        log.Info("PhysicalOrder too late to cancel or already canceled, ignoring: " + origBrokerOrder);
		    }
            return physicalOrder;
		}
		
		private void CreateBrokerOrder(PhysicalOrder order) {
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
		
		public void OnCreateBrokerOrder(PhysicalOrder order)
		{
			if( debug) log.Debug("OnCreateBrokerOrder( " + order + ")");
			if( order.Size <= 0) {
				throw new ApplicationException("Sorry, Size of order must be greater than zero: " + order);
			}
			CreateBrokerOrder(order);
			ProcessOrders();
			if( confirmOrders != null) confirmOrders.OnCreateBrokerOrder(order);
		}
		
		public void OnCancelBrokerOrder(SymbolInfo symbol, object origBrokerOrder)
		{
			if( debug) log.Debug("OnCancelBrokerOrder( " + origBrokerOrder + ")");
			var order = CancelBrokerOrder(origBrokerOrder);
			if( confirmOrders != null) confirmOrders.OnCancelBrokerOrder(symbol, origBrokerOrder);
		}
		
		public void ProcessOrders() {
			if( hasCurrentTick) {
				ProcessOrdersInternal( currentTick);
			}
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
			var next = marketOrders.First;
			for( var node = next; node != null; node = node.Next) {
				var order = node.Value;
				OnProcessOrder(order, tick);
			}
			next = increaseOrders.First;
			for( var node = next; node != null; node = node.Next) {
				var order = node.Value;
				OnProcessOrder(order, tick);
			}
			next = decreaseOrders.First;
			for( var node = next; node != null; node = node.Next) {
				var order = node.Value;
				OnProcessOrder(order, tick);
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
		
		private void SortAdjust(PhysicalOrder order) {
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
		
		private void AssureNode(PhysicalOrder order) {
			if( order.Reference == null) {
				order.Reference = nodePool.Create(order);
			}
		}
		
		private void Adjust(ActiveList<PhysicalOrder> list, PhysicalOrder order) {
			AssureNode(order);
			var node = (ActiveListNode<PhysicalOrder>) order.Reference;
			if( node.List == null ) {
				list.AddLast(node);
			} else if( !node.List.Equals(list)) {
				node.List.Remove(node);
				list.AddLast(node);
			}
		}
		
		private void SortAdjust(ActiveList<PhysicalOrder> list, PhysicalOrder order, Func<PhysicalOrder,PhysicalOrder,double> compare) {
			AssureNode(order);
			var orderNode = (ActiveListNode<PhysicalOrder>) order.Reference;
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

		private void VerifySide(PhysicalOrder order) {
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
		
		private void OrderSideWrongReject(PhysicalOrder order) {
			var message = "Sorry, improper setting of a " + order.Side + " order when position is " + actualPosition;
			lock( orderMapLocker) {
				orderMap.Remove((string)order.BrokerOrder);
			}
			var node = (ActiveListNode<PhysicalOrder>) order.Reference;
			if( node.List != null) {
				node.List.Remove(node);
			}
			if( onRejectOrder != null) {
				onRejectOrder( order, message);
			} else {
				throw new ApplicationException( message + " while handling order: " + order);
			}
		}
		
		private void VerifySellSide( PhysicalOrder order) {
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
		
		private void VerifyBuySide( PhysicalOrder order) {
			if( order.Side != OrderSide.Buy) {
				OrderSideWrongReject(order);
			}
		}
		
		private void OnProcessOrder(PhysicalOrder order, Tick tick)
		{
			switch (order.Type) {
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
		private bool ProcessBuyStop(PhysicalOrder order, Tick tick)
		{
			bool retVal = false;
			long price = tick.IsTrade ? tick.lPrice : tick.lAsk;
			if (price >= order.Price.ToLong()) {
				CreatePhysicalFillHelper(order.Size, price.ToDouble(), tick.Time, tick.UtcTime, order);
				retVal = true;
			}
			return retVal;
		}

		private bool ProcessSellStop(PhysicalOrder order, Tick tick)
		{
			bool retVal = false;
			long price = tick.IsQuote ? tick.lBid : tick.lPrice;
			if (price <= order.Price.ToLong()) {
				CreatePhysicalFillHelper(-order.Size, price.ToDouble(), tick.Time, tick.UtcTime, order);
				retVal = true;
			}
			return retVal;
		}

		private bool ProcessBuyMarket(PhysicalOrder order, Tick tick)
		{
			double price = tick.IsQuote ? tick.Ask : tick.Price;
			CreatePhysicalFillHelper(order.Size, price, tick.Time, tick.UtcTime, order);
			return true;
		}

        private bool ProcessBuyLimitTrade(PhysicalOrder order, Tick tick)
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
                default:
                    throw new InvalidOperationException("Unknown limit order trade simulation: " + limitOrderTradeSimulation);
            }
            if (result)
            {
                CreatePhysicalFillHelper(order.Size, fillPrice, tick.Time, tick.UtcTime, order);
            }
            return result;
        }

        private bool ProcessBuyLimitQuote(PhysicalOrder order, Tick tick)
		{
            var orderPrice = order.Price.ToLong();
            var result = false;
            var fillPrice = 0D;
            switch (limitOrderQuoteSimulation)
            {
                case LimitOrderQuoteSimulation.OppositeQuoteTouch:
                    long price = tick.IsQuote ? tick.lAsk : tick.lPrice;
                    if (tick.lAsk <= orderPrice)
                    {
                        fillPrice = tick.Ask;
                        result = true;
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown limit order quote simulation: " + limitOrderQuoteSimulation);
            }
            if (result) {
				CreatePhysicalFillHelper(order.Size, fillPrice, tick.Time, tick.UtcTime, order);
			}
			return result;
		}

        private bool ProcessSellLimitTrade(PhysicalOrder order, Tick tick)
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
                default:
                    throw new InvalidOperationException("Unknown limit order trade simulation: " + limitOrderTradeSimulation);
            }
            if( result)
            {
                CreatePhysicalFillHelper(-order.Size, fillPrice, tick.Time, tick.UtcTime, order);
            }
            return true;
        }

        private bool ProcessSellLimitQuote(PhysicalOrder order, Tick tick)
		{
            var orderPrice = order.Price.ToLong();
            var result = false;
            var fillPrice = 0D;
            switch (limitOrderQuoteSimulation)
            {
                case LimitOrderQuoteSimulation.OppositeQuoteTouch:
                    if (tick.lBid >= orderPrice)
                    {
                        fillPrice = tick.Bid;
                        result = true;
                    }
                    break;
                //case LimitOrderQuoteSimulation.SameSideQuoteTouch:
                //    ProcessSellLimitMM(order, tick);
                //    break;
                //case LimitOrderQuoteSimulation.OppositeQuoteThrough:
                //    ProcessSellLimitQuoteThrough(order, tick);
                //    break;
                default:
                    throw new InvalidOperationException("Unknown limit order quote simulation: " + limitOrderQuoteSimulation);
            }
            if( result) {
                CreatePhysicalFillHelper(-order.Size, fillPrice, tick.Time, tick.UtcTime, order);
                result = true;
            }
			return result;
		}
		
		private bool ProcessSellMarket(PhysicalOrder order, Tick tick)
		{
			if( !tick.IsQuote && !tick.IsTrade) {
				throw new ApplicationException("tick w/o either trade or quote data? " + tick);
			}
			double price = tick.IsQuote ? tick.Bid : tick.Price;
			CreatePhysicalFillHelper(-order.Size, price, tick.Time, tick.UtcTime, order);
			return true;
		}

		private int maxPartialFillsPerOrder = 10;
		private void CreatePhysicalFillHelper(int totalSize, double price, TimeStamp time, TimeStamp utcTime, PhysicalOrder order) {
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
				if( order.Size == 0) {
					CancelBrokerOrder(order.BrokerOrder);
				}
				CreateSingleFill( lastSize, totalSize, cumulativeQuantity, order.Size, price, time, utcTime, order);
			}
		}
	
		private void CreateSingleFill(int size, int totalSize, int cumulativeSize, int remainingSize, double price, TimeStamp time, TimeStamp utcTime, PhysicalOrder order) {
			if( debug) log.Debug("Changing actual position from " + this.actualPosition + " to " + (actualPosition+size) + ". Fill size is " + size);
			this.actualPosition += size;
			if( onPositionChange != null) {
				onPositionChange( actualPosition);
			}
			var fill = new PhysicalFillDefault(size,price,time,utcTime,order,createSimulatedFills);
			if( debug) log.Debug("Fill: " + fill );
			if( onPhysicalFill == null) {
				throw new ApplicationException("Please set the OnPhysicalFill property.");
			} else {
				if( SyncTicks.Enabled) tickSync.AddPhysicalFill(fill);
				onPhysicalFill(fill,totalSize,cumulativeSize,remainingSize);
			}
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
		
		public PhysicalOrderHandler ConfirmOrders {
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
		
		public Action<PhysicalOrder, string> OnRejectOrder {
			get { return onRejectOrder; }
			set { onRejectOrder = value; }
		}
	}
}