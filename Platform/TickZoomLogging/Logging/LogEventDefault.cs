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
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Drawing;

using log4net.Core;
using log4net.Filter;
using TickZoom.Api;

namespace TickZoom.Logging
{
	public class LogEventDefault : LogEvent {
		private bool isAudioAlarm;			
		public bool IsAudioAlarm {
			get { return isAudioAlarm; }
			set { isAudioAlarm = value; }
		}
		private Color color;					
		public Color Color {
			get { return color; }
			set { color = value; }
		}
		private string loggerName;
		public string LoggerName {
			get { return loggerName; }
			set { loggerName = value; }
		}
		public string message;
		public string Message {
			get { return message; }
			set { message = value; }
		}
	}
}
