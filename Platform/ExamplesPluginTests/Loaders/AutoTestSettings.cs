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
using TickZoom.Api;

namespace Loaders
{
	[Flags]
	public enum TestType {
		None = 0x00,
		Stats = 0x01,
		BarData = 0x02,
	}
	public struct AutoTestSettingsBinary {
		public TestType ignoreTests;
		public AutoTestMode mode;
		public string name;
		public ModelLoaderInterface loader;
		public string symbols;
		public bool storeKnownGood;
		public bool showCharts;
		public TimeStamp startTime;
		public TimeStamp endTime;
		public Elapsed relativeEndTime;
		public Interval intervalDefault;
		public IList<string> categories;
	}
	public class AutoTestSettings {
		AutoTestSettingsBinary binary;
		public AutoTestSettings() {
			binary.endTime = TimeStamp.MaxValue;
			binary.categories = new List<string>();
		}
		
		public AutoTestSettings(AutoTestSettingsBinary binary) {
			this.binary = binary;
		}
		
		public AutoTestSettings Copy() {
			return new AutoTestSettings( binary);
		}
		
		public IList<string> Categories {
			get { return binary.categories; }
			set { binary.categories = value; }
		}
		
		public TestType IgnoreTests {
			get { return binary.ignoreTests; }
			set { binary.ignoreTests = value; }
		}
		
		public AutoTestMode Mode {
			get { return binary.mode; }
			set { binary.mode = value; }
		}
		
		public string Name {
			get { return binary.name; }
			set { binary.name = value; }
		}
		
		public ModelLoaderInterface Loader {
			get { return binary.loader; }
			set { binary.loader = value; }
		}
		
		public string Symbols {
			get { return binary.symbols; }
			set { binary.symbols = value; }
		}
		
		public bool StoreKnownGood {
			get { return binary.storeKnownGood; }
			set { binary.storeKnownGood = value; }
		}
		
		public bool ShowCharts {
			get { return binary.showCharts; }
			set { binary.showCharts = value; }
		}
		
		public TimeStamp StartTime {
			get { return binary.startTime; }
			set { binary.startTime = value; }
		}
		
		public TimeStamp EndTime {
			get { return binary.endTime; }
			set { binary.endTime = value; }
		}
		
		public Interval IntervalDefault {
			get { return binary.intervalDefault; }
			set { binary.intervalDefault = value; }
		}
		
		public Elapsed RelativeEndTime {
			get { return binary.relativeEndTime; }
			set { binary.relativeEndTime = value; }
		}
	}
}
