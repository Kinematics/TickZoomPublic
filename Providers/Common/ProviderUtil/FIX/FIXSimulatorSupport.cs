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
using TickZoom.Api;

namespace TickZoom.FIX
{
	public class FIXSimulatorSupport : FIXSimulator
	{
		private string localAddress = "0.0.0.0";
		private static Log log = Factory.SysLog.GetLogger(typeof(FIXSimulatorSupport));
		private static bool trace = log.IsTraceEnabled;
		private static bool debug = log.IsDebugEnabled;
		private SimpleLock symbolHandlersLocker = new SimpleLock();
		private FIXTFactory1_1 fixFactory;
		private long realTimeOffset;
		private object realTimeOffsetLocker = new object();
		private YieldMethod MainLoopMethod;

		// FIX fields.
		private ushort fixPort = 0;
		private Socket fixListener;
		protected Socket fixSocket;
		private Packet fixReadPacket;
		private Packet fixWritePacket;
		private Task task;
		private bool isFIXSimulationStarted = false;
		private PacketFactory fixPacketFactory;

		// Quote fields.
		private ushort quotesPort = 0;
		private Socket quoteListener;
		protected Socket quoteSocket;
		private Packet quoteReadPacket;
		private Packet quoteWritePacket;
		private bool isQuoteSimulationStarted = false;
		private PacketFactory quotePacketFactory;
		protected FastQueue<Packet> fixPacketQueue = Factory.TickUtil.FastQueue<Packet>("SimulatorFIX");
		protected FastQueue<Packet> quotePacketQueue = Factory.TickUtil.FastQueue<Packet>("SimulatorQuote");
		private Dictionary<long, FIXServerSymbolHandler> symbolHandlers = new Dictionary<long, FIXServerSymbolHandler>();
		private bool isPlayBack = false;

		public FIXSimulatorSupport(string mode, ushort fixPort, ushort quotesPort, PacketFactory fixPacketFactory, PacketFactory quotePacketFactory)
		{
			isPlayBack = !string.IsNullOrEmpty(mode) && mode == "PlayBack";
			this.fixPacketFactory = fixPacketFactory;
			this.quotePacketFactory = quotePacketFactory;
			ListenToFIX(fixPort);
			ListenToQuotes(quotesPort);
			MainLoopMethod = MainLoop;
		}

		private void ListenToFIX(ushort fixPort)
		{
			this.fixPort = fixPort;
			fixListener = Factory.Provider.Socket(typeof(FIXSimulatorSupport).Name);
			fixListener.Bind( localAddress, fixPort);
			fixListener.Listen( 5);
			fixListener.OnConnect = OnConnectFIX;
			fixListener.OnDisconnect = OnDisconnectFIX;
			Factory.Provider.Manager.AddReader( fixListener);
			fixPort = fixListener.Port;
			log.Info("Listening to " + localAddress + " on port " + fixPort);
		}

		private void ListenToQuotes(ushort quotesPort)
		{
			this.quotesPort = quotesPort;
			quoteListener = Factory.Provider.Socket(typeof(FIXSimulatorSupport).Name);
			quoteListener.Bind( localAddress, quotesPort);
			quoteListener.Listen( 5);
			quoteListener.OnConnect = OnConnectQuotes;
			quoteListener.OnDisconnect = OnDisconnectQuotes;
			Factory.Provider.Manager.AddReader(quoteListener);
			quotesPort = quoteListener.Port;
			log.Info("Listening to " + localAddress + " on port " + quotesPort);
		}

		protected virtual void OnConnectFIX(Socket socket)
		{
			fixSocket = socket;
			fixSocket.PacketFactory = fixPacketFactory;
			Factory.Provider.Manager.AddReader(socket);
			Factory.Provider.Manager.AddWriter(socket);
			log.Info("Received FIX connection: " + socket);
			StartFIXSimulation();
			TryInitializeTask();
			fixSocket.ReceiveQueue.Connect( task);
		}

		protected virtual void OnConnectQuotes(Socket socket)
		{
			quoteSocket = socket;
			quoteSocket.PacketFactory = quotePacketFactory;
			Factory.Provider.Manager.AddReader(socket);
			Factory.Provider.Manager.AddWriter(socket);
			log.Info("Received quotes connection: " + socket);
			StartQuoteSimulation();
			TryInitializeTask();
			quoteSocket.ReceiveQueue.Connect( task);
		}
		
		private void TryInitializeTask() {
			if( task == null) {
				task = Factory.Parallel.Loop("FIXSimulator", OnException, MainLoop);
				quotePacketQueue.Connect( task);
				fixPacketQueue.Connect( task);
				task.Start();
			}
		}

		private void OnDisconnectFIX(Socket socket)
		{
			if (this.fixSocket == socket) {
				log.Info("FIX socket disconnect: " + socket);
				CloseSockets();
			}
		}

		private void OnDisconnectQuotes(Socket socket)
		{
			if (this.quoteSocket == socket) {
				log.Info("Quotes socket disconnect: " + socket);
				CloseSockets();
			}
		}

		protected virtual void CloseSockets()
		{
			if (symbolHandlers != null) {
				if (symbolHandlersLocker.TryLock()) {
					try {
						foreach (var kvp in symbolHandlers) {
							var handler = kvp.Value;
							handler.Dispose();
						}
						symbolHandlers.Clear();
					} finally {
						symbolHandlersLocker.Unlock();
					}
				}
			}
			if (task != null) {
				task.Stop();
				task.Join();
			}
			if (fixSocket != null) {
				fixSocket.Dispose();
			}
			if (task != null) {
				task.Stop();
				task.Join();
			}
			if (quoteSocket != null) {
				quoteSocket.Dispose();
			}
		}

		public virtual void StartFIXSimulation()
		{
			isFIXSimulationStarted = true;
		}

		public virtual void StartQuoteSimulation()
		{
			isQuoteSimulationStarted = true;
		}
		
		private enum State { Start, ProcessFIX, WriteFIX, ProcessQuotes, WriteQuotes, Return };
		private State state = State.Start;
		private bool hasQuotePacket = false;
		private bool hasFIXPacket = false;
		private Yield MainLoop() {
			var result = false;
			switch( state) {
				case State.Start:
					if( FIXReadLoop()) {
						result = true;
					} else {
						TryRequestHeartbeat( TimeStamp.UtcNow);
					}
				ProcessFIX:
					hasFIXPacket = ProcessFIXPackets();
					if( hasFIXPacket ) {
						result = true;
					}
				WriteFIX:
					if( hasFIXPacket) {
						if( !WriteToFIX()) {
							state = State.WriteFIX;
							return Yield.NoWork.Repeat;
						}
						if( fixPacketQueue.Count > 0) {
							state = State.ProcessFIX;
							return Yield.DidWork.Repeat;
						}
					}
					if( QuotesReadLoop()) {
						result = true;
					}
				ProcessQuotes: 
					hasQuotePacket = ProcessQuotePackets();
					if( hasQuotePacket) {
						result = true;
					}
				WriteQuotes:
					if( hasQuotePacket) {
						if( !WriteToQuotes()) {
							state = State.WriteQuotes;
							return Yield.NoWork.Repeat;
						}
						if( quotePacketQueue.Count > 0) {
							state = State.ProcessQuotes;
							return Yield.DidWork.Invoke(MainLoopMethod);
						}
					}
					break;
				case State.ProcessFIX:
					goto ProcessFIX;
				case State.WriteFIX:
					goto WriteFIX;
				case State.WriteQuotes:
					goto WriteQuotes;
				case State.ProcessQuotes:
					goto ProcessQuotes;
			}
			state = State.Start;
			if( result) {
				return Yield.DidWork.Repeat;
			} else {
				return Yield.NoWork.Repeat;
			}
		}

		private bool ProcessFIXPackets() {
			if( fixWritePacket == null && fixPacketQueue.Count == 0) {
				return false;
			}
			if( trace) log.Trace("ProcessFIXPackets( " + fixPacketQueue.Count + " packets in queue.)");
			if( fixPacketQueue.DequeueStruct(ref fixWritePacket)) {
				fixPacketQueue.RemoveStruct();
				return true;
			} else {
				return false;
			}
		}
		private bool ProcessQuotePackets() {
			if( quoteWritePacket == null && quotePacketQueue.Count == 0) {
				return false;
			}
			if( trace) log.Trace("ProcessQuotePackets( " + quotePacketQueue.Count + " packets in queue.)");
			if( quotePacketQueue.DequeueStruct(ref quoteWritePacket)) {
				QuotePacketQueue.RemoveStruct();
				return true;
			} else {
				return false;
			}
		}
		private bool Resend(PacketFIXT1_1 packetFIX) {
			var writePacket = fixSocket.CreatePacket();			
			var mbtMsg = fixFactory.Create();
			mbtMsg.AddHeader("2");
			mbtMsg.SetBeginSeqNum(sequenceCounter);
			mbtMsg.SetEndSeqNum(0);
			string message = mbtMsg.ToString();
			if( debug) log.Debug("Sending resend request: " + message);
			writePacket.DataOut.Write(message.ToCharArray());
			writePacket.UtcTime = TimeStamp.UtcNow.Internal;
			return fixPacketQueue.EnqueueStruct(ref writePacket, writePacket.UtcTime);
		}
		private Random random = new Random(1234);
		private int sequenceCounter = 1;
		private bool FIXReadLoop()
		{
			if (isFIXSimulationStarted) {
				if (fixSocket.TryGetPacket(out fixReadPacket)) {
					var packetFIX = (PacketFIXT1_1) fixReadPacket;
					if( fixFactory != null && random.Next(10) == 1) {
						// Ignore this message. Pretend we never received it.
						// This will test the message recovery.
						if( debug) log.Debug("Ignoring fix message sequence " + packetFIX.Sequence);
						return Resend(packetFIX);
					}
					if (trace) log.Trace("Local Read: " + fixReadPacket);
					if( packetFIX.Sequence != sequenceCounter) {
						return Resend(packetFIX);
					} else {
						sequenceCounter++;
						ParseFIXMessage(fixReadPacket);
						return true;
					}
				}
			}
			return false;
		}
		
		public long GetRealTimeOffset( long utcTime) {
			lock( realTimeOffsetLocker) {
				if( realTimeOffset == 0L) {
					var currentTime = TimeStamp.UtcNow;
					var tickUTCTime = new TimeStamp(utcTime);
				   	log.Info("First historical playback tick UTC tick time is " + tickUTCTime);
				   	log.Info("Current tick UTC time is " + currentTime);
				   	realTimeOffset = currentTime.Internal - utcTime;
				   	var microsecondsInMinute = 1000L * 1000L * 60L;
				   	var extra = realTimeOffset % microsecondsInMinute;
				   	realTimeOffset -= extra;
				   	realTimeOffset += microsecondsInMinute;
				   	var elapsed = new Elapsed( realTimeOffset);
				   	log.Info("Setting real time offset to " + elapsed);
				}
			}
			return realTimeOffset;
		}

		public void AddSymbol(string symbol, Action<Packet, SymbolInfo, Tick> onTick, Action<PhysicalFill,int,int,int> onPhysicalFill, Action<PhysicalOrder,string> onOrderReject)
		{
			var symbolInfo = Factory.Symbol.LookupSymbol(symbol);
			if (!symbolHandlers.ContainsKey(symbolInfo.BinaryIdentifier)) {
				var symbolHandler = new FIXServerSymbolHandler(this, isPlayBack, symbol, onTick, onPhysicalFill, onOrderReject);
				symbolHandlers.Add(symbolInfo.BinaryIdentifier, symbolHandler);
			}
		}

		public int GetPosition(SymbolInfo symbol)
		{
			var symbolHandler = symbolHandlers[symbol.BinaryIdentifier];
			return symbolHandler.ActualPosition;
		}

		public void CreateOrder(PhysicalOrder order)
		{
			var symbolHandler = symbolHandlers[order.Symbol.BinaryIdentifier];
			symbolHandler.CreateOrder(order);
		}

		public void ChangeOrder(PhysicalOrder order, object origBrokerOrder)
		{
			var symbolHandler = symbolHandlers[order.Symbol.BinaryIdentifier];
			symbolHandler.ChangeOrder(order, origBrokerOrder);
		}

		public void CancelOrder(SymbolInfo symbol, object origBrokerOrder)
		{
			var symbolHandler = symbolHandlers[symbol.BinaryIdentifier];
			symbolHandler.CancelOrder( origBrokerOrder);
		}
		
		public PhysicalOrder GetOrderById(SymbolInfo symbol, string clientOrderId) {
			var symbolHandler = symbolHandlers[symbol.BinaryIdentifier];
			return symbolHandler.GetOrderById(clientOrderId);
		}

		private bool QuotesReadLoop()
		{
			if (isQuoteSimulationStarted) {
				if (quoteSocket.TryGetPacket(out quoteReadPacket)) {
					if (trace)	log.Trace("Local Read: " + quoteReadPacket);
					ParseQuotesMessage(quoteReadPacket);
					return true;
				}
			}
			return false;
		}

		public virtual void ParseFIXMessage(Packet packet)
		{
			if (debug) log.Debug("Received FIX message: " + packet);
		}

		public virtual void ParseQuotesMessage(Packet packet)
		{
			if (debug) log.Debug("Received Quotes message: " + packet);
		}

		public bool WriteToFIX()
		{
			if (!isFIXSimulationStarted || fixWritePacket == null) return true;
			if( fixSocket.TrySendPacket(fixWritePacket)) {
				if (trace) log.Trace("Local Write: " + fixWritePacket);
				fixWritePacket = null;
				return true;
			} else {
				return false;
			}
		}

		public bool WriteToQuotes()
		{
			if (!isQuoteSimulationStarted || quoteWritePacket == null) return true;
			if( quoteSocket.TrySendPacket(quoteWritePacket)) {
				if (trace) log.Trace("Local Write: " + quoteWritePacket);
				quoteWritePacket = null;
				return true;
			} else {
				return false;
			}
		}

		private TimeStamp heartbeatTimer;
		private bool firstHeartbeat = true;
		private void IncreaseHeartbeat(TimeStamp currentTime) {
			heartbeatTimer = currentTime;
			heartbeatTimer.AddSeconds(30);
		}		

		private void TryRequestHeartbeat(TimeStamp currentTime) {
			if( firstHeartbeat) {
				IncreaseHeartbeat(currentTime);
				firstHeartbeat = false;
				return;
			}
			if( currentTime > heartbeatTimer) {
				IncreaseHeartbeat(currentTime);
				OnHeartbeat();
			}
		}
		
		protected virtual Yield OnHeartbeat() {
			if( fixSocket != null && FixFactory != null) {
				var writePacket = fixSocket.CreatePacket();
				var mbtMsg = (FIXMessage4_4) FixFactory.Create();
				mbtMsg.AddHeader("1");
				string message = mbtMsg.ToString();
				writePacket.DataOut.Write(message.ToCharArray());
				writePacket.UtcTime = TimeStamp.UtcNow.Internal;
				if( trace) log.Trace("Requesting heartbeat: " + message);
				while( !fixPacketQueue.EnqueueStruct(ref writePacket,writePacket.UtcTime)) {
					if( fixPacketQueue.IsFull) {
						throw new ApplicationException("Fix Queue is full.");
					}
				}
			}
			return Yield.DidWork.Return;
		}
		
		public void OnException(Exception ex)
		{
			log.Error("Exception occurred", ex);
		}

		protected volatile bool isDisposed = false;
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!isDisposed) {
				isDisposed = true;
				if (disposing) {
					if (debug)
						log.Debug("Dispose()");
					if (symbolHandlers != null) {
						if (symbolHandlersLocker.TryLock()) {
							try {
								foreach (var kvp in symbolHandlers) {
									var handler = kvp.Value;
									handler.Dispose();
								}
								symbolHandlers.Clear();
							} finally {
								symbolHandlersLocker.Unlock();
							}
						}
					}
					if (task != null) {
						task.Stop();
					}
					if (fixListener != null) {
						fixListener.Dispose();
					}
					if (fixSocket != null) {
						fixSocket.Dispose();
					}
					if( fixPacketQueue != null) {
						fixPacketQueue.Clear();
					}
					if( quotePacketQueue != null) {
						quotePacketQueue.Clear();
					}
					if (quoteListener != null) {
						quoteListener.Dispose();
					}
					if (quoteSocket != null) {
						quoteSocket.Dispose();
					}
				}
			}
		}

		public ushort FIXPort {
			get { return fixPort; }
		}

		public ushort QuotesPort {
			get { return quotesPort; }
		}
		
		public FIXTFactory1_1 FixFactory {
			get { return fixFactory; }
			set { fixFactory = value; }
		}
				
		public long RealTimeOffset {
			get { return realTimeOffset; }
		}
		
		public Socket QuoteSocket {
			get { return quoteSocket; }
		}
		
		public FastQueue<Packet> QuotePacketQueue {
			get { return quotePacketQueue; }
		}
	}
}
