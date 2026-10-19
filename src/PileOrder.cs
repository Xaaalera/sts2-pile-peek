namespace PilePeek;

internal static class PileOrder
{
    internal static IReadOnlyList<T> ForDisplay<T>(IEnumerable<T> cards, bool isDraw, Comparison<T> comparison)
    {
        var result = cards.ToList();
        if (isDraw) { result.Sort(comparison); }
        return result;
    }
}
