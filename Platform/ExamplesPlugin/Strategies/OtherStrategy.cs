using System;
using System.Drawing;
using System.Threading;
using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    [Flags]
    public enum StrategyState
    {
        None,
        Active = 0x01,
        HighRisk = 0x02,
        EndForWeek = 0x04,
        OverSize = 0x08,
        ProcessSizing = Active | HighRisk,
        ProcessOrders = Active | OverSize | HighRisk,
    }

    public static class LocalExtensions
    {
        public static bool AnySet( this Enum input, Enum matchInfo)
        {
            var inputInt = Convert.ToUInt32(input);
            var matchInt = Convert.ToUInt32(matchInfo);
            return ((inputInt & matchInt) != 0);
        }
    }

    public class OtherStrategy : Strategy
    {
        #region Fields
        IndicatorCommon bidLine;
        IndicatorCommon askLine;
        IndicatorCommon position;
        IndicatorCommon averagePrice;
        private double indifferencePrice;
        bool isFirstTick = true;
        double minimumTick;
        private int increaseSpreadInTicks = 3;
        private int reduceSpreadInTicks = 3;
        private int closeProfitInTicks = 20;
        private int volatileSpreadInTicks = 3;
        private double volatileSpread;
        private double reduceSpread;
        private double increaseSpread;
        private int lotSize = 1000;
        double ask, marketAsk;
        double bid, marketBid;
        private double midPoint;
        private ActiveList<LocalFill> fills = new ActiveList<LocalFill>();
        private bool isVisible = false;
        private long totalVolume = 0;
        private int maxLots = int.MaxValue;
        private int lastSize = 0;
        private int buySize = 1;
        private int sellSize = 1;
        private Direction stretchDirection = Direction.Sideways;
        private double maxStretch;
        private IndicatorCommon rubberBand;
        private IndicatorCommon waitRetraceLine;
        private IndicatorCommon bestIndifferenceLine;
        private IndicatorCommon retraceLine;
        private IndicatorCommon maxExcursionLine;
        public int positionPriorToWeekend = 0;
        public volatile StrategyState beforeWeekendState = StrategyState.Active;
        public volatile StrategyState state = StrategyState.Active;
        private int nextIncreaseLots;

        private bool enableManageRisk = false;
        private bool enableSizing = true;
        private bool enablePegging = true;
        private bool limitSize = false;
        private bool throttleIncreasing = false;
        private bool filterIndifference = false;
        private TradeSide oversizeSide;

        private readonly long commission = -0.0000195D.ToLong();
        private double lastMidPoint;
        private int startSizingLots = 30;
        private int retraceErrorMarginInTicks = 25;
        private int sequentialIncreaseCount;
        private double lastMarketBid;
        private double lastMarketAsk;
        private int maxTradeSize;
        private double maxEquity;
        private double drawDown;
        private double maxDrawDown;
        #endregion


        #region Initialize
        public OtherStrategy()
        {
            RequestUpdate(Intervals.Second1);
        }

        public override void OnInitialize()
        {
            Performance.Equity.GraphEquity = true; // Graphed by portfolio.
            Performance.GraphTrades = isVisible;

            minimumTick = Data.SymbolInfo.MinimumTick;
            increaseSpread = increaseSpreadInTicks * minimumTick;
            reduceSpread = reduceSpreadInTicks * minimumTick;
            volatileSpread = volatileSpreadInTicks*minimumTick;

            askLine = Formula.Indicator();
            askLine.Name = "Ask";
            askLine.Drawing.IsVisible = false;

            bidLine = Formula.Indicator();
            bidLine.Name = "Bid";
            bidLine.Drawing.IsVisible = false;

            averagePrice = Formula.Indicator();
            averagePrice.Name = "BE";
            averagePrice.Drawing.IsVisible = isVisible;
            averagePrice.Drawing.Color = Color.Black;

            waitRetraceLine = Formula.Indicator();
            waitRetraceLine.Name = "Wait Retrace";
            waitRetraceLine.Drawing.IsVisible = false;
            waitRetraceLine.Drawing.Color = Color.ForestGreen;

            rubberBand = Formula.Indicator();
            rubberBand.Name = "Rubber Band";
            rubberBand.Drawing.IsVisible = isVisible;
            rubberBand.Drawing.Color = Color.Plum;

            bestIndifferenceLine = Formula.Indicator();
            bestIndifferenceLine.Name = "Best Indifference";
            bestIndifferenceLine.Drawing.IsVisible = isVisible;
            bestIndifferenceLine.Drawing.Color = Color.Orange;

            retraceLine = Formula.Indicator();
            retraceLine.Name = "Retrace";
            retraceLine.Drawing.IsVisible = isVisible;
            retraceLine.Drawing.Color = Color.Magenta;

            maxExcursionLine = Formula.Indicator();
            maxExcursionLine.Name = "Excursion";
            maxExcursionLine.Drawing.IsVisible = enableSizing && isVisible;
            maxExcursionLine.Drawing.Color = Color.Magenta;

            position = Formula.Indicator();
            position.Name = "Position";
            position.Drawing.PaneType = PaneType.Secondary;
            position.Drawing.GroupName = "Position";
            position.Drawing.IsVisible = isVisible;

        }
        #endregion

        private void ResetRubberBand()
        {
            maxStretch = rubberBand[0] = midPoint;
        }

        public override bool OnWriteReport(string folder)
        {
            return false;
        }

        private void Reset()
        {
            if( Position.HasPosition)
            {
                buySize = 0;
                sellSize = 0;
                Orders.Exit.ActiveNow.GoFlat();
            }
        }

        #region UpdateIndicators
        private void UpdateIndicators(Tick tick)
        {
            if( double.IsNaN(maxExcursionLine[0]))
            {
                maxExcursionLine[0] = midPoint;
            }
            var equity = Performance.Equity.CurrentEquity;
            if( equity > maxEquity)
            {
                maxEquity = Performance.Equity.CurrentEquity;
                drawDown = 0D;
            }
            else
            {
                drawDown = maxEquity - equity;
                if( drawDown > maxDrawDown)
                {
                    maxDrawDown = drawDown;
                }
			}
            var rubber = rubberBand[0];
            if (double.IsNaN(rubber))
            {
                ResetRubberBand();
                rubber = rubberBand[0];
            }
            switch (stretchDirection)
            {
                case Direction.UpTrend:
                    if (marketBid > maxStretch)
                    {
                        var increase = (marketBid - maxStretch) / 2;
                        rubberBand[0] += increase;
                        maxStretch = marketBid;
                    }
                    else if (marketAsk <= rubberBand[0])
                    {
                        stretchDirection = Direction.Sideways;
                        goto Sideways;
                    }
                    break;
                case Direction.DownTrend:
                    if (marketAsk < maxStretch)
                    {
                        var decrease = (maxStretch - marketAsk) / 2;
                        rubberBand[0] -= decrease;
                        maxStretch = marketAsk;
                    }
                    else if (marketBid >= rubberBand[0])
                    {
                        stretchDirection = Direction.Sideways;
                        goto Sideways;
                    }
                    break;
                case Direction.Sideways:
            Sideways:
                    if (midPoint > rubberBand[0])
                    {
                        stretchDirection = Direction.UpTrend;
                    }
                    else
                    {
                        stretchDirection = Direction.DownTrend;
                    }
                    ResetRubberBand();
                    break;
            }
            var comboTrades = Performance.ComboTrades;
            if (comboTrades.Count > 0)
            {
                var comboTrade = comboTrades.Tail;
                indifferencePrice = CalcIndifferencePrice(comboTrade);
                if( filterIndifference)
                {
                    var avgDivergence = Math.Abs(tick.Bid - indifferencePrice) / minimumTick;
                    if (avgDivergence > 150 || Position.IsFlat)
                    {
                        averagePrice[0] = double.NaN;
                    }
                    else
                    {
                        averagePrice[0] = indifferencePrice;
                    }
                }
                else
                {
                    averagePrice[0] = indifferencePrice;
                }
            }
            else
            {
                indifferencePrice = (tick.Ask + tick.Bid)/2;
                averagePrice[0] = indifferencePrice;
            }

            if( comboTrades.Count > 0 && !comboTrades.Tail.Completed)
            {
                var lots = Position.Size/lotSize;
                var currentTrade = comboTrades.Tail;
                var retracePercent = 0.30;
                var waitRetracePercent = 0.10;

                if (double.IsNaN(retraceLine[0]))
                {
                    retraceLine[0] = midPoint;
                    waitRetraceLine[0] = midPoint;
                }

                var retraceAmount = (currentTrade.EntryPrice - maxExcursionLine[0]) * retracePercent;
                var retraceLevel = maxExcursionLine[0] + retraceAmount;
                var waitRetraceAmount = (currentTrade.EntryPrice - maxExcursionLine[0]) * waitRetracePercent;
                var waitRetraceLevel = maxExcursionLine[0] + waitRetraceAmount;
                if (Position.IsLong)
                {
                    if (retraceLevel < retraceLine[0])
                    {
                        retraceLine[0] = retraceLevel;
                    }
                    if (waitRetraceLevel < waitRetraceLine[0])
                    {
                        waitRetraceLine[0] = waitRetraceLevel;
                    }
               
                }
                if (Position.IsShort)
                {
                    if (retraceLevel > retraceLine[0])
                    {
                        retraceLine[0] = retraceLevel;
                    }
                    if (waitRetraceLevel > waitRetraceLine[0])
                    {
                        waitRetraceLine[0] = waitRetraceLevel;
                    }
                }

            } 
            else
            {
                retraceLine[0] = double.NaN;
            }

            if (bidLine.Count > 0)
            {
                position[0] = Position.Current / lotSize;
            }
        }
        #endregion

        #region OnProcessTick
        public override bool OnProcessTick(Tick tick)
        {
            if (!tick.IsQuote)
            {
                throw new InvalidOperationException("This strategy requires bid/ask quote data to operate.");
            }

            Orders.SetAutoCancel();

            CalcMarketPrices(tick);

            UpdateIndicators(tick);

            if( enableManageRisk) HandleRisk(tick);

            if( enablePegging) HandlePegging(tick);

            if (state.AnySet( StrategyState.ProcessSizing ))
            {
                PerformSizing(tick);
            }

            if (limitSize) ManageOverSize(tick);

            HandleWeekendRollover(tick);

            if( state.AnySet(StrategyState.ProcessOrders))
            {
                ProcessOrders(tick);
            }
            return true;
        }
        #endregion

        private double riskRetrace;
        private void HandleRisk(Tick tick)
        {
            if( state == StrategyState.HighRisk)
            {
                if( Position.IsLong)
                {
                    if( marketAsk < riskRetrace)
                    {
                        riskRetrace = marketAsk;
                    }
                    if( marketBid > riskRetrace)
                    {
                        state = StrategyState.Active;
                        SetupBidAsk(maxExcursionLine[0]);
                    }
                }
                if( Position.IsShort)
                {
                    if( marketBid > riskRetrace)
                    {
                        riskRetrace = marketBid;
                    }
                    if( marketAsk < riskRetrace)
                    {
                        state = StrategyState.Active;
                        SetupBidAsk(maxExcursionLine[0]);
                    }
                }
            }
            else
            {
                riskRetrace = midPoint;
            }
            //if( drawDown >= 2000)
            //{
            //    Reset();
            //}
        }

        #region WeekendRollover
        private void HandleWeekendRollover(Tick tick)
        {
            var time = tick.Time;
            var utcTime = tick.UtcTime;
            var dayOfWeek = time.GetDayOfWeek();
            switch (state)
            {
                default:
                    if (dayOfWeek == 5)
                    {
                        var hour = time.Hour;
                        var minute = time.Minute;
                        if (hour == 16 && minute > 30)
                        {
                            beforeWeekendState = state;
                            state = StrategyState.EndForWeek;
                            goto EndForWeek;
                        }
                    }
                    break;
                case StrategyState.EndForWeek:
            EndForWeek:
                    if( dayOfWeek == 5)
                    {
                        if (Position.Current != 0)
                        {
                            positionPriorToWeekend = Position.Current;
                            if (positionPriorToWeekend > 0)
                            {
                                Orders.Change.ActiveNow.SellMarket(positionPriorToWeekend);
                            }
                            else if (positionPriorToWeekend < 0)
                            {
                                Orders.Change.ActiveNow.BuyMarket(Math.Abs(positionPriorToWeekend));
                            }
                        }
                        buySize = 0;
                        sellSize = 0;
                        return;
                    }
                    if( Position.Current == positionPriorToWeekend)
                    {
                        state = beforeWeekendState;
                    }
                    else
                    {
                        if (positionPriorToWeekend > 0)
                        {
                            Orders.Change.ActiveNow.BuyMarket(positionPriorToWeekend);
                        }
                        if (positionPriorToWeekend < 0)
                        {
                            Orders.Change.ActiveNow.SellMarket(Math.Abs(positionPriorToWeekend));
                        }
                    }
                    break;
            }
        }
        #endregion

        private int lastLots;
        private void ManageOverSize( Tick tick)
        {
            var lots = Position.Size/lotSize;
            if( Position.IsShort)
            {
                if (lots >= maxLots)
                {
                    sellSize = 0;
                    buySize = lots; // Math.Max(1, maxLots / 20);
                    state = StrategyState.OverSize;
                    oversizeSide = TradeSide.Sell;
                }
                else if (state == StrategyState.OverSize)
                {
                    if( Position.IsFlat || oversizeSide == TradeSide.Buy)
                    {
                        state = StrategyState.Active;
                    }
                    else
                    {
                        sellSize = 0;
                        buySize = Math.Max(1, maxLots / 20);
                        buySize = Math.Min(lots, buySize);
                    }
                }
            }

            if( Position.IsLong)
            {
                if( lots != lastLots)
                {
                    lastLots = lots;
                }
                if (lots >= maxLots)
                {
                    buySize = 0;
                    sellSize = lots; // Math.Max(1, maxLots / 20);
                    state = StrategyState.OverSize;
                    oversizeSide = TradeSide.Buy;
                }
                else if (state == StrategyState.OverSize)
                {
                    if (Position.IsFlat || oversizeSide == TradeSide.Sell)
                    {
                        state = StrategyState.Active;
                    }
                    else
                    {
                        buySize = 0;
                        sellSize = Math.Max(1, maxLots / 20);
                        sellSize = Math.Min(lots, sellSize);
                    }
                }
            }
        }

        private void PerformSizing(Tick tick)
        {
            var lots = Position.Size/lotSize;
            sellSize = 1;
            buySize = 1;

            if( Performance.ComboTrades.Count <= 0) return;

            var size = Math.Max(2, lots / 5);
            var currentTrade = Performance.ComboTrades.Tail;
            var indifferenceCompare = retraceLine[0];
            if (Position.IsShort)
            {
                var buyIndifference = CalcIndifferenceUpdate(indifferencePrice, Position.Size, bid, -buySize * lotSize);
                var retraceDelta = indifferenceCompare - buyIndifference;
                buySize = 0;

                if (!enableSizing || lots <= 10) return;

                if ( ask > maxExcursionLine[0] &&
                    indifferencePrice < indifferenceCompare)
                {
                    sellSize = CalcAdjustmentSize(indifferencePrice, Position.Size, indifferenceCompare + retraceErrorMarginInTicks * minimumTick, ask);
                    sellSize = Math.Min(sellSize, 10000);
                    if( limitSize)
                    {
                        sellSize = Math.Min(sellSize, maxLots - lots);
                    }

                }
            }

            if (Position.IsLong)
            {
                var sellIndifference = CalcIndifferenceUpdate(indifferencePrice, Position.Size, ask, -sellSize * lotSize);
                var retraceDelta = sellIndifference - indifferenceCompare;
                sellSize = 0;

                if (!enableSizing || lots <= 10) return;

                if (bid < maxExcursionLine[0] &&
                    indifferencePrice > indifferenceCompare)
                {
                    buySize = CalcAdjustmentSize(indifferencePrice, Position.Size, indifferenceCompare - retraceErrorMarginInTicks * minimumTick, bid);
                    buySize = Math.Min(buySize, 10000);
                    if (limitSize)
                    {
                        buySize = Math.Min(buySize, maxLots - lots);
                    }
                }
            }
        }

        private void HandlePegging(Tick tick)
        {
            if( marketAsk < bid || marketBid > ask)
            {
                SetupBidAsk(midPoint);
            }
            if (marketAsk < lastMarketAsk)
            {
                lastMarketAsk = marketAsk;
                if (marketAsk < ask - increaseSpread && midPoint > lastMidPoint)
                {
                    SetupAsk(midPoint);
                }
            }

            if (marketBid > lastMarketBid)
            {
                lastMarketBid = marketBid;
                if (marketBid > bid + increaseSpread && midPoint < lastMidPoint)
                {
                    SetupBid(midPoint);
                }
            }
        }

        private void ProcessOrders(Tick tick)
        {
            if (Position.IsFlat)
            {
                OnProcessFlat(tick);
            }
            else if (Position.IsLong)
            {
                OnProcessLong(tick);
            }
            else if (Position.IsShort)
            {
                OnProcessShort(tick);
            }

        }

        private void OnProcessFlat(Tick tick)
        {
            if (isFirstTick)
            {
                isFirstTick = false;
                SetFlatBidAsk();
            }
            var comboTrades = Performance.ComboTrades;
            if (comboTrades.Count == 0 || comboTrades.Tail.Completed)
            {
                if( buySize > 0)
                {
                    Orders.Enter.ActiveNow.BuyLimit(bid, buySize * lotSize);
                }

                if( sellSize > 0)
                {
                    Orders.Enter.ActiveNow.SellLimit(ask, sellSize * lotSize);
                }
            }
            else
            {
                if( buySize > 0)
                {
                    Orders.Change.ActiveNow.BuyLimit(bid, buySize * lotSize);
                }
                if( sellSize > 0)
                {
                    Orders.Change.ActiveNow.SellLimit(ask, sellSize * lotSize);
                }
            }
        }

        private double CalcIndifferencePrice(TransactionPairBinary comboTrade)
        {
            var size = Math.Abs(comboTrade.CurrentPosition);
            if (size == 0)
            {
                return midPoint;
            }
            var sign = -Math.Sign(comboTrade.CurrentPosition);
            var openPoints = comboTrade.AverageEntryPrice.ToLong() * size;
            var closedPoints = comboTrade.ClosedPoints.ToLong() * sign;
            var grossProfit = openPoints + closedPoints;
            var transaction = 0; // size * commission * sign;
            var expectedTransaction = 0; // size * commission * sign;
            var result = (grossProfit - transaction - expectedTransaction) / size;
            result = ((result + 5000) / 10000) * 10000;
            return result.ToDouble();
        }

        private int CalcAdjustmentSize(double indifference, int size, double desiredIndifference, double currentPrice)
        {
            var lots = size/lotSize;
            var delta = Math.Abs(desiredIndifference - currentPrice);
            if (delta < minimumTick || lots > 100000)
            {
                return 1;
            }
            var result = (size*indifference - size*desiredIndifference)/(delta);
            result = Math.Abs(result);
            if( result >= int.MaxValue)
            {
                System.Diagnostics.Debugger.Break();
            }
            return Math.Max(1,(int) (result / lotSize));
        }

        private double CalcIndifferenceUpdate(double indifference, int size, double currentPrice, int changeSize)
        {
            var result = (size*indifference + changeSize*currentPrice)/(size + changeSize);
            return result;
        }

        private void OnProcessLong(Tick tick)
        {
            var lots = Position.Size / lotSize;
            var halfRetrace = maxExcursionLine[0] + (indifferencePrice - maxExcursionLine[0])/5;
            if( buySize > 0)
            {
                Orders.Change.ActiveNow.BuyLimit(bid, buySize * lotSize);
            }
            if (sellSize > 0)
            {
                if( lots == sellSize)
                {
                    Orders.Reverse.ActiveNow.SellLimit(ask, lotSize);
                }
                else
                {
                    Orders.Change.ActiveNow.SellLimit(ask, sellSize * lotSize);
                    Orders.Reverse.ActiveNow.SellLimit(indifferencePrice + closeProfitInTicks * minimumTick, sellSize * lotSize);
                }
            }
            else
            {
                Orders.Reverse.ActiveNow.SellLimit(indifferencePrice + closeProfitInTicks * minimumTick, lotSize);
            }
        }

        private void OnProcessShort(Tick tick)
        {
            var lots = Position.Size / lotSize;
            var halfRetrace = maxExcursionLine[0] - (maxExcursionLine[0] - indifferencePrice) / 5;
            if (sellSize > 0)
            {
                Orders.Change.ActiveNow.SellLimit(ask, sellSize * lotSize);
            }
            if( buySize > 0)
            {
                if (lots == buySize)
                {
                    Orders.Reverse.ActiveNow.BuyLimit(bid, lotSize);
                }
                else
                {
                    Orders.Change.ActiveNow.BuyLimit(bid, buySize * lotSize);
                    Orders.Reverse.ActiveNow.BuyLimit(indifferencePrice - closeProfitInTicks * minimumTick, buySize * lotSize);
                }
            } 
            else
            {
                Orders.Reverse.ActiveNow.BuyLimit(indifferencePrice - closeProfitInTicks * minimumTick, lotSize);
            }
        }

        public override void OnEndHistorical()
        {
            if( Performance.ComboTrades.Count > 0)
            {
                var lastTrade = Performance.ComboTrades.Tail;
                totalVolume += lastTrade.Volume;
            }
            Log.Notice("Total volume was " + totalVolume + ". With commission paid of " + ((totalVolume / 1000) * 0.02D));
        }

        public class LocalFill
        {
            public int Size;
            public double Price;
            public TimeStamp Time;
            public LocalFill(LogicalFill fill)
            {
                Size = Math.Abs(fill.Position);
                Price = fill.Price;
                Time = fill.Time;
            }
            public LocalFill(int size, double price, TimeStamp time)
            {
                Size = size;
                Price = price;
                Time = time;
            }
            public override string ToString()
            {
                return Size + " at " + Price;
            }
        }

        private void SetupBidAsk(double price)
        {
            if( Performance.ComboTrades.Count > 0)
            {
                indifferencePrice = CalcIndifferencePrice(Performance.ComboTrades.Tail);
            }
            else
            {
                indifferencePrice = price;
            }
            lastMidPoint = price;
            SetupAsk(price);
            SetupBid(price);
        }

        private void CalcMarketPrices(Tick tick)
        {
            // Calculate market prics.
            marketAsk = Math.Max(tick.Ask, tick.Bid);
            marketBid = Math.Min(tick.Ask, tick.Bid);
            midPoint = (tick.Ask + tick.Bid) / 2;
        }

        private void SetupAsk(double price)
        {
            var sequentialAdjustment = throttleIncreasing ? sequentialIncreaseCount*10*minimumTick : 0;
            var lots = Position.Size/lotSize;
            var tempVolatileSpread = Math.Min(1000*increaseSpread, Math.Pow(1.1D, lots - 1)*increaseSpread);
            var tempIncreaseSpread = state == StrategyState.HighRisk ? tempVolatileSpread : volatileSpread;
            var myAsk = Position.IsLong ? price + reduceSpread : maxExcursionLine[0] + tempIncreaseSpread + sequentialAdjustment;
            ask = Math.Max(myAsk, marketAsk);
            askLine[0] = ask;
        }

        private void SetupBid(double price)
        {
            var sequentialAdjustment = throttleIncreasing ? sequentialIncreaseCount*10*minimumTick : 0;
            var lots = Position.Size / lotSize;
            var tempVolatileSpread = Math.Min(1000 * increaseSpread, Math.Pow(1.1D, lots - 1) * increaseSpread);
            var tempIncreaseSpread = state == StrategyState.HighRisk ? tempVolatileSpread : volatileSpread;
            var myBid = Position.IsLong ? (maxExcursionLine[0] - tempIncreaseSpread) - sequentialAdjustment : price - reduceSpread;
            bid = Math.Min(myBid, marketBid);
            bidLine[0] = bid;
        }

        public override void OnEnterTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            lastSize = Math.Abs(comboTrade.CurrentPosition);
            nextIncreaseLots = 50;
            bestIndifferenceLine[0] = CalcIndifferencePrice(comboTrade);
            fills.AddFirst(new LocalFill(fill));
            maxExcursionLine[0] = fill.Price;
            sequentialIncreaseCount = 0;
            lastMarketAsk = marketAsk;
            lastMarketBid = marketBid;
            ResetRubberBand();
            maxEquity = Performance.Equity.CurrentEquity;
            maxDrawDown = 0D;
            drawDown = 0D;
            SetupBidAsk(fill.Price);
            LogFills("OnEnterTrade");
        }

        private void LogFills(string onChange)
        {
            if( IsDebug)
            {
                Log.Debug(onChange + " fills");
                for (var current = fills.First; current != null; current = current.Next)
                {
                    var fill = current.Value;
                    Log.Debug("Fill: " + fill.Size + " at " + fill.Price + " " + fill.Time);
                }
            }
        }

        public override void OnChangeTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {
            var tick = Ticks[0];
            var midpoint = (tick.Ask + tick.Bid)/2;
            if (!fill.IsComplete) return;
            if (Position.Size > maxTradeSize)
            {
                maxTradeSize = Position.Size;
            }
            if (comboTrade.CurrentPosition > 0 && fill.Price < maxExcursionLine[0])
            {
                maxExcursionLine[0] = fill.Price;
            }
            if (comboTrade.CurrentPosition < 0 && fill.Price > maxExcursionLine[0])
            {
                maxExcursionLine[0] = fill.Price;
            }
            var size = Math.Abs(comboTrade.CurrentPosition);
            var lots = size/lotSize;
            var change = size - lastSize;
            lastSize = size;
            if (change > 0)
            {
                if( enableManageRisk)
                {
                    state = StrategyState.HighRisk;
                }
                var tempIndifference = CalcIndifferencePrice(comboTrade);
                if( Position.IsLong && tempIndifference < bestIndifferenceLine[0])
                {
                    bestIndifferenceLine[0] = tempIndifference;
                }
                if( Position.IsShort && tempIndifference > bestIndifferenceLine[0])
                {
                    bestIndifferenceLine[0] = tempIndifference;
                }
                if (lots > nextIncreaseLots)
                {
                    nextIncreaseLots = (nextIncreaseLots * 3) / 2;
                }
                sequentialIncreaseCount++;
                var changed = false;
                change = Math.Abs(change);
                if (fills.First != null)
                {
                    var firstFill = fills.First.Value;
                    if (firstFill.Size + change <= lotSize)
                    {
                        firstFill.Size += change;
                        changed = true;
                    }
                }
                if( !changed)
                {
                    fills.AddFirst(new LocalFill(change, fill.Price, fill.Time));
                }
                SetupBidAsk(fill.Price);
            }
            else
            {
                sequentialIncreaseCount=0;
                change = Math.Abs(change);
                var prevFill = fills.First.Value;
                if (change <= prevFill.Size)
                {
                    prevFill.Size -= change;
                    if (prevFill.Size == 0)
                    {
                        fills.RemoveFirst();
                        if (fills.Count > 0)
                        {
                            SetupBidAsk(fill.Price);
                        }
                    }
                    return;
                }
                change -= prevFill.Size;
                fills.RemoveFirst();
                SetupBidAsk(fill.Price);
                return;

                for (var current = fills.Last; current != null; current = current.Previous)
                {
                    prevFill = current.Value;
                    if (change > prevFill.Size)
                    {
                        change -= prevFill.Size;
                        fills.Remove(current);
                        if (fills.Count > 0)
                        {
                            SetupBidAsk(fill.Price);
                        }
                    }
                    else
                    {
                        prevFill.Size -= change;
                        if (prevFill.Size == 0)
                        {
                            fills.Remove(current);
                            if (fills.Count > 0)
                            {
                                SetupBidAsk(fill.Price);
                            }
                        }
                        break;
                    }
                }
            }
            LogFills("OnChange");
        }

        private void SetFlatBidAsk()
        {
            var tick = Ticks[0];
            var midPoint = (tick.Bid + tick.Ask) / 2;
            var myAsk = midPoint + increaseSpread / 2;
            var myBid = midPoint - increaseSpread / 2;
            marketAsk = Math.Max(tick.Ask, tick.Bid);
            marketBid = Math.Min(tick.Ask, tick.Bid);
            ask = Math.Max(myAsk, marketAsk);
            bid = Math.Min(myBid, marketBid);
            bidLine[0] = bid;
            askLine[0] = ask;
        }

        private long lessThan100Count;
        public override void OnExitTrade(TransactionPairBinary comboTrade, LogicalFill fill, LogicalOrder filledOrder)
        {            
            fills.Clear();
            SetFlatBidAsk();
            bestIndifferenceLine[0] = double.NaN;
            if (!comboTrade.Completed)
            {
                throw new InvalidOperationException("Trade must be completed.");
            }
            totalVolume += comboTrade.Volume;
            maxExcursionLine[0] = double.NaN;
            if( maxDrawDown < 500.00)
            {
                lessThan100Count++;
            }
            else
            {
                var pnl = Performance.ComboTrades.CurrentProfitLoss;
                Log.Info(Math.Round(maxDrawDown,2)+","+Math.Round(pnl,2)+","+Performance.Equity.CurrentEquity+","+lessThan100Count+","+comboTrade.EntryTime+","+comboTrade.ExitTime);
            }
            //if (maxTradeSize <= 100 * lotSize)
            //{
            //    lessThan100Count++;
            //}
            //else
            //{
            //    Log.Info((maxTradeSize / lotSize) + "," + lessThan100Count + "," + fill.Time);
            //}
            maxTradeSize = 0;
            LogFills("OnEnterTrade");
        }

        public bool IsVisible
        {
            get { return isVisible; }
            set { isVisible = value; }
        }

    }
}