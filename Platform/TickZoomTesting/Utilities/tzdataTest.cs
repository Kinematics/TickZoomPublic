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
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;

using Microsoft.Win32;
using NUnit.Framework;
using TickZoom.Api;
using TickZoom.TZData;

namespace TickZoom.Utilities
{
	
	public static class TzDataExtensionMethods {
		public static void WriteLine( this StringBuilder sb, string value) {
			sb.AppendLine(value);
		}
	}

	[TestFixture]
	public class tzdataTest
	{
		public static void Main( string[] args) {
			var fixture = new tzdataTest();
			fixture.TestImport();
		}
		[Test]
		public void TestFilter()
		{
	       	string storageFolder = Factory.Settings["AppDataFolder"];
	       	if( storageFolder == null) {
	       		throw new ApplicationException( "Must set AppDataFolder property in app.config");
	       	}
			string[] args = {
				storageFolder + @"\Test\\DataCache\Daily4Ticks.tck",
				storageFolder + @"\Test\\DataCache\Daily4SimZB.tck",
				"2005/05/05",
				"2005/05/10"
			};
	       	Filter filter = new Filter();
	       	filter.AssemblyName = "tzdata";
	       	filter.Run(args);
		}
		
		
		[Test]
		public void TestFilterDates()
		{
	       	string storageFolder = Factory.Settings["AppDataFolder"];
	       	if( storageFolder == null) {
	       		throw new ApplicationException( "Must set AppDataFolder property in app.config");
	       	}
			string[] args = {
				"USD_JPY",
				storageFolder + @"\Test\\DataCache\USD_JPY.tck",
				storageFolder + @"\Test\\DataCache\USD_JPY_Back.tck",
				"2005/05/05",
				"2005/05/10"
			};
	       	Filter filter = new Filter();
	       	filter.AssemblyName = "tzdata";
	       	var sb = new StringBuilder();
	       	filter.Output = sb.WriteLine;
	       	filter.Run(args);
			string expectedOutput = "USD_JPY: 10113 ticks.\r\nFrom 2005-05-05 07:01:17.187 to 2005-05-10 07:00:07.355\r\n0 duplicates elimated.\r\n";
			string output = sb.ToString();
			Assert.AreEqual(expectedOutput,output);			
		}
		
		[Test]
		public void TestImport()
		{
	       	string storageFolder = Factory.Settings["AppDataFolder"];
	       	if( storageFolder == null) {
	       		throw new ApplicationException( "Must set AppDataFolder property in app.config");
	       	}
	       	var symbol = Factory.Symbol.LookupSymbol("KC");
			string[] args = {
	       		symbol.ToString(),
				storageFolder + @"\Test\\ImportData\KC.csv",
			};
	       	var import = new Import();
	       	import.DataFolder = @"Test\DataCache";
	       	import.AssemblyName = "tzdata";
	       	import.Run(args);
	       	
	       	var reader = Factory.TickUtil.TickReader();
	       	reader.Initialize(@"DataCache", symbol.ToString());
	       	var queue = reader.ReadQueue;
	       	var binary = new TickBinary();
	       	var tickIO = Factory.TickUtil.TickIO();
	       	var sb = new StringBuilder();
	       	long lineCount = 0;
	       	using( var inputFile = new StreamReader(storageFolder + @"\Test\\ImportData\KC.csv")) {
	       		lineCount++;
				var heading = inputFile.ReadLine();
		       	while( true) {
	       			try {
			       		while( ! queue.TryDequeue( ref binary)) {
			       			Thread.Sleep(1);
			       		}
						queue.RemoveStruct();
			       		lineCount++;
						tickIO.Inject(binary);
						sb.Length = 0;
						sb.AppendFormat("{0:00}",tickIO.Time.Month);
						sb.Append("/");
						sb.AppendFormat("{0:00}",tickIO.Time.Day);
						sb.Append("/");
						sb.AppendFormat("{0:00}",tickIO.Time.Year);
						sb.Append(",");
						
						sb.AppendFormat("{0:00}",tickIO.Time.Hour);
						sb.Append(":");
						sb.AppendFormat("{0:00}",tickIO.Time.Minute);
						sb.Append(":");
						sb.AppendFormat("{0:00}",tickIO.Time.Second);
						sb.Append(",");
						
						sb.AppendFormat("{0:.00}", tickIO.Price);
						sb.Append(",");
						
						sb.AppendFormat("{0:.00}", tickIO.Price);
						sb.Append(",");
						
						sb.AppendFormat("{0:.00}", tickIO.Price);
						sb.Append(",");
						
						sb.AppendFormat("{0:.00}", tickIO.Price);
						sb.Append(",");
						
						sb.Append(tickIO.Size);
						var original = inputFile.ReadLine();
						Assert.AreEqual(original,sb.ToString(),"comparing at line " + lineCount);
	       			} catch( QueueException) {
	       				break;
	       			}
		       	}
	       	}
		}

        [Test]
        public void TestExport()
        {
            string storageFolder = Factory.Settings["AppDataFolder"];
            if (storageFolder == null)
            {
                throw new ApplicationException("Must set AppDataFolder property in app.config");
            }
            var symbol = Factory.Symbol.LookupSymbol("KC");
            var sb = new StringBuilder();
            string[] args =
            {
                storageFolder + @"\Test\\DataCache\ESH0.tck",
                "2010-02-16 16:00",
                "2010-02-16 21:49:28.793"
            };
            var export = new Export();
            export.Output = sb.WriteLine;
            export.DataFolder = @"Test\DataCache";
            export.AssemblyName = "tzdata";
            export.Run(args);

            var expectedOutput = @"2010-02-16 16:49:28.769 1063,10, 0/0 0,0,0,0,0|0,0,0,0,0
2010-02-16 16:49:28.791 1062.75,1, 0/0 0,0,0,0,0|0,0,0,0,0
2010-02-16 16:49:28.792 1062.75,1, 0/0 0,0,0,0,0|0,0,0,0,0
2010-02-16 16:49:28.793 1062.75,2, 0/0 0,0,0,0,0|0,0,0,0,0
";
            Assert.AreEqual(expectedOutput,sb.ToString());
        }

        [Test]
		public void TestMigrate()
		{
	       	string storageFolder = Factory.Settings["AppDataFolder"];
	       	if( storageFolder == null) {
	       		throw new ApplicationException( "Must set AppDataFolder property in app.config");
	       	}
	       	string origFile = storageFolder + @"\Test\\DataCache\Migrate.tck";
	       	string tempFile = origFile + ".temp";
	       	string backupFile = origFile + ".back";
	       	File.Delete( backupFile);
	       	File.Delete( origFile);
	       	string fileName = storageFolder + @"\Test\\DataCache\USD_JPY.tck";
	       	if( !File.Exists(fileName)) {
	       		fileName = fileName.Replace(".tck","_Tick.tck");
	       	}
	       	File.Copy(fileName, origFile);
	       	
	       	string[] args = { "USD/JPY", storageFolder + @"\Test\\DataCache\Migrate.tck" };
	       	
	       	Migrate migrate = new Migrate();
	       	migrate.Run(args);
			Assert.IsTrue( File.Exists( origFile));
			Assert.IsTrue( File.Exists( backupFile));
			Assert.IsFalse( File.Exists( tempFile));
		}
		
		[Test]
		public void TestQuery()
		{
			var appData = Factory.Settings["AppDataFolder"];
			string[] args = { appData + @"\Test\\DataCache\ESH0.tck" };
			Query query = new Query();
			query.Run(args);
			string expectedOutput = "Symbol: /ESH0" + Environment.NewLine +
"Version: 8" + Environment.NewLine +
"Ticks: 15683" + Environment.NewLine +
"Trade Only: 15683" + Environment.NewLine +
"From: 2010-02-16 16:49:28.769.0 (local), 2010-02-16 21:49:28.769.0 (UTC)" + Environment.NewLine +
"  To: 2010-02-16 16:59:56.140.0 (local), 2010-02-16 21:59:56.140.0 (UTC)" + Environment.NewLine +
"Prices duplicates: 14489" + Environment.NewLine +
"";
			string output = query.ToString();
			Assert.AreEqual(expectedOutput,output);			
		}
		
		[Test]
		public void TestRegister()
		{
	       	Register register = new Register();
	       	register.Directory = Path.GetFullPath(".");
	       	register.Run(null);
	       	
			RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Environment",true);
			string variable = "TickZoom";
			string tickZoom = (string) key.GetValue(variable);
			string path = (string) key.GetValue("Path");
			
			Assert.NotNull(tickZoom);
			Assert.NotNull(path);
			Assert.AreEqual(register.Directory,tickZoom,"TickZoom environment variable equals directory.");
			Assert.IsTrue(path.Contains(tickZoom),"path has directory");
		}
	}
}
