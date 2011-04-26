using System;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Statistics
{
    public class ProfitLossCallback2 : ProfitLoss2 {
        double slippage = 0.0D;
        double commission = 0.0D;
        double fullPointValue = 1D;
        Model model;
        bool firstTime = true;
        bool userImplemented = false;
        SymbolInfo symbol;
		
        public ProfitLossCallback2() {
        }
		
        public ProfitLossCallback2(Model model) {
            this.model = model;
        }

        public void CalculateProfit(TransactionPairBinary trade, out double grossProfit, out double costs)
        {
            if( firstTime ){
                try {
                    if( model != null) {
                        model.OnCalculateProfitLoss(trade, out grossProfit, out costs);
                        userImplemented = true;
                    } else {
                        userImplemented = false;
                    }
                } catch( NotImplementedException) {
                    userImplemented = false;							
                }
                firstTime = false;
            }
			
            if( userImplemented) {
                model.OnCalculateProfitLoss(trade, out grossProfit, out costs);
            } else {
                costs = (slippage + commission)*fullPointValue*Math.Abs(trade.CurrentPosition);
                var grossPoints = (trade.AverageEntryPrice - trade.EntryPrice)*trade.CurrentPosition + trade.ClosedPoints;
                grossProfit = Math.Round(grossPoints, symbol.MinimumTickPrecision) * symbol.FullPointValue;
            }
        }

        public double CalculateProfit(double position, double entry, double exit)
        {
            throw new NotImplementedException("Please use the other CalculateProfit method.");
        }
		
        public SymbolInfo Symbol {
            get { return symbol; }
            set { symbol = value; }
        }
    }
}