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
    public struct PhysicalOrderBinary
    {
        public int sequence;
    	public OrderAction action;
        public OrderState orderState;
        public TimeStamp lastStateChange;
        public SymbolInfo symbol;
        public OrderType type;
        public double price;
        public int size;
        public OrderSide side;
        public int logicalOrderId;
        public long logicalSerialNumber;
        public string brokerOrder;
        public string tag;
        public object reference;
        public CreateOrChangeOrder originalOrder;
        public CreateOrChangeOrder replacedBy;
        public TimeStamp utcCreateTime;
    }

	public class CreateOrChangeOrderDefault : CreateOrChangeOrder
	{
	    private PhysicalOrderBinary binary;
		
        public CreateOrChangeOrderDefault(OrderState orderState, SymbolInfo symbol, CreateOrChangeOrder origOrder)
        {
            binary.action = OrderAction.Cancel;
            binary.orderState = orderState;
            binary.lastStateChange = TimeStamp.UtcNow;
            binary.symbol = symbol;
            binary.side = default(OrderSide);
            binary.type = default(OrderType);
            binary.price = 0D;
            binary.size = 0;
            binary.logicalOrderId = 0;
            binary.logicalSerialNumber = 0L;
            binary.tag = null;
            binary.reference = null;
            binary.brokerOrder = CreateBrokerOrderId(binary.logicalOrderId);
            binary.utcCreateTime = TimeStamp.UtcNow;
            if( origOrder == null)
            {
                throw new NullReferenceException("original order cannot be null for a cancel order.");
            }
            binary.originalOrder = origOrder;
            binary.replacedBy = default(CreateOrChangeOrder);
        }

        public CreateOrChangeOrder Clone()
        {
            var clone = new CreateOrChangeOrderDefault();
            clone.binary = this.binary;
            return clone;
        }

        private CreateOrChangeOrderDefault()
        {
            
        }

		public CreateOrChangeOrderDefault(OrderAction orderAction, SymbolInfo symbol, LogicalOrder logical, OrderSide side, int size, double price)
            : this(OrderState.Active,symbol,logical,side,size,price)
		{
		    binary.action = orderAction;
		}
		
		public CreateOrChangeOrderDefault(OrderState orderState, SymbolInfo symbol, LogicalOrder logical, OrderSide side, int size, double price)
		{
            binary.action = OrderAction.Create;
			binary.orderState = orderState;
		    binary.lastStateChange = TimeStamp.UtcNow;
			binary.symbol = symbol;
			binary.side = side;
			binary.type = logical.Type;
			binary.price = price;
			binary.size = size;
			binary.logicalOrderId = logical.Id;
			binary.logicalSerialNumber = logical.SerialNumber;
			binary.tag = logical.Tag;
			binary.reference = null;
			binary.replacedBy = null;
		    binary.originalOrder = null;
			binary.brokerOrder = CreateBrokerOrderId(binary.logicalOrderId);
		    binary.utcCreateTime = logical.UtcChangeTime;
		}

	    public CreateOrChangeOrderDefault(OrderAction action, OrderState orderState, SymbolInfo symbol, OrderSide side, OrderType type, double price, int size, int logicalOrderId, long logicalSerialNumber, string brokerOrder, string tag, TimeStamp utcCreateTime)
	    {
            binary.action = action;
			binary.orderState = orderState;
		    binary.lastStateChange = TimeStamp.UtcNow;
			binary.symbol = symbol;
			binary.side = side;
			binary.type = type;
			binary.price = price;
			binary.size = size;
			binary.logicalOrderId = logicalOrderId;
			binary.logicalSerialNumber = logicalSerialNumber;
			binary.tag = tag;
			binary.brokerOrder = brokerOrder;
			binary.reference = null;
			binary.replacedBy = null;
	        binary.originalOrder = null;
			if( binary.brokerOrder == null) {
                binary.brokerOrder = CreateBrokerOrderId(binary.logicalOrderId);
			}
	        binary.utcCreateTime = utcCreateTime;
	    }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(binary.action);
            sb.Append(" ");
            sb.Append(binary.orderState);
            sb.Append(" ");
            sb.Append(binary.side);
            sb.Append(" ");
            sb.Append(binary.size);
            sb.Append(" ");
            sb.Append(binary.type);
            sb.Append(" ");
            sb.Append(binary.symbol);
            if (binary.type != OrderType.BuyMarket && binary.type != OrderType.SellMarket)
            {
                sb.Append(" at ");
                sb.Append(binary.price);
            }
            sb.Append(" and logical id: ");
            sb.Append(binary.logicalOrderId);
            sb.Append("-");
            sb.Append(binary.logicalSerialNumber);
            if (binary.brokerOrder != null)
            {
                sb.Append(" broker: ");
                sb.Append(binary.brokerOrder);
            }
            if (binary.originalOrder != null)
            {
                sb.Append(" original: ");
                sb.Append(binary.originalOrder.BrokerOrder);
            }
            if (binary.replacedBy != null)
            {
                sb.Append(" replaced by: ");
                sb.Append(binary.replacedBy.BrokerOrder);
            }
            if (binary.tag != null)
            {
                sb.Append(" ");
                sb.Append(binary.tag);
            }
            return sb.ToString();
        }

        private static long lastId = TimeStamp.UtcNow.Internal;
		private static string CreateBrokerOrderId(int logicalId) {
			var longId = Interlocked.Increment(ref lastId);
			return logicalId + "." + longId;
		}
		
		public OrderType Type {
            get { return binary.type; }
		}
		
		public double Price {
            get { return binary.price; }
		}
		
		public int Size {
            get { return binary.size; }
            set { binary.size = value; }
		}
		
		public string BrokerOrder {
            get { return binary.brokerOrder; }
			set
			{
                binary.brokerOrder = value;
			}
		}

		
		public SymbolInfo Symbol {
            get { return binary.symbol; }
		}
		
		public int LogicalOrderId {
            get { return binary.logicalOrderId; }
		}
		
		public OrderSide Side {
            get { return binary.side; }
		}
		
		public OrderState OrderState {
            get { return binary.orderState; }
            set
            {
                if (value != binary.orderState)
                {
                    binary.orderState = value;
                    binary.lastStateChange = TimeStamp.UtcNow;
                }
            }
		}

		public string Tag {
            get { return binary.tag; }
		}
		
		public long LogicalSerialNumber {
            get { return binary.logicalSerialNumber; }
		}
		
		public object Reference {
            get { return binary.reference; }
            set { binary.reference = value; }
		}

		public CreateOrChangeOrder ReplacedBy {
            get { return binary.replacedBy; }
            set { binary.replacedBy = value; }
		}

        public override bool Equals(object obj)
        {
            if( ! (obj is CreateOrChangeOrder))
            {
                return false;
            }
            var other = (CreateOrChangeOrder) obj;
            return binary.brokerOrder == other.BrokerOrder;
            //return logicalOrderId == other.LogicalOrderId && logicalSerialNumber == other.LogicalSerialNumber &&
            //       action == other.Action && orderState == other.OrderState &&
            //       lastStateChange == other.LastStateChange && symbol == other.Symbol &&
            //       type == other.Type && price == other.Price &&
            //       size == other.Size && side == other.Side &&
            //       brokerOrder == other.BrokerOrder && utcCreateTime == other.UtcCreateTime;
        }

        public override int GetHashCode()
        {
            return binary.brokerOrder.GetHashCode();
            //return action.GetHashCode() ^ orderState.GetHashCode() ^
            //       lastStateChange.GetHashCode() ^ symbol.GetHashCode() ^
            //       type.GetHashCode() ^ price.GetHashCode() ^
            //       size.GetHashCode() ^ side.GetHashCode() ^
            //       logicalOrderId.GetHashCode() ^ logicalSerialNumber.GetHashCode() ^
            //       brokerOrder.GetHashCode() ^ utcCreateTime.GetHashCode();
        }

	    public TimeStamp LastStateChange
	    {
            get { return binary.lastStateChange; }
	    }

	    public TimeStamp UtcCreateTime
	    {
            get { return binary.utcCreateTime; }
	    }

	    public OrderAction Action
	    {
            get { return binary.action; }
	    }

        public CreateOrChangeOrder OriginalOrder
	    {
            get { return binary.originalOrder; }
            set { binary.originalOrder = value; }
	    }

        public int Sequence
        {
            get { return binary.sequence; }
            set { binary.sequence = value; }
        }
    }
}