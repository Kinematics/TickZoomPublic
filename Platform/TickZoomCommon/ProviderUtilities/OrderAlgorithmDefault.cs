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
    public class OrderAlgorithmDefault : OrderAlgorithm {
		private static readonly Log staticLog = Factory.SysLog.GetLogger(typeof(OrderAlgorithmDefault));
		private readonly bool debug = staticLog.IsDebugEnabled;
		private readonly bool trace = staticLog.IsTraceEnabled;
		private Log log;
		private SymbolInfo symbol;
		private PhysicalOrderHandler physicalOrderHandler;
        private ActiveList<CreateOrChangeOrder> originalPhysicals;
        private SimpleLock bufferedLogicalsLocker = new SimpleLock();
        private volatile bool bufferedLogicalsChanged = false;
		private ActiveList<LogicalOrder> bufferedLogicals;
        private ActiveList<LogicalOrder> canceledLogicals;
        private ActiveList<LogicalOrder> originalLogicals;
		private ActiveList<LogicalOrder> logicalOrders;
        private ActiveList<CreateOrChangeOrder> physicalOrders;
		private List<LogicalOrder> extraLogicals = new List<LogicalOrder>();
		private int desiredPosition;
		private Action<SymbolInfo,LogicalFillBinary> onProcessFill;
		private bool handleSimulatedExits = false;
		private int actualPosition = 0;
		private int sentPhysicalOrders = 0;
		private TickSync tickSync;
		private Dictionary<long,long> filledOrders = new Dictionary<long,long>();
	    private LogicalOrderCache logicalOrderCache;
	    private PhysicalOrderCache physicalOrderCache;
        private bool isPositionSynced = false;
        private long minimumTick;
        private List<MissingLevel> missingLevels = new List<MissingLevel>();

        public struct MissingLevel
        {
            public int Size;
            public long Price;
        }
		
		public OrderAlgorithmDefault(string name, SymbolInfo symbol, PhysicalOrderHandler brokerOrders, LogicalOrderCache logicalOrderCache) {
			this.log = Factory.SysLog.GetLogger(typeof(OrderAlgorithmDefault).FullName + "." + symbol.Symbol.StripInvalidPathChars() + "." + name );
			this.symbol = symbol;
		    this.logicalOrderCache = logicalOrderCache;
            this.physicalOrderCache = new PhysicalOrderCache(name,symbol);
			this.tickSync = SyncTicks.GetTickSync(symbol.BinaryIdentifier);
			this.physicalOrderHandler = brokerOrders;
            this.canceledLogicals = new ActiveList<LogicalOrder>();
            this.originalLogicals = new ActiveList<LogicalOrder>();
			this.bufferedLogicals = new ActiveList<LogicalOrder>();
            this.originalPhysicals = new ActiveList<CreateOrChangeOrder>();
			this.logicalOrders = new ActiveList<LogicalOrder>();
            this.physicalOrders = new ActiveList<CreateOrChangeOrder>();
		    this.minimumTick = symbol.MinimumTick.ToLong();
		}
		
		private List<CreateOrChangeOrder> TryMatchId( ActiveList<CreateOrChangeOrder> list, LogicalOrder logical, bool remove)
		{
		    var result = false;
            var physicalOrderMatches = new List<CreateOrChangeOrder>();
            for (var current = list.First; current != null; current = current.Next)
		    {
		        var physical = current.Value;
				if( logical.Id == physical.LogicalOrderId) {
                    switch( physical.OrderState)
                    {
                        case OrderState.Suspended:
                            if (debug) log.Debug("Cannot change a suspended order: " + physical);
                            break;
                        case OrderState.Filled:
                            if (debug) log.Debug("Cannot change a filled order: " + physical);
                            break;
                        default:
                            physicalOrderMatches.Add(physical);
                            list.Remove(current);
                            result = true;
                            break;
                    }
				}
			}

			return physicalOrderMatches;
		}
		
		private bool TryMatchTypeOnly( LogicalOrder logical, out CreateOrChangeOrder createOrChangeOrder) {
			double difference = logical.Position - Math.Abs(actualPosition);
		    var next = originalPhysicals.First;
		    for (var current = next; current != null; current = next)
		    {
		        next = current.Next;
		        var physical = current.Value;
				if( logical.Type == physical.Type) {
					if( logical.TradeDirection == TradeDirection.Entry) {
						if( difference != 0) {
							createOrChangeOrder = physical;
							return true;
						}
					}
					if( logical.TradeDirection == TradeDirection.Exit) {
						if( actualPosition != 0) {
							createOrChangeOrder = physical;
							return true;
						}
					}
				}
			}
			createOrChangeOrder = default(CreateOrChangeOrder);
			return false;
		}

        private bool TryCancelBrokerOrder(CreateOrChangeOrder physical)
        {
			bool result = false;
            if (physical.OrderState != OrderState.Pending &&
                // Market orders can't be canceled.
                physical.Type != OrderType.BuyMarket &&
                physical.Type != OrderType.SellMarket)
            {
                var cancelOrder = new CreateOrChangeOrderDefault(OrderState.Pending, symbol, physical);
                if (!physicalOrderCache.AddCancelOrder(cancelOrder))
                {
                    if (debug) log.Debug("Ignoring cancel broker order " + physical.BrokerOrder + " as physical order cache has it already.");
                    result = false;
                }
                else
                {
                    if (debug) log.Debug("Cancel Broker Order: " + physical);
                    sentPhysicalOrders++;
                    TryAddPhysicalOrder(physical);
                    physicalOrderHandler.OnCancelBrokerOrder(cancelOrder);
                    result = true;
                }
            }
		    return result;
		}
		
		private void TryChangeBrokerOrder(CreateOrChangeOrder createOrChange, CreateOrChangeOrder origOrder) {
            if (createOrChange.OrderState == OrderState.Active)
            {
                createOrChange.OriginalOrder = origOrder;
                if (!physicalOrderCache.AddCancelOrder(createOrChange))
                {
                    if (debug) log.Debug("Ignoring broker order " + origOrder.BrokerOrder + " as physical order cache has it already.");
                    return;
                }
                if (debug) log.Debug("Change Broker Order: " + createOrChange);
				sentPhysicalOrders++;
				TryAddPhysicalOrder(createOrChange);
                physicalOrderHandler.OnChangeBrokerOrder(createOrChange);
			}
		}
		
		private void TryAddPhysicalOrder(CreateOrChangeOrder createOrChange) {
			if( SyncTicks.Enabled) tickSync.AddPhysicalOrder(createOrChange);
		}

        private void TryCreateBrokerOrder(CreateOrChangeOrder physical)
        {
			if( debug) log.Debug("Create Broker Order " + physical);
            if (physical.Size <= 0)
            {
                throw new ApplicationException("Sorry, order size must be greater than or equal to zero.");
            }
            if (!physicalOrderCache.AddCreateOrder(physical))
            {
                if( debug) log.Debug("Ignoring broker order as physical order cache has it already.");
                return;
            }
            sentPhysicalOrders++;
            TryAddPhysicalOrder(physical);
            physicalOrderHandler.OnCreateBrokerOrder(physical);
        }

        private string ToString(List<CreateOrChangeOrder> matches)
        {
            var sb = new StringBuilder();
            foreach( var physical in matches)
            {
                sb.AppendLine(physical.ToString());
            }
            return sb.ToString();
        }

        public virtual void ProcessMatchPhysicalEntry(LogicalOrder logical, List<CreateOrChangeOrder> matches)
		{
            if (matches.Count != 1)
            {
                throw new ApplicationException("Expected 1 match but found " + matches.Count + " matches for logical order: " +
                                               logical + "\n" + ToString(matches));
            }
            ProcessMatchPhysicalEntry(logical, matches[0], logical.Position, logical.Price);
            return;
		}

        protected void ProcessMatchPhysicalEntry(LogicalOrder logical, CreateOrChangeOrder physical, int position, double price)
        {
			log.Trace("ProcessMatchPhysicalEntry()");
			var strategyPosition = logical.StrategyPosition;
			var difference = position - Math.Abs(strategyPosition);
			log.Trace("position difference = " + difference);
			if( difference == 0) {
				TryCancelBrokerOrder(physical);
			} else if( difference != physical.Size) {
				var origBrokerOrder = physical.BrokerOrder;
				if( strategyPosition == 0) {
					physicalOrders.Remove(physical);
					var side = GetOrderSide(logical.Type);
					var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active,symbol,logical,side,difference,price);
                    TryChangeBrokerOrder(changeOrder, physical);
				} else {
					if( strategyPosition > 0) {
						if( logical.Type == OrderType.BuyStop || logical.Type == OrderType.BuyLimit) {
							physicalOrders.Remove(physical);
							var side = GetOrderSide(logical.Type);
                            var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, difference, price);
                            TryChangeBrokerOrder(changeOrder, physical);
						} else {
                            if (debug) log.Debug("Strategy position is long " + strategyPosition + " so canceling " + logical.Type + " order..");
                            TryCancelBrokerOrder(physical);
						}
					}
					if( strategyPosition < 0) {
						if( logical.Type == OrderType.SellStop || logical.Type == OrderType.SellLimit) {
							physicalOrders.Remove(physical);
							var side = GetOrderSide(logical.Type);
                            var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, difference, price);
                            TryChangeBrokerOrder(changeOrder, physical);
                        }
                        else
                        {
                            if (debug) log.Debug("Strategy position is short " + strategyPosition + " so canceling " + logical.Type + " order..");
							TryCancelBrokerOrder(physical);
						}
					}
				}
			} else if( price.ToLong() != physical.Price.ToLong()) {
				var origBrokerOrder = physical.BrokerOrder;
				physicalOrders.Remove(physical);
				var side = GetOrderSide(logical.Type);
                var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, difference, price);
                TryChangeBrokerOrder(changeOrder, physical);
            }
            else
            {
				VerifySide( logical, physical, price);
			}
		}

        private void ProcessMatchPhysicalReverse(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            if (matches.Count != 1)
            {
                throw new ApplicationException("Expected 1 match but found " + matches.Count +
                                               " matches for logical order: " + logical + "\n" + ToString(matches));
            }
            var physical = matches[0];
            ProcessMatchPhysicalReverse(logical, physical, logical.Position, logical.Price);
            return;
        }

        private void ProcessMatchPhysicalReverse(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
			var strategyPosition = logical.StrategyPosition;
			var logicalPosition =
				logical.Type == OrderType.BuyLimit ||
				logical.Type == OrderType.BuyMarket ||
				logical.Type == OrderType.BuyStop ? 
				position : - position;
			var physicalPosition = 
				createOrChange.Side == OrderSide.Buy ?
				createOrChange.Size : - createOrChange.Size;
			var delta = logicalPosition - strategyPosition;
			var difference = delta - physicalPosition;
			if( delta == 0 || strategyPosition > 0 && logicalPosition > 0 ||
			  strategyPosition < 0 && logicalPosition < 0) {
				TryCancelBrokerOrder(createOrChange);
			} else if( difference != 0) {
				var origBrokerOrder = createOrChange.BrokerOrder;
				if( delta > 0) {
                    var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, OrderSide.Buy, Math.Abs(delta), price);
                    TryChangeBrokerOrder(changeOrder, createOrChange);
                }
                else
                {
					OrderSide side;
					if( strategyPosition > 0 && logicalPosition < 0) {
						side = OrderSide.Sell;
						delta = strategyPosition;
						if( delta == createOrChange.Size) {
							ProcessMatchPhysicalChangePriceAndSide( logical, createOrChange, delta, price);
							return;
						}
					} else {
						side = OrderSide.SellShort;
					}
					side = (long) strategyPosition >= (long) Math.Abs(delta) ? OrderSide.Sell : OrderSide.SellShort;
                    var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, Math.Abs(delta), price);
                    TryChangeBrokerOrder(changeOrder, createOrChange);
                }
			} else {
				ProcessMatchPhysicalChangePriceAndSide( logical, createOrChange, delta, price);
			}
		}
		
		private void ProcessMatchPhysicalReversePriceAndSide(LogicalOrder logical, CreateOrChangeOrder createOrChange, int delta, double price) {
			if( logical.Price.ToLong() != createOrChange.Price.ToLong()) {
				var origBrokerOrder = createOrChange.BrokerOrder;
				physicalOrders.Remove(createOrChange);
				var side = GetOrderSide(logical.Type);
                var changeOrder= new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, Math.Abs(delta), price);
                TryChangeBrokerOrder(changeOrder, createOrChange);
            }
            else
            {
				VerifySide( logical, createOrChange, price);
			}
		}

        private void MatchLogicalToPhysicals(LogicalOrder logical, List<CreateOrChangeOrder> matches, Action<LogicalOrder, CreateOrChangeOrder, int, double> onMatchCallback)
        {
            var price = logical.Price.ToLong();
            var sign = 1;
            var levels = 1;
            switch (logical.Type)
            {
                case OrderType.BuyMarket:
                case OrderType.SellMarket:
                    break;
                case OrderType.BuyLimit:
                case OrderType.SellStop:
                    sign = -1;
                    levels = logical.Levels;
                    break;
                case OrderType.SellLimit:
                case OrderType.BuyStop:
                    levels = logical.Levels;
                    break;
                default:
                    throw new InvalidOperationException("Unknown logical order type: " + logical.Type);

            }
            missingLevels.Clear();
            var levelSize = logical.Levels == 1 ? logical.Position : logical.LevelSize;
            var logicalPosition = logical.Position;
            var level = logical.Levels - 1;
            for (var i = 0; i < logical.Levels; i++, level --, logicalPosition -= levelSize)
            {
                var size = Math.Min(logicalPosition,levelSize) ;
                if( size == 0) break;
                var levelPrice = price + sign*minimumTick*logical.LevelIncrement*level;
                // Find a match.
                var matched = false;
                for (var j = 0; j < matches.Count; j++)
                {
                    var physical = matches[j];
                    if (physical.Price.ToLong() != levelPrice) continue;
                    onMatchCallback(logical, physical, size, levelPrice.ToDouble());
                    matches.RemoveAt(j);
                    matched = true;
                    break;
                }
                if (!matched)
                {
                    missingLevels.Add(new MissingLevel { Price = levelPrice, Size = size });
                }
            }
            for (var i = 0; i < matches.Count; i++)
            {
                var physical = matches[i];
                if( missingLevels.Count > 0)
                {
                    var missingLevel = missingLevels[0];
                    onMatchCallback(logical, physical, missingLevel.Size, missingLevel.Price.ToDouble());
                    missingLevels.RemoveAt(0);
                }
                else
                {
                    ProcessExtraPhysical(physical);
                }

            }
            for (var i = 0; i < missingLevels.Count; i++ ) 
            {
                var missingLevel = missingLevels[i];
                ProcessMissingPhysical(logical, missingLevel.Size, missingLevel.Price.ToDouble());
            }
        }

        private void ProcessMatchPhysicalChange(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            MatchLogicalToPhysicals(logical, matches, ProcessMatchPhysicalChange);
        }

        private void ProcessMatchPhysicalChange(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
			var strategyPosition = logical.StrategyPosition;
			var logicalPosition = 
				logical.Type == OrderType.BuyLimit ||
				logical.Type == OrderType.BuyMarket ||
				logical.Type == OrderType.BuyStop ? 
				position : - position;
			logicalPosition += strategyPosition;
			var physicalPosition = 
				createOrChange.Side == OrderSide.Buy ?
				createOrChange.Size : - createOrChange.Size;
			var delta = logicalPosition - strategyPosition;
			var difference = delta - physicalPosition;
			if( debug) log.Debug("PhysicalChange("+logical.SerialNumber+") delta="+delta+", strategyPosition="+strategyPosition+", difference="+difference);
//			if( delta == 0 || strategyPosition == 0) {
			if( delta == 0) {
				if( debug) log.Debug("(Delta=0) Canceling: " + createOrChange);
				TryCancelBrokerOrder(createOrChange);
			} else if( difference != 0) {
				var origBrokerOrder = createOrChange.BrokerOrder;
				if( delta > 0) {
                    var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, OrderSide.Buy, Math.Abs(delta), price);
					if( debug) log.Debug("(Delta) Changing " + origBrokerOrder + " to " + changeOrder);
                    TryChangeBrokerOrder(changeOrder, createOrChange);
                }
                else
                {
					OrderSide side;
					if( strategyPosition > 0 && logicalPosition < 0) {
						side = OrderSide.Sell;
						delta = strategyPosition;
						if( delta == createOrChange.Size) {
							if( debug) log.Debug("Delta same as size: Check Price and Side.");
							ProcessMatchPhysicalChangePriceAndSide(logical,createOrChange,delta,price);
							return;
						}
					} else {
						side = OrderSide.SellShort;
					}
					side = (long) strategyPosition >= (long) Math.Abs(delta) ? OrderSide.Sell : OrderSide.SellShort;
					if( side == createOrChange.Side) {
                        var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, Math.Abs(delta), price);
						if( debug) log.Debug("(Size) Changing " + origBrokerOrder + " to " + changeOrder);
                        TryChangeBrokerOrder(changeOrder, createOrChange);
                    }
                    else
                    {
						if( debug) log.Debug("(Side) Canceling " + createOrChange);
						TryCancelBrokerOrder(createOrChange);
					}
				}
			} else {
				ProcessMatchPhysicalChangePriceAndSide(logical,createOrChange,delta,price);
			}
		}
		
		private void ProcessMatchPhysicalChangePriceAndSide(LogicalOrder logical, CreateOrChangeOrder createOrChange, int delta, double price) {
			if( price.ToLong() != createOrChange.Price.ToLong()) {
				var origBrokerOrder = createOrChange.BrokerOrder;
				physicalOrders.Remove(createOrChange);
				var side = GetOrderSide(logical.Type);
				if( side == createOrChange.Side) {
                    var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, Math.Abs(delta), price);
					if( debug) log.Debug("(Price) Changing " + origBrokerOrder + " to " + changeOrder);
                    TryChangeBrokerOrder(changeOrder, createOrChange);
                }
                else
                {
					if( debug) log.Debug("(Price) Canceling wrong side" + createOrChange);
					TryCancelBrokerOrder(createOrChange);
				}
			} else {
				VerifySide( logical, createOrChange, price);
			}
		}

        private void ProcessMatchPhysicalExit(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            if (matches.Count != 1)
            {
                throw new ApplicationException("Expected 1 match but found " + matches.Count +
                                               " matches for logical order: " + logical + "\n" + ToString(matches));
            }
            var physical = matches[0];
            ProcessMatchPhysicalExit(logical,physical,logical.Position,logical.Price);
            return;
        }

        private void ProcessMatchPhysicalExit(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
			var strategyPosition = logical.StrategyPosition;
			if( strategyPosition == 0) {
				TryCancelBrokerOrder(createOrChange);
			} else if( Math.Abs(strategyPosition) != createOrChange.Size || price.ToLong() != createOrChange.Price.ToLong()) {
				var origBrokerOrder = createOrChange.BrokerOrder;
				physicalOrders.Remove(createOrChange);
				var side = GetOrderSide(logical.Type);
                var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, Math.Abs(strategyPosition), price);
                TryChangeBrokerOrder(changeOrder, createOrChange);
            }
            else
            {
				VerifySide( logical, createOrChange, price);
			}
		}

        private void ProcessMatchPhysicalExitStrategy(LogicalOrder logical, List<CreateOrChangeOrder> matches)
        {
            if( logical.Levels == 1) {
                if (matches.Count != 1)
                {
                    throw new ApplicationException("Expected 1 match but found " + matches.Count +
                                                   " matches for logical order: " + logical + "\n" + ToString(matches));
                }
                var physical = matches[0];
                ProcessMatchPhysicalExitStrategy(logical,physical,logical.Position,logical.Price);
                return;
            }
            MatchLogicalToPhysicals(logical, matches, ProcessMatchPhysicalExitStrategy);
        }

        private void ProcessMatchPhysicalExitStrategy(LogicalOrder logical, CreateOrChangeOrder createOrChange, int position, double price)
        {
			var strategyPosition = logical.StrategyPosition;
			if( strategyPosition == 0) {
				TryCancelBrokerOrder(createOrChange);
			} else if( Math.Abs(strategyPosition) != createOrChange.Size || price.ToLong() != createOrChange.Price.ToLong()) {
				var origBrokerOrder = createOrChange.BrokerOrder;
				physicalOrders.Remove(createOrChange);
				var side = GetOrderSide(logical.Type);
                var changeOrder = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, Math.Abs(strategyPosition), price);
                TryChangeBrokerOrder(changeOrder, changeOrder);
            }
            else
            {
				VerifySide( logical, createOrChange, price);
			}
		}
		
		private void ProcessMatch(LogicalOrder logical, List<CreateOrChangeOrder> matches) {
			if( trace) log.Trace("Process Match()");
			switch( logical.TradeDirection) {
				case TradeDirection.Entry:
					ProcessMatchPhysicalEntry( logical, matches);
					break;
				case TradeDirection.Exit:
					ProcessMatchPhysicalExit( logical, matches);
					break;
				case TradeDirection.ExitStrategy:
					ProcessMatchPhysicalExitStrategy( logical, matches);
					break;
				case TradeDirection.Reverse:
					ProcessMatchPhysicalReverse( logical, matches);
					break;
				case TradeDirection.Change:
					ProcessMatchPhysicalChange( logical, matches);
					break;
				default:
					throw new ApplicationException("Unknown TradeDirection: " + logical.TradeDirection);
			}
		}

		private void VerifySide( LogicalOrder logical, CreateOrChangeOrder createOrChange, double price) {
			var side = GetOrderSide(logical.Type);
			if( createOrChange.Side != side) {
                if (debug) log.Debug("Canceling because " + createOrChange.Side + " != " + side + ": " + createOrChange);
				TryCancelBrokerOrder(createOrChange);
                createOrChange = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, createOrChange.Size, price);
				TryCreateBrokerOrder(createOrChange);
			}
		}
		
		private void ProcessExtraLogical(LogicalOrder logical) {
			// When flat, allow entry orders.
			switch(logical.TradeDirection) {
				case TradeDirection.Entry:
					if( logical.StrategyPosition == 0) {
						ProcessMissingPhysical(logical);
					}
					break;
				case TradeDirection.Exit:
				case TradeDirection.ExitStrategy:
					if( logical.StrategyPosition != 0) {
						ProcessMissingPhysical(logical);
					}
					break;
				case TradeDirection.Reverse:
					ProcessMissingPhysical(logical);
					break;
				case TradeDirection.Change:
					ProcessMissingPhysical(logical);
					break;
				default:
					throw new ApplicationException("Unknown trade direction: " + logical.TradeDirection);
			}
		}

        private void ProcessMissingPhysical(LogicalOrder logical)
        {
            if( logical.Levels == 1)
            {
                ProcessMissingPhysical(logical, logical.Position, logical.Price);
                return;
            }
            var price = logical.Price.ToLong();
            var sign = 1;
            switch( logical.Type)
            {
                case OrderType.BuyMarket:
                case OrderType.SellMarket:
                    ProcessMissingPhysical(logical, logical.Position, logical.Price);
                    return;
                case OrderType.BuyLimit:
                case OrderType.SellStop:
                    sign = -1;
                    break;
                case OrderType.SellLimit:
                case OrderType.BuyStop:
                    break;
                default:
                    throw new InvalidOperationException("Unknown logical order type: " + logical.Type);

            }
            var logicalPosition = logical.Position;
            var level = sign > 0 ? logical.Levels-1 : 0;
            for( var i=0; i< logical.Levels; i++, level-=sign)
            {
                var size = Math.Min(logical.LevelSize, logicalPosition);
                var levelPrice = price + sign * minimumTick * logical.LevelIncrement * level;
                ProcessMissingPhysical(logical, size, levelPrice.ToDouble());
                logicalPosition -= logical.LevelSize;
            }
        }

        private void ProcessMissingPhysical(LogicalOrder logical, int position, double price) {
			switch(logical.TradeDirection) {
				case TradeDirection.Entry:
					if(debug) log.Debug("ProcessMissingPhysicalEntry("+logical+")");
					var side = GetOrderSide(logical.Type);
                    var physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, position, price);
					TryCreateBrokerOrder(physical);
					break;
				case TradeDirection.Exit:
				case TradeDirection.ExitStrategy:
					var size = Math.Abs(logical.StrategyPosition);
					ProcessMissingExit( logical, size, price);
					break;
				case TradeDirection.Reverse:
					var logicalPosition =
						logical.Type == OrderType.BuyLimit ||
						logical.Type == OrderType.BuyMarket ||
						logical.Type == OrderType.BuyStop ?
						position : - position;
					size = Math.Abs(logicalPosition - logical.StrategyPosition);
                    if( size != 0 && Math.Sign(logicalPosition) != Math.Sign(logical.StrategyPosition)) {
						ProcessMissingReverse( logical, size, price);
                    }
					break;
				case TradeDirection.Change:
					logicalPosition = 
						logical.Type == OrderType.BuyLimit ||
						logical.Type == OrderType.BuyMarket ||
						logical.Type == OrderType.BuyStop ?
						position : - position;
					logicalPosition += logical.StrategyPosition;
					size = Math.Abs(logicalPosition - logical.StrategyPosition);
					if( size != 0) {
						if(debug) log.Debug("ProcessMissingPhysical("+logical+")");
						side = GetOrderSide(logical.Type);
                        physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, size, price);
						TryCreateBrokerOrder(physical);
					}
					break;
				default:
					throw new ApplicationException("Unknown trade direction: " + logical.TradeDirection);
			}
		}

        private void ProcessMissingReverse(LogicalOrder logical, int size, double price)
        {
            if (debug) log.Debug("ProcessMissingPhysical(" + logical + ")");
            var side = GetOrderSide(logical.Type);
            var physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, size, price);
            TryCreateBrokerOrder(physical);
        }

        private void ProcessMissingExit(LogicalOrder logical, int size, double price)
        {
			if( logical.StrategyPosition > 0) {
				if( logical.Type == OrderType.SellLimit ||
				  logical.Type == OrderType.SellStop ||
				  logical.Type == OrderType.SellMarket) {
					if(debug) log.Debug("ProcessMissingPhysical("+logical+")");
					var side = GetOrderSide(logical.Type);
                    var physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, size, price);
					TryCreateBrokerOrder(physical);
				}
			}
			if( logical.StrategyPosition < 0) {
				if( logical.Type == OrderType.BuyLimit ||
				  logical.Type == OrderType.BuyStop ||
				  logical.Type == OrderType.BuyMarket) {
					if(debug) log.Debug("ProcessMissingPhysical("+logical+")");
					var side = GetOrderSide(logical.Type);
                    var physical = new CreateOrChangeOrderDefault(OrderState.Active, symbol, logical, side, size, price);
					TryCreateBrokerOrder(physical);
				}
			}
		}

        private bool CheckFilledOrder(LogicalOrder logical, int position)
        {
            switch (logical.Type)
            {
                case OrderType.BuyLimit:
                case OrderType.BuyMarket:
                case OrderType.BuyStop:
                    if (logical.TradeDirection == TradeDirection.Change)
                    {
                        return position >= logical.Position + logical.StrategyPosition;
                    }
                    else
                    {
                        return position >= logical.Position;
                    }
                case OrderType.SellLimit:
                case OrderType.SellMarket:
                case OrderType.SellStop:
                    if (logical.TradeDirection == TradeDirection.Change)
                    {
                        return position <= -logical.Position + logical.StrategyPosition;
                    }
                    else
                    {
                        return position <= -logical.Position;
                    }
                default:
                    throw new ApplicationException("Unknown OrderType: " + logical.Type);
            }
        }
		
		private OrderSide GetOrderSide(OrderType type) {
			switch( type) {
				case OrderType.BuyLimit:
				case OrderType.BuyMarket:
				case OrderType.BuyStop:
					return OrderSide.Buy;
				case OrderType.SellLimit:
				case OrderType.SellMarket:
				case OrderType.SellStop:
					if( actualPosition > 0) {
						return OrderSide.Sell;
					} else {
						return OrderSide.SellShort;
					}
				default:
					throw new ApplicationException("Unknown OrderType: " + type);
			}
		}
		
		private bool ProcessExtraPhysical(CreateOrChangeOrder createOrChange) {
			return TryCancelBrokerOrder( createOrChange);
		}
		
		private int FindPendingAdjustments() {
			var positionDelta = desiredPosition - actualPosition;
			var pendingAdjustments = 0;

            originalPhysicals.Clear();
            originalPhysicals.AddLast(physicalOrderHandler.GetActiveOrders(symbol));
            originalPhysicals.AddLast(physicalOrderCache.CreateOrderQueue);

			var next = originalPhysicals.First;
			for( var node = next; node != null; node = next) {
				next = node.Next;
				CreateOrChangeOrder order = node.Value;
				if(order.Type != OrderType.BuyMarket &&
				   order.Type != OrderType.SellMarket) {
					continue;
				}
				if( order.LogicalOrderId == 0) {
					if( order.Type == OrderType.BuyMarket) {
						pendingAdjustments += order.Size;
					}
					if( order.Type == OrderType.SellMarket) {
						pendingAdjustments -= order.Size;
					}
					if( positionDelta > 0) {
						if( pendingAdjustments > positionDelta) {
							TryCancelBrokerOrder(order);
							pendingAdjustments -= order.Size;
						} else if( pendingAdjustments < 0) {
							TryCancelBrokerOrder(order);
							pendingAdjustments += order.Size;
						}
					}
					if( positionDelta < 0) {
						if( pendingAdjustments < positionDelta) {
							TryCancelBrokerOrder(order);
							pendingAdjustments += order.Size;
						} else if( pendingAdjustments > 0) {
							TryCancelBrokerOrder(order);
							pendingAdjustments -= order.Size;
						}
					}
					if( positionDelta == 0) {
						TryCancelBrokerOrder(order);
						pendingAdjustments += order.Type == OrderType.SellMarket ? order.Size : -order.Size;
					}
					physicalOrders.Remove(order);
				}
			}
			return pendingAdjustments;
		}

        public void TrySyncPosition(Iterable<StrategyPosition> strategyPositions)
        {
            if (isPositionSynced)
            {
                if (debug) log.Debug("TrySyncPosition() ignore. Position already synced.");
                return;
            }
            logicalOrderCache.SyncPositions(strategyPositions);
            SyncPosition();
        }

	    private void SyncPosition()
        {
            // Find any pending adjustments.
            var pendingAdjustments = FindPendingAdjustments();
            var positionDelta = desiredPosition - actualPosition;
			var delta = positionDelta - pendingAdjustments;
			CreateOrChangeOrder createOrChange;
            if( delta != 0)
            {
                log.Notice("SyncPositionInternal() Issuing adjustment order because expected position is " + desiredPosition + " but actual is " + actualPosition + " plus pending adjustments " + pendingAdjustments);
                if (debug) log.Debug("TrySyncPosition - " + tickSync);
            }
            else
            {
                IsPositionSynced = true;
                log.Notice("SyncPositionInternal() found position currently synced. With expected " + desiredPosition + " and actual " + actualPosition + " plus pending adjustments " + pendingAdjustments);
            }
			if( delta > 0) {
				createOrChange = new CreateOrChangeOrderDefault(OrderAction.Create, OrderState.Active, symbol,OrderSide.Buy,OrderType.BuyMarket,0,delta,0,0,null,null,default(TimeStamp));
                log.Info("Sending adjustment order to position: " + createOrChange);
                TryCreateBrokerOrder(createOrChange);
                if (SyncTicks.Enabled)
                {
                    tickSync.AddProcessPhysicalOrders();
                }
            }
            else if (delta < 0)
            {
                OrderSide side;
				if( actualPosition > 0 && desiredPosition < 0) {
					side = OrderSide.Sell;
					delta = actualPosition;
				} else {
					side = OrderSide.SellShort;
				}
				side = actualPosition >= Math.Abs(delta) ? OrderSide.Sell : OrderSide.SellShort;
                createOrChange = new CreateOrChangeOrderDefault(OrderAction.Create, OrderState.Active, symbol, side, OrderType.SellMarket, 0, Math.Abs(delta), 0, 0, null, null,default(TimeStamp));
                log.Info("Sending adjustment order to correct position: " + createOrChange);
                TryCreateBrokerOrder(createOrChange);
                if( SyncTicks.Enabled)
                {
                    tickSync.AddProcessPhysicalOrders();
                }
            }
        }

        public void SetLogicalOrders(Iterable<LogicalOrder> inputLogicals, Iterable<StrategyPosition> strategyPositions)
        {
			if( trace) {
				int count = originalLogicals == null ? 0 : originalLogicals.Count;
				log.Trace("SetLogicalOrders() order count = " + count);
			}
            if (CheckForFilledOrders(inputLogicals))
            {
                if (debug) log.Debug("Found already filled orders in position change event. Ignoring until recent fills get posted.");
                return;
            }
            logicalOrderCache.SetActiveOrders(inputLogicals);
			using( bufferedLogicalsLocker.Using()) {
				bufferedLogicals.Clear();
				bufferedLogicals.AddLast(logicalOrderCache.ActiveOrders);
			    canceledLogicals.AddLast(logicalOrderCache.ActiveOrders);
			    bufferedLogicalsChanged = true;
                if( debug) log.Debug("SetLogicalOrders( logicals " + bufferedLogicals.Count + ", strategy positions " + strategyPositions.Count);
			}
		}
		
		public void SetDesiredPosition(	int position) {
			this.desiredPosition = position;
		}
		
		private bool CheckForPending() {
			var result = false;
		    var next = originalPhysicals.First;
		    for (var current = next; current != null; current = next)
		    {
		        next = current.Next;
		        var order = current.Value;
				if( order.OrderState == OrderState.Pending ||
				    order.Type == OrderType.BuyMarket ||
				    order.Type == OrderType.SellMarket) {
					if( debug) log.Debug("Pending order: " + order);
					result = true;	
				}
			}
			return result;
		}
		
		private LogicalOrder FindLogicalOrder(long serialNumber) {
		    for (var current = originalLogicals.First; current != null; current = current.Next)
		    {
		        var order = current.Value;
				if( order.SerialNumber == serialNumber) {
					return order;
				}
			}
            for (var current = canceledLogicals.Last; current != null; current = current.Previous)
            {
                var order = current.Value;
				if( order.SerialNumber == serialNumber) {
                    return order;
                }
            }
            while( canceledLogicals.Count > 20)
            {
                canceledLogicals.RemoveFirst();
            }
            throw new ApplicationException("LogicalOrder was not found for order serial number: " + serialNumber);
		}

        private void TryCleanCanceledLogicals()
        {
            //if( canceledLogicals.Count > 100)
            //{
            //    canceledLogicals.RemoveFirst();
            //}
        }
		
		public void ProcessFill( PhysicalFill physical, int totalSize, int cumulativeSize, int remainingSize) {
            if (!isPositionSynced)
            {
                if (debug) log.Debug("ProcessFill() ignored. Position not yet synced.");
                return;
            }
            if (debug) log.Debug("ProcessFill() physical: " + physical);
		    var beforePosition = actualPosition;
            actualPosition += physical.Size;
            if( debug) log.Debug("Updating actual position from " + beforePosition + " to " + actualPosition + " from fill size " + physical.Size);
			var isCompletePhysicalFill = remainingSize == 0;
			if( isCompletePhysicalFill) {
				if( debug) log.Debug("Physical order completely filled: " + physical.Order);
				originalPhysicals.Remove(physical.Order);
                physicalOrders.Remove(physical.Order);
                if (physical.Order.ReplacedBy != null)
                {
                    originalPhysicals.Remove(physical.Order.ReplacedBy);
                    physicalOrders.Remove(physical.Order.ReplacedBy);
                }
            }
            else
            {
				if( debug) log.Debug("Physical order partially filled: " + physical.Order);
			}
			LogicalFillBinary fill;
			try { 
				var logical = FindLogicalOrder(physical.Order.LogicalSerialNumber);
				desiredPosition += physical.Size;
				if( debug) log.Debug("Adjusting symbol position to desired " + desiredPosition + ", physical fill was " + physical.Size);
				var position = logical.StrategyPosition + physical.Size;
				if( debug) log.Debug("Creating logical fill with position " + position + " from strategy position " + logical.StrategyPosition);
                var strategyPosition = (StrategyPositionDefault)logical.Strategy;
				fill = new LogicalFillBinary(
					position, strategyPosition.Recency+1, physical.Price, physical.Time, physical.UtcTime, physical.Order.LogicalOrderId, physical.Order.LogicalSerialNumber,logical.Position,physical.IsSimulated);
			} catch( ApplicationException ex) {
                log.Warn("Leaving symbol position at desired " + desiredPosition + ", since this appears to be an adjustment market order: " + physical.Order);
                if (debug) log.Debug("Skipping logical fill for an adjustment market order.");
				if( debug) log.Debug("Performing extra compare.");
				PerformCompareProtected();
				TryRemovePhysicalFill(physical);
				return;
			}
			if( debug) log.Debug("Fill price: " + fill);
			ProcessFill( fill, isCompletePhysicalFill);
		}		

        private TaskLock performCompareLocker = new TaskLock();
		private void PerformCompareProtected() {
			var count = Interlocked.Increment(ref recursiveCounter);
			if( count == 1) {
				while( recursiveCounter > 0) {
					Interlocked.Exchange( ref recursiveCounter, 1);
					try
					{
                        if (!isPositionSynced)
                        {
                            SyncPosition();
                        }
                        // Is it still not synced?
                        if (!isPositionSynced)
                        {
                            var extra = SyncTicks.Enabled ? tickSync.ToString() : "";
                            if (debug) log.Debug("PerformCompare ignored. Position not yet synced. " + extra);
                            return;
                        }
                        PerformCompareInternal();
                        physicalOrderHandler.ProcessOrders();
                        physicalOrderCache.Clear();
                        if( SyncTicks.Enabled)
                        {
                            tickSync.RollbackPhysicalOrders();
                            tickSync.RollbackPositionChange();
                            tickSync.RollbackProcessPhysicalOrders();
                            tickSync.RollbackPhysicalFills();
                        }
                        if (trace) log.Trace("PerformCompare finished - " + tickSync);
                    }
                    finally {
						Interlocked.Decrement( ref recursiveCounter);
                    }
				}
			}
		}
		private long nextOrderId = 1000000000;
		private bool useTimeStampId = true;
		private long GetUniqueOrderId() {
			if( useTimeStampId) {
				return TimeStamp.UtcNow.Internal;
			} else {
				return Interlocked.Increment(ref nextOrderId);
			}
		}
		
		private void TryRemovePhysicalFill(PhysicalFill fill) {
			if( SyncTicks.Enabled) tickSync.RemovePhysicalFill(fill);
		}
		
		private void ProcessFill( LogicalFillBinary fill, bool isCompletePhysicalFill) {
			if( debug) log.Debug( "ProcessFill() logical: " + fill );
			int orderId = fill.OrderId;
			if( orderId == 0) {
				// This is an adjust-to-position market order.
				// Position gets set via SetPosition instead.
				return;
			}
			
			var filledOrder = FindLogicalOrder( fill.OrderSerialNumber);
			if( debug) log.Debug( "Matched fill with order: " + filledOrder);

            var strategyPosition = filledOrder.StrategyPosition;
            var orderPosition =
                filledOrder.Type == OrderType.BuyLimit ||
                filledOrder.Type == OrderType.BuyMarket ||
                filledOrder.Type == OrderType.BuyStop ?
                filledOrder.Position : -filledOrder.Position;
            if (filledOrder.TradeDirection == TradeDirection.Change)
            {
				if( debug) log.Debug("Change order fill = " + orderPosition + ", strategy = " + strategyPosition + ", fill = " + fill.Position);
				fill.IsComplete = orderPosition + strategyPosition == fill.Position;
                var change = fill.Position - strategyPosition;
                filledOrder.Position = Math.Abs(orderPosition - change);
                if (debug) log.Debug("Changing order to position: " + filledOrder.Position);
            }
            else
            {
                fill.IsComplete = CheckFilledOrder(filledOrder, fill.Position);
            }
			if( fill.IsComplete)
			{
                if (debug) log.Debug("LogicalOrder is completely filled.");
			    MarkAsFilled(filledOrder);
			}
			UpdateOrderCache(filledOrder, fill);
            if (isCompletePhysicalFill && !fill.IsComplete)
            {
                if (filledOrder.TradeDirection == TradeDirection.Entry && fill.Position == 0)
                {
                    if (debug) log.Debug("Found a entry order which flattened the position. Likely due to bracketed entries that both get filled: " + filledOrder);
                    MarkAsFilled(filledOrder);
                }
                else 
                {
                    if (debug) log.Debug("Found complete physical fill but incomplete logical fill. Physical orders...");
                    var matches = TryMatchId(originalPhysicals, filledOrder, false);
                    if( matches.Count > 0)
                    {
                        ProcessMatch(filledOrder, matches);
                    }
                    else
                    {
                        ProcessMissingPhysical(filledOrder);
                    }
                }
			}
            if (onProcessFill != null)
            {
                if (debug) log.Debug("Sending logical fill for " + symbol + ": " + fill);
                onProcessFill(symbol, fill);
            }
            if (fill.IsComplete || isCompletePhysicalFill)
            {
				if( debug) log.Debug("Performing extra compare.");
				PerformCompareProtected();
			}
        }

        private void MarkAsFilled(LogicalOrder filledOrder)
        {
            try
            {
                if (debug) log.Debug("Marking order id " + filledOrder.Id + " as completely filled.");
                filledOrders.Add(filledOrder.SerialNumber, TimeStamp.UtcNow.Internal);
                originalLogicals.Remove(filledOrder);
                CleanupAfterFill(filledOrder);
            }
            catch (ApplicationException)
            {

            }
            catch (ArgumentException ex)
            {
                log.Warn(ex.Message + " Was the order already marked as filled? : " + filledOrder);
            }
        }

        private void CancelLogical(LogicalOrder order)
        {
            originalLogicals.Remove(order);
        }


		private void CleanupAfterFill(LogicalOrder filledOrder) {
			bool clean = false;
			bool cancelAllEntries = false;
			bool cancelAllExits = false;
			bool cancelAllExitStrategies = false;
			bool cancelAllReverse = false;
			bool cancelAllChanges = false;
			if( filledOrder.StrategyPosition == 0) {
				cancelAllChanges = true;
				clean = true;
			}
			switch( filledOrder.TradeDirection) {
				case TradeDirection.Change:
					break;
				case TradeDirection.Entry:
					cancelAllEntries = true;
					clean = true;
					break;
				case TradeDirection.Exit:
				case TradeDirection.ExitStrategy:
					cancelAllExits = true;
					cancelAllExitStrategies = true;
					cancelAllChanges = true;
					clean = true;
					break;
				case TradeDirection.Reverse:
					cancelAllReverse = true;
					clean = true;
					break;
				default:
					throw new ApplicationException("Unknown trade direction: " + filledOrder.TradeDirection);
			}
			if( clean) {
                TryCleanCanceledLogicals();
			    var next = logicalOrders.First;
			    for (var current = next; current != null; current = next)
			    {
			        next = current.Next;
			        var order = current.Value;
					if( order.StrategyId == filledOrder.StrategyId) {
						switch( order.TradeDirection) {
							case TradeDirection.Entry:
								if( cancelAllEntries) CancelLogical(order);
								break;
							case TradeDirection.Change:
                                if (cancelAllChanges) CancelLogical(order);
								break;
							case TradeDirection.Exit:
                                if (cancelAllExits) CancelLogical(order);
								break;
							case TradeDirection.ExitStrategy:
                                if (cancelAllExitStrategies) CancelLogical(order);
								break;
							case TradeDirection.Reverse:
                                if (cancelAllReverse) CancelLogical(order);
								break;
							default:
								throw new ApplicationException("Unknown trade direction: " + filledOrder.TradeDirection);
						}
					}
				}
			}
		}
	
		private void UpdateOrderCache(LogicalOrder order, LogicalFill fill) {
			var strategyPosition = (StrategyPositionDefault) order.Strategy;
            if( debug) log.Debug("Adjusting strategy position to " + fill.Position + " from " + strategyPosition.ActualPosition + ". Recency " + fill.Recency + " for strategy id " + strategyPosition.Id);
            strategyPosition.TrySetPosition(fill.Position, fill.Recency);
//			orderCache.RemoveInactive(order);
		}
		
		public int ProcessOrders() {
            sentPhysicalOrders = 0;
			PerformCompareProtected();
            return sentPhysicalOrders;
		}

		private bool CheckForFilledOrders(Iterable<LogicalOrder> orders) {
            if( orders == null) return false;
		    var next = orders.First;
		    for (var current = next; current != null; current = next)
		    {
		        next = current.Next;
		        var logical = current.Value;
				var binaryTime = 0L;
				if( filledOrders.TryGetValue( logical.SerialNumber, out binaryTime)) {
					if( debug) log.Debug("Found already filled order: " + logical);
				   	return true;
				}
			}
			return false;
		}
		
		private int recursiveCounter;
		private void PerformCompareInternal() {
			if( debug)
			{
			    log.Debug("PerformCompare for " + symbol + " with " +
			              actualPosition + " actual " +
			              desiredPosition + " desired.");
			}
				
            originalPhysicals.Clear();
            originalPhysicals.AddLast(physicalOrderHandler.GetActiveOrders(symbol));
            originalPhysicals.AddLast(physicalOrderCache.CreateOrderQueue);

            if (CheckForPending())
            {
                if (debug) log.Debug("Found pending physical orders. Skipping compare.");
                return;
            }

            if( bufferedLogicalsChanged)
            {
                log.Debug("Buffered logicals were updated so refreshing original logicals list ...");
                using (bufferedLogicalsLocker.Using())
                {
                    originalLogicals.Clear();
                    if (bufferedLogicals != null)
                    {
                        originalLogicals.AddLast(bufferedLogicals);
                    }
                    bufferedLogicalsChanged = false;
                }
            }

            if (debug)
            {
                log.Debug(originalLogicals.Count + " logicals, " + originalPhysicals.Count + " physicals.");
            }

            if (debug)
            {
                var next = originalLogicals.First;
                for (var node = next; node != null; node = node.Next)
                {
                    var order = node.Value;
                    log.Debug("Logical Order: " + order);
                }
            }

            if (debug)
            {
                var next = originalPhysicals.First;
                for (var node = next; node != null; node = node.Next)
                {
                    var order = node.Value;
                    log.Debug("Physical Order: " + order);
                }
            }
            logicalOrders.Clear();
			logicalOrders.AddLast(originalLogicals);
			
			physicalOrders.Clear();
			if(originalPhysicals != null) {
				physicalOrders.AddLast(originalPhysicals);
			}
			
			CreateOrChangeOrder createOrChange;
			extraLogicals.Clear();
			while( logicalOrders.Count > 0) {
				var logical = logicalOrders.First.Value;
			    var matches = TryMatchId(physicalOrders, logical, true);
                if( matches.Count > 0)
                {
                    ProcessMatch( logical, matches);
                }
                else
                {
                    extraLogicals.Add(logical);
				}
				logicalOrders.Remove(logical);
			}

			
			if( trace) log.Trace("Found " + physicalOrders.Count + " extra physicals.");
			int cancelCount = 0;
			while( physicalOrders.Count > 0) {
				createOrChange = physicalOrders.First.Value;
				if( ProcessExtraPhysical(createOrChange)) {
					cancelCount++;
				}
				physicalOrders.Remove(createOrChange);
			}
			
			if( cancelCount > 0) {
				// Wait for cancels to complete before creating any orders.
				return;
			}

			if( trace) log.Trace("Found " + extraLogicals.Count + " extra logicals.");
			while( extraLogicals.Count > 0) {
				var logical = extraLogicals[0];
				ProcessExtraLogical(logical);
				extraLogicals.Remove(logical);
			}
		}
	
		public int ActualPosition {
			get { return actualPosition; }
		}

		public void SetActualPosition( int position)
		{
		    var value = Interlocked.Exchange(ref actualPosition, position);
            if (debug) log.Debug("SetActualPosition(" + value + ")");
        }

        public void IncreaseActualPosition( int position)
        {
            var count = Math.Abs(position);
            var result = actualPosition;
            for( var i=0; i<count; i++)
            {
                if (position > 0)
                {
                    result = Interlocked.Increment(ref actualPosition);
                } else
                {
                    result = Interlocked.Decrement(ref actualPosition);
                }
            }
            if( debug) log.Debug("Changed actual postion to " + result);
        }

		public PhysicalOrderHandler PhysicalOrderHandler {
			get { return physicalOrderHandler; }
		}
		
		public Action<SymbolInfo,LogicalFillBinary> OnProcessFill {
			get { return onProcessFill; }
			set { onProcessFill = value; }
		}
		
		public bool HandleSimulatedExits {
			get { return handleSimulatedExits; }
			set { handleSimulatedExits = value; }
		}

	    public LogicalOrderCache LogicalOrderCache
	    {
	        get { return logicalOrderCache; }
	    }

	    public bool IsPositionSynced
	    {
	        get { return isPositionSynced; }
	        set { isPositionSynced = value; }
	    }

	    // This is a callback to confirm order was properly placed.
		public void OnChangeBrokerOrder(CreateOrChangeOrder order)
		{
			PerformCompareProtected();
			if( SyncTicks.Enabled) {
                tickSync.SetReprocessPhysicalOrders();
                tickSync.RemovePhysicalOrder(order);
            }
		}

        public bool HasBrokerOrder( CreateOrChangeOrder order)
        {
            return false;
        }
		
		public void OnCreateBrokerOrder(CreateOrChangeOrder order)
		{
			PerformCompareProtected();
            if( SyncTicks.Enabled) {
                tickSync.SetReprocessPhysicalOrders();
                tickSync.RemovePhysicalOrder(order);
            }
		}
		
		public void OnCancelBrokerOrder(CreateOrChangeOrder order)
		{
			PerformCompareProtected();
			if( SyncTicks.Enabled) {
                tickSync.SetReprocessPhysicalOrders();
                tickSync.RemovePhysicalOrder(order.OriginalOrder.BrokerOrder);
            }
		}
		
		public Iterable<CreateOrChangeOrder> GetActiveOrders(SymbolInfo symbol)
		{
			throw new NotImplementedException();
		}

    }
}
