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
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace TickZoom.Api
{
	public static class Diagnose
	{
        private static Log log = Factory.Log.GetLogger("TickZoom.Api.Diagnose");
        private static Dictionary<long, long> symbols;
	    public static readonly bool TraceTicks = false;
        private static TaskLock metricsLocker = new TaskLock();
        private static DiagnoseTicksMetric[] metrics = new DiagnoseTicksMetric[8];
	    private static DataSeries<DiagnoseTickEntry> tickLog = Factory.Engine.Series<DiagnoseTickEntry>();

        public struct DiagnoseTickEntry
        {
            public int MetricId;
            public TickBinary TickBinary;
        }

	    public static void Assert(bool condition, Func<object> logMessage)
        {
			if( !condition) {
				var message = logMessage();
				throw new InvalidOperationException("Debug assertion failed: " + message);
			}
		}

	    private static int nextMetricId = 0;

        public static void LogTicks(int quantity)
        {
            if (tickLog.Count > 0)
            {
                var sb = new StringBuilder();
                var tickIO = Factory.TickUtil.TickIO();
                for (var i = 0; i < tickLog.Count && i < quantity; i++)
                {
                    var entry = tickLog[i];
                    var metric = metrics[entry.MetricId - 1];
                    var label = metric.Name;
                    var tick = entry.TickBinary;
                    tickIO.Inject(tick);
                    sb.AppendLine(label + ": " + tick.Id + " " + tickIO.ToString() + " " + tickIO.Time.Microsecond.ToString("d3") + " " + Factory.Symbol.LookupSymbol(tick.Symbol));
                }
                if( sb.Length > 0)
                {
                    log.Info("Last ticks:\n" + sb.ToString());
                }
            }
        }

        public class DiagnoseTicksMetric
        {
            public int Id;
            public string Name;
            public bool Enabled;
        }

        public static int RegisterMetric(string metricName)
        {
            using( metricsLocker.Using())
            {
                for (int i = 0; i < nextMetricId; i++)
                {
                    if (metricName == metrics[i].Name)
                    {
                        return i + 1; // ids are 1 based.
                    }
                }
                // not found
                var metricId = Interlocked.Increment(ref nextMetricId);
                if (metricId > metrics.Length)
                {
                    Array.Resize(ref metrics, metrics.Length * 2);
                }
                var metric = metrics[metricId - 1] = new DiagnoseTicksMetric
                {
                    Id = metricId,
                    Name = metricName,
                    Enabled = true,
                };
                if (metric.Name.Contains("PoolTicks"))
                {
                    metric.Enabled = true;
                }
                return metricId;
            }
        }

        //public static void LogTicks(long symbol, int quantity)
        //{
        //    symbols = new Dictionary<long, long>();
        //    LogTicks(symbol, quantity);
        //    foreach( var kvp in symbols)
        //    {
        //        var otherSymbol = kvp.Value;
        //        if( otherSymbol == symbol) continue;
        //        for( var i=0; i<nextMetricId; i++)
        //        {
        //            var metric = metrics[i];
        //            LogTicks(otherSymbol, metric, quantity);
        //        }
        //    }
        //}

        public static void AddTick(int metricId, ref TickBinary binary)
        {
            var metric = metrics[metricId - 1];
            if( metric.Enabled)
            {
                tickLog.Add(new DiagnoseTickEntry { MetricId = metricId, TickBinary = binary});
            }
        }
	}
}