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
using System.Reflection;

namespace TickZoom.Api
{
	[CLSCompliant(false)]
	public interface UtilityFactory {
		ProviderService CommandLineProcess();
		ProviderService WindowsService();
		LogicalOrderHandler LogicalOrderHandler(SymbolInfo symbol, PhysicalOrderHandler handler);
		PhysicalOrder PhysicalOrder( bool isActive, SymbolInfo symbol, OrderSide side, OrderType type, double price, int size, int logicalOrderId, object brokerOrder, object tag);
		SymbolHandler SymbolHandler(SymbolInfo symbol, Receiver receiver);
		VerifyFeed VerifyFeed();
		FillSimulator FillSimulator();
		FillSimulator FillSimulator(StrategyInterface strategy);
		BreakPointInterface BreakPoint();
		PositionInterface Position(ModelInterface model);
	}
}
