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
        long lastLoginTry = long.MinValue;
		long loginRetryTime = 10000; //milliseconds = 10 seconds.
        public enum RecoverProgress
        {
            InProgress,
            Completed,
            None,
        }
        private RecoverProgress isPositionUpdateComplete = RecoverProgress.None;
        private RecoverProgress isOrderUpdateComplete = RecoverProgress.None;
        private string fixDestination = "MBT";
		
		public MBTFIXProvider(string name)
		{
			log.Notice("Using config file name: " + name);
			ProviderName = "MBTFIXProvider";
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
            OrderStore.ForceSnapShot();
            if (IsRecovered)
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

        private void SendLogin(int localSequence)
        {
            lastLoginTry = Factory.Parallel.TickCount;

            FixFactory = new FIXFactory4_4(localSequence+1, UserName, fixDestination);
            var mbtMsg = FixFactory.Create();
            mbtMsg.SetEncryption(0);
            mbtMsg.SetHeartBeatInterval(30);
            if( localSequence == 0)
            {
                mbtMsg.ResetSequence();
            }
            mbtMsg.SetEncoding("554_H1");
            mbtMsg.SetPassword(Password);
            mbtMsg.AddHeader("A");
            if (debug)
            {
                log.Debug("Login message: \n" + mbtMsg);
            }
            SendMessage(mbtMsg);
        }

        public override bool OnLogin()
        {
            if (debug) log.Debug("Login()");
            isPositionUpdateComplete = RecoverProgress.None;
            isOrderUpdateComplete = RecoverProgress.None;
            foreach (var kvp in orderAlgorithms)
            {
                var algorithm = kvp.Value;
                algorithm.IsPositionSynced = false;
            }

            if (OrderStore.Recover())
            {
                if (debug) log.Debug("Recovered orders from snapshot: \n" + OrderStore.LogOrders());
                RemoteSequence = OrderStore.RemoteSequence;
                OrderStore.UpdateSequence(RemoteSequence,OrderStore.LocalSequence+500);
                SendLogin(OrderStore.LocalSequence);
                isOrderUpdateComplete = RecoverProgress.Completed;
            }
            else
            {
                if( debug) log.Debug("Unable to recover from snapshot. Beginning full recovery.");
                RemoteSequence = 1;
                SendLogin(0);
                isOrderUpdateComplete = RecoverProgress.Completed;
            }
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
			if( !LogRecovery) {
				MessageFIXT1_1.IsQuietRecovery = true;
			}
            TryEndRecovery();
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
            //fixMsg.SetSubscriptionRequestType(1);
			fixMsg.AddHeader("AN");
			SendMessage(fixMsg);
		}

		private void RequestOrders() {
            OrderStore.Clear();
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

        private unsafe bool VerifyLoginAck(MessageFIXT1_1 message)
		{
		    var result = false;
		    var packetFIX = message;
		    if ("A" == packetFIX.MessageType &&
		        "FIX.4.4" == packetFIX.Version &&
		        "MBT" == packetFIX.Sender &&
		        UserName == packetFIX.Target &&
		        "0" == packetFIX.Encryption)
		    {
		        RetryStart = RetryMaximum = packetFIX.HeartBeatInterval;
                return true;
            }
            else
		    {
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
                log.Error(textMessage.ToString());
                return false;
            }
		}

        protected override void HandleLogon(MessageFIXT1_1 message)
        {
            if (ConnectionStatus != Status.PendingLogin)
            {
                throw new InvalidOperationException("Attempt logon when in " + ConnectionStatus +
                                                    " instead of expected " + Status.PendingLogin);
            }
            if (VerifyLoginAck(message))
            {
                return;
            }
            else
            {
                RegenerateSocket();
                return;
            }
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
                    if( packetFIX.Symbol != null)
                    {
                        var symbol = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                        var algo = orderAlgorithms[symbol.BinaryIdentifier];
                        algo.ProcessOrders();
                    }
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
				case "h":  // Trading session status
                    if( ConnectionStatus == Status.PendingLogin)
                    {
                        StartRecovery();
                    }
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
		
		protected override void TryEndRecovery() {
            if (debug) log.Debug("TryEndRecovery Status " + ConnectionStatus + ", OrderUpdate " + isOrderUpdateComplete + ", PositionUpdate " + isPositionUpdateComplete +
                ", Resend " + IsResendComplete);
            if( ConnectionStatus == Status.Recovered) return;
            if (isOrderUpdateComplete == RecoverProgress.Completed &&
                isPositionUpdateComplete == RecoverProgress.Completed &&
                IsResendComplete)
			{
			    EndRecovery();
                StartPositionSync();
                return;
			}
            if( isOrderUpdateComplete !=  RecoverProgress.Completed )
            {
                if( isOrderUpdateComplete != RecoverProgress.InProgress )
                {
                    isOrderUpdateComplete = RecoverProgress.InProgress;
                    RequestOrders();
                }
            }
            else if (isPositionUpdateComplete != RecoverProgress.Completed)
            {
                if( isPositionUpdateComplete != RecoverProgress.InProgress)
                {
                    isPositionUpdateComplete = RecoverProgress.InProgress;
                    RequestPositions();
                }
            }
		}
		
		private bool isCancelingPendingOrders = false;
		
        //private bool TryCancelRejectedOrders() {
        //    var pending = orderStore.GetOrders((o) => o.OrderState == OrderState.Pending && "ReplaceRejected".Equals(o.Tag));
        //    if( pending.Count == 0) {
        //        isCancelingPendingOrders = false;
        //        return false;
        //    } else if( !isCancelingPendingOrders) {
        //        isCancelingPendingOrders = true;
        //        log.Info("Recovery completed with pending orders. Canceling them now..");
        //        foreach( var order in pending) {
        //            log.Info("Canceling Pending Order: " + order);
        //            OnCancelBrokerOrder(order.Symbol, order.BrokerOrder);
        //        }
        //    }
        //    return isCancelingPendingOrders;
        //}

		private void StartPositionSync() {
			StringBuilder sb = new StringBuilder();
		    var list = OrderStore.GetOrders((x) => true);
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
				isPositionUpdateComplete = RecoverProgress.Completed;
				if(debug) log.Debug("PositionUpdate Complete.");
                TryEndRecovery();
			}
            else 
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
                if (!IsRecovered)
                {
                    var orderHandler = GetAlgorithm(symbolInfo.BinaryIdentifier);
                    orderHandler.SetActualPosition(position);
                }
            }
		}
		
		private void ExecutionReport( MessageFIX4_4 packetFIX) {
			if( packetFIX.Text == "END") {
				isOrderUpdateComplete = RecoverProgress.Completed;
				if(debug) log.Debug("ExecutionReport Complete.");
                TryEndRecovery();
			} else {
				if( debug && (LogRecovery || !IsRecovery) ) {
					log.Debug("ExecutionReport: " + packetFIX);
				}
				CreateOrChangeOrder order;
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
                            if( order != null)
                            {
                                var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
                                algorithm.ConfirmCreate(order, IsRecovered);
                            }
						}
						break;
					case "1": // Partial
						UpdateOrder( packetFIX, OrderState.Active, null);
						SendFill( packetFIX);
						break;
					case "2":  // Filled 
                        if( packetFIX.CumulativeQuantity < packetFIX.LastQuantity)
                        {
                            log.Warn("Ignoring message due to CumQty " + packetFIX.CumulativeQuantity + " less than " + packetFIX.LastQuantity);
                            break;
                        }
						SendFill( packetFIX);
						break;
					case "5": // Replaced
						order = ReplaceOrder( packetFIX);
						if( order != null) {
							var algorithm = GetAlgorithm( order.Symbol.BinaryIdentifier);
							algorithm.ConfirmChange( order, IsRecovered);
						} else if( IsRecovered) {
							log.Warn("Changing order status after cancel/replace failed. Probably due to already being canceled or filled. Ignoring.");
						}
						break;
					case "4": // Canceled
				        {
                            var symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                            var algorithm = GetAlgorithm(symbolInfo.BinaryIdentifier);
				            CreateOrChangeOrder clientOrder;
                            if( !OrderStore.TryGetOrderById( packetFIX.ClientOrderId, out clientOrder))
                            {
                                log.Warn("Cancel order for " + packetFIX.ClientOrderId + " was not found. Probably already canceled.");
                            }
				            CreateOrChangeOrder origOrder;
                            if (!OrderStore.TryGetOrderById(packetFIX.OriginalClientOrderId, out origOrder))
                            {
                                log.Warn("Orig order for " + packetFIX.ClientOrderId + " was not found. Probably already canceled.");
                            }
                            if( clientOrder != null && clientOrder.ReplacedBy != null)
                            {
                                algorithm.ConfirmCancel(clientOrder,IsRecovered);
                            }
                            else if (origOrder != null && origOrder.ReplacedBy != null)
                            {
                                algorithm.ConfirmCancel(origOrder,IsRecovered);
                            }
                            else
                            {
                                throw new ApplicationException("Neither order found by client id nor original client id had replaced by property set: \nClient Order: " + clientOrder + "\nOriginal Client Order: " + origOrder);
                            }
                            break;
                        }
					case "6": // Pending Cancel
                        if (!string.IsNullOrEmpty(packetFIX.Text) && packetFIX.Text.Contains("multifunction order"))
                        {
                            if( debug && (LogRecovery || IsRecovered))
                            {
                                log.Debug("Pending cancel of multifunction order, so removing " + packetFIX.ClientOrderId + " and " + packetFIX.OriginalClientOrderId);
                            }
                            order = OrderStore.RemoveOrder(packetFIX.ClientOrderId);
                            OrderStore.RemoveOrder(packetFIX.OriginalClientOrderId);
                            break;
                        }
                        else
                        {
                            UpdateCancelOrder(packetFIX, OrderState.Pending);
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
			if( packetFIX.LastQuantity > 0) {
                if (debug) log.Debug("TryHandlePiggyBackFill triggering fill because LastQuantity = " + packetFIX.LastQuantity);
                SendFill(packetFIX);
			}
		}
		
		private void CancelRejected( MessageFIX4_4 packetFIX) {
			if( debug && (LogRecovery || !IsRecovery) ) {
				log.Debug("ExecutionReport: " + packetFIX);
			}
			string orderStatus = packetFIX.OrderStatus;
            CreateOrChangeOrder order;
		    bool removeOriginal = false;
		    OrderStore.TryGetOrderById(packetFIX.ClientOrderId, out order);
			switch( orderStatus) {
				case "8": // Rejected
					var rejectReason = false;
                    switch( packetFIX.Text)
                    {
                        case "No such order":
                            rejectReason = true;
                            removeOriginal = true;
                            break;
                        case "USD/JPY: Cannot cancel order. Probably already filled or canceled.":
                        case "Order pending remote":
                        case "Cancel request already pending":
                        case "ORDER in pending state":
                        case "General Order Replace Error":
                            rejectReason = true;
                            break;
                        default:
                            break;
                    }
                    OrderStore.RemoveOrder(packetFIX.ClientOrderId);
                    if (removeOriginal)
                    {
                        OrderStore.RemoveOrder(packetFIX.OriginalClientOrderId);
                    }
                    else
                    {
                        ResetFromPending(packetFIX.OriginalClientOrderId);
                    }
                    if (order != null)
                    {
                        var algo = orderAlgorithms[order.Symbol.BinaryIdentifier];
                        algo.RejectOrder(order,removeOriginal,IsRecovered);
                    }
                    else if( SyncTicks.Enabled )
                    {
                        var symbol = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                        var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
                        tickSync.RemovePhysicalOrder();
                    }
                    if (!rejectReason && IsRecovered)
                    {
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
                CreateOrChangeOrder order;
                if( OrderStore.TryGetOrderById( packetFIX.ClientOrderId, out order)) {
                    order.OrderState = OrderState.Filled;
				    TimeStamp executionTime;
				    if( UseLocalFillTime) {
					    executionTime = TimeStamp.UtcNow;
				    } else {
					    executionTime = new TimeStamp(packetFIX.TransactionTime);
				    }
				    var configTime = executionTime;
				    configTime.AddSeconds( timeZone.UtcOffset(executionTime));
                    var fill = Factory.Utility.PhysicalFill(fillPosition, packetFIX.LastPrice, configTime, executionTime, order, false, packetFIX.OrderQuantity, packetFIX.CumulativeQuantity, packetFIX.LeavesQuantity, IsRecovered);
				    if( debug) log.Debug( "Sending physical fill: " + fill);
	                algorithm.ProcessFill( fill);
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

		public void RejectOrder( MessageFIX4_4 packetFIX) {
			var rejectReason = false;
			rejectReason = packetFIX.Text.Contains("Outside trading hours") ? true : rejectReason;
			rejectReason = packetFIX.Text.Contains("not accepted this session") ? true : rejectReason;
			rejectReason = packetFIX.Text.Contains("Pending live orders") ? true : rejectReason;
			rejectReason = packetFIX.Text.Contains("Trading temporarily unavailable") ? true : rejectReason;
			rejectReason = packetFIX.Text.Contains("improper setting") ? true : rejectReason;
		    rejectReason = packetFIX.Text.Contains("No position to close") ? true : rejectReason;
			OrderStore.RemoveOrder( packetFIX.ClientOrderId);
			OrderStore.RemoveOrder( packetFIX.OriginalClientOrderId);
		    if( IsRecovered && !rejectReason ) {
			    var message = "Order Rejected: " + packetFIX.Text + "\n" + packetFIX;
			    var ignore = "The reject error message '" + packetFIX.Text + "' was unrecognized. So it is being ignored. ";
			    var handle = "If this reject causes any other problems please report it to have it added and properly handled.";
			    log.Warn( message);
			    log.Error( ignore + handle);
		    } else if( LogRecovery || IsRecovered) {
			    log.Info( "RejectOrder(" + packetFIX.Text + ") Removed cancel order: " + packetFIX.ClientOrderId + " and original order: " + packetFIX.OriginalClientOrderId);
		    }
            if (SyncTicks.Enabled)
            {
                var symbol = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
                var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
                tickSync.RemovePhysicalOrder();
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
            CreateOrChangeOrder oldOrder = null;
            try
            {
                oldOrder = OrderStore.GetOrderById(clientOrderId);
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

        public CreateOrChangeOrder UpdateOrder(MessageFIX4_4 packetFIX, OrderState orderState, object note)
        {
			var clientOrderId = packetFIX.ClientOrderId;
			if( debug && (LogRecovery || !IsRecovery) ) {
				log.Debug("UpdateOrder( " + clientOrderId + ", state = " + orderState + ")");
			}
			return UpdateOrReplaceOrder( packetFIX, packetFIX.OriginalClientOrderId, clientOrderId, orderState, note);
		}
		
		public CreateOrChangeOrder ReplaceOrder( MessageFIX4_4 packetFIX) {
			if( debug && (LogRecovery || !IsRecovery) ) {
				log.Debug("ReplaceOrder( " + packetFIX.OriginalClientOrderId + " => " + packetFIX.ClientOrderId + ")");
			}
			CreateOrChangeOrder order;
            if( !OrderStore.TryGetOrderById( packetFIX.ClientOrderId, out order)) {
				if( IsRecovery )
				{
                    order = UpdateOrReplaceOrder(packetFIX, packetFIX.ClientOrderId, packetFIX.ClientOrderId, OrderState.Active, "ReplaceOrder");
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
            OrderStore.SetSequences(RemoteSequence,FixFactory.LastSequence);
			return order;
		}
		
		public CreateOrChangeOrder UpdateOrReplaceOrder( MessageFIX4_4 packetFIX, string oldClientOrderId, string newClientOrderId, OrderState orderState, object note) {
			SymbolInfo symbolInfo;
			try {
				symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
			} catch( ApplicationException ex) {
				log.Error("Error looking up " + packetFIX.Symbol + ": " + ex.Message);
				return null;
			}
            CreateOrChangeOrder order;
            long logicalSerialNumber = 0;
		    if( OrderStore.TryGetOrderById( newClientOrderId, out order)) {
				logicalSerialNumber = order.LogicalSerialNumber;
			} else {
				if( debug && (LogRecovery || IsRecovered)) {
					log.Debug("Client Order ID# " + newClientOrderId + " was not found.");
				}
                return null;
            }
		    CreateOrChangeOrder oldOrder = null;
            if ( !string.IsNullOrEmpty(oldClientOrderId) && !OrderStore.TryGetOrderById(oldClientOrderId, out oldOrder))
            {
                if (LogRecovery || !IsRecovery)
                {
                    if( debug) log.Debug("Original Order ID# " + oldClientOrderId + " not found for update or replace. Normal.");
                }
            }
            int quantity = packetFIX.LeavesQuantity;
			var type = GetOrderType( packetFIX);
            if( type == OrderType.None)
            {
                type = order.Type;
            }
			var side = GetOrderSide( packetFIX);
			var logicalId = GetLogicalOrderId( packetFIX);
		    var replace = order.ReplacedBy;
            order = Factory.Utility.PhysicalOrder(OrderAction.Create, orderState, symbolInfo, side, type, packetFIX.Price, packetFIX.LeavesQuantity, logicalId, logicalSerialNumber, newClientOrderId, null, TimeStamp.UtcNow);
		    order.OriginalOrder = oldOrder;
            if( oldOrder != null)
            {
                if( debug && (LogRecovery || !IsRecovery)) log.Debug("Setting replace property of " + oldOrder.BrokerOrder + " to be replaced by " + order.BrokerOrder);
                oldOrder.ReplacedBy = order;
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
            OrderStore.AddOrder(order);
            OrderStore.SetSequences(RemoteSequence,FixFactory.LastSequence);
			if( trace) {
				log.Trace("Updated order list:");
			    var logOrders = OrderStore.LogOrders();
				log.Trace( "Broker Orders:\n" + logOrders);
			}
			return order;
		}

        public CreateOrChangeOrder UpdateCancelOrder(MessageFIX4_4 packetFIX, OrderState orderState)
        {
            var newClientOrderId = packetFIX.ClientOrderId;
            var oldClientOrderId = packetFIX.OriginalClientOrderId;
            SymbolInfo symbolInfo;
            try
            {
                symbolInfo = Factory.Symbol.LookupSymbol(packetFIX.Symbol);
            }
            catch (ApplicationException ex)
            {
                log.Error("Error looking up " + packetFIX.Symbol + ": " + ex.Message);
                return null;
            }
            CreateOrChangeOrder oldOrder;
            if (!OrderStore.TryGetOrderById(oldClientOrderId, out oldOrder))
            {
                if (debug && (LogRecovery || !IsRecovery))
                {
                    log.Debug("Original Order ID# " + oldClientOrderId + " not found for updating cancel order. Normal.");
                }
                return null;
            }
            CreateOrChangeOrder order;
            if (! OrderStore.TryGetOrderById(newClientOrderId, out order))
            {
                if (debug && (LogRecovery || IsRecovered))
                {
                    log.Debug("Client Order ID# " + newClientOrderId + " was not found. Recreating.");
                }
                order = Factory.Utility.PhysicalOrder(orderState, symbolInfo, oldOrder);
                order.BrokerOrder = newClientOrderId;
            }
            OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
            if (oldOrder != null)
            {
                if (debug && (LogRecovery || !IsRecovery)) log.Debug("Setting replace property of " + oldOrder.BrokerOrder + " to be replaced by " + order.BrokerOrder);
                oldOrder.ReplacedBy = order;
            }
            if (trace)
            {
                log.Trace("Updated order list:");
                var logOrders = OrderStore.LogOrders();
                log.Trace("Broker Orders:\n" + logOrders);
            }
            return order;
        }

        private void TestMethod(MessageFIX4_4 packetFIX)
        {
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
			CreateOrChangeOrder origOrder;
			try {
				origOrder = OrderStore.GetOrderById(clientOrderId);
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
					algorithm = Factory.Utility.OrderAlgorithm( "mbtfix", symbolInfo, this, orderCache, OrderStore);
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

        public override void PositionChange(Receiver receiver, SymbolInfo symbol, int desiredPosition, Iterable<LogicalOrder> inputOrders, Iterable<StrategyPosition> strategyPositions)
		{
            if (!IsRecovered)
            {
                log.Warn("PositionChange event received while FIX was offline or recovering. Ignoring. Current connection status is: " + ConnectionStatus);
                return;
            }
			var count = inputOrders == null ? 0 : inputOrders.Count;
			if( debug) log.Debug( "PositionChange " + symbol + ", desired " + desiredPosition + ", order count " + count);
			
			var algorithm = GetAlgorithm(symbol.BinaryIdentifier);
            algorithm.SetDesiredPosition(desiredPosition);
            algorithm.SetLogicalOrders(inputOrders, strategyPositions);
            lock (orderAlgorithmLocker)
            {
                if( !algorithm.IsPositionSynced)
                {
                    OrderStore.ClearPendingOrders(symbol);
                    algorithm.TrySyncPosition(strategyPositions);
                }

                algorithm.ProcessOrders();

                if(SyncTicks.Enabled)
                {
                    var tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
                    tickSync.RemovePositionChange();
                }
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
	        
		public void OnCreateBrokerOrder(CreateOrChangeOrder createOrChangeOrder)
		{
			createOrChangeOrder.OrderState = OrderState.Pending;
			if( debug) log.Debug( "OnCreateBrokerOrder " + createOrChangeOrder);
			OnCreateOrChangeBrokerOrder(createOrChangeOrder,null, false);
		}
	        
		private void OnCreateOrChangeBrokerOrder(CreateOrChangeOrder createOrChangeOrder, object origBrokerOrder, bool isChange)
		{
			var fixMsg = (FIXMessage4_4) FixFactory.Create();
            OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
			
			if( debug) log.Debug( "Adding Order to open order list: " + createOrChangeOrder);
			if( isChange) {
				fixMsg.SetClientOrderId((string)createOrChangeOrder.BrokerOrder);
				fixMsg.SetOriginalClientOrderId((string)origBrokerOrder);
				var origOrder = OrderStore.GetOrderById((string) origBrokerOrder);
				if( origOrder != null) {
					origOrder.ReplacedBy = createOrChangeOrder;
					if( debug) log.Debug("Setting replace property of " + origBrokerOrder + " to " + createOrChangeOrder.BrokerOrder);
				}
			} else {
				fixMsg.SetClientOrderId((string)createOrChangeOrder.BrokerOrder);
			}
			fixMsg.SetAccount(AccountNumber);
			if( isChange) {
				fixMsg.AddHeader("G");
			} else {
				fixMsg.AddHeader("D");
				if( createOrChangeOrder.Symbol.Destination.ToLower() == "default") {
					fixMsg.SetDestination("MBTX");
				} else {
					fixMsg.SetDestination(createOrChangeOrder.Symbol.Destination);
				}
			}
			fixMsg.SetHandlingInstructions(1);
			fixMsg.SetSymbol(createOrChangeOrder.Symbol.Symbol);
			fixMsg.SetSide( GetOrderSide(createOrChangeOrder.Side));
			switch( createOrChangeOrder.Type) {
				case OrderType.BuyLimit:
					fixMsg.SetOrderType(2);
					fixMsg.SetPrice(createOrChangeOrder.Price);
					fixMsg.SetTimeInForce(1);
					break;
				case OrderType.BuyMarket:
					fixMsg.SetOrderType(1);
					fixMsg.SetTimeInForce(0);
					break;
				case OrderType.BuyStop:
					fixMsg.SetOrderType(3);
					fixMsg.SetPrice(createOrChangeOrder.Price);
					fixMsg.SetStopPrice(createOrChangeOrder.Price);
					fixMsg.SetTimeInForce(1);
					break;
				case OrderType.SellLimit:
					fixMsg.SetOrderType(2);
					fixMsg.SetPrice(createOrChangeOrder.Price);
					fixMsg.SetTimeInForce(1);
					break;
				case OrderType.SellMarket:
					fixMsg.SetOrderType(1);
					fixMsg.SetTimeInForce(0);
					break;
				case OrderType.SellStop:
					fixMsg.SetOrderType(3);
					fixMsg.SetPrice(createOrChangeOrder.Price);
					fixMsg.SetStopPrice(createOrChangeOrder.Price);
					fixMsg.SetTimeInForce(1);
					break;
			}
			fixMsg.SetLocateRequired("N");
			fixMsg.SetTransactTime(createOrChangeOrder.UtcCreateTime);
			fixMsg.SetOrderQuantity((int)createOrChangeOrder.Size);
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
		
		public void OnCancelBrokerOrder(CreateOrChangeOrder order)
		{
			if( debug) log.Debug( "OnCancelBrokerOrder " + order);
            OrderStore.SetSequences(RemoteSequence, FixFactory.LastSequence);
            CreateOrChangeOrder createOrChangeOrder;
			try {
                createOrChangeOrder = OrderStore.GetOrderById(order.OriginalOrder.BrokerOrder);
			} catch( ApplicationException ex) {
				log.Warn("Order probably already canceled. " + ex.Message);
				if( SyncTicks.Enabled) {
					var tickSync = SyncTicks.GetTickSync(order.Symbol.BinaryIdentifier);
					tickSync.RemovePhysicalOrder();
				}
				return;
			}
			createOrChangeOrder.OrderState = OrderState.Pending;
		    createOrChangeOrder.ReplacedBy = order;
			
			var fixMsg = (FIXMessage4_4) FixFactory.Create();
		    string newClientOrderId = order.BrokerOrder;
			fixMsg.SetOriginalClientOrderId((string)order.OriginalOrder.BrokerOrder);
			fixMsg.SetClientOrderId(newClientOrderId);
			fixMsg.SetAccount(AccountNumber);
			fixMsg.SetSide( GetOrderSide(createOrChangeOrder.Side));
			fixMsg.AddHeader("F");
			fixMsg.SetSymbol(createOrChangeOrder.Symbol.Symbol);
			fixMsg.SetTransactTime(TimeStamp.UtcNow);
			SendMessage(fixMsg);
		}
		
		public void OnChangeBrokerOrder(CreateOrChangeOrder createOrChangeOrder)
		{
			createOrChangeOrder.OrderState = OrderState.Pending;
			if( debug) log.Debug( "OnChangeBrokerOrder( " + createOrChangeOrder + ")");
			OnCreateOrChangeBrokerOrder( createOrChangeOrder, createOrChangeOrder.OriginalOrder.BrokerOrder, true);
		}

	    public bool HasBrokerOrder(CreateOrChangeOrder order)
	    {
	        CreateOrChangeOrder queueOrder;
            if( OrderStore.TryGetOrderBySerial(order.LogicalSerialNumber, out queueOrder))
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