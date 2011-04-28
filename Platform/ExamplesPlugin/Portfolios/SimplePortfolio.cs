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
            shortSide.IsVisible = false;
            shortSide.Direction = Direction.Short;
            shortSide.MaxLots = maxLots;
            longSide = Strategies[1] as SimpleStrategy;
            longSide.Name = "Next Strategy";
            longSide.Direction = Direction.Long;
            longSide.IsVisible = true;
            longSide.IsActive = true;
            longSide.MaxLots = maxLots;
        }

        public override bool OnProcessTick(TickZoom.Api.Tick tick)
        {
            var shortLots = shortSide.Position.Size/lotSize;
            var longLots = longSide.Position.Size/lotSize;
            if( shortLots > 20 && longLots < 20)
            {
                longSide.LotSize = 2 * lotSize;
                shortSide.LotSize = lotSize;
            }
            else if( shortLots < 20 && longLots > 20)
            {
                shortSide.LotSize = 2 * lotSize;
                longSide.LotSize = lotSize;
            } else
            {
                shortSide.LotSize = lotSize;
                longSide.LotSize = lotSize;
            }
            return true;
        }

        public void OnDirectionChange(SimpleStrategy strategy)
        {
            return;
        }
    }
}