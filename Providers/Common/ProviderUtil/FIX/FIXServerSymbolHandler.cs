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
using TickZoom.Api;

namespace TickZoom.FIX
{
	public class FIXServerSymbolHandler : IDisposable {
		private static Log log = Factory.SysLog.GetLogger(typeof(FIXServerSymbolHandler));
		private static bool trace = log.IsTraceEnabled;
		private static bool debug = log.IsDebugEnabled;
		private FillSimulator fillSimulator;
		private TickReader reader;
		private Action<Packet,SymbolInfo,Tick> onTick;
		private Task queueTask;
		private TickSync tickSync;
		private SymbolInfo symbol;
		private TickIO nextTick = Factory.TickUtil.TickIO();
		private bool isFirstTick = true;
		private bool isPlayBack = false;
		private long playbackOffset;
		private FIXSimulatorSupport fixSimulatorSupport;
		private LatencyMetric latency;
		private TrueTimer tickTimer;
		private TrueTimer packetTimer;
		private long intervalTime = 1000000;
		private long prevTickTime;
		private bool isVolumeTest = false;
		private long tickCounter = 0;
		
		public FIXServerSymbolHandler( FIXSimulatorSupport fixSimulatorSupport, 
		    bool isPlayBack, string symbolString,
		    Action<Packet,SymbolInfo,Tick> onTick,
		    Action<PhysicalFill, int,int,int> onPhysicalFill,
		    Action<PhysicalOrder,string> onRejectOrder) {
			this.fixSimulatorSupport = fixSimulatorSupport;
			this.isPlayBack = isPlayBack;
			this.onTick = onTick;
			this.symbol = Factory.Symbol.LookupSymbol(symbolString);
			reader = Factory.TickUtil.TickReader();
			reader.Initialize("Test\\MockProviderData", symbolString);
			fillSimulator = Factory.Utility.FillSimulator( "FIX", symbol, false);
			fillSimulator.OnPhysicalFill = onPhysicalFill;
			fillSimulator.OnRejectOrder = onRejectOrder;
			tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
			tickSync.ForceClear();
			queueTask = Factory.Parallel.Loop("FIXServerSymbol-"+symbolString, OnException, ProcessQueue);
			tickTimer = Factory.Parallel.CreateTimer(queueTask,PlayBackTick);
			packetTimer = Factory.Parallel.CreateTimer(queueTask,TryEnqueuePacket);
			queueTask.Scheduler = Scheduler.RoundRobin;
			reader.ReadQueue.Connect( queueTask);
			queueTask.Start();
			latency = new LatencyMetric("FIXServerSymbolHandler-"+symbolString.StripInvalidPathChars());
			reader.ReadQueue.StartEnqueue();
		}
		
		private void HasItem( object source) {
			queueTask.IncreaseActivity();
		}
		
	    private void TryCompleteTick() {
	    	if( tickSync.Completed) {
		    	if( trace) log.Trace("TryCompleteTick()");
		    	tickSync.Clear();
	    	} else if( tickSync.OnlyProcessPhysicalOrders) {
				fillSimulator.StartTick(nextTick);
				fillSimulator.ProcessOrders();
				tickSync.RemoveProcessPhysicalOrders();
	    	}
		}
		
		public int ActualPosition {
			get {
				return (int) fillSimulator.ActualPosition;
			}
		}
		
		public void CreateOrder(PhysicalOrder order) {
			fillSimulator.OnCreateBrokerOrder( order);
		}
		
		public void ChangeOrder(PhysicalOrder order, object origBrokerOrder) {
			fillSimulator.OnChangeBrokerOrder( order, origBrokerOrder);
		}
		
		public void CancelOrder(object origBrokerOrder) {
			fillSimulator.OnCancelBrokerOrder( symbol, origBrokerOrder);
		}
		
		public PhysicalOrder GetOrderById(string clientOrderId) {
			return fillSimulator.GetOrderById( clientOrderId);
		}
		
		private Yield ProcessQueue() {
			if( SyncTicks.Enabled) {
				if( !tickSync.TryLock()) {
					TryCompleteTick();
					return Yield.NoWork.Repeat;
				} else {
					if( trace) log.Trace("Locked tickSync for " + symbol);
				}
			}
			if( tickStatus == TickStatus.None || tickStatus == TickStatus.Sent) {
				return Yield.DidWork.Invoke(DequeueTick);
			} else {
				return Yield.NoWork.Repeat;
			}
		}

        private long GetNextUtcTime( long utcTime)
        {
            if (isVolumeTest)
            {
                return prevTickTime + intervalTime;
            }
            else
            {
                return utcTime + playbackOffset;
            }
        }

		private Yield DequeueTick() {
			var result = Yield.NoWork.Repeat;
			var binary = new TickBinary();
			
			try { 
				if( reader.ReadQueue.TryDequeue( ref binary))
				{
					tickStatus = TickStatus.None;
					tickCounter++;
				   	if( isPlayBack) {
						if( isFirstTick) {
							playbackOffset = fixSimulatorSupport.GetRealTimeOffset(binary.UtcTime);
							prevTickTime = TimeStamp.UtcNow.Internal + 5000000;
					   		isFirstTick = false;
						}
				   	    binary.UtcTime = GetNextUtcTime(binary.UtcTime);
				   	    prevTickTime = binary.UtcTime;
						if( tickCounter > 10) {
							intervalTime = 1000;
						}
						var time = new TimeStamp( binary.UtcTime);
				   	} 
				   	nextTick.Inject( binary);
				   	tickSync.AddTick();
				   	if( !isPlayBack) {
				   		if( isFirstTick) {
						   	fillSimulator.StartTick( nextTick);
					   		isFirstTick = false;
					   	} else { 
					   		fillSimulator.ProcessOrders();
					   	}
				   	}
				   	if( trace) log.Trace("Dequeue tick " + nextTick.UtcTime + "." + nextTick.UtcTime.Microsecond);
				   	result = Yield.DidWork.Invoke(ProcessTick);
				}
			} catch( QueueException ex) {
				if( ex.EntryType != EventType.EndHistorical) {
					throw;
				}
			}
			return result;
		}
		
		public enum TickStatus {
			None,
			Timer,
			Sent,
		}

		private volatile TickStatus tickStatus = TickStatus.None;
		private Yield ProcessTick() {
			var result = Yield.NoWork.Repeat;
			if( isPlayBack ) {
				switch( tickStatus) {
					case TickStatus.None:
						var overlapp = 300L;
						if( tickTimer.Active) tickTimer.Cancel();
                        var currentTime = TimeStamp.UtcNow;
                        if (nextTick.UtcTime.Internal > currentTime.Internal + overlapp &&
						   tickTimer.Start(nextTick.UtcTime)) {
							if( trace) log.Trace("Set next timer for " + nextTick.UtcTime  + "." + nextTick.UtcTime.Microsecond + " at " + currentTime  + "." + currentTime.Microsecond);							
							tickStatus = TickStatus.Timer;
						} else {
							if( trace) log.Trace("Current time " + currentTime + " was greater than tick time " + nextTick.UtcTime + "." + nextTick.UtcTime.Microsecond);
							result = Yield.DidWork.Invoke(SendPlayBackTick);
						}		
						break;
					case TickStatus.Sent:
						result = Yield.DidWork.Invoke(ProcessQueue);
						break;
					case TickStatus.Timer:
						break;
					default:
						throw new ApplicationException("Unknown tick status: " + tickStatus);
				}
				return result;
			} else {
				if( trace) log.Trace("Invoking ProcessOnTickCallBack");
				return Yield.DidWork.Invoke( ProcessOnTickCallBack);
			}
		}
		
		private Yield SendPlayBackTick() {
			latency.TryUpdate( nextTick.lSymbol, nextTick.UtcTime.Internal);
		   	if( isFirstTick) {
			   	fillSimulator.StartTick( nextTick);
		   		isFirstTick = false;
		   	} else { 
		   		fillSimulator.ProcessOrders();
		   	}
			return Yield.DidWork.Invoke(ProcessOnTickCallBack);
		}

		private Packet quotePacket;
		private Yield ProcessOnTickCallBack() {
			if( quotePacket == null) {
				quotePacket = fixSimulatorSupport.QuoteSocket.CreatePacket();
			} else if( quotePacket.IsFull) {
				if( fixSimulatorSupport.QuotePacketQueue.EnqueueStruct(ref quotePacket, quotePacket.UtcTime)) {
					quotePacket = fixSimulatorSupport.QuoteSocket.CreatePacket();
				} else {
					return Yield.NoWork.Repeat;
				}
			}
			onTick( quotePacket, symbol, nextTick);
			if( trace) log.Trace("Added tick to packet: " + nextTick.UtcTime);
			var current = nextTick.UtcTime.Internal;
			if( current < quotePacket.UtcTime) {
				quotePacket.UtcTime = current;
			}
 			reader.ReadQueue.RemoveStruct();
			tickStatus = TickStatus.Sent;
            if( isPlayBack && reader.ReadQueue.Count > 0)
            {
                var result = Yield.DidWork.Invoke(ProcessQueue);
                var item = default(QueueItem);
                reader.ReadQueue.PeekStruct(ref item);
                if (item.EventType == (int)EventType.Tick)
                {
                    var nextTime = GetNextUtcTime(item.Tick.UtcTime);
                    var currentTime = TimeStamp.UtcNow.Internal;
                    if (nextTime > currentTime)
                    {
                        if( trace) log.Trace("Sending tick because next " + new TimeStamp(nextTime).TimeOfDay + " greater than current " + new TimeStamp(currentTime).TimeOfDay);
                        return Yield.DidWork.Invoke(TryEnqueuePacket);
                    } else
                    {
                        if (trace) log.Trace("Buffering tick because next " + new TimeStamp(nextTime).TimeOfDay + " less than current " + new TimeStamp(currentTime).TimeOfDay);
                    }
                }
                return result;
            }
            else
            {
                return Yield.DidWork.Invoke(TryEnqueuePacket);
            }
		}

		private Yield TryEnqueuePacket() {
			if( quotePacket.Data.GetBuffer().Length == 0) {
				return Yield.NoWork.Repeat;
			}
            TickBinary nextBinary;
		    while( !fixSimulatorSupport.QuotePacketQueue.EnqueueStruct(ref quotePacket,quotePacket.UtcTime))
		    {
		        if( fixSimulatorSupport.QuotePacketQueue.IsFull)
		        {
		            return Yield.NoWork.Repeat;
		        }
		    }
			if( trace) log.Trace("Enqueued tick packet: " + new TimeStamp(quotePacket.UtcTime));
			quotePacket = fixSimulatorSupport.QuoteSocket.CreatePacket();
			return Yield.DidWork.Return;
		}
		
		private Yield PlayBackTick() {
            var result = Yield.DidWork.Repeat;
			if( tickStatus == TickStatus.Timer) {
				if( trace) log.Trace("Sending tick from timer event: " + nextTick.UtcTime);
				result = Yield.DidWork.Invoke(SendPlayBackTick);
			}
			return result;
		}
		
		private void OnException( Exception ex) {
			// Attempt to propagate the exception.
			log.Error("Exception occurred", ex);
			Dispose();
		}
		
	 	protected volatile bool isDisposed = false;
	    public void Dispose() 
	    {
	        Dispose(true);
	        GC.SuppressFinalize(this);      
	    }
	
	    protected virtual void Dispose(bool disposing)
	    {
	       		if( !isDisposed) {
	            isDisposed = true;   
	            if (disposing) {
	            	if( debug) log.Debug("Dispose()");
	            	if( reader != null) {
	            		reader.Dispose();
	            	}
	            	if( queueTask != null) {
	            		queueTask.Stop();
	            	}
	            }
    		}
	    }    
	        
		public bool IsPlayBack {
			get { return isPlayBack; }
			set { isPlayBack = value; }
		}
	}
}