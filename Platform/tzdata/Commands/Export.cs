using System;
using System.Collections;
using System.IO;
using System.Reflection;
using TickZoom.Api;

namespace TickZoom.TZData
{
    public class Export : Command
    {
        string assemblyName;
        string dataFolder = "DataCache";

        // Log log = Factory.SysLog.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        SymbolInfo symbol;
        TickIO tickIO = Factory.TickUtil.TickIO();
        string fromFile;
        TickReader reader = Factory.TickUtil.TickReader();

        private TimeStamp startTime = TimeStamp.MinValue;
        private TimeStamp endTime = TimeStamp.MaxValue;

        public Export()
        {
            Assembly assembly = Assembly.GetEntryAssembly();
            if (assembly != null)
            {
                assemblyName = assembly.GetName().Name;
            }
        }

        public override void Run(string[] args)
        {
            string symbolString;

            if (args.Length == 1)
            {
                string filePath = args[0];
                reader.Initialize(filePath);
            }
            else if (args.Length == 2)
            {
                symbolString = args[0];
                symbol = Factory.Symbol.LookupSymbol(symbolString);
                string filePath = args[1];
                reader.Initialize(filePath, symbol);
            }
            else if( args.Length == 3)
            {
                string filePath = args[0];
                reader.Initialize(filePath);
                startTime = new TimeStamp(args[1]);
                endTime = new TimeStamp(args[2]);
            }
            else if (args.Length == 4)
            {
                symbolString = args[0];
                symbol = Factory.Symbol.LookupSymbol(symbolString);
                string filePath = args[1];
                reader.Initialize(filePath, symbol);
                startTime = new TimeStamp(args[2]);
                endTime = new TimeStamp(args[3]);
            }
            else
            {
                Output("Export Usage:");
                Output("tzdata " + Usage());
                return;
            }
            ReadFile();
        }

        public void ReadFile()
        {
            TickIO tickIO = Factory.TickUtil.TickIO();
            TickBinary tickBinary = new TickBinary();
            using( var queue = reader.ReadQueue)
            {
                try
                {
                    while (true)
                    {
                        queue.Dequeue(ref tickBinary);
                        queue.RemoveStruct();
                        tickIO.Inject(tickBinary);
                        if (tickIO.UtcTime > endTime)
                        {
                            break;
                        }
                        if( tickIO.UtcTime > startTime)
                        {
                            Output(tickIO.ToString());
                        }
                    }
                }
                catch (QueueException ex)
                {
                    if (ex.EntryType != EventType.EndHistorical)
                    {
                        throw;
                    }
                }
            }
        }

        public override string[] Usage()
        {
            return new string[] { assemblyName + " export [<symbol>] <file> [<starttimestamp> <endtimestamp>]" };
        }

        public string AssemblyName
        {
            get { return assemblyName; }
            set { assemblyName = value; }
        }

        public string DataFolder
        {
            get { return dataFolder; }
            set { dataFolder = value; }
        }
    }
}