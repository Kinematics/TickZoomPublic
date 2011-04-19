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
using System.Collections;
using TickZoom.Api;

namespace TickZoom.Transactions
{
	public class TransactionPairs : IEnumerable
	{
		private readonly static Log log = Factory.SysLog.GetLogger(typeof(TransactionPairs));
		private readonly bool debug = log.IsDebugEnabled;
		private readonly bool trace = log.IsTraceEnabled;
		string name = "TradeList";
		TransactionPairsBinary transactionPairs;
		int totalProfit = 0;
		ProfitLoss profitLossCalculation;
        ProfitLoss2 profitLoss2Calculation;
		Func<double,double> currentPrice;

	    public TransactionPairBinary Tail
	    {
            get { return transactionPairs.Tail;  }
	    }
		
		public BinaryStore TradeData {
			get { return transactionPairs.TradeData; }
		}
		
		public TransactionPairs(Func<double,double> currentPrice, ProfitLoss pnl)
		{
			this.currentPrice = currentPrice;
			ProfitLossCalculation = pnl;
			this.transactionPairs = new TransactionPairsBinary(TradeData);
		}
		public TransactionPairs(Func<double,double> currentPrice, ProfitLoss pnl,TransactionPairsBinary transactionPairs)
		{
			this.currentPrice = currentPrice;
			ProfitLossCalculation = pnl;
			this.transactionPairs = transactionPairs;
		}
		
		public int Current {
			get {
				return transactionPairs.Count - 1;
			}
		}
		
		public int Count { 
			get { return transactionPairs.Count; }
		}
		
		/// <summary>
		/// Returns the profit or loss of the most recent trade whether
		/// completed or still open.
		/// </summary>
		public double CurrentProfitLoss {
			get { if( Count == 0) {
					return 0D;
				} else {
					System.Diagnostics.Debug.Assert(currentPrice!=null);
					return ProfitInPosition(transactionPairs.Tail,currentPrice(transactionPairs.Tail.Direction));
				}
			}
		}
		
		/// <summary>
		/// Returns the open profit or loss of the most recent trade only
		/// if still open. Zero if the most recent rade already closed.
		/// </summary>
		public double OpenProfitLoss {
			get { if( Count == 0 || transactionPairs.Tail.Completed) {
					return 0D;
				} else {
					System.Diagnostics.Debug.Assert(currentPrice!=null);
					return ProfitInPosition(transactionPairs.Tail,currentPrice(transactionPairs.Tail.Direction));
				}
			}
		}
		
		public double CalcProfitLoss(int index) {
			if( index >= Count) return 0D;
			double value = ProfitInPosition(index,transactionPairs[index].ExitPrice);
			return value;
		}
		
		public double CalcMaxAdverse(int index) {
			double value = transactionPairs[index].Direction < 0 ?
       			ProfitInPosition(index,transactionPairs[index].MaxPrice) :
       			ProfitInPosition(index,transactionPairs[index].MinPrice);
			return Math.Round(value,3);
		}
		
		public double CalcMaxFavorable(int index) {
			double value = transactionPairs[index].Direction > 0 ?
       			ProfitInPosition(index,transactionPairs[index].MaxPrice) :
       			ProfitInPosition(index,transactionPairs[index].MinPrice);
			return Math.Round(value,3);
		}

        private double CalcProfitLoss( TransactionPairBinary binary)
        {
            double result;
            if (profitLoss2Calculation == null)
            {
                result = profitLossCalculation.CalculateProfit(binary.Direction, binary.EntryPrice, binary.ExitPrice);
            }
            else
            {
                double profitLoss;
                double costs;
                profitLoss2Calculation.CalculateProfit(binary, out profitLoss, out costs);
                result = profitLoss - costs;
            }
            return result.Round();
        }
		
		internal double ProfitInPosition( TransactionPairBinary binary, double price) {
			if( !binary.Completed) {
			    binary.ExitPrice = price;
			}
		    return CalcProfitLoss(binary);
		}
		
		public double ProfitInPosition( int index, double price) {
			TransactionPairBinary binary = transactionPairs[index];
			if( trace) log.Trace("direction = " + binary.Direction + ", currentPosition = " + binary.CurrentPosition + ", volume = " + binary.Volume);
            binary.ExitPrice = price;
            return CalcProfitLoss(binary);
		}
		
		public TransactionPair this [int index] {
			get { return new TransactionPairImpl(transactionPairs[index]);  }
		}
		
		public TransactionPairBinary GetBinary(int index) {
			return transactionPairs[index];
		}
		
		public int TotalProfit {
			get { return totalProfit; }
		}
		
		public override string ToString()
		{
			if( name != "") {
				return base.ToString() + ": " + name;
			} else {
				return base.ToString();
			}
			
		}
		
		public string Name {
			get { return name; }
			set { name = value; }
		}
		
		public ProfitLoss ProfitLossCalculation {
			get { return profitLossCalculation; }
			set { profitLossCalculation = value;
			    profitLoss2Calculation = value as ProfitLoss2; 
			}
		}
		
		public IEnumerator GetEnumerator()
		{
			for( int i=0; i<transactionPairs.Count; i++) {
				TransactionPairBinary binary = transactionPairs[i];
				yield return binary;
			}
		}
	}
}
