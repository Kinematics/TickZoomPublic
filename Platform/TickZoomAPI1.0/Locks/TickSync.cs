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
using System.Threading;

namespace TickZoom.Api
{
    public struct TickSyncState
    {
        public int ticks;
        public int positionChange;
        public int processPhysical;
        public int reprocessPhysical;
        public int physicalFills;
        public int physicalOrders;
        public bool Compare(TickSyncState other)
        {
            return ticks == other.ticks && positionChange == other.positionChange &&
                   processPhysical == other.processPhysical && physicalFills == other.physicalFills &&
                   reprocessPhysical == other.reprocessPhysical && physicalOrders == other.physicalOrders;

        }
    }

    public class TickSync : SimpleLock
    {
        private static readonly Log staticLog = Factory.SysLog.GetLogger(typeof(TickSync));
        private readonly bool debug = staticLog.IsDebugEnabled;
        private readonly bool trace = staticLog.IsTraceEnabled;
        private Log log;
        private TickSyncState state;
        private SymbolInfo symbolInfo;
        private string symbol;
        private bool rollbackNeeded = false;

        internal TickSync(long symbolId)
        {
            this.symbolInfo = Factory.Symbol.LookupSymbol(symbolId);
            this.symbol = symbolInfo.Symbol.StripInvalidPathChars();
            this.log = Factory.SysLog.GetLogger(typeof(TickSync).FullName + "." + symbol);
            if (trace) log.Trace("created with binary symbol id = " + symbolId);
        }
        public bool Completed
        {
            get
            {
                var value = CheckCompletedInternal();
                return value;
            }
        }
        private bool CheckCompletedInternal()
        {
            return state.ticks == 0 && state.positionChange == 0 &&
                   state.physicalOrders == 0 && state.physicalFills == 0 &&
                   state.processPhysical == 0 && state.reprocessPhysical == 0;
        }
        private bool CheckOnlyProcessingOrders()
        {
            return state.positionChange == 0 && state.physicalOrders == 0 &&
                state.physicalFills == 0 && state.processPhysical > 0;
        }

        private bool CheckOnlyReprocessOrders()
        {
            return state.positionChange == 0 && state.physicalOrders == 0 &&
                state.physicalFills == 0 && state.reprocessPhysical > 0;
        }

        private bool CheckProcessingOrders()
        {
            return state.positionChange > 0 || state.physicalOrders > 0 || state.physicalFills > 0;
        }

        private TickSyncState rollback;

        public void CaptureState()
        {
            rollback = state;
        }

        public void Clear()
        {
            if (!CheckCompletedInternal())
            {
                System.Diagnostics.Debugger.Break();
                throw new ApplicationException("Tick, position changes, physical orders, and physical fills, must all complete before clearing the tick sync. Current numbers are: " + this);
            }
            ForceClear();
        }

        public void ForceClear()
        {
            var ticks = Interlocked.Exchange(ref state.ticks, 0);
            var orders = Interlocked.Exchange(ref state.physicalOrders, 0);
            var process = Interlocked.Exchange(ref state.processPhysical, 0);
            var reprocess = Interlocked.Exchange(ref state.reprocessPhysical, 0);
            var changes = Interlocked.Exchange(ref state.positionChange, 0);
            var fills = Interlocked.Exchange(ref state.physicalFills, 0);
            Unlock();
            if (trace) log.Trace("ForceClear() " + this);
        }

        public void ForceClearOrders()
        {
            var orders = Interlocked.Exchange(ref state.physicalOrders, 0);
            var changes = Interlocked.Exchange(ref state.positionChange, 0);
            var process = Interlocked.Exchange(ref state.processPhysical, 0);
            var reprocess = Interlocked.Exchange(ref state.reprocessPhysical, 0);
            var fills = Interlocked.Exchange(ref state.physicalFills, 0);
            if (trace) log.Trace("ForceClearOrders() " + this);
        }

        public override string ToString()
        {
            return ToString(state);
        }

        private string ToString(TickSyncState temp)
        {
            return "TickSync Ticks " + temp.ticks + ", Sent Orders " + temp.physicalOrders + ", Changes " + temp.positionChange + ", Process Orders " + temp.processPhysical + ", Reprocess " + temp.reprocessPhysical + ", Fills " + temp.physicalFills + ")";
        }

        public void AddTick()
        {
            var value = Interlocked.Increment(ref state.ticks);
            if (trace) log.Trace("AddTick(" + value + ")");
        }
        public void RemoveTick()
        {
            var value = Interlocked.Decrement(ref state.ticks);
            if (trace) log.Trace("RemoveTick(" + value + ")");
            if (value < 0)
            {
                Interlocked.Increment(ref state.ticks);
                if (trace) System.Diagnostics.Debugger.Break();
            }
        }

        public void AddPhysicalFill(PhysicalFill fill)
        {
            var value = Interlocked.Increment(ref state.physicalFills);
            RollbackPhysicalFills();
            if (trace) log.Trace("AddPhysicalFill(" + value + "," + fill + "," + fill.Order + ")");
        }

        public void RollbackPhysicalFills()
        {
            while (rollback.physicalFills > 0 && state.physicalFills > 0)
            {
                Interlocked.Decrement(ref state.physicalFills);
                Interlocked.Decrement(ref rollback.physicalFills);
            }
        }

        public void RemovePhysicalFill(object fill)
        {
            var value = Interlocked.Decrement(ref state.physicalFills);
            if (trace) log.Trace("RemovePhysicalFill(" + value + "," + fill + ")");
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref state.physicalFills);
                log.Warn("PhysicalFills counter was " + value + ". Incremented to " + temp);
                if (trace) System.Diagnostics.Debugger.Break();
            }
        }

        public void AddPhysicalOrder(CreateOrChangeOrder order)
        {
            var value = Interlocked.Increment(ref state.physicalOrders);
            RollbackPhysicalOrders();
            if (trace) log.Trace("AddPhysicalOrder(" + value + "," + order + ")");
        }

        public void RollbackPhysicalOrders()
        {
            while (rollback.physicalOrders > 0 && state.physicalOrders > 0)
            {
                Interlocked.Decrement(ref state.physicalOrders);
                Interlocked.Decrement(ref rollback.physicalOrders);
            }
        }
        public void RemovePhysicalOrder(object order)
        {
            var value = Interlocked.Decrement(ref state.physicalOrders);
            if (trace) log.Trace("RemovePhysicalOrder(" + value + "," + order + ")");
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref state.physicalOrders);
                log.Warn("PhysicalOrders counter was " + value + ". Incremented to " + temp);
                if (trace) System.Diagnostics.Debugger.Break();
            }
        }
        public void RemovePhysicalOrder()
        {
            var value = Interlocked.Decrement(ref state.physicalOrders);
            if (trace) log.Trace("RemovePhysicalOrder(" + value + ")");
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref state.physicalOrders);
                log.Warn("PhysicalOrders counter was " + value + ". Incremented to " + temp);
                if (trace) System.Diagnostics.Debugger.Break();
            }
        }
        public void AddPositionChange()
        {
            var value = Interlocked.Increment(ref state.positionChange);
            RollbackPositionChange();
            if (trace) log.Trace("AddPositionChange(" + value + ")");
        }

        public void RollbackPositionChange()
        {
            while (rollback.positionChange > 0 && state.positionChange > 0)
            {
                Interlocked.Decrement(ref state.positionChange);
                Interlocked.Decrement(ref rollback.positionChange);
            }
        }

        public void RemovePositionChange()
        {
            var value = Interlocked.Decrement(ref state.positionChange);
            if (trace) log.Trace("RemovePositionChange(" + value + ")");
            if (value < 0)
            {
                log.Warn("Completed: value below zero: " + state.positionChange);
            }
            if (value < 0)
            {
                var temp = Interlocked.Increment(ref state.positionChange);
                log.Warn("PositionChange counter was " + value + ". Incremented to " + temp);
                if (trace) System.Diagnostics.Debugger.Break();
            }
        }

        public void AddProcessPhysicalOrders()
        {
            var value = Interlocked.Increment(ref state.processPhysical);
            RollbackProcessPhysicalOrders();
            if (trace) log.Trace("AddProcessPhysicalOrders(" + value + ")");
        }

        public void RollbackProcessPhysicalOrders()
        {
            while (rollback.processPhysical > 0 && state.processPhysical >= 0)
            {
                Interlocked.Decrement(ref state.processPhysical);
                Interlocked.Decrement(ref rollback.processPhysical);
            }
        }

        public void RemoveProcessPhysicalOrders()
        {
            var value = Interlocked.Decrement(ref state.processPhysical);
            if (trace) log.Trace("RemoveProcessPhysicalOrders(" + value + ")");
            if (value < 0)
            {
                log.Warn("Completed: value below zero: " + state.processPhysical);
            }
            if (value < 0)
            {
                //var temp = Interlocked.Increment(ref state.processPhysical);
                //log.Warn("ProcessPhysical counter was " + value + ". Incremented to " + temp);
                if (trace) System.Diagnostics.Debugger.Break();
            }
        }

        public void SetReprocessPhysicalOrders()
        {
            if( state.reprocessPhysical == 0)
            {
                var value = Interlocked.Increment(ref state.reprocessPhysical);
                if (trace) log.Trace("SetReprocessPhysicalOrders(" + value + ")");
            }
        }

        public void AddReprocessPhysicalOrders()
        {
            var value = Interlocked.Increment(ref state.reprocessPhysical);
            if (trace) log.Trace("AddReprocessPhysicalOrders(" + value + ")");
        }

        public void RemoveReprocessPhysicalOrders()
        {
            var value = Interlocked.Decrement(ref state.reprocessPhysical);
            if (trace) log.Trace("RemoveReprocessPhysicalOrders(" + value + ")");
            if (value < 0)
            {
                log.Warn("Completed: value below zero: " + state.reprocessPhysical);
            }
            if (value < 0)
            {
                if (trace) System.Diagnostics.Debugger.Break();
            }
        }

        public bool SentPhysicalOrders
        {
            get { return state.physicalOrders > 0; }
        }

        public bool SentPhysicalFills
        {
            get { return state.physicalFills > 0; }
        }

        public bool SentPositionChange
        {
            get { return state.positionChange > 0; }
        }

        public bool OnlyReprocessPhysicalOrders
        {
            get { return CheckOnlyReprocessOrders(); }
        }

        public bool OnlyProcessPhysicalOrders
        {
            get { return CheckOnlyProcessingOrders(); }
        }

        public bool IsProcessingOrders
        {
            get { return CheckProcessingOrders(); }
        }

        public bool SentProcessPhysicalOrders
        {
            get { return state.processPhysical > 0; }
        }

        public int PhysicalFills
        {
            get { return state.physicalFills; }
        }

        public bool RollbackNeeded
        {
            get { return rollbackNeeded; }
            set { rollbackNeeded = value; }
        }
    }
}
