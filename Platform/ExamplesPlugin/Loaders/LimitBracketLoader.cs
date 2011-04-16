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

        public override void OnLoad(ProjectProperties model)
        {
            TopModel = new SimpleStrategy();
        }

    }
    public class LimitBracketLoader : ModelLoaderCommon
    {
        public LimitBracketLoader()
        {
            /// <summary>
            /// IMPORTANT: You can personalize the name of each model loader.
            /// </summary>
            category = "Example";
            name = "Limit Order Bracket Single-Symbol";
        }

        public override void OnInitialize(ProjectProperties properties)
        {
        }

        public override void OnLoad(ProjectProperties model)
        {
            TopModel = new LimitBracketStrategy();
        }

    }
}