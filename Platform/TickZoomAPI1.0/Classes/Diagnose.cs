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
        public static void Assert(bool condition, Func<object> logMessage)
        {
			if( !condition) {
				var message = logMessage();
				throw new InvalidOperationException("Debug assertion failed: " + message);
			}
		}

        private static DataSeries<TickBinary> symbolHandlerTicks = Factory.Engine.Series<TickBinary>();
        private static DataSeries<TickBinary> simulatorTicks = Factory.Engine.Series<TickBinary>();
        private static DataSeries<TickBinary> quoteProviderTicks = Factory.Engine.Series<TickBinary>();

        public static void LogTicks(DataSeries<TickBinary> list, int quantity, string label)
        {
            if (list.Count > 0)
            {
                var sb = new StringBuilder();
                var tickIO = Factory.TickUtil.TickIO();
                for (int i = 0; i < list.Count && i < quantity; i++)
                {
                    tickIO.Inject(list[i]);
                    sb.AppendLine(tickIO.ToString() + " " + tickIO.Time.Microsecond.ToString("d3"));
                }
                log.Info("Last Ticks " + label + ":\n" + sb.ToString());
            }
        }

	    public static DataSeries<TickBinary> SymbolHandlerTicks
	    {
	        get { return symbolHandlerTicks; }
	    }

	    public static DataSeries<TickBinary> SimulatorTicks
	    {
	        get { return simulatorTicks; }
	    }

	    public static DataSeries<TickBinary> QuoteProviderTicks
	    {
	        get { return quoteProviderTicks; }
	    }
	}
}
