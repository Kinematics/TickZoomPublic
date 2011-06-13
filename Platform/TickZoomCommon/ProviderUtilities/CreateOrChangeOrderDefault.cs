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
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;

using TickZoom.Api;

namespace TickZoom.Common
{
	public struct CreateOrChangeOrderDefault : CreateOrChangeOrder
	{
	    private OrderAction action;
		private OrderState orderState;
	    private TimeStamp lastStateChange;
		private SymbolInfo symbol;
		private OrderType type;
		private double price;
		private int size;
		private OrderSide side;
		private int logicalOrderId;
		private long logicalSerialNumber;
		private string brokerOrder;
		private string tag;
        private object reference;
        private CreateOrChangeOrder originalOrder;
        private CreateOrChangeOrder replacedBy;
        private TimeStamp utcCreateTime;
		
        public CreateOrChangeOrderDefault(OrderState orderState, SymbolInfo symbol, CreateOrChangeOrder origOrder)
        {
            this.action = OrderAction.Cancel;
            this.orderState = orderState;
            this.lastStateChange = TimeStamp.UtcNow;
            this.symbol = symbol;
            this.side = default(OrderSide);
            this.type = default(OrderType);
            this.price = 0D;
            this.size = 0;
            this.logicalOrderId = 0;
            this.logicalSerialNumber = 0L;
            this.tag = null;
            this.reference = null;
            this.brokerOrder = CreateBrokerOrderId(logicalOrderId);
            this.utcCreateTime = TimeStamp.UtcNow;
            if( origOrder == null)
            {
                throw new NullReferenceException("original order cannot be null for a cancel order.");
            }
            this.originalOrder = origOrder;
            this.replacedBy = default(CreateOrChangeOrder);
        }
		
		public CreateOrChangeOrderDefault(OrderState orderState, SymbolInfo symbol, LogicalOrder logical, OrderSide side, int size, double price)
		{
            this.action = OrderAction.Create;
			this.orderState = orderState;
		    this.lastStateChange = TimeStamp.UtcNow;
			this.symbol = symbol;
			this.side = side;
			this.type = logical.Type;
			this.price = price;
			this.size = size;
			this.logicalOrderId = logical.Id;
			this.logicalSerialNumber = logical.SerialNumber;
			this.tag = logical.Tag;
			this.reference = null;
			this.replacedBy = null;
		    this.originalOrder = null;
			this.brokerOrder = CreateBrokerOrderId(logicalOrderId);
		    this.utcCreateTime = logical.UtcChangeTime;
		}

	    public CreateOrChangeOrderDefault(OrderAction action, OrderState orderState, SymbolInfo symbol, OrderSide side, OrderType type, double price, int size, int logicalOrderId, long logicalSerialNumber, string brokerOrder, string tag, TimeStamp utcCreateTime)
	    {
            this.action = action;
			this.orderState = orderState;
		    this.lastStateChange = TimeStamp.UtcNow;
			this.symbol = symbol;
			this.side = side;
			this.type = type;
			this.price = price;
			this.size = size;
			this.logicalOrderId = logicalOrderId;
			this.logicalSerialNumber = logicalSerialNumber;
			this.tag = tag;
			this.brokerOrder = brokerOrder;
			this.reference = null;
			this.replacedBy = null;
	        this.originalOrder = null;
			if( this.brokerOrder == null) {
				this.brokerOrder = CreateBrokerOrderId(logicalOrderId);
			}
	        this.utcCreateTime = utcCreateTime;
	    }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(action);
            sb.Append(" ");
            sb.Append(orderState);
            sb.Append(" ");
            sb.Append(side);
            sb.Append(" ");
            sb.Append(size);
            sb.Append(" ");
            sb.Append(type);
            sb.Append(" ");
            sb.Append(symbol);
            if (type != OrderType.BuyMarket && type != OrderType.SellMarket)
            {
                sb.Append(" at ");
                sb.Append(price);
            }
            sb.Append(" and logical id: ");
            sb.Append(logicalOrderId);
            sb.Append("-");
            sb.Append(logicalSerialNumber);
            if (brokerOrder != null)
            {
                sb.Append(" broker: ");
                sb.Append(brokerOrder);
            }
            if( originalOrder != null)
            {
                sb.Append(" original: ");
                sb.Append(originalOrder.BrokerOrder);
            }
            if (replacedBy != null)
            {
                sb.Append(" replaced by: ");
                sb.Append(replacedBy.BrokerOrder);
            }
            if (tag != null)
            {
                sb.Append(" ");
                sb.Append(tag);
            }
            return sb.ToString();
        }

        private static long lastId = TimeStamp.UtcNow.Internal;
		private static string CreateBrokerOrderId(int logicalId) {
			var longId = Interlocked.Increment(ref lastId);
			return logicalId + "." + longId;
		}
		
		public OrderType Type {
			get { return type; }
		}
		
		public double Price {
			get { return price; }
		}
		
		public int Size {
			get { return size; }
			set { size = value; }
		}
		
		public string BrokerOrder {
			get { return brokerOrder; }
			set
			{
			    brokerOrder = value;
			}
		}

		
		public SymbolInfo Symbol {
			get { return symbol; }
		}
		
		public int LogicalOrderId {
			get { return logicalOrderId; }
		}
		
		public OrderSide Side {
			get { return side; }
		}
		
		public OrderState OrderState {
			get { return orderState; }
            set
            {
                if( value != orderState)
                {
                    orderState = value;
                    lastStateChange = TimeStamp.UtcNow;
                }
            }
		}

		public string Tag {
			get { return tag; }
		}
		
		public long LogicalSerialNumber {
			get { return logicalSerialNumber; }
		}
		
		public object Reference {
			get { return reference; }
			set { reference = value; }
		}

		public CreateOrChangeOrder ReplacedBy {
			get { return replacedBy; }
			set { replacedBy = value; }
		}

        public override bool Equals(object obj)
        {
            if( ! (obj is CreateOrChangeOrder))
            {
                return false;
            }
            var other = (CreateOrChangeOrder) obj;
            return brokerOrder == other.BrokerOrder;
            //return logicalOrderId == other.LogicalOrderId && logicalSerialNumber == other.LogicalSerialNumber &&
            //       action == other.Action && orderState == other.OrderState &&
            //       lastStateChange == other.LastStateChange && symbol == other.Symbol &&
            //       type == other.Type && price == other.Price &&
            //       size == other.Size && side == other.Side &&
            //       brokerOrder == other.BrokerOrder && utcCreateTime == other.UtcCreateTime;
        }

        public override int GetHashCode()
        {
            return brokerOrder.GetHashCode();
            //return action.GetHashCode() ^ orderState.GetHashCode() ^
            //       lastStateChange.GetHashCode() ^ symbol.GetHashCode() ^
            //       type.GetHashCode() ^ price.GetHashCode() ^
            //       size.GetHashCode() ^ side.GetHashCode() ^
            //       logicalOrderId.GetHashCode() ^ logicalSerialNumber.GetHashCode() ^
            //       brokerOrder.GetHashCode() ^ utcCreateTime.GetHashCode();
        }

	    public TimeStamp LastStateChange
	    {
	        get { return lastStateChange; }
	    }

	    public TimeStamp UtcCreateTime
	    {
	        get { return utcCreateTime; }
	    }

	    public OrderAction Action
	    {
	        get { return action; }
	    }

        public CreateOrChangeOrder OriginalOrder
	    {
	        get { return originalOrder; }
	        set { originalOrder = value; }
	    }
	}
}