using WearableDistributor;

internal static class OutfitSelectionChecks
{
    public static void Run()
    {
        Require(OutfitPartSelection.BodySlots("4").SetEquals(OutfitPartSelection.BodySlots("Body")), "Numeric and named body slots must conflict.");
        Require(OutfitPartSelection.BodySlots("132").SetEquals(OutfitPartSelection.BodySlots("Body | Feet")), "A numeric bit mask must expand into all its slots.");
        Require(OutfitPartSelection.BodySlots("Long Hair, Unnamed46").SetEquals(["Slot 41", "Slot 46"]), "Extended and spaced slot names must normalize.");
        var pieces = new List<WearableRecord>
        {
            Part(0x800, "Tunic (Dark)", "Body"), Part(0x801, "Tunic alternate (Dark)", "4"),
            Part(0x802, "Tunic (Light)", "Body"), Part(0x803, "Boots (Dark)", "Feet"),
            Part(0x804, "Boots (Light)", "Feet"), Part(0x805, "Belt", "Unnamed49")
        };
        var selected = OutfitPartSelection.FindCompatibleSet(pieces);
        Require(selected.Select(piece => piece.LocalId).ToHashSet().SetEquals([0x800U, 0x803U, 0x805U]), "Compatible selection must choose one color, preserve shared parts, and avoid duplicate slots.");
        Require(pieces.All(piece => piece.Include), "A suggested set must not mutate saved selection without the user's explicit action.");
        Require(OutfitPartSelection.FindCompatibleSet([]).Count == 0, "Empty candidate lists must be supported.");
    }

    private static WearableRecord Part(uint id, string name, string slots) => new() { Plugin = "ExampleArmor.esp", LocalId = id, Name = name, Slots = slots };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
