using Xunit;

namespace PilePeek.Tests;

public sealed class PileOrderTests
{
    private sealed record Card(string Identity, int Rarity);
    private static int Compare(Card first, Card second) => first.Rarity != second.Rarity
        ? first.Rarity.CompareTo(second.Rarity) : string.Compare(first.Identity, second.Identity, StringComparison.Ordinal);

    [Fact]
    public void Draw_preview_is_the_same_for_every_hidden_permutation_and_never_mutates_the_pile()
    {
        Card[] cards = [new("STRIKE", 0), new("DEFEND", 0), new("RARE", 2)];
        foreach (var first in cards)
        foreach (var second in cards.Where(card => !ReferenceEquals(card, first)))
        {
            var third = cards.Single(card => !ReferenceEquals(card, first) && !ReferenceEquals(card, second));
            var input = new[] { first, second, third };
            var before = input.ToArray();
            var displayed = PileOrder.ForDisplay(input, true, Compare);
            Assert.Equal(new[] { "DEFEND", "STRIKE", "RARE" }, displayed.Select(card => card.Identity));
            Assert.Equal(before, input);
        }
    }

    [Fact]
    public void Non_draw_piles_preserve_the_native_entry_sequence_without_calling_a_sorter()
    {
        Card[] cards = [new("RARE", 2), new("STRIKE", 0), new("DEFEND", 0)];
        var displayed = PileOrder.ForDisplay(cards, false, (_, _) => throw new InvalidOperationException("Unexpected sorting"));
        Assert.Equal(cards, displayed);
        Assert.NotSame(cards, displayed);
    }

    [Fact]
    public void Duplicate_card_instances_are_not_lost_or_grouped_by_title()
    {
        Card[] cards = [new("STRIKE", 0), new("STRIKE", 0), new("DEFEND", 0)];
        var displayed = PileOrder.ForDisplay(cards, true, Compare);
        Assert.Equal(3, displayed.Count);
        Assert.All(cards, card => Assert.Contains(displayed, candidate => ReferenceEquals(candidate, card)));
    }

    [Fact]
    public void Empty_piles_need_no_sort_or_random_selection()
    {
        Assert.Empty(PileOrder.ForDisplay(Array.Empty<Card>(), true, (_, _) => throw new InvalidOperationException("Unexpected comparison")));
    }
}
