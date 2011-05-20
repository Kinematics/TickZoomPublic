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
using TickZoom.Api;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;

namespace TickZoom.FIX
{
	public class MessageFactoryFix44 : MessageFactory
	{
	    private static readonly Log log = Factory.Log.GetLogger(typeof (MessageFactoryFix44));
        private Pool<MessageFIX4_4> pool = Factory.TickUtil.PoolChecked<MessageFIX4_4>();
	    private static int stackCounter = 0;
        public Message Create()
        {
            var message = pool.Create();
            message.Clear();
            return (Message)message;
        }
		public void Release(Message message)
		{
            pool.Free((MessageFIX4_4)message);
		}
	}
}
