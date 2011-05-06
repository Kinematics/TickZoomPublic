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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using TickZoom.Api;
using TickZoom.FIX;
using TickZoom.MBTQuotes;

namespace TickZoom.MBTFIX
{
	public class MBTFIXSimulator : FIXSimulatorSupport {
		private static Log log = Factory.SysLog.GetLogger(typeof(MBTFIXSimulator));
		private static bool trace = log.IsTraceEnabled;
		private static bool debug = log.IsDebugEnabled;
		private ServerState quoteState = ServerState.Startup;
		
		public MBTFIXSimulator(string mode) : base( mode, 6489, 6488, new MessageFactoryFix44(), new MessageFactoryMbtQuotes()) {
		    
		}

        protected override void OnConnectFIX(Socket socket)
		{
			quoteState = ServerState.Startup;
			base.OnConnectFIX(socket);
		}
		
		public override void StartFIXSimulation()
		{
			base.StartFIXSimulation();
		}
	
		public override void StartQuoteSimulation()
		{
			base.StartQuoteSimulation();
		}
		
		public override void ParseFIXMessage(Message message)
		{
			var packetFIX = (MessageFIX4_4) message;
			switch( packetFIX.MessageType) {
				case "AF": // Request Orders
					FIXOrderList( packetFIX);
					break;
				case "AN": // Request Positions
					FIXPositionList( packetFIX);
					break;
				case "G":
					FIXChangeOrder( packetFIX);
					break;
				case "D":
					FIXCreateOrder( packetFIX);
					break;
				case "F":
					FIXCancelOrder( packetFIX);
					break;
				case "0":
					if( debug) log.Debug("Received heartbeat response.");
					break;
                case "5":
                    log.Info("Received logout message.");
                    Dispose();
                    break;
                default: 
					throw new ApplicationException("Unknown FIX message type '" + packetFIX.MessageType + "'\n" + packetFIX);
			}			
		}
		
		public override void ParseQuotesMessage(Message message)
		{
			var packetQuotes = (MessageMbtQuotes) message;
			char firstChar = (char) packetQuotes.Data.GetBuffer()[packetQuotes.Data.Position];
			switch( firstChar) {
				case 'L': // Login
					QuotesLogin( packetQuotes);
					break;
				case 'S':
					SymbolRequest( packetQuotes);
					break;
			}			
		}
		
		private void FIXOrderList(MessageFIX4_4 packet)
		{
			var mbtMsg = (FIXMessage4_4) FixFactory.Create();
			mbtMsg.SetText("END");
			mbtMsg.AddHeader("8");
            if (debug) log.Debug("Sending end of order list: " + mbtMsg);
            SendMessage(mbtMsg);
        }

		private void FIXPositionList(MessageFIX4_4 packet)
		{
			var mbtMsg = (FIXMessage4_4) FixFactory.Create();
			mbtMsg.SetText("DONE");
			mbtMsg.AddHeader("AO");
            if (debug) log.Debug("Sending end of position list: " + mbtMsg);
            SendMessage(mbtMsg);
		}
		
		private void FIXChangeOrder(MessageFIX4_4 packet) {
			var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
			PhysicalOrder origOrder = null;
			if( debug) log.Debug( "FIXChangeOrder() for " + packet.Symbol + ". Client id: " + packet.ClientOrderId + ". Original client id: " + packet.OriginalClientOrderId);
			try {
				origOrder = GetOrderById( symbol, packet.OriginalClientOrderId);
			} catch( ApplicationException) {
				log.Warn( symbol + ": Cannot change order by client id: " + packet.OriginalClientOrderId + ". Probably already filled or canceled. Should send a reject in this case.");
				if( SyncTicks.Enabled) {
					var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
					tickSync.RemovePhysicalOrder();
				}
				return;
			}
			var order = ConstructOrder( packet, packet.ClientOrderId);
			if( order.Side != origOrder.Side) {
				var message = "Cannot change " + origOrder.Side + " to " + order.Side;
				log.Error( message);
				OnRejectOrder(origOrder,message);
				return;     
			}
			if( order.Type != origOrder.Type) {
				var message = "Cannot change " + origOrder.Type + " to " + order.Type;
				log.Error( message);
				OnRejectOrder(origOrder,message);
				return;     
			}
			SendExecutionReport( order, "E", 0.0, 0, 0, 0, (int) order.Size, TimeStamp.UtcNow, packet);
			SendPositionUpdate( order.Symbol, GetPosition(order.Symbol));
			SendExecutionReport( order, "5", 0.0, 0, 0, 0, (int) order.Size, TimeStamp.UtcNow, packet);
			SendPositionUpdate( order.Symbol, GetPosition(order.Symbol));
			ChangeOrder(order, packet.OriginalClientOrderId);
		}
		
		private Yield FIXCancelOrder(MessageFIX4_4 packet) {
			var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
			if( debug) log.Debug( "FIXCancelOrder() for " + packet.Symbol + ". Original client id: " + packet.OriginalClientOrderId);
			PhysicalOrder order = null;
			try {
				order = GetOrderById( symbol, packet.OriginalClientOrderId);
			} catch( ApplicationException) {
				if( debug) log.Debug( symbol + ": Cannot cancel order by client id: " + packet.OriginalClientOrderId + ". Probably already filled or canceled. Should send a reject in this case.");
				if( SyncTicks.Enabled) {
					var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
					tickSync.RemovePhysicalOrder();
				}
				return Yield.DidWork.Return;
			}
//			log.Info( Message.Symbol + ": Canceling order for client id: " + Message.OriginalClientOrderId);
			CancelOrder( symbol, order.BrokerOrder);
			SendExecutionReport( order, "6", 0.0, 0, 0, 0, (int) order.Size, TimeStamp.UtcNow, packet);
			SendPositionUpdate( order.Symbol, GetPosition(order.Symbol));
			SendExecutionReport( order, "4", 0.0, 0, 0, 0, (int) order.Size, TimeStamp.UtcNow, packet);
			SendPositionUpdate( order.Symbol, GetPosition(order.Symbol));
			return Yield.DidWork.Repeat;
		}
		
		private Yield FIXCreateOrder(MessageFIX4_4 packet) {
			if( debug) log.Debug( "FIXCreateOrder() for " + packet.Symbol + ". Client id: " + packet.ClientOrderId);
			var order = ConstructOrder( packet, packet.ClientOrderId);
//			log.Info( Message.Symbol + ": Creating order for client id: " + Message.ClientOrderId);
			if( string.IsNullOrEmpty(packet.ClientOrderId)) {
				System.Diagnostics.Debugger.Break();
			}
			SendExecutionReport( order, "A", 0.0, 0, 0, 0, (int) order.Size, TimeStamp.UtcNow, packet);
			SendPositionUpdate( order.Symbol, GetPosition(order.Symbol));
			SendExecutionReport( order, "0", 0.0, 0, 0, 0, (int) order.Size, TimeStamp.UtcNow, packet);
			SendPositionUpdate( order.Symbol, GetPosition(order.Symbol));
			CreateOrder( order);
			return Yield.DidWork.Repeat;
		}
		
		private PhysicalOrder ConstructOrder(MessageFIX4_4 packet, string clientOrderId) {
			if( string.IsNullOrEmpty(clientOrderId)) {
				var message = "Client order id was null or empty. FIX Message is: " + packet;
				log.Error(message);
				throw new ApplicationException(message);
			}
			var symbol = Factory.Symbol.LookupSymbol(packet.Symbol);
			var side = OrderSide.Buy;
			switch( packet.Side) {
				case "1":
					side = OrderSide.Buy;
					break;
				case "2":
					side = OrderSide.Sell;
					break;
				case "5":
					side = OrderSide.SellShort;
					break;
			}
			var type = OrderType.BuyLimit;
			switch( packet.OrderType) {
				case "1":
					if( side == OrderSide.Buy) {
						type = OrderType.BuyMarket;
					} else {
						type = OrderType.SellMarket;
					}
					break;
				case "2":
					if( side == OrderSide.Buy) {
						type = OrderType.BuyLimit;
					} else {
						type = OrderType.SellLimit;
					}
					break;
				case "3":
					if( side == OrderSide.Buy) {
						type = OrderType.BuyStop;
					} else {
						type = OrderType.SellStop;
					}
					break;
			}
			var clientId = clientOrderId.Split(new char[] {'.'});
			var logicalId = int.Parse(clientId[0]);
			var physicalOrder = Factory.Utility.PhysicalOrder(
				OrderState.Active, symbol, side, type,
				packet.Price, packet.OrderQuantity, logicalId, 0, clientOrderId, null);
			if( debug) log.Debug("Received physical Order: " + physicalOrder);
			return physicalOrder;
		}

        protected override FIXTFactory1_1 CreateFIXFactory(int sequence, string target, string sender)
        {
            this.target = target;
            this.sender = sender;
            return new FIXFactory4_4(sequence, target, sender);
        }
		
		private string target;
		private string sender;
		
		private void QuotesLogin(MessageMbtQuotes message) {
			if( quoteState != ServerState.Startup) {
				CloseWithQuotesError(message, "Invalid login request. Already logged in.");
			}
			quoteState = ServerState.LoggedIn;
		    var writePacket = quoteSocket.MessageFactory.Create();
			string textMessage = "G|100=DEMOXJSP;8055=demo01\n";
			if( debug) log.Debug("Login response: " + textMessage);
			writePacket.DataOut.Write(textMessage.ToCharArray());
			while( !quotePacketQueue.EnqueueStruct(ref writePacket,message.SendUtcTime)) {
				if( quotePacketQueue.IsFull) {
					throw new ApplicationException("Quote Queue is full.");
				}
			}
		}
		
		private void OnPhysicalFill( PhysicalFill fill, int totalSize, int cumulativeSize, int remainingSize) {
			if( debug) log.Debug("Converting physical fill to FIX: " + fill);
			SendPositionUpdate(fill.Order.Symbol, GetPosition(fill.Order.Symbol));
			var orderStatus = cumulativeSize == totalSize ? "2" : "1";
			SendExecutionReport( fill.Order, orderStatus, "F", fill.Price, totalSize, cumulativeSize, fill.Size, remainingSize, fill.UtcTime, null);
		}

		private void OnRejectOrder( PhysicalOrder order, string error)
		{
			var mbtMsg = (FIXMessage4_4) FixFactory.Create();
			mbtMsg.SetAccount( "33006566");
			mbtMsg.SetClientOrderId( order.BrokerOrder.ToString());
			mbtMsg.SetOrderStatus("8");
			mbtMsg.SetText(error);
			mbtMsg.AddHeader("8");
            if (debug) log.Debug("Sending position update: " + mbtMsg);
            SendMessage(mbtMsg);
        }	
		
		private void SendPositionUpdate(SymbolInfo symbol, int position)
		{
			var mbtMsg = (FIXMessage4_4) FixFactory.Create();
			mbtMsg.SetAccount( "33006566");
			mbtMsg.SetSymbol( symbol.Symbol);
			if( position <= 0) {
				mbtMsg.SetShortQty( position);
			} else {
				mbtMsg.SetLongQty( position);
			}
			mbtMsg.AddHeader("AP");
            SendMessage(mbtMsg);
			if(debug) log.Debug("Sending position update: " + mbtMsg);
        }	

		private void SendExecutionReport(PhysicalOrder order, string status, double price, int orderQty, int cumQty, int lastQty, int leavesQty, TimeStamp time, MessageFIX4_4 packet)
		{
		    SendExecutionReport(order, status, status, price, orderQty, cumQty, lastQty, leavesQty, time, packet);
		}

	    private void SendExecutionReport(PhysicalOrder order, string status, string executionType, double price, int orderQty, int cumQty, int lastQty, int leavesQty, TimeStamp time, MessageFIX4_4 packet) {
			int orderType = 0;
			switch( order.Type) {
				case OrderType.BuyMarket:
				case OrderType.SellMarket:
					orderType = 1;
					break;
				case OrderType.BuyLimit:
				case OrderType.SellLimit:
					orderType = 2;
					break;
				case OrderType.BuyStop:
				case OrderType.SellStop:
					orderType = 3;
					break;
			}
			int orderSide = 0;
			switch( order.Side) {
				case OrderSide.Buy:
					orderSide = 1;
					break;
				case OrderSide.Sell:
					orderSide = 2;
					break;
				case OrderSide.SellShort:
					orderSide = 5;
					break;
			}
			var mbtMsg = (FIXMessage4_4) FixFactory.Create();
			mbtMsg.SetAccount( "33006566");
			mbtMsg.SetDestination("MBTX");
			mbtMsg.SetOrderQuantity( orderQty);
			mbtMsg.SetLastQuantity( Math.Abs(lastQty));
			if( lastQty != 0) {
				mbtMsg.SetLastPrice( price);
			}
			mbtMsg.SetCumulativeQuantity( Math.Abs(cumQty));
			mbtMsg.SetOrderStatus(status);
			mbtMsg.SetPositionEffect( "O");
			mbtMsg.SetOrderType( orderType);
			mbtMsg.SetSide( orderSide);
			if( packet == null) {
				mbtMsg.SetClientOrderId( order.BrokerOrder.ToString());
			} else {
				mbtMsg.SetClientOrderId( packet.ClientOrderId);
				if( packet.OriginalClientOrderId != null) {
					mbtMsg.SetOriginalClientOrderId( packet.OriginalClientOrderId);
				}
			}
			mbtMsg.SetPrice( order.Price);
			mbtMsg.SetSymbol( order.Symbol.Symbol);
			mbtMsg.SetTimeInForce( 0);
			mbtMsg.SetExecutionType( executionType);
			mbtMsg.SetTransactTime( time);
			mbtMsg.SetLeavesQuantity( Math.Abs(leavesQty));
			mbtMsg.AddHeader("8");
            SendMessage(mbtMsg);
			if(debug) log.Debug("Sending execution report: " + mbtMsg);
		}

		
		private unsafe Yield SymbolRequest(MessageMbtQuotes message) {
			var symbolInfo = Factory.Symbol.LookupSymbol(message.Symbol);
			log.Info("Received symbol request for " + symbolInfo);
			AddSymbol(symbolInfo.Symbol, OnTick, OnPhysicalFill, OnRejectOrder);
			switch( message.FeedType) {
				case "20000": // Level 1
					if( symbolInfo.QuoteType != QuoteType.Level1) {
						throw new ApplicationException("Requested data feed of Level1 but Symbol.QuoteType is " + symbolInfo.QuoteType);
					}
					break;
				case "20001": // Level 2
					if( symbolInfo.QuoteType != QuoteType.Level2) {
						throw new ApplicationException("Requested data feed of Level2 but Symbol.QuoteType is " + symbolInfo.QuoteType);
					}
					break;
				case "20002": // Level 1 & Level 2
					if( symbolInfo.QuoteType != QuoteType.Level2) {
						throw new ApplicationException("Requested data feed of Level1 and Level2 but Symbol.QuoteType is " + symbolInfo.QuoteType);
					}
					break;
				case "20003": // Trades
					if( symbolInfo.TimeAndSales != TimeAndSales.ActualTrades) {
						throw new ApplicationException("Requested data feed of Trades but Symbol.TimeAndSale is " + symbolInfo.TimeAndSales);
					}
					break;
				case "20004": // Option Chains
					break;
				default:
					throw new ApplicationException("Sorry, unknown data type: " + message.FeedType);
			}
			return Yield.DidWork.Repeat;
		}

        private StringBuilder quoteBuilder = new StringBuilder(256);
        private Dictionary<long, TickIO> lastTicks = new Dictionary<long, TickIO>();
		private void OnTick( Message quoteMessage, SymbolInfo symbol, Tick tick) {
			if( trace) log.Trace("Sending tick: " + tick);
			TickIO lastTick;
			if( !lastTicks.TryGetValue( symbol.BinaryIdentifier, out lastTick)) {
			   	lastTick = Factory.TickUtil.TickIO();
			   	lastTicks[symbol.BinaryIdentifier] = lastTick;
			}
			quoteBuilder.Length = 0;
            if( tick.IsTrade) {
				quoteBuilder.Append("3|"); // Trade
			} else {
				quoteBuilder.Append("1|"); // Level 1
			}
			quoteBuilder.Append("2026=USD;"); //Currency
			quoteBuilder.Append("1003="); //Symbol
			quoteBuilder.Append(symbol.Symbol);
			quoteBuilder.Append(';');
			quoteBuilder.Append("2037=0;"); //Open Interest
			quoteBuilder.Append("2085=.144;"); //Unknown
			quoteBuilder.Append("2048=00/00/2009;"); //Unknown
			quoteBuilder.Append("2049=00/00/2009;"); //Unknown
			if( tick.IsTrade) {
				quoteBuilder.Append("2002="); //Last Trade.
				quoteBuilder.Append(tick.Price);
				quoteBuilder.Append(';');
				quoteBuilder.Append("2007=");
				quoteBuilder.Append(tick.Size);
				quoteBuilder.Append(';');
			}
			quoteBuilder.Append("2050=0;"); //Unknown
			if( tick.lBid != lastTick.lBid) {
				quoteBuilder.Append("2003="); // Last Bid
				quoteBuilder.Append(tick.Bid);
				quoteBuilder.Append(';');
			}
			quoteBuilder.Append("2051=0;"); //Unknown
			if( tick.lAsk != lastTick.lAsk) {
				quoteBuilder.Append("2004="); //Last Ask 
				quoteBuilder.Append(tick.Ask);
				quoteBuilder.Append(';');
			}
			quoteBuilder.Append("2052=00/00/2010;"); //Unknown
			var askSize = Math.Max((int)tick.AskLevel(0),1);
			if( askSize != lastTick.AskLevel(0)) {
				quoteBuilder.Append("2005="); 
				quoteBuilder.Append(askSize);
				quoteBuilder.Append(';');
			}
			var bidSize = Math.Max((int)tick.BidLevel(0),1);
			quoteBuilder.Append("2053=00/00/2010;"); //Unknown
			if( bidSize != lastTick.BidLevel(0)) {
				quoteBuilder.Append("2006=");
				quoteBuilder.Append(bidSize);
				quoteBuilder.Append(';');
			}
			quoteBuilder.Append("2008=0.0;"); // Yesterday Close
			quoteBuilder.Append("2056=0.0;"); // Unknown
			quoteBuilder.Append("2009=0.0;"); // High today
			quoteBuilder.Append("2057=0;"); // Unknown
			quoteBuilder.Append("2010=0.0"); // Low today
			quoteBuilder.Append("2058=1;"); // Unknown
			quoteBuilder.Append("2011=0.0;"); // Open Today
			quoteBuilder.Append("2012=6828928;"); // Volume Today
			quoteBuilder.Append("2013=20021;"); // Up/Down Tick
			quoteBuilder.Append("2014="); // Time
			quoteBuilder.Append(tick.UtcTime.TimeOfDay);
			quoteBuilder.Append(".");
			quoteBuilder.Append(tick.UtcTime.Microsecond);
			quoteBuilder.Append(';');
			quoteBuilder.Append("2015=");
			quoteBuilder.Append(tick.UtcTime.Month.ToString("00"));
			quoteBuilder.Append('/');
			quoteBuilder.Append(tick.UtcTime.Day.ToString("00"));
			quoteBuilder.Append('/');
			quoteBuilder.Append(tick.UtcTime.Year);
			quoteBuilder.Append('\n');
			var message = quoteBuilder.ToString();
			if( trace) log.Trace("Tick message: " + message);
			quoteMessage.DataOut.Write(message.ToCharArray());
			lastTick.Inject(tick.Extract());
		}
		
		private void CloseWithQuotesError(MessageMbtQuotes message, string textMessage) {
		}
		
		private void CloseWithFixError(MessageFIX4_4 packet, string textMessage)
		{
			var fixMsg = (FIXMessage4_4) FixFactory.Create();
			TimeStamp timeStamp = TimeStamp.UtcNow;
			fixMsg.SetAccount(packet.Account);
			fixMsg.SetText( textMessage);
			fixMsg.AddHeader("j");
		    SendMessage(fixMsg);
        }
		
		protected override void Dispose(bool disposing)
		{
			if( !isDisposed) {
				if( disposing) {
					base.Dispose(disposing);
				}
			}
		}
	}
}
