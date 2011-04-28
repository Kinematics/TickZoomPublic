using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Examples
{
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