using Shapes.Core.Primitives;
using Shapes.Core.State;

namespace Shapes.Godot.Adapter;

// Picks the two card arts used as player portraits for a match.
//
// The avatars are pure decoration -- nothing about them reaches GameState -- but they still must
// be DERIVED FROM THE MATCH SEED rather than drawn fresh, for two reasons:
//
//   1. Resuming. A saved match replays its seed through the action log to rebuild state (see
//      GameSession.Resume); the avatars are not in the log, so seeding them from anything else
//      would hand a resumed game two new faces mid-match.
//   2. Never touching the game's IRandomSource. That stream drives the shuffle and every random
//      effect, so drawing two numbers from it would shift every subsequent draw and desync the
//      replay outright. This takes the seed VALUE and builds its own SeededRandom instead.
//
// The candidate list is passed in rather than read from the card database: only cards that
// actually have an art file can serve as a portrait, and which those are is a question about
// res:// contents that only the Godot layer can answer (see CardArt.Has).
public static class AvatarPicker
{
    // One card that could serve as a portrait: its id, and the resource type its play cost is
    // paid in. Only creatures are offered (a spell has no board presence, so a spell illustration
    // as a player's face reads as an effect that is in play rather than as an avatar), and the
    // caller does that filtering -- CardKind lives in Shapes.Core, so a candidate list is already
    // a card-database question by the time it reaches here.
    //
    // Type is what the two seats must DIFFER on. "Type comes from resource cost, always"
    // (PLAN.md 0), so this is CardText.SinglePipType of the card's cost -- the same derivation
    // the cost badge and placeholder art use, not a second opinion about what a card's type is.
    public readonly record struct Candidate(string Id, ResourceType Type);

    // Mixed into the match seed so the avatar stream cannot coincide with any other seed-derived
    // stream -- BuildAgents derives its per-seat agent streams by multiplying the same seed, and
    // an unmixed reuse here would tie a portrait to an agent's first decision.
    private const ulong SeedMixer = 0x9E3779B97F4A7C15;

    // Two cards of DIFFERENT resource types, or fewer when the candidates cannot supply two.
    //
    // Different types rather than merely different cards: the portraits are the one place a seat
    // is identified by a picture rather than a label, so two same-type faces (two wheel
    // creatures) would be two variations on the same colour and shape family sitting at opposite
    // ends of the rail -- exactly the confusion the avatars exist to prevent.
    //
    // Returns null for a seat rather than throwing when there is nothing to pick: art is filled
    // in one card at a time (CardArt's own note), so a pool too thin to offer two distinct types
    // is a legitimate state that must degrade to the flat placeholder, not crash the match.
    public static (string? One, string? Two) Pick(ulong seed, IEnumerable<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // Sorted before picking, so the result depends only on the SET of available art and the
        // seed -- not on the order the caller happened to enumerate the card database in.
        // Distinct by ID: the same card cannot appear twice under two types.
        var pool = candidates
            .DistinctBy(candidate => candidate.Id, StringComparer.Ordinal)
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToList();

        if (pool.Count == 0)
        {
            return (null, null);
        }

        var random = new SeededRandom(seed ^ SeedMixer);

        var first = pool[random.Next(pool.Count)];

        // The second seat draws only from the cards whose type differs from the first's, which is
        // what enforces the rule directly rather than by rejection-sampling until the types
        // happen to disagree -- rerolling would consume an unbounded number of draws (making the
        // stream's position depend on luck rather than the seed) and would never terminate at all
        // on a single-type pool.
        var others = pool.Where(candidate => candidate.Type != first.Type).ToList();
        if (others.Count == 0)
        {
            // Every art file shares one type. The first seat still gets a portrait and the second
            // falls back to the placeholder: showing a same-type face would break the very
            // distinction this method exists to guarantee, and showing the SAME face twice would
            // read as a rendering bug rather than as a thin art set.
            return (first.Id, null);
        }

        return (first.Id, others[random.Next(others.Count)].Id);
    }
}
