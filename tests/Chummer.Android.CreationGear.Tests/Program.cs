using Chummer.Android.Native;
using Chummer.Contracts.Characters;

internal static class Program
{
    private static void Main()
    {
        IncrementRemoveAndBoundsFailClosed();
        SelectionProjectionIsStableAndSorted();
        Console.WriteLine("Creation Gear phone interaction tests passed: 2");
    }

    private static void IncrementRemoveAndBoundsFailClosed()
    {
        var empty = new Dictionary<string, int>(StringComparer.Ordinal);
        Require(CreationGearPhoneBasket.TrySetQuantity(
            empty, "gear-b", 1, maximumLines: 2, maximumQuantity: 3, out Dictionary<string, int> first));
        Require(empty.Count == 0);
        Require(first.Count == 1 && first["gear-b"] == 1);
        Require(CreationGearPhoneBasket.TrySetQuantity(
            first, "gear-b", 3, maximumLines: 2, maximumQuantity: 3, out Dictionary<string, int> maximum));
        Require(!CreationGearPhoneBasket.TrySetQuantity(
            maximum, "gear-b", 4, maximumLines: 2, maximumQuantity: 3, out _));
        Require(CreationGearPhoneBasket.TrySetQuantity(
            maximum, "gear-a", 1, maximumLines: 2, maximumQuantity: 3, out Dictionary<string, int> full));
        Require(!CreationGearPhoneBasket.TrySetQuantity(
            full, "gear-c", 1, maximumLines: 2, maximumQuantity: 3, out _));
        Require(CreationGearPhoneBasket.TrySetQuantity(
            full, "gear-b", 0, maximumLines: 2, maximumQuantity: 3, out Dictionary<string, int> removed));
        Require(removed.Count == 1 && removed.ContainsKey("gear-a"));
    }

    private static void SelectionProjectionIsStableAndSorted()
    {
        var basket = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["gear-z"] = 2,
            ["gear-a"] = 1
        };
        Require(CreationGearPhoneBasket.TryCreateSelections(
            basket, maximumLines: 2, maximumQuantity: 3, out CharacterCreationGearSelection[] selections));
        Require(selections.SequenceEqual(
        [
            new CharacterCreationGearSelection("gear-a", 1),
            new CharacterCreationGearSelection("gear-z", 2)
        ]));
        Require(!CreationGearPhoneBasket.TryCreateSelections(
            new Dictionary<string, int> { ["gear-a"] = 0 },
            maximumLines: 2,
            maximumQuantity: 3,
            out _));
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Creation Gear phone interaction assertion failed.");
    }
}
