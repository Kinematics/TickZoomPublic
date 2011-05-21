using TickZoom.Common;

namespace TickZoom.Examples
{
    public class SimplePortfolio : Portfolio
    {
        private SimpleStrategy shortSide;
        private SimpleStrategy longSide;
        private int lotSize = 1000;
        private int maxLots = int.MaxValue;
        public SimplePortfolio()
        {
        }

        public override void OnInitialize()
        {
            Performance.Equity.GraphEquity = true;
            shortSide = Strategies[0] as SimpleStrategy;
            shortSide.Name = "Short Strategy";
            shortSide.OnDirectionChange = OnDirectionChange;
            shortSide.IsActive = true;
            shortSide.IsVisible = true;
            longSide = Strategies[1] as SimpleStrategy;
            longSide.Name = "Next Strategy";
            longSide.IsVisible = true;
            longSide.IsActive = false;
        }

        private int IncreaseLotSize = 4;

        public override bool OnProcessTick(TickZoom.Api.Tick tick)
        {
            //var shortLots = shortSide.Position.Size/lotSize;
            //var longLots = longSide.Position.Size/lotSize;
            //if( shortLots > 20 && longLots < 20)
            //{
            //    longSide.IncreaseLotSize = 2 * lotSize;
            //    shortSide.IncreaseLotSize = lotSize;
            //}
            //else if( shortLots < 20 && longLots > 20)
            //{
            //    shortSide.IncreaseLotSize = 2 * lotSize;
            //    longSide.IncreaseLotSize = lotSize;
            //} else
            //{
            //    shortSide.IncreaseLotSize = lotSize;
            //    longSide.IncreaseLotSize = lotSize;
            //}
            return true;
        }

        public void OnDirectionChange(SimpleStrategy strategy)
        {
            return;
        }
    }
}