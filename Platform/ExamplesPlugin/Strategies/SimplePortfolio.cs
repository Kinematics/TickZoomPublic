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
            start.IsVisible = true;
            start.Direction = Direction.Short;
            next = Strategies[1] as SimpleStrategy;
            next.Name = "Next Strategy";
            next.Direction = Direction.Long;
            next.IsVisible = true;
            next.IsActive = true;

        }

        public void OnDirectionChange(SimpleStrategy strategy)
        {
            return;
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