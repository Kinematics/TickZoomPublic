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
using System.IO;
using System.Threading;
using log4net;
using log4net.Appender;
using TickZoom.Api;

namespace TickZoom.Logging
{

    public class FileAppender : log4net.Appender.FileAppender
    {
		private LoggingActionQueue actionQueue = new LoggingActionQueue(100);
		private Task actionTask;
		private Exception actionException;
		public FileAppender() {
			actionTask = Factory.Parallel.IOLoop("FileAppender", OnException, ActionLoop);
			actionTask.Start();
		}

        protected override void Reset()
        {
            while( actionQueue.Count > 0)
            {
                Thread.Sleep(1);
            }
            base.Reset();
        }
		
		private Yield ActionLoop() {
			var result = Yield.NoWork.Repeat;
			Action action;
			if( actionQueue.TryDequeue( out action)) {
				action();
				result = Yield.DidWork.Repeat;
			}
			return result;
		}
		
		private void OnException( Exception ex) {
			actionException = ex;
		}
		
        public override string File
        {
            get
            {
                return base.File;
            }

            set
            {
                try
                {
                    // get the log file name from the config file.
                    string logFileName = value.Replace("LogFolder",Factory.SysLog.LogFolder);

                    base.File = logFileName;
                }
                catch (Exception)
                {
                    base.File = value;
                }
            }
        }
        
		protected override void Append(log4net.Core.LoggingEvent loggingEvent)
		{
			if( actionException != null) {
				throw new ApplicationException("Asynchronous logging exception: " + actionException.Message, actionException);
			}
			actionQueue.EnQueue( () => {
				AppendBase(loggingEvent);
			});
		}
		
		private void AppendBase(log4net.Core.LoggingEvent loggingEvent)
		{
			base.Append(loggingEvent);
		}
		
		protected override void Append(log4net.Core.LoggingEvent[] loggingEvents)
		{
			if( actionException != null) {
				throw new ApplicationException("Asynchronous logging exception: " + actionException.Message, actionException);
			}
			actionQueue.EnQueue( () => {
				AppendBase(loggingEvents);
			});
		}
		
		private void AppendBase(log4net.Core.LoggingEvent[] loggingEvents)
		{
			base.Append(loggingEvents);
		}
    }
}