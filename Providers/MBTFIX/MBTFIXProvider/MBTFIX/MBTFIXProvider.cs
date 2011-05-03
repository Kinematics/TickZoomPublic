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
using System.IO;
using System.Text;
using System.Threading;

using TickZoom.Api;
using TickZoom.FIX;

namespace TickZoom.MBTFIX
{
    [SkipDynamicLoad]
	public class MBTFIXProvider : FIXProviderSupport, PhysicalOrderHandler
	{
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(MBTFIXProvider));
		private static readonly bool info = log.IsDebugEnabled;
		private static readonly bool debug = log.IsDebugEnabled;
		private static readonly bool trace = log.IsTraceEnabled;
		private static long nextConnectTime = 0L;
		private readonly object orderAlgorithmLocker = new object();
        private Dictionary<long,OrderAlgorithm> orderAlgorithms = new Dictionary<long,OrderAlgorithm>();
        private PhysicalOrderStore orderStore;
        long lastLoginTry = long.MinValue;
		long loginRetryTime = 10000; //milliseconds = 10 seconds.
		private bool isPositionUpdateComplete = false;
		private bool isOrderUpdateComplete = false;
		private string fixDestination = "MBT";
        private bool isPositionSynced = false;
		
		public MBTFIXProvider(string name)
		{
			log.Notice("Using config file name: " + name);
			ProviderName = "MBTFIXProvider";
            orderStore = new PhysicalOrderStore(ProviderName);
			if( name.Contains(".config")) {
				throw new ApplicationException("Please remove .config from config section name.");
			}
  			ConfigSection = name;
  			if( SyncTicks.Enabled) {
	  			HeartbeatDelay = int.MaxValue;
  			} else {
	  			HeartbeatDelay = 40;
  			}
  			FIXFilter = new MBTFIXFilter();
		}
		
		public override void OnDisconnect() {
            if( ConnectionStatus == Status.Recovered)
            {
                log.Error("Logging out -- Sending EndBroker event.");
                SendEndBroker();
            }
        }

		public override void OnRetry() {
		}
		
		private void SendStartBroker() {
			lock( symbolsRequestedLocker) {
				foreach( var kvp in symbolsRequested) {
					SymbolInfo symbol = kvp.Value;
					long end = Factory.Parallel.TickCount + 5000;
					while( !receiver.OnEvent(symbol,(int)EventType.StartBroker,symbol)) {
						if( isDisposed) return;
						if( Factory.Parallel.TickCount > end) {
							throw new ApplicationException("Timeout while sending start broker.");
						}
						Factory.Parallel.Yield();
					}
				}
			}
		}

        public int ProcessOrders()
        {
            return 0;
        }
		
		private void SendEndBroker() {
			lock( symbolsRequestedLocker) {
				foreach(var kvp in symbolsRequested) {
					SymbolInfo symbol = kvp.Value;
					long end = Factory.Parallel.TickCount + 2000;
					while( !receiver.OnEvent(symbol,(int)EventType.EndBroker,symbol)) {
						if( isDisposed) return;
						if( Factory.Parallel.TickCount > end) {
							throw new ApplicationException("Timeout while sending end broker.");
						}
						Factory.Parallel.Yield();
					}
				}
			}
		}

        private void SendLogin()
        {
            lastLoginTry = Factory.Parallel.TickCount;

            FixFactory = new FIXFactory4_4(1, UserName, fixDestination);
            var mbtMsg = FixFactory.Create();
            mbtMsg.SetEncryption(0);
            mbtMsg.SetHeartBeatInterval(30);
            mbtMsg.ResetSequence();
            mbtMsg.SetEncoding("554_H1");
            mbtMsg.SetPassword(Password);
            mbtMsg.AddHeader("A");
            if (debug)
            {
                log.Debug("Login message: \n" + mbtMsg);
            }
            SendMessage(mbtMsg);
        }
		

		public override bool OnLogin() {
			if( debug) log.Debug("Login()");

            SendLogin();

		    string errorMessage;
            if( !LookForLoginAck(out errorMessage))
            {
                return false;
            }
			
			StartRecovery();
			
            return true;
        }

        public override void OnLogout()
        {
            var mbtMsg = FixFactory.Create();
            mbtMsg.AddHeader("5");
            SendMessage(mbtMsg);
            log.Info("Logout message sent: " + mbtMsg);
            log.Info("Logging out -- Sending EndBroker event.");
            SendEndBroker();
        }
		
		protected override void OnStartRecovery()
		{
			isPositionUpdateComplete = false;
			isOrderUpdateComplete = false;
		    isPositionSynced = false;
			if( !LogRecovery) {
				MessageFIXT1_1.IsQuietRecovery = true;
			}

			RequestOrders();
		}
		
		public override void OnStartSymbol(SymbolInfo symbol)
		{
        	if( IsRecovered) {
				long end = Factory.Parallel.TickCount + 2000;
        		while( !receiver.OnEvent(symbol,(int)EventType.StartBroker,symbol)) {
        			if( IsInterrupted) return;
					if( Factory.Parallel.TickCount > end) {
						throw new ApplicationException("Timeout while sending start broker.");
					}
        			Factory.Parallel.Yield();
        		}
        	}
		}

		public override void OnStopSymbol(SymbolInfo symbol)
		{
			long end = Factory.Parallel.TickCount + 2000;
			while( !receiver.OnEvent(symbol,(int)EventType.EndBroker,symbol)) {
				if( IsInterrupted) return;
				if( Factory.Parallel.TickCount > end) {
					throw new ApplicationException("Timeout while sending stop broker.");
				}
				Factory.Parallel.Yield();
			}
		}
		

		private void RequestPositions() {
			var fixMsg = (FIXMessage4_4) FixFactory.Create();
			fixMsg.SetSubscriptionRequestType(0);
			fixMsg.SetAccount(AccountNumber);
			fixMsg.SetPositionRequestId(1);
			fixMsg.SetPositionRequestType(0);
			fixMsg.AddHeader("AN");
			SendMessage(fixMsg);
		}

		private void RequestOrders() {
            orderStore.Clear();
            var fixMsg = (FIXMessage4_4)FixFactory.Create();
			fixMsg.SetAccount(AccountNumber);
			fixMsg.SetMassStatusRequestID(TimeStamp.UtcNow);
			fixMsg.SetMassStatusRequestType(90);
			fixMsg.AddHeader("AF");
			SendMessage(fixMsg);
		}
		
		private void SendHeartbeat() {
			var fixMsg = (FIXMessage4_4) FixFactory.Create();
			fixMsg.AddHeader("0");
			SendMessage( fixMsg);
		}

        private unsafe bool LookForLoginAck(out string errorMessage)
        {
            var timeout = 30;
            var end = Factory.Parallel.TickCount + timeout * 1000;
            Message message;
            while (!Socket.TryGetMessage(out message))
            {
                if (IsInterrupted)
                {
                    errorMessage = "Wait on socket interrupting. Shutting down.";
                    return false;
                }
                Factory.Parallel.Yield();
                if (Factory.Parallel.TickCount > end)
                {
                    errorMessage = "Timeout of " + timeout + " seconds while waiting for login acknowledgment.";
                    return false;
                }
            }

            if (debug) log.Debug("Received FIX message: " + message);
            try
            {
                if (VerifyLoginAck(message, out errorMessage))
                {
                    return true;
                } else
                {
                    return false;
                }
            } finally
            {
                Socket.MessageFactory.Release(message);
            }
            return false;
        }

        private unsafe bool VerifyLoginAck(Message message, out string errorMessage)
		{
		    var result = false;
		    MessageFIX4_4 packetFIX = (MessageFIX4_4) message;
		    if ("A" == packetFIX.MessageType &&
		        "FIX.4.4" == packetFIX.Version &&
		        "MBT" == packetFIX.Sender &&
		        UserName == packetFIX.Target &&
		        "0" == packetFIX.Encryption &&
		        30 == packetFIX.HeartBeatInterval)
		    {
		        errorMessage = null;
                return 1 == packetFIX.Sequence;
            }
            //if ("1" == packetFIX.MessageType)
            //{
            //    return true;
            //}
            //if ("0" == packetFIX.MessageType)
            //{
            //    return true;
            //}
            var textMessage = new StringBuilder();
            textMessage.AppendLine("Invalid login response:");
            textMessage.AppendLine("  message type = " + packetFIX.MessageType);
            textMessage.AppendLine("  version = " + packetFIX.Version);
            textMessage.AppendLine("  sender = " + packetFIX.Sender);
            textMessage.AppendLine("  target = " + packetFIX.Target);
            textMessage.AppendLine("  encryption = " + packetFIX.Encryption);
            textMessage.AppendLine("  sequence = " + packetFIX.Sequence);
            textMessage.AppendLine("  heartbeat interval = " + packetFIX.HeartBeatInterval);
            textMessage.AppendLine(packetFIX.ToString());
            errorMessage = textMessage.ToString();
            return false;
		}
		
		protected override void ReceiveMessage(Message message) {
			var packetFIX = (MessageFIX4_4) message;
			switch( packetFIX.MessageType) {
				case "AP":
				case "AO":
					PositionUpdate( packetFIX);
					break;
				case "8":
					ExecutionReport( packetFIX);
					break;
				case "9":
					CancelRejected( packetFIX);
					break;
				case "1":
					SendHeartbeat();
					break;
				case "0":
					// Received heartbeat
					break;
				case "j":
					BusinessReject( packetFIX);
					break;
				case "h":
					log.Info("Ignoring Message: '" + packetFIX.MessageType + "'\n" + packetFIX);
					break;
				default:
					log.Warn("Ignoring Message: '" + packetFIX.MessageType + "'\n" + packetFIX);
					break;
			}
		}
		
		private void BusinessReject(MessageFIX4_4 packetFIX) {
			var lower = packetFIX.Text.ToLower();
			var text = packetFIX.Text;
			var errorOkay = false;
			errorOkay = lower.Contains("order") && lower.Contains("server") ? true : errorOkay;
			errorOkay = text.Contains("DEMOORDS") ? true : errorOkay;
			errorOkay = text.Contains("FXORD1") ? true : errorOkay;
			errorOkay = text.Contains("FXORD2") ? true : errorOkay;
			errorOkay = text.Contains("FXORD01") ? true : errorOkay;
			errorOkay = text.Contains("FXORD02") ? true : errorOkay;
			if( errorOkay) {
				log.Error( packetFIX.Text + " -- Sending EndBroker event.");
				SendEndBroker();
				log.Info( packetFIX.Text + " Sent EndBroker event due to Message:\n" + packetFIX);
			} else {
				string message = "FIX Server reported an error: " + packetFIX.Text + "\n" + packetFIX;
				throw new ApplicationException( message);
			}
		}
		
		private void TryEndRecovery() {
			if( isPositionUpdateComplete && isOrderUpdateComplete) {
				isPositionUpdateComplete = false;
				isOrderUpdateComplete = false;
				if( !TryCancelRejectedOrders() ) {
					ReportRecovery();
					EndRecovery();
				}
			}
		}
		
		private bool isCancelingPendingOrders = false;
		
		private bool TryCancelRejectedOrders() {
			var pending = orderStore.GetOrders((o) => o.OrderState == OrderState.Pending && "ReplaceRejected".Equals(o.Tag));
			if( pending.Count == 0) {
				isCancelingPendingOrders = false;
				return false;
			} else if( !isCancelingPendingOrders) {
				isCancelingPendingOrders = true;
				log.Info("Recovery completed with pending orders. Canceling them now..");
				foreach( var order in pending) {
					log.Info("Canceling Pending Order: " + order);
					OnCancelBrokerOrder(order.Symbol, order.BrokerOrder);
				}
			}
			return isCancelingPendingOrders;
		}

		private void ReportRecovery() {
			StringBuilder sb = new StringBuilder();
		    var list = orderStore.GetOrders((x) => true);
			foreach( var order in list) {
				sb.Append( "    ");
				sb.Append( (string) order.BrokerOrder);
				sb.Append( " ");
				sb.Append( order);
				sb.AppendLine();
			}
			log.Info("Recovered Open Orders:\n" + sb);
			SendStartBroker();
			MessageFIXT1_1.IsQuietRecovery = false;
		}
		
		private void PositionUpdate( MessageFIX4_4 packetFIX) {
			if( packetFIX.MessageType == "AO") {
				isPositionUpdateComplete = true;
				if(debug) log.Debug("PositionUpdate Complete.");
				TryEndRecovery();
			} else if (!IsRecovered)
            {
                var position = packetFIX.LongQuantity + packetFIX.ShortQuantity;
                SymbolInfo symbolInfo;
                try
                {
                    symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                }
                catch (ApplicationException ex)
                {
                    log.Error("Error looking up " + packetFIX.Symbol + ": " + ex.Message);
                    return;
                }
                if (debug) log.Debug("PositionUpdate: " + symbolInfo + "=" + position);
                var orderHandler = GetAlgorithm(symbolInfo.BinaryIdentifier);
                orderHandler.SetActualPosition(position);
            }
		}
		
		private void ExecutionReport( MessageFIX4_4 packetFIX) {
			if( packetFIX.Text == "END") {
				isOrderUpdateComplete = true;
				if(debug) log.Debug("ExecutionReport Complete.");
                RequestPositions();
			} else {
				if( debug && (LogRecovery || !IsRecovery) ) {
					log.Debug("ExecutionReport: " + packetFIX);
				}
				PhysicalOrder order;
				string orderStatus = packetFIX.OrderStatus;
				switch( orderStatus) {
					case "0": // New
						SymbolInfo symbol = null;
						try {
							symbol = Factory.Symbol.LookupSymbol( packetFIX.Symbol);
						} catch( ApplicationException) {
							// symbol unknown.
						}
						if( symbol != null) {
							order = UpdateOrder( packetFIX, OrderState.Active, null);
							if( IsRecovered) {
								var algorithm = GetAlgorithm( symbol.BinaryIdentifier);
								algorithm.OnCreateBrokerOrder( order);
							}
						}
						break;
					case "1": // Partial
						UpdateOrder( packetFIX, OrderState.Active, null);
						if( IsRecovered) {
							SendFill( packetFIX);
						}
						break;
					case "2":  // Filled 
                        if( packetFIX.CumulativeQuantity < packetFIX.LastQuantity)
                        {
                            log.Warn("Ignoring message due to CumQty " + packetFIX.CumulativeQuantity + " less than " + packetFIX.LastQuantity);
                            break;
                        }
						if( IsRecovered) {
							SendFill( packetFIX);
						}
						order = orderStore.RemoveOrder( packetFIX.ClientOrderId);
						if( order != null && IsRecovered) {
							var algorithm = GetAlgorithm( order.Symbol.BinaryIdentifier);
							algorithm.ProcessOrders();
						}
						if( order != null && order.Replace != null) {
							if( debug) log.Debug( "Found this order in the replace property. Removing it also: " + order.Replace);
                            orderStore.RemoveOrder(order.Replace.BrokerOrder.ToString());
						}
						break;
					case "5": // Replaced
						order = ReplaceOrder( packetFIX);
						if( IsRecovered) {
							if( order != null) {
								var algorithm = GetAlgorithm( order.Symbol.BinaryIdentifier);
								algorithm.OnChangeBrokerOrder( order, packetFIX.OriginalClientOrderId);
							} else {
								log.Warn("Changing order status after cancel/replace failed. Probably due to already being canceled or filled. Ignoring.");
							}
						}
						break;
					case "4": // Canceled
                        order = orderStore.RemoveOrder(packetFIX.ClientOrderId);
                        order = orderStore.RemoveOrder(packetFIX.OriginalClientOrderId);
						if( IsRecovered) {
							if( order != null) {
								var algorithm = GetAlgorithm( order.Symbol.BinaryIdentifier);
								algorithm.OnCancelBrokerOrder( order.Symbol, packetFIX.ClientOrderId);
							} else if( IsRecovered) {
								log.Notice("Order " + packetFIX.ClientOrderId + " was already removed after cancel. Ignoring.");
							}
						}
						break;
					case "6": // Pending Cancel
                        if (!string.IsNullOrEmpty(packetFIX.Text) && packetFIX.Text.Contains("multifunction order"))
                        {
                            if( debug && (LogRecovery || IsRecovered))
                            {
                                log.Debug("Pending cancel of multifunction order, so removing " + packetFIX.ClientOrderId + " and " + packetFIX.OriginalClientOrderId);
                            }
                            order = orderStore.RemoveOrder(packetFIX.ClientOrderId);
                            orderStore.RemoveOrder(packetFIX.OriginalClientOrderId);
                            break;
                        }
                        else
                        {
                            UpdateOrder(packetFIX, OrderState.Pending, "PendingCancel");
                            TryHandlePiggyBackFill(packetFIX);
                        }
						break;
					case "8": // Rejected
						RejectOrder( packetFIX);
						break;
					case "9": // Suspended
						UpdateOrder( packetFIX, OrderState.Suspended, packetFIX);
						// Ignore 
						break;
					case "A": // PendingNew
						UpdateOrder( packetFIX, OrderState.Active, null);
						break;
					case "E": // Pending Replace
                        var clientOrderId = packetFIX.ClientOrderId;
                        var orderState = OrderState.Pending;
                        if (debug && (LogRecovery || !IsRecovery))
                        {
                            log.Debug("PendingReplace( " + clientOrderId + ", state = " + orderState + ")");
                        }
                        UpdateOrReplaceOrder(packetFIX, packetFIX.OriginalClientOrderId, clientOrderId, orderState, null);
						TryHandlePiggyBackFill(packetFIX);
						break;
					case "R": // Resumed.
						UpdateOrder( packetFIX, OrderState.Active, null);
						// Ignore
						break;
					default:
						throw new ApplicationException("Unknown order status: '" + orderStatus + "'");
				}
			}
		}

		private void TryHandlePiggyBackFill(MessageFIX4_4 packetFIX) {
			if( packetFIX.LastQuantity > 0 && IsRecovered) {
                if (debug) log.Debug("TryHandlePiggyBackFill triggering fill because LastQuantity = " + packetFIX.LastQuantity);
                SendFill(packetFIX);
			}
			if( packetFIX.LeavesQuantity == 0) {
                if (debug) log.Debug("TryHandlePiggyBackFill found completely filled so removing " + packetFIX.ClientOrderId);
                var order = orderStore.RemoveOrder(packetFIX.ClientOrderId);
                if (order != null && order.Replace != null)
                {
                    if (debug) log.Debug("Found this order in the replace property. Removing it also: " + order.Replace);
                    orderStore.RemoveOrder(order.Replace.BrokerOrder.ToString());
                }
                if (IsRecovered)
                {
                    var algorithm = GetAlgorithm(order.Symbol.BinaryIdentifier);
                    algorithm.ProcessOrders();
                }
			}
		}
		
		private void CancelRejected( MessageFIX4_4 packetFIX) {
			if( debug && (LogRecovery || !IsRecovery) ) {
				log.Debug("ExecutionReport: " + packetFIX);
			}
			string orderStatus = packetFIX.OrderStatus;
			switch( orderStatus) {
				case "8": // Rejected
					var rejectReason = false;
                    switch( packetFIX.Text)
                    {
                        case "No such order":
                            rejectReason = true;
                            orderStore.RemoveOrder( packetFIX.ClientOrderId);
    						orderStore.RemoveOrder( packetFIX.OriginalClientOrderId);
                            break;
                        case "Order pending remote":
                        case "Cancel request already pending":
                        case "ORDER in pending state":
                        case "General Order Replace Error":
                            rejectReason = true;
                            ResetFromPending(packetFIX.OriginalClientOrderId);
                            orderStore.RemoveOrder(packetFIX.ClientOrderId);
                            break;
                        default:
                            ResetFromPending(packetFIX.OriginalClientOrderId);
                            orderStore.RemoveOrder(packetFIX.ClientOrderId);
                            break;
                    }
					if( !rejectReason && IsRecovered) {
						var message = "Order Rejected: " + packetFIX.Text + "\n" + packetFIX;
						var ignore = "The cancel reject error message '" + packetFIX.Text + "' was unrecognized. So it is being ignored. ";
						var handle = "If this reject causes any other problems please report it to have it added and properly handled.";
						log.Warn( message);
						log.Error( ignore + handle);
					} else {
						if( LogRecovery || !IsRecovery) {
							log.Info( "CancelReject(" + packetFIX.Text + ") Removed cancel order: " + packetFIX.ClientOrderId);
						}
					}
					break;
				default:
					throw new ApplicationException("Unknown cancel rejected order status: '" + orderStatus + "'");
			}
		}
		
		private bool GetLogicalOrderId( string clientOrderId, out int logicalOrderId) {
			logicalOrderId = 0;
			string[] parts = clientOrderId.Split(DOT_SEPARATOR);
			try {
				logicalOrderId = int.Parse(parts[0]);
			} catch( FormatException) {
				log.Warn("Fill received from order " + clientOrderId + " created externally. So it lacks any logical order id. That means a fill cannot be sent to the strategy. This will get resolved at next synchronization.");
				return false;
			}
			return true;
		}
		
		private int SideToSign( string side) {
			switch( side) {
				case "1": // Buy
					return 1;
				case "2": // Sell
				case "5": // SellShort
					return -1;
				default:
					throw new ApplicationException("Unknown order side: " + side);
			}
		}
		
		public void SendFill( MessageFIX4_4 packetFIX) {
			if( debug ) log.Debug("SendFill( " + packetFIX.ClientOrderId + ")");
			var symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
			var timeZone = new SymbolTimeZone(symbolInfo);
            var algorithm = GetAlgorithm(symbolInfo.BinaryIdentifier);
            var fillPosition = packetFIX.LastQuantity * SideToSign(packetFIX.Side);
            if (GetSymbolStatus(symbolInfo))
            {
                PhysicalOrder order;
                if( orderStore.TryGetOrderById( packetFIX.ClientOrderId, out order)) {
                    order.OrderState = OrderState.Filled;
				    TimeStamp executionTime;
				    if( UseLocalFillTime) {
					    executionTime = TimeStamp.UtcNow;
				    } else {
					    executionTime = new TimeStamp(packetFIX.TransactionTime);
				    }
				    var configTime = executionTime;
				    configTime.AddSeconds( timeZone.UtcOffset(executionTime));
				    var fill = Factory.Utility.PhysicalFill(fillPosition,packetFIX.LastPrice,configTime,executionTime,order,false);
				    if( debug) log.Debug( "Sending physical fill: " + fill);
	                algorithm.ProcessFill( fill,packetFIX.OrderQuantity,packetFIX.CumulativeQuantity,packetFIX.LeavesQuantity);
                }
                else
                {
                    algorithm.IncreaseActualPosition( fillPosition);
                    log.Notice("Fill id " + packetFIX.ClientOrderId + " not found. Must have been a manual trade.");
                }
			}
		}

        [Serializable]
        public class PhysicalOrderNotFoundException : Exception
	    {
	        public PhysicalOrderNotFoundException(string value) : base( value)
	        {
	            
	        }
    	}
		
		public void ProcessFill( SymbolInfo symbol, LogicalFillBinary fill) {
			if( debug) log.Debug("Sending fill event for " + symbol + " to receiver: " + fill);
			while( !receiver.OnEvent(symbol,(int)EventType.LogicalFill,fill)) {
				Factory.Parallel.Yield();
			}
		}

		public Iterable<PhysicalOrder> GetActiveOrders(SymbolInfo symbol) {
			var result = new ActiveList<PhysicalOrder>();
		    var list = orderStore.GetOrders((o) => o.Symbol == symbol);
            foreach( var order in list)
            {
                result.AddLast(order);
            }
			return result;
		}
		
		public void RejectOrder( MessageFIX4_4 packetFIX) {
			var rejectReason = false;
			rejectReason = packetFIX.Text.Contains("Outside trading hours") ? true : rejectReason;
			rejectReason = packetFIX.Text.Contains("not accepted this session") ? true : rejectReason;
			rejectReason = packetFIX.Text.Contains("Pending live orders") ? true : rejectReason;
			rejectReason = packetFIX.Text.Contains("Trading temporarily unavailable") ? true : rejectReason;
			rejectReason = packetFIX.Text.Contains("improper setting") ? true : rejectReason;			
			rejectReason = packetFIX.Text.Contains("No position to close") ? true : rejectReason;			
			orderStore.RemoveOrder( packetFIX.ClientOrderId);
			orderStore.RemoveOrder( packetFIX.OriginalClientOrderId);
		    if( IsRecovered && !rejectReason ) {
			    var message = "Order Rejected: " + packetFIX.Text + "\n" + packetFIX;
			    var ignore = "The reject error message '" + packetFIX.Text + "' was unrecognized. So it is being ignored. ";
			    var handle = "If this reject causes any other problems please report it to have it added and properly handled.";
			    log.Warn( message);
			    log.Error( ignore + handle);
		    } else if( LogRecovery || IsRecovered) {
			    log.Info( "RejectOrder(" + packetFIX.Text + ") Removed cancel order: " + packetFIX.ClientOrderId + " and original order: " + packetFIX.OriginalClientOrderId);
		    }
		}
		
		private static readonly char[] DOT_SEPARATOR = new char[] { '.' };
		
		private OrderType GetOrderType(MessageFIX4_4 packetFIX) {
			var orderType = OrderType.None;
			switch( packetFIX.Side) {
				case "1":
					switch( packetFIX.OrderType) {
						case "1":
							orderType = OrderType.BuyMarket;
							break;
						case "2":
							orderType = OrderType.BuyLimit;
							break;
						case "3":
							orderType = OrderType.BuyStop;
							break;
						default:
							break;
					}
					break;
				case "2":
				case "5":
					switch( packetFIX.OrderType) {
						case "1":
							orderType = OrderType.SellMarket;
							break;
						case "2":
							orderType = OrderType.SellLimit;
							break;
						case "3":
							orderType = OrderType.SellStop;
							break;
						default:
							break;
					}
					break;
				default:
					throw new ApplicationException("Unknown order side: '" + packetFIX.Side + "'\n" + packetFIX);
			}
			return orderType;
		}

		private OrderSide GetOrderSide( MessageFIX4_4 packetFIX) {
			OrderSide side;
			switch( packetFIX.Side) {
				case "1":
					side = OrderSide.Buy;
					break;
				case "2":
					side = OrderSide.Sell;
					break;
				case "5":
					side = OrderSide.SellShort;
					break;
				default:
					throw new ApplicationException( "Unknown order side: " + packetFIX.Side + "\n" + packetFIX);
			}
			return side;
		}

		private int GetLogicalOrderId( MessageFIX4_4 packetFIX) {
			string[] parts = packetFIX.ClientOrderId.Split(DOT_SEPARATOR);
			int logicalOrderId = 0;
			try {
				logicalOrderId = int.Parse(parts[0]);
			} catch( FormatException) {
			}
			return logicalOrderId;
		}

        public void ResetFromPending(string clientOrderId)
        {
            PhysicalOrder oldOrder = null;
            try
            {
                oldOrder = orderStore.GetOrderById(clientOrderId);
                if( oldOrder.OrderState == OrderState.Pending)
                {
                    oldOrder.OrderState = OrderState.Active;
                }
            }
            catch (ApplicationException)
            {
                if (!IsRecovery)
                {
                    log.Warn("Order ID# " + clientOrderId + " was not found for update or replace.");
                }
            }
        }

        public PhysicalOrder UpdateOrder(MessageFIX4_4 packetFIX, OrderState orderState, object note)
        {
			var clientOrderId = packetFIX.ClientOrderId;
			if( debug && (LogRecovery || !IsRecovery) ) {
				log.Debug("UpdateOrder( " + clientOrderId + ", state = " + orderState + ")");
			}
			return UpdateOrReplaceOrder( packetFIX, packetFIX.OriginalClientOrderId, clientOrderId, orderState, note);
		}
		
		public PhysicalOrder ReplaceOrder( MessageFIX4_4 packetFIX) {
			if( debug && (LogRecovery || !IsRecovery) ) {
				log.Debug("ReplaceOrder( " + packetFIX.OriginalClientOrderId + " => " + packetFIX.ClientOrderId + ")");
			}
			PhysicalOrder order;
            if( !orderStore.TryGetOrderById( packetFIX.ClientOrderId, out order)) {
				if( IsRecovery )
				{
                    order = UpdateOrReplaceOrder(packetFIX, packetFIX.ClientOrderId, packetFIX.ClientOrderId, OrderState.Active, "ReplaceOrder");
                    orderStore.RemoveOrder(packetFIX.OriginalClientOrderId);
                    return order;
				}
                else
                {
                    log.Warn("Order ID# " + packetFIX.ClientOrderId + " was not found for replace.");
					return null;
				}		
			}
		    order.OrderState = OrderState.Active;
			int quantity = packetFIX.LeavesQuantity;
			if( quantity > 0) {
				if( info && (LogRecovery || !IsRecovery) ) {
					if( debug) log.Debug("Updated order: " + order + ".  Executed: " + packetFIX.CumulativeQuantity + " Remaining: " + packetFIX.LeavesQuantity);
				}
			} else {
				if( info && (LogRecovery || !IsRecovery) ) {
					if( debug) log.Debug("Order Completely Filled. Id: " + packetFIX.ClientOrderId + ".  Executed: " + packetFIX.CumulativeQuantity);
				}
			}
            if( !packetFIX.ClientOrderId.Equals((string)order.BrokerOrder))
            {
                throw new InvalidOperationException("client order id mismatch with broker order property.");
            }
            orderStore.AssignById((string)order.BrokerOrder,order);
			if( trace) {
				log.Trace("Updated order list:");
			    var logOrders = orderStore.LogOrders();
				log.Trace( "Broker Orders:\n" + logOrders);
			}
			orderStore.RemoveOrder( packetFIX.OriginalClientOrderId);
			return order;
		}
		
		public PhysicalOrder UpdateOrReplaceOrder( MessageFIX4_4 packetFIX, string oldClientOrderId, string newClientOrderId, OrderState orderState, object note) {
			SymbolInfo symbolInfo;
			try {
				symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
			} catch( ApplicationException ex) {
				log.Error("Error looking up " + packetFIX.Symbol + ": " + ex.Message);
				return null;
			}
            PhysicalOrder order;
            long logicalSerialNumber = 0;
		    if( orderStore.TryGetOrderById( newClientOrderId, out order)) {
				logicalSerialNumber = order.LogicalSerialNumber;
			} else {
				if( !IsRecovery ) {
					log.Warn("Client Order ID# " + newClientOrderId + " was not found for update or replace.");
					return null;
				}			
			}
            PhysicalOrder oldOrder;
            if ( !orderStore.TryGetOrderById(oldClientOrderId, out oldOrder))
            {
                if (debug && (LogRecovery || !IsRecovery))
                {
                    if (debug && (LogRecovery || !IsRecovery))
                    {
                        log.Debug("Original Order ID# " + oldClientOrderId + " not found for update or replace. Normal.");
                    }
                }
            }
            int quantity = packetFIX.LeavesQuantity;
			var type = GetOrderType( packetFIX);
			var side = GetOrderSide( packetFIX);
			var logicalId = GetLogicalOrderId( packetFIX);
		    var replace = order != null ? order.Replace : null;
			order = Factory.Utility.PhysicalOrder(orderState, symbolInfo, side, type, packetFIX.Price, packetFIX.LeavesQuantity, logicalId, logicalSerialNumber, newClientOrderId, null);
		    order.Replace = replace;
            if( oldOrder != null)
            {
                if( debug && (LogRecovery || !IsRecovery)) log.Debug("Setting replace property of " + oldOrder.BrokerOrder + " to be replaced by " + order.BrokerOrder);
                oldOrder.Replace = order;
            }
		    if( quantity > 0) {
				if( info && (LogRecovery || !IsRecovery) ) {
					if( debug) log.Debug("Updated order: " + order + ".  Executed: " + packetFIX.CumulativeQuantity + " Remaining: " + packetFIX.LeavesQuantity);
				}
			} else {
				if( info && (LogRecovery || !IsRecovery) ) {
					if( debug) log.Debug("Order completely filled or canceled. Id: " + packetFIX.ClientOrderId + ".  Executed: " + packetFIX.CumulativeQuantity);
				}
			}
		    orderStore.AssignById(newClientOrderId, order);
			if( trace) {
				log.Trace("Updated order list:");
			    var logOrders = orderStore.LogOrders();
				log.Trace( "Broker Orders:\n" + logOrders);
			}
			return order;
		}

		private void TestMethod(MessageFIX4_4 packetFIX) {
			string account = packetFIX.Account;
			string destination = packetFIX.Destination;
			int orderQuantity = packetFIX.OrderQuantity;
			double averagePrice = packetFIX.AveragePrice;
			string orderID = packetFIX.OrderId;
			string massStatusRequestId = packetFIX.MassStatusRequestId;
			string positionEffect = packetFIX.PositionEffect;
			string orderType = packetFIX.OrderType;
			string clientOrderId = packetFIX.ClientOrderId;
			double price = packetFIX.Price;
			int cumulativeQuantity = packetFIX.CumulativeQuantity;
			string executionId = packetFIX.ExecutionId;
			int productType = packetFIX.ProductType;
			string symbol = packetFIX.Symbol;
			string side = packetFIX.Side;
			string timeInForce = packetFIX.TimeInForce;
			string executionType = packetFIX.ExecutionType;
			string internalOrderId = packetFIX.InternalOrderId;
			string transactionTime = packetFIX.TransactionTime;
			int leavesQuantity = packetFIX.LeavesQuantity;
		}
		
		private OrderAlgorithm GetAlgorithm(string clientOrderId) {
			PhysicalOrder origOrder;
			try {
				origOrder = orderStore.GetOrderById(clientOrderId);
			} catch( ApplicationException) {
				throw new ApplicationException("Unable to find physical order by client id: " + clientOrderId);
			}
			return GetAlgorithm( origOrder.Symbol.BinaryIdentifier);
		}
		
		private OrderAlgorithm GetAlgorithm(long symbol) {
			OrderAlgorithm algorithm;
			lock( orderAlgorithmLocker) {
				if( !orderAlgorithms.TryGetValue(symbol, out algorithm)) {
					var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
				    var orderCache = Factory.Engine.LogicalOrderCache(symbolInfo, false);
					algorithm = Factory.Utility.OrderAlgorithm( "mbtfix", symbolInfo, this, orderCache);
					orderAlgorithms.Add(symbol,algorithm);
					algorithm.OnProcessFill = ProcessFill;
				}
			}
			return algorithm;
		}
		
		private bool RemoveOrderHandler(long symbol) {
			lock( orderAlgorithmLocker) {
				if( orderAlgorithms.ContainsKey(symbol)) {
					orderAlgorithms.Remove(symbol);
					return true;
				} else {
					return false;
				}
			}
		}
		
		public override void PositionChange(Receiver receiver, SymbolInfo symbol, int desiredPosition, Iterable<LogicalOrder> inputOrders)
		{
			if( !IsRecovered) {
				if( HasFirstRecovery) {
					log.Warn("PositionChange event received while FIX was offline or recovering. Current connection status is: " + ConnectionStatus);
					return;
				} else {
					throw new ApplicationException("PositionChange event received prior to completing FIX recovery. Current connection status is: " + ConnectionStatus);
				}
			}
			var count = inputOrders == null ? 0 : inputOrders.Count;
			if( debug) log.Debug( "PositionChange " + symbol + ", desired " + desiredPosition + ", order count " + count);
			
			var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
            algorithm.SetDesiredPosition(desiredPosition);
            algorithm.SetLogicalOrders(inputOrders);
            lock (orderAlgorithmLocker)
            {
                if (!isPositionSynced && !algorithm.TrySyncPosition())
                {
                    isPositionSynced = true;
                }
                else
                {
                    algorithm.ProcessOrders();
                }
            }
			
			var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
			if( SyncTicks.Enabled ) {
				tickSync.RemovePositionChange();
			}				
		}
		
		
	    protected override void Dispose(bool disposing)
	    {
	    	base.Dispose(disposing);
           	nextConnectTime = Factory.Parallel.TickCount + 10000;
	    }    
	        
		private int GetLogicalOrderId(int physicalOrderId) {
        	int logicalOrderId;
        	if( physicalToLogicalOrderMap.TryGetValue(physicalOrderId,out logicalOrderId)) {
        		return logicalOrderId;
        	} else {
        		return 0;
        	}
		}
		Dictionary<int,int> physicalToLogicalOrderMap = new Dictionary<int, int>();
	        
		public void OnCreateBrokerOrder(PhysicalOrder physicalOrder)
		{
			physicalOrder.OrderState = OrderState.Pending;
			if( debug) log.Debug( "OnCreateBrokerOrder " + physicalOrder);
			OnCreateOrChangeBrokerOrder(physicalOrder,null, false);
		}
	        
		private void OnCreateOrChangeBrokerOrder(PhysicalOrder physicalOrder, object origBrokerOrder, bool isChange)
		{
			var fixMsg = (FIXMessage4_4) FixFactory.Create();
		    orderStore.AssignById((string) physicalOrder.BrokerOrder, physicalOrder);
			
			if( debug) log.Debug( "Adding Order to open order list: " + physicalOrder);
			if( isChange) {
				fixMsg.SetClientOrderId((string)physicalOrder.BrokerOrder);
				fixMsg.SetOriginalClientOrderId((string)origBrokerOrder);
				var origOrder = orderStore.GetOrderById((string) origBrokerOrder);
				if( origOrder != null) {
					origOrder.Replace = physicalOrder;
					if( debug) log.Debug("Setting replace property of " + origBrokerOrder + " to " + physicalOrder.BrokerOrder);
				}
			} else {
				fixMsg.SetClientOrderId((string)physicalOrder.BrokerOrder);
			}
			fixMsg.SetAccount(AccountNumber);
			if( isChange) {
				fixMsg.AddHeader("G");
			} else {
				fixMsg.AddHeader("D");
				if( physicalOrder.Symbol.Destination.ToLower() == "default") {
					fixMsg.SetDestination("MBTX");
				} else {
					fixMsg.SetDestination(physicalOrder.Symbol.Destination);
				}
			}
			fixMsg.SetHandlingInstructions(1);
			fixMsg.SetSymbol(physicalOrder.Symbol.Symbol);
			fixMsg.SetSide( GetOrderSide(physicalOrder.Side));
			switch( physicalOrder.Type) {
				case OrderType.BuyLimit:
					fixMsg.SetOrderType(2);
					fixMsg.SetPrice(physicalOrder.Price);
					fixMsg.SetTimeInForce(1);
					break;
				case OrderType.BuyMarket:
					fixMsg.SetOrderType(1);
					fixMsg.SetTimeInForce(0);
					break;
				case OrderType.BuyStop:
					fixMsg.SetOrderType(3);
					fixMsg.SetPrice(physicalOrder.Price);
					fixMsg.SetStopPrice(physicalOrder.Price);
					fixMsg.SetTimeInForce(1);
					break;
				case OrderType.SellLimit:
					fixMsg.SetOrderType(2);
					fixMsg.SetPrice(physicalOrder.Price);
					fixMsg.SetTimeInForce(1);
					break;
				case OrderType.SellMarket:
					fixMsg.SetOrderType(1);
					fixMsg.SetTimeInForce(0);
					break;
				case OrderType.SellStop:
					fixMsg.SetOrderType(3);
					fixMsg.SetPrice(physicalOrder.Price);
					fixMsg.SetStopPrice(physicalOrder.Price);
					fixMsg.SetTimeInForce(1);
					break;
			}
			fixMsg.SetLocateRequired("N");
			fixMsg.SetTransactTime(TimeStamp.UtcNow);
			fixMsg.SetOrderQuantity((int)physicalOrder.Size);
			fixMsg.SetOrderCapacity("A");
			fixMsg.SetUserName();
			if( isChange) {
				if( debug) log.Debug("Change order: \n" + fixMsg);
			} else {
				if( debug) log.Debug("Create new order: \n" + fixMsg);
			}
			SendMessage( fixMsg);
		}

		private int GetOrderSide( OrderSide side) {
			switch( side) {
				case OrderSide.Buy:
					return 1;
				case OrderSide.Sell:
					return 2;
				case OrderSide.SellShort:
					return 5;
				case OrderSide.SellShortExempt:
					return 6;
				default:
					throw new ApplicationException("Unknown OrderSide: " + side);
			}
		}
		

		private long GetUniqueOrderId() {
			return TimeStamp.UtcNow.Internal;
		}
		
		public void OnCancelBrokerOrder(SymbolInfo symbol, string origBrokerOrder)
		{
			PhysicalOrder physicalOrder;
			try {
				physicalOrder = orderStore.GetOrderById( origBrokerOrder);
			} catch( ApplicationException ex) {
				log.Warn("Order probably already canceled. " + ex.Message);
				if( SyncTicks.Enabled) {
					var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
					tickSync.RemovePhysicalOrder();
				}
				return;
			}
			physicalOrder.OrderState = OrderState.Pending;
			if( debug) log.Debug( "OnCancelBrokerOrder " + physicalOrder);
			
			var fixMsg = (FIXMessage4_4) FixFactory.Create();
			string newClientOrderId = physicalOrder.LogicalOrderId + "." + GetUniqueOrderId();
			fixMsg.SetOriginalClientOrderId((string)origBrokerOrder);
			fixMsg.SetClientOrderId(newClientOrderId);
			fixMsg.SetAccount(AccountNumber);
			fixMsg.SetSide( GetOrderSide(physicalOrder.Side));
			fixMsg.AddHeader("F");
			fixMsg.SetSymbol(physicalOrder.Symbol.Symbol);
			fixMsg.SetTransactTime(TimeStamp.UtcNow);
			SendMessage(fixMsg);
		}
		
		public void OnChangeBrokerOrder(PhysicalOrder physicalOrder, string origBrokerOrder)
		{
			physicalOrder.OrderState = OrderState.Pending;
			if( debug) log.Debug( "OnChangeBrokerOrder( " + physicalOrder + ")");
			OnCreateOrChangeBrokerOrder( physicalOrder, origBrokerOrder, true);
		}

	    public bool HasBrokerOrder(PhysicalOrder order)
	    {
	        PhysicalOrder queueOrder;
            if( orderStore.TryGetOrderBySerial(order.LogicalSerialNumber, out queueOrder))
            {
                switch (queueOrder.OrderState)
                {
                    case OrderState.Pending:
                    case OrderState.Active:
                        return true;
                    case OrderState.Filled:
                    case OrderState.Suspended:
                        return false;
                    default:
                        throw new InvalidOperationException("Unknow order state: " + order.OrderState);
                }
            } else
            {
                return false;
            }
	    }
	}
}