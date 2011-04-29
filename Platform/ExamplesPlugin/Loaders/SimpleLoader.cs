using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
    public class SimpleLoader : ModelLoaderCommon
    {
        public SimpleLoader()
        {
            /// <summary>
            /// IMPORTANT: You can personalize the name of each model loader.
            /// </summary>
            category = "Example";
            name = "Simple Single-Symbol";
        }

        public override void OnInitialize(ProjectProperties properties)
        {
        }

        public override void OnLoad(ProjectProperties properties)
        {
            foreach( var symbol in properties.Starter.SymbolProperties)
            {
                symbol.LimitOrderQuoteSimulation = LimitOrderQuoteSimulation.SameSideQuoteThrough;
                symbol.LimitOrderTradeSimulation = LimitOrderTradeSimulation.None;
            }
            var portfolio = new SimplePortfolio();
            var strategy = new SimpleStrategy();
            strategy.IsActive = false;
            portfolio.AddDependency(strategy);
            strategy = new SimpleStrategy();
            strategy.IsActive = false;
            portfolio.AddDependency(strategy);
            TopModel = portfolio;
        }
    }
}