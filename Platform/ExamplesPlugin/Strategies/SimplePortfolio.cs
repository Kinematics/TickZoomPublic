using TickZoom.Common;

namespace TickZoom.Examples
{
    public class SimplePortfolio : Portfolio
    {
        private SimpleStrategy start;
        private SimpleStrategy next;
        public SimplePortfolio()
        {
        }

        public override void OnInitialize()
        {
            Performance.Equity.GraphEquity = true;
            start = Strategies[0] as SimpleStrategy;
            start.Name = "Short Strategy";
            start.OnDirectionChange = OnDirectionChange;
            start.IsActive = true;
            start.IsVisible = false;
            start.Direction = Direction.Both;
            next = Strategies[1] as SimpleStrategy;
            next.Name = "Next Strategy";
            next.IsVisible = false;

        }

        public void OnDirectionChange(SimpleStrategy strategy)
        {
            switch( strategy.Direction)
            {
                case Direction.Short:
                    next.IsActive = true;
                    next.Direction = Direction.Long;
                    break;
                case Direction.Long:
                    next.IsActive = true;
                    next.Direction = Direction.Short;
                    break;
                case Direction.Both:
                    break;
            }
        }
    }
}