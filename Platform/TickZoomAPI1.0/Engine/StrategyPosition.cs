namespace TickZoom.Api
{
    public interface StrategyPosition
    {
        int ActualPosition { get; }
        long Recency { get; }
        int ExpectedPosition { get; }
        SymbolInfo Symbol { get; }
        int Id { get; }
        void SetExpectedPosition(int position);
        void SetActualPosition(int position);
        void TrySetPosition( int position, long recency);
    }
}