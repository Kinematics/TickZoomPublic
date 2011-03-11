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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;

using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Symbols
{
	/// <summary>
	/// Description of SymbolDictionary.
	/// </summary>
	public class SymbolDictionary : IEnumerable<SymbolProperties>
	{
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(SymbolDictionary));
		private readonly bool trace = log.IsTraceEnabled;
		private readonly bool debug = log.IsDebugEnabled;
		private static object locker = new object();
		private SymbolProperties @default;
		private List<SymbolCategory> categories = new List<SymbolCategory>();
		
		public SymbolDictionary()
		{
			@default = new SymbolProperties();
		}

		public static SymbolDictionary Create(string name, string defaultContents) {
			lock( locker) {
				string storageFolder = Factory.Settings["AppDataFolder"];
				string dictionaryPath = storageFolder + @"\Dictionary\"+name+".tzdict";
				Directory.CreateDirectory(Path.GetDirectoryName(dictionaryPath));
				SymbolDictionary dictionary;
				if( File.Exists(dictionaryPath) ) {
					using( StreamReader streamReader = new StreamReader(new FileStream(dictionaryPath,FileMode.Open,FileAccess.Read,FileShare.Read))) {
						dictionary = SymbolDictionary.Create( streamReader);
					}
					return dictionary;
				} else {
					string contents = BeautifyXML(defaultContents);
			        using (StreamWriter sw = new StreamWriter(dictionaryPath)) 
			        {
			            // Add some text to the file.
			            sw.Write( contents);
			        }
			        Thread.Sleep(1000);
					dictionary = SymbolDictionary.Create( new StreamReader(dictionaryPath));
				}
				return dictionary;
			}
		}
		
		public static SymbolDictionary Create(TextReader projectXML) {
			lock( locker) {
				SymbolDictionary project = new SymbolDictionary();
				project.Load(projectXML);
				return project;
			}
		}
		
		private static string BeautifyXML(string xml)
		{
			using( StringReader reader = new StringReader(xml)) {
				XmlDocument doc = new XmlDocument();
				doc.Load( reader);
			    StringBuilder sb = new StringBuilder();
			    XmlWriterSettings settings = new XmlWriterSettings();
			    settings.Indent = true;
			    settings.IndentChars = "  ";
			    settings.NewLineChars = "\r\n";
			    settings.NewLineHandling = NewLineHandling.Replace;
			    using( XmlWriter writer = XmlWriter.Create(sb, settings)) {
				    doc.Save(writer);
			    }
			    return sb.ToString();
			}
		}
			
		public void Load(TextReader projectXML) {
			
			XmlReaderSettings settings = new XmlReaderSettings();
			settings.IgnoreComments = true;
			settings.IgnoreWhitespace = true;
			
			using (XmlReader reader = XmlReader.Create(projectXML))
			{
				try {
					bool process = true;
					// Read nodes one at a time  
					while( process)  
					{  
						reader.Read();
					    // Print out info on node  
					    switch( reader.NodeType) {
					    	case XmlNodeType.Element:
					    		if( "category".Equals(reader.Name) ) {
				    				SymbolCategory category = new SymbolCategory();
						    		HandleCategory(category,reader);
						    		categories.Add(category);
					    		} else {
					    			Error(reader,"unexpected tag " + reader.Name );
					    		}
					    		projectXML.Close();
					    		process = false;
					    		break;
					    }
					}  				
				} catch( Exception ex) {
					Error( reader, ex.ToString());
					projectXML.Close();
				}
			}
			projectXML.Close();
			projectXML.Dispose();
		}
		
		private void HandleCategory(SymbolCategory category, XmlReader reader) {
			string tagName = reader.Name;
			category.Name = reader.GetAttribute("name");
			log.Debug("Handle category " + category.Name);
			if( reader.IsEmptyElement) { return; }
			log.Indent();
			while( reader.Read()) {
			    // Print out info on node  
			    switch( reader.NodeType) {
			    	case XmlNodeType.Element:
			    		if( "property".Equals(reader.Name) ) {
			    			string name = reader.GetAttribute("name");
			    			string value = reader.GetAttribute("value");
			    			HandleProperty(reader,category.Default, reader.GetAttribute("name"), reader.GetAttribute("value"));
			    			if( trace) log.Trace("Property " + name + " = " + value);
			    		} else if( "category".Equals(reader.Name)) {
			    			SymbolCategory subCategory = new SymbolCategory(category.Default.Copy());
			    			HandleCategory(subCategory,reader);
			    			category.Categories.Add(subCategory);
			    		} else if( "symbol".Equals(reader.Name)) {
			    			string name = reader.GetAttribute("name");
			    			string universal = reader.GetAttribute("universal");
			    			SymbolProperties symbol = category.Default.Copy();
		    				symbol.Symbol = name;
		    				if( universal != null) {
//		    					symbol.UniversalSymbol = universal;
		    				}
			    			HandleSymbol(symbol,reader);
			    			category.Symbols.Add(symbol);
			    		} else {
			    			Error(reader,"unexpected tag " + reader.Name );
			    		}
			    		break;
			    	case XmlNodeType.EndElement:
			    		if( tagName.Equals(reader.Name)) {
			    			log.Outdent();
				    		return;
			    		} else {
			    			Error(reader,"End of " + tagName + " was expected instead of end of " + reader.Name);
			    		}
			    		break;
			    } 
			}
			Error(reader,"Unexpected end of file");
			return;
		}
		
		
		private void HandleSymbol(object obj, XmlReader reader) {
			string tagName = reader.Name;
			if( trace) log.Trace("Handle " + obj.GetType().Name);
			if( reader.IsEmptyElement) { return; }			
			log.Indent();
			while( reader.Read()) {
			    // Print out info on node  
			    switch( reader.NodeType) {
			    	case XmlNodeType.Element:
			    		if( "property".Equals(reader.Name) ) {
			    			HandleProperty(reader,obj, reader.GetAttribute("name"), reader.GetAttribute("value"));
			    		} else {
			    			Error(reader,"End of " + tagName + " was expected instead of end of " + reader.Name);
			    		}
			    		break;
			    	case XmlNodeType.EndElement:
			    		if( tagName.Equals(reader.Name)) {
			    			log.Outdent();
				    		return;
			    		} else {
			    			Error(reader,"End of " + reader.Name + " tag in xml was unexpected");
			    		}
			    		break;
			    }
			}
			Error(reader,"Unexpected end of file");
		}
		
		private void HandleProperty( XmlReader reader, object obj, string name, string str) {
			PropertyInfo property = obj.GetType().GetProperty(name);
			if( property == null) {
				Warning(reader,obj.GetType() + " does not have the property: " + name);
				return;
			}
			Type propertyType = property.PropertyType;
			object value = TickZoom.Api.Converters.Convert(propertyType,str);
			property.SetValue(obj,value,null);
			if( trace) log.Trace("Property " + property.Name + " = " + value);
		}
		
		private void Error( XmlReader reader, string msg) {
			IXmlLineInfo lineInfo = reader as IXmlLineInfo;
			string lineStr = "";
			if( lineInfo != null) {
				lineStr += " on line " + lineInfo.LineNumber + " at position " + lineInfo.LinePosition;
			}
			log.Debug(msg + lineStr);
			throw new ApplicationException(msg + lineStr);
		}
		
		private void Warning( XmlReader reader, string msg) {
			IXmlLineInfo lineInfo = reader as IXmlLineInfo;
			string lineStr = "";
			if( lineInfo != null) {
				lineStr += " on line " + lineInfo.LineNumber + " at position " + lineInfo.LinePosition;
			}
			log.Warn(msg + lineStr);
		}
		
		public SymbolProperties Get(string symbol) {
			foreach( SymbolProperties properties in this) {
				if( symbol == properties.Symbol) {
					return properties;
				}
			}
			throw new ApplicationException("Symbol " + symbol + " was not found in the dictionary.");
		}
		
		
		public IEnumerator<SymbolProperties> GetEnumerator()
		{
			foreach( SymbolCategory category in categories) {
				foreach( SymbolProperties properties in category) {
					yield return properties;
				}
			}
		}
		
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
#region UNIVERSAL_DICTIONARY
		public static string UniversalDictionary = @"<?xml version=""1.0"" encoding=""utf-16""?>
<category name=""Universal"">
  <category name=""Synthetic"">
    <property name=""DisplayTimeZone"" value=""Local"" />
    	<symbol name=""TimeSync""/>
  </category>
  <category name=""Stock"">
    <property name=""DisplayTimeZone"" value=""Local"" />
    <property name=""Level2LotSize"" value=""100"" />
    <property name=""Level2LotSizeMinimum"" value=""1"" />
    <property name=""Level2Increment"" value=""0.01"" />
    <property name=""FullPointValue"" value=""1"" />
    <property name=""MinimumTick"" value=""0.01"" />
    <property name=""SessionStart"" value=""08:00:00"" />
    <property name=""SessionEnd"" value=""16:30:00"" />
    <property name=""TimeAndSales"" value=""ActualTrades"" />
    <property name=""QuoteType"" value=""Level1"" />
    <symbol name=""SPY"">
      <property name=""MaxPositionSize"" value=""1000"" />
      <property name=""MaxOrderSize"" value=""1000"" />
      <property name=""TimeAndSales"" value=""ActualTrades"" />
      <property name=""QuoteType"" value=""None"" />
    </symbol>
    <symbol name=""SPYTest"">
      <property name=""TimeAndSales"" value=""ActualTrades"" />
      <property name=""QuoteType"" value=""None"" />
    </symbol>
    <symbol name=""SPYTradeOnly"">
      <property name=""TimeAndSales"" value=""ActualTrades"" />
      <property name=""QuoteType"" value=""None"" />
    </symbol>
    <symbol name=""SPYQuoteOnly"">
      <property name=""TimeAndSales"" value=""None"" />
      <property name=""QuoteType"" value=""Level1"" />
    </symbol>
    <category name=""Testing"">
      <symbol name=""CSCO"">
        <property name=""TimeAndSales"" value=""ActualTrades"" />
        <property name=""QuoteType"" value=""None"" />
      </symbol>
      <symbol name=""MSFT"">
        <property name=""TimeAndSales"" value=""None"" />
        <property name=""QuoteType"" value=""Level2"" />
      </symbol>
      <symbol name=""IBM"">
        <property name=""MaxPositionSize"" value=""5"" />
        <property name=""MaxOrderSize"" value=""5"" />
        <property name=""TimeAndSales"" value=""None"" />
        <property name=""QuoteType"" value=""Level1"" />
      </symbol>
      <symbol name=""GOOG"">
        <property name=""TimeAndSales"" value=""None"" />
        <property name=""QuoteType"" value=""Level1"" />
      </symbol>
      <symbol name=""GE""/>
      <symbol name=""INTC""/>
      <symbol name=""Design"" />
      <symbol name=""FullTick"" />
      <symbol name=""Daily4Ticks"" />
      <symbol name=""MockFull"" />
      <symbol name=""Mock4Ticks"" />
      <symbol name=""Mock4Sim"" />
      <symbol name=""Daily4Sim"">
        <property name=""DisplayTimeZone"" value=""Local"" />
      </symbol>
      <symbol name=""Daily4Test"" />
      <symbol name=""TXF"" />
      <symbol name=""spyTestBars"" />
      <symbol name=""ErrorTest"" />
    </category>
  </category>
  <category name=""Forex"">
    <property name=""TimeZone"" value=""Eastern Standard Time"" />
    <property name=""DisplayTimeZone"" value=""Local"" />
    <property name=""Level2LotSize"" value=""10000"" />
    <property name=""Level2LotSizeMinimum"" value=""100"" />
    <property name=""Level2Increment"" value=""10"" />
    <property name=""FullPointValue"" value=""1"" />
    <property name=""TimeAndSales"" value=""Extrapolated"" />
    <property name=""QuoteType"" value=""Level1"" />
    <category name=""4 Pip"">
    <property name=""MinimumTick"" value=""0.00001"" />
      <symbol name=""USD/CHF"" universal=""USDCHF"">
        <property name=""TimeAndSales"" value=""Extrapolated"" />
      </symbol>
      <symbol name=""USD/CAD"" universal=""USDCAD"" />
      <symbol name=""AUD/USD"" universal=""AUDUSD"" />
      <symbol name=""USD/NOK"" universal=""USDNOK"" />
      <symbol name=""EUR/USD"" universal=""EURUSD"" >
        <property name=""MinimumTick"" value=""0.00001"" />
        <property name=""UseSyntheticLimits"" value=""false"" />
        <property name=""UseSyntheticStops"" value=""false"" />
        <property name=""UseSyntheticMarkets"" value=""false"" />
      </symbol>
      <symbol name=""USD/SEK"" universal=""USDSEK"" />
      <symbol name=""USD/DKK"" universal=""USDDKK"" />
      <symbol name=""GBP/USD"" universal=""GBPUSD"" />
      <symbol name=""EUR/CHF"" universal=""EURCHF"" />
      <symbol name=""EUR/GBP"" universal=""EURGBP"" />
      <symbol name=""EUR/NOK"" universal=""EURNOK"" />
      <symbol name=""EUR/SEK"" universal=""EURSEK"" />
      <symbol name=""GBP/CHF"" universal=""GBPCHF"" />
      <symbol name=""NZD/USD"" universal=""NZDUSD"" />
      <symbol name=""AUD/CHF"" universal=""AUDCHF"" />
      <symbol name=""AUD/CAD"" universal=""AUDCAD"" />
    </category>
    <category name=""2 Pip"">
      <property name=""MinimumTick"" value=""0.001"" />
      <symbol name=""USD/JPY"" >
        <property name=""UseSyntheticLimits"" value=""false"" />
        <property name=""UseSyntheticStops"" value=""false"" />
        <property name=""UseSyntheticMarkets"" value=""false"" />
      </symbol>
      <category name=""Testing"">
        <symbol name=""USD/JPY_Synthetic"" >
          <property name=""UseSyntheticLimits"" value=""true"" />
          <property name=""UseSyntheticStops"" value=""true"" />
          <property name=""UseSyntheticMarkets"" value=""true"" />
         </symbol>
        <symbol name=""USD_JPY"">
          <property name=""DisplayTimeZone"" value=""UTC"" />
          <property name=""SessionStart"" value=""01:00:00"" />
          <property name=""SessionEnd"" value=""10:00:00.000"" />
        </symbol>
        <symbol name=""USD_JPY2"" universal=""USD_JPY"">
          <property name=""DisplayTimeZone"" value=""Exchange"" />
          <property name=""SessionStart"" value=""06:00:00"" />
          <property name=""SessionEnd"" value=""15:00:00.000"" />
        </symbol>
        <symbol name=""USD_JPY_YEARS"">
          <property name=""DisplayTimeZone"" value=""UTC"" />
        </symbol>
        <symbol name=""USDJPYBenchMark"">
		   <property name=""FullPointValue"" value=""0.0076651847309520159435842403801932"" />
        </symbol>
        <symbol name=""USD_JPY_Volume"" />
        <symbol name=""USD_JPY_TEST"">
          <property name=""SessionEnd"" value=""09:22:13.000"" />
        </symbol>
        <category name=""TCK file testing"">
          <property name=""MinimumTick"" value=""0.0001"" />
          <symbol name=""TST_TST"" />
          <symbol name=""TST_VR2"" />
          <symbol name=""TST_VR3"" />
          <symbol name=""TST_VR4"" />
          <symbol name=""TST_VR5"" />
          <symbol name=""TST_VR6"" />
          <symbol name=""TST_VR7"" />
          <symbol name=""TST_VR8"" />
          <symbol name=""TST_VR9"" />
          <symbol name=""TST_VR10"" />
        </category>
      </category>
      <symbol name=""CHF/JPY"" universal=""CHFJPY"" />
      <symbol name=""EUR/JPY"" universal=""EURJPY"" />
      <symbol name=""GBP/JPY"" universal=""GBPJPY"" />
      <symbol name=""AUD/JPY"" universal=""AUDJPY"" />
      <symbol name=""CAD/JPY"" universal=""CADJPY"" />
    </category>
  </category>
  <category name=""Futures"">
    <property name=""DisplayTimeZone"" value=""Local"" />
    <property name=""Level2LotSize"" value=""1"" />
    <property name=""Level2LotSizeMinimum"" value=""1"" />
    <property name=""Level2Increment"" value=""1"" />
    <property name=""FullPointValue"" value=""50"" />
    <property name=""MinimumTick"" value=""0.25"" />
    <property name=""TimeAndSales"" value=""ActualTrades"" />
    <property name=""QuoteType"" value=""Level1"" />
    <category name=""Testing"">
      <symbol name=""ES"">
        <property name=""UseSyntheticLimits"" value=""false"" />
        <property name=""UseSyntheticStops"" value=""false"" />
        <property name=""UseSyntheticMarkets"" value=""false"" />
      </symbol>
      <symbol name=""KC"">
	    <property name=""TimeZone"" value=""Eastern Standard Time"" />
	    <property name=""MinimumTick"" value=""0.05"" />
      </symbol>
      <symbol name=""/ESZ9"">
        <property name=""Commission"" value=""CustomCommission"" />
        <property name=""Fees"" value=""CustomFees"" />
        <property name=""Slippage"" value=""CustomSlippage"" />
        <property name=""Destination"" value=""CustomDestination"" />
      </symbol>
      <symbol name=""/ESZ0"" />
      <symbol name=""/ESU0"" />
      <symbol name=""/NQU0"" />
      <symbol name=""/ESH1""/>
      <symbol name=""TestException"" />
      <category name=""TradeOnly"">
        <property name=""TimeAndSales"" value=""ActualTrades"" />
        <property name=""QuoteType"" value=""None"" />
        <symbol name=""/ESH0""/>
        <symbol name=""/ESH0TradeBar""/>
      </category>
    </category>
        <category name=""Coffee"">
          <property name=""FullPointValue"" value=""375"" />
          <property name=""MinimumTick"" value=""0.05"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""3:30:00"" />
	    <property name=""SessionEnd"" value=""14:00:00"" />
              <symbol name=""KC""/>
        </category>
        <category name=""Cotton"">
          <property name=""FullPointValue"" value=""500"" />
          <property name=""MinimumTick"" value=""0.01"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""21:00:00"" />
	    <property name=""SessionEnd"" value=""14:15:00"" />
              <symbol name=""CT""/>
        </category>
        <category name=""Cocoa"">
          <property name=""FullPointValue"" value=""10"" />
          <property name=""MinimumTick"" value=""1"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""4:00:00"" />
	    <property name=""SessionEnd"" value=""14:00:00"" />
              <symbol name=""CC""/>
        </category>
        <category name=""Orange juice"">
          <property name=""FullPointValue"" value=""150"" />
          <property name=""MinimumTick"" value=""0.05"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""8:00:00"" />
	    <property name=""SessionEnd"" value=""14:00:00"" />
              <symbol name=""JO""/>
        </category>
        <category name=""Lumber"">
          <property name=""FullPointValue"" value=""110"" />
          <property name=""MinimumTick"" value=""0.1"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""9:00:00"" />
	    <property name=""SessionEnd"" value=""13:55:00"" />
              <symbol name=""LB""/>
        </category>
        <category name=""Copper"">
          <property name=""FullPointValue"" value=""250"" />
          <property name=""MinimumTick"" value=""0.05"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""17:15:00"" />
              <symbol name=""HG""/>
        </category>
        <category name=""Nymex crude"">
          <property name=""FullPointValue"" value=""1000"" />
          <property name=""MinimumTick"" value=""0.01"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""17:15:00"" />
              <symbol name=""CL""/>
        </category>
        <category name=""Heating oil"">
          <property name=""FullPointValue"" value=""42000"" />
          <property name=""MinimumTick"" value=""0.0001"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""17:15:00"" />
              <symbol name=""HO""/>
        </category>
        <category name=""Feeder cattle"">
          <property name=""FullPointValue"" value=""500"" />
          <property name=""MinimumTick"" value=""0.025"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:00:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""FC""/>
        </category>
        <category name=""Live cattle"">
          <property name=""FullPointValue"" value=""400"" />
          <property name=""MinimumTick"" value=""0.025"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:00:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""LC""/>
        </category>
        <category name=""Lean hog"">
          <property name=""FullPointValue"" value=""400"" />
          <property name=""MinimumTick"" value=""0.025"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:00:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""LH""/>
        </category>
        <category name=""Pork bellie"">
          <property name=""FullPointValue"" value=""400"" />
          <property name=""MinimumTick"" value=""0.025"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:00:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""PB""/>
        </category>
        <category name=""Eurodollar"">
          <property name=""FullPointValue"" value=""1000"" />
          <property name=""MinimumTick"" value=""0.005"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""17:15:00"" />
              <symbol name=""ED""/>
        </category>
        <category name=""Comex palladium"">
          <property name=""FullPointValue"" value=""100"" />
          <property name=""MinimumTick"" value=""0.05"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""17:15:00"" />
              <symbol name=""PA""/>
        </category>
        <category name=""Comex platinum"">
          <property name=""FullPointValue"" value=""50"" />
          <property name=""MinimumTick"" value=""0.10"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""17:15:00"" />
              <symbol name=""PL""/>
        </category>
        <category name=""Comex silver"">
          <property name=""FullPointValue"" value=""50"" />
          <property name=""MinimumTick"" value=""0.005"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""17:15:00"" />
              <symbol name=""SV""/>
        </category>
        <category name=""Comex Gold"">
          <property name=""FullPointValue"" value=""100"" />
          <property name=""MinimumTick"" value=""0.10"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""17:15:00"" />
              <symbol name=""GC""/>
        </category>
        <category name=""CME currency 125k variants"">
          <property name=""FullPointValue"" value=""125000"" />
          <property name=""MinimumTick"" value=""0.0001"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:00:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""JY""/>
              <symbol name=""EC""/>
              <symbol name=""SF""/>
        </category>
        <category name=""CME currency 100k variants"">
          <property name=""FullPointValue"" value=""100000"" />
          <property name=""MinimumTick"" value=""0.0001"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:00:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""AD""/>
              <symbol name=""CD""/>
        </category>
        <category name=""British Pound variants"">
          <property name=""FullPointValue"" value=""62500"" />
          <property name=""MinimumTick"" value=""0.0001"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:00:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""BP""/>
        </category>
        <category name=""Mexican Peso variants"">
          <property name=""FullPointValue"" value=""500000"" />
          <property name=""MinimumTick"" value=""0.000025"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:00:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""ME""/>
        </category>
        <category name=""Dollar Index variants"">
          <property name=""FullPointValue"" value=""1000"" />
          <property name=""MinimumTick"" value=""0.005"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""15:00:00"" />
              <symbol name=""DX""/>
        </category>
        <category name=""Nasdaq full size variants"">
          <property name=""FullPointValue"" value=""100"" />
          <property name=""MinimumTick"" value=""0.25"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""15:30:00"" />
	    <property name=""SessionEnd"" value=""15:15:00"" />
              <symbol name=""ND""/>
        </category>
        <category name=""Natural gas variants"">
          <property name=""FullPointValue"" value=""10000"" />
          <property name=""MinimumTick"" value=""0.001"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:00:00"" />
	    <property name=""SessionEnd"" value=""16:15:00"" />
              <symbol name=""NG""/>
        </category>
        <category name=""Nikkei variants"">
          <property name=""FullPointValue"" value=""5"" />
          <property name=""MinimumTick"" value=""5"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""15:30:00"" />
	    <property name=""SessionEnd"" value=""15:15:00"" />
              <symbol name=""NK""/>
        </category>
        <category name=""Sugar variants"">
          <property name=""FullPointValue"" value=""1120"" />
          <property name=""MinimumTick"" value=""0.01"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""3:30:00"" />
	    <property name=""SessionEnd"" value=""14:00:00"" />
              <symbol name=""SB""/>
        </category>
        <category name=""Mini Russell variants"">
          <property name=""FullPointValue"" value=""100"" />
          <property name=""MinimumTick"" value=""0.10"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""20:00:00"" />
	    <property name=""SessionEnd"" value=""18:00:00"" />
              <symbol name=""ER""/>
        </category>
        <category name=""Full size S and P Symbol SP variants"">
          <property name=""FullPointValue"" value=""250"" />
          <property name=""MinimumTick"" value=""0.05"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""15:30:00"" />
	    <property name=""SessionEnd"" value=""15:15:00"" />
              <symbol name=""SP""/>
        </category>
        <category name=""Emini S and P Symbol ES variants"">
          <property name=""FullPointValue"" value=""50"" />
          <property name=""MinimumTick"" value=""0.25"" />
	    <property name=""TimeZone"" value=""Central Standard Time"" />
	    <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""15:30:00"" />
	    <property name=""SessionEnd"" value=""15:15:00"" />
              <symbol name=""ES""/>
        </category>
        <category name=""Emini NASDAQ Symbol NQ variants"">
          <property name=""FullPointValue"" value=""20"" />
          <property name=""MinimumTick"" value=""0.25"" />
	    <property name=""TimeZone"" value=""Central Standard Time"" />
	    <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""15:30:00"" />
	    <property name=""SessionEnd"" value=""15:15:00"" />
         	    <symbol name=""NQ""/>
        </category>
        <category name=""Full size S and P 400 MidCap variants"">
          <property name=""FullPointValue"" value=""500"" />
          <property name=""MinimumTick"" value=""0.05"" />
	    <property name=""TimeZone"" value=""Central Standard Time"" />
	    <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""15:30:00"" />
	    <property name=""SessionEnd"" value=""15:15:00"" />
          	    <symbol name=""MD""/>
        </category>
        <category name=""Emini S and P 400 MidCap variants"">
          <property name=""FullPointValue"" value=""100"" />
          <property name=""MinimumTick"" value=""0.10"" />
	    <property name=""TimeZone"" value=""Central Standard Time"" />
	    <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""15:30:00"" />
	    <property name=""SessionEnd"" value=""15:15:00"" />
          	    <symbol name=""MI""/>
        </category>
        <category name=""Emini MSCI EAFE variants"">
          <property name=""FullPointValue"" value=""50"" />
          <property name=""MinimumTick"" value=""0.10"" />
	    <property name=""TimeZone"" value=""Central Standard Time"" />
	    <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""15:30:00"" />
	    <property name=""SessionEnd"" value=""15:15:00"" />
          	    <symbol name=""MG""/>
        </category>
        <category name=""Full size Dow DJIA ($10) variants"">
          <property name=""FullPointValue"" value=""10"" />
          <property name=""MinimumTick"" value=""1"" />
	    <property name=""TimeZone"" value=""Central Standard Time"" />
	    <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""15:30:00"" />
	    <property name=""SessionEnd"" value=""15:15:00"" />
          	    <symbol name=""DJ""/>
        </category>
        <category name=""Mini Dow variants"">
          <property name=""FullPointValue"" value=""5"" />
          <property name=""MinimumTick"" value=""1"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""15:30:00"" />
	    <property name=""SessionEnd"" value=""15:15:00"" />
              <symbol name=""YM""/>
        </category>
        <category name=""CBT grain $50 variants"">
          <property name=""FullPointValue"" value=""50"" />
          <property name=""MinimumTick"" value=""0.25"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""9:30:00"" />
	    <property name=""SessionEnd"" value=""13:15:00"" />
              <symbol name=""CN""/>
              <symbol name=""OA""/>
              <symbol name=""SY""/>
              <symbol name=""WC""/>
        </category>
        <category name=""Soybean meal variants"">
          <property name=""FullPointValue"" value=""100"" />
          <property name=""MinimumTick"" value=""0.1"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""13:15:00"" />
              <symbol name=""SM""/>
        </category>
        <category name=""Soybean oil variants"">
          <property name=""FullPointValue"" value=""600"" />
          <property name=""MinimumTick"" value=""0.01"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""13:15:00"" />
              <symbol name=""BO""/>
        </category>
        <category name=""2 YR notes variants"">
          <property name=""FullPointValue"" value=""2000"" />
          <!-- 32nds and quarters from CME.com as of 1/6/2011 -->
          <property name=""MinimumTick"" value=""0.0078125"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:30:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""TU""/>
        </category>
        <category name=""5 YR notes variants"">
          <property name=""FullPointValue"" value=""1000"" />
          <!-- 32nds and quarters from CME.com as of 1/6/2011 -->
          <property name=""MinimumTick"" value=""0.0078125"" />
	  <property name=""SessionStart"" value=""17:30:00"" />
	  <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""FV""/>
        </category>
        <category name=""10 YR notes variants"">
          <property name=""FullPointValue"" value=""1000"" />
          <!-- 32nds and halves from CME.com as of 1/6/2011 -->
          <property name=""MinimumTick"" value=""0.015625"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:30:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
              <symbol name=""TY""/>
        </category>
        <category name=""30 YR bond variants"">
          <property name=""FullPointValue"" value=""1000"" />
          <!-- 32nds from CME.com as of 1/6/2011, tick data in 32nds and halves -->
          <property name=""MinimumTick"" value=""0.015625"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""17:30:00"" />
	    <property name=""SessionEnd"" value=""16:00:00"" />
	    <!-- *** pit trading times, start after major reports *** -->
              <!-->
	    <property name=""SessionStart"" value=""7:45:00"" />
	    <property name=""SessionEnd"" value=""14:00:00"" />
              <-->
              <symbol name=""US""/>
              <symbol name=""ZB""/>
        </category>
        <category name=""Mini gold variants"">
          <property name=""FullPointValue"" value=""33.20"" />
          <property name=""MinimumTick"" value=""0.1"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""19:16:00"" />
	    <property name=""SessionEnd"" value=""17:00:00"" />
              <symbol name=""XG""/>
        </category>
        <category name=""Mini silver variants"">
          <property name=""FullPointValue"" value=""5"" />
          <property name=""MinimumTick"" value=""1"" />
	  <property name=""TimeZone"" value=""Eastern Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""19:16:00"" />
	    <property name=""SessionEnd"" value=""17:00:00"" />
              <symbol name=""YS""/>
        </category>
        <category name=""RBOB Gasoline variants"">
          <property name=""FullPointValue"" value=""42000"" />
          <property name=""MinimumTick"" value=""0.0001"" />
	  <property name=""TimeZone"" value=""Central Standard Time"" />
	  <property name=""DisplayTimeZone"" value=""Local"" />
	    <property name=""SessionStart"" value=""18:00:00"" />
	    <property name=""SessionEnd"" value=""17:15:00"" />
              <symbol name=""XB""/>
              <symbol name=""HU""/>
        </category>
  </category>
</category>";
#endregion

#region USER_DICTIONARY
		public static string UserDictionary = @"<?xml version=""1.0"" encoding=""utf-16""?>
<category name=""MB Trading"">
  <category name=""Stock"">
    <property name=""DisplayTimeZone"" value=""Exchange"" />
    <property name=""FullPointValue"" value=""1"" />
    <property name=""MinimumTick"" value=""0.01"" />
    <category name=""Testing"">
      <property name=""TimeZone"" value=""UTC-4"" />
      <symbol name=""FullTick"" />
      <symbol name=""Daily4Sim"" />
      <symbol name=""Mock4Sim"" >
        <property name=""UseSyntheticLimits"" value=""true"" />
        <property name=""UseSyntheticStops"" value=""true"" />
        <property name=""UseSyntheticMarkets"" value=""true"" />
      </symbol>
      <symbol name=""TXF"" />
      <symbol name=""spyTestBars"" />
    </category>
  </category>
</category>";
#endregion
	}
}
