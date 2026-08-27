using Shapes.Ai.Agents;
using Shapes.Ai.Search;
using Shapes.Core.Actions;
using Shapes.Core.Cards;
using Shapes.Core.Primitives;
using Shapes.Core.Rules;
using Shapes.Core.State;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Ai;

// DESIGN.md step 2.3, and the suite its own "Determinization must respect observations -- sampling
// a deck containing a card already in the graveyard is a correctness bug that silently degrades
// play. Needs its own test suite" calls for.
//
// Three properties, in descending order of how badly a violation hurts:
//
//   CONSISTENCY -- the sampled world agrees with every observation. A determinizer that gets
//   this wrong searches positions that cannot happen, and its conclusions are about a different
//   game than the one being played.
//
//   SOUNDNESS -- no sampled world is impossible. This is the multiset-accounting half: a hand
//   containing a card that is sitting in the opponent's discard pile is the specific failure the
//   plan names, and it is invisible without a test because such a state plays perfectly legally.
//
//   NON-DEGENERACY -- it actually samples. Every consistency test above passes for an
//   implementation that deals the unseen cards in sorted order every time, which would collapse
//   IS-MCTS into searching one arbitrary world very thoroughly. These are the tests that stop
//   that, and they are why the suite runs over many seeds rather than one.
//
// Uses the REAL card set rather than synthetic cards: the decklist accounting is the thing under
// test, and a two-card fixture deck would not exercise the multiset arithmetic that a 36-card
// set with copiesPerCard = 2 does.
public class DeterminizerTests
{
    private static CardDatabase Cards { get; } =
        CardLoader.FromDirectory(Path.Combine(AppContext.BaseDirectory, "Content", "cards"));

    private static RuleSet Rules => RuleSet.Default;

    private static Determinizer Subject => new(Cards);

    // -- Consistency: the sample agrees with what was observed -----------------------------------

    [Fact]
    public void The_observers_own_hand_is_reproduced_exactly()
    {
        var (observed, _) = Observe(seed: 7);

        var sampled = Subject.Determinize(observed, new SeededRandom(1));

        Assert.Equal(observed.Self.Hand, sampled[observed.Observer].Hand);
    }

    [Fact]
    public void Both_discard_piles_are_reproduced_exactly()
    {
        var (observed, _) = Observe(seed: 11, actions: 400);

        var sampled = Subject.Determinize(observed, new SeededRandom(1));

        Assert.Equal(observed.Self.Discard, sampled[observed.Observer].Discard);
        Assert.Equal(observed.Opponent.Discard, sampled[observed.Observer.Opponent()].Discard);
    }

    [Fact]
    public void Resources_scores_and_turn_bookkeeping_are_reproduced_exactly()
    {
        var (observed, _) = Observe(seed: 13, actions: 250);

        var sampled = Subject.Determinize(observed, new SeededRandom(1));
        var self = sampled[observed.Observer];
        var opponent = sampled[observed.Observer.Opponent()];

        Assert.Equal(observed.Self.Resources, self.Resources);
        Assert.Equal(observed.Self.PendingNextTurnResources, self.PendingNextTurnResources);
        Assert.Equal(observed.Self.Score, self.Score);
        Assert.Equal(observed.Opponent.Resources, opponent.Resources);
        Assert.Equal(observed.Opponent.Score, opponent.Score);

        Assert.Equal(observed.ActivePlayer, sampled.ActivePlayer);
        Assert.Equal(observed.Phase, sampled.Phase);
        Assert.Equal(observed.TurnNumber, sampled.TurnNumber);
        Assert.Equal(observed.PendingDiscards, sampled.PendingDiscards);
    }

    [Fact]
    public void Hand_and_deck_sizes_match_the_observed_counts_on_both_sides()
    {
        // The sizes are observations, not guesses. Getting them wrong would let the search
        // believe the opponent can play more (or fewer) cards than they hold.
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var (observed, _) = Observe(seed, actions: 200);
            var sampled = Subject.Determinize(observed, new SeededRandom(seed));

            var self = sampled[observed.Observer];
            var opponent = sampled[observed.Observer.Opponent()];

            Assert.Equal(observed.Self.Hand.Count, self.Hand.Count);
            Assert.Equal(observed.Self.DeckSize, self.Deck.Count);
            Assert.Equal(observed.Opponent.HandSize, opponent.Hand.Count);
            Assert.Equal(observed.Opponent.DeckSize, opponent.Deck.Count);
        }
    }

    [Fact]
    public void The_board_is_reproduced_but_not_shared_with_the_real_game()
    {
        // ObservedState.Board is the LIVE board object. A rollout mutating the sampled state must
        // not reach back into the real game, so the sample must hold clones -- equal in content,
        // different in identity.
        var (observed, real) = Observe(seed: 17, actions: 150);

        var sampled = Subject.Determinize(observed, new SeededRandom(1));

        foreach (var slot in AllSlots())
        {
            var realCreature = real.Board[slot];
            var sampledCreature = sampled.Board[slot];

            if (realCreature is null)
            {
                Assert.Null(sampledCreature);
                continue;
            }

            Assert.NotNull(sampledCreature);
            Assert.NotSame(realCreature, sampledCreature);
            Assert.Equal(realCreature.CardId, sampledCreature!.CardId);
            Assert.Equal(realCreature.Health, sampledCreature.Health);
            Assert.Equal(realCreature.MaxHealth, sampledCreature.MaxHealth);
            Assert.Equal(realCreature.MergedFrom, sampledCreature.MergedFrom);
        }

        // Mutating the sample must leave the real board untouched -- the property the clone
        // exists for, asserted directly rather than inferred from NotSame.
        var occupied = AllSlots().First(s => sampled.Board[s] is not null);
        var before = real.Board[occupied]!.Health;
        sampled.Board[occupied]!.TakeDamage(1);

        Assert.Equal(before, real.Board[occupied]!.Health);
    }

    [Fact]
    public void The_observers_own_deck_matches_its_real_composition_as_a_multiset()
    {
        // Composition is known to its owner; only the ORDER is not. So the sampled deck must be
        // a permutation of the real one -- not a resample of it.
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var (observed, real) = Observe(seed, actions: 200);
            var sampled = Subject.Determinize(observed, new SeededRandom(seed));

            AssertSameMultiset(
                real[observed.Observer].Deck,
                sampled[observed.Observer].Deck,
                "the observer's own deck");
        }
    }

    [Fact]
    public void The_sampled_state_is_playable_by_the_ordinary_engine()
    {
        // The payoff of returning a real GameState: the whole existing engine runs on it
        // unchanged. A sample the generator cannot produce actions for would deadlock a search.
        for (ulong seed = 1; seed <= 25; seed++)
        {
            var (observed, _) = Observe(seed, actions: 200);
            var sampled = Subject.Determinize(observed, new SeededRandom(seed));

            if (sampled.IsOver)
            {
                continue;
            }

            var actions = ActionGenerator.Generate(sampled, Cards);
            Assert.NotEmpty(actions);

            // And it can actually be played forward, not merely enumerated.
            var random = new SeededRandom(seed);
            for (var i = 0; i < 100 && !sampled.IsOver; i++)
            {
                var legal = ActionGenerator.Generate(sampled, Cards);
                if (legal.Count == 0)
                {
                    break;
                }

                ActionExecutor.Apply(sampled, Cards, legal[random.Next(legal.Count)]);
            }
        }
    }

    // -- Soundness: no impossible world ----------------------------------------------------------

    [Fact]
    public void Sampled_opponent_cards_never_include_a_card_that_is_visibly_elsewhere()
    {
        // THE bug DESIGN.md names: sampling a deck containing a card already in the graveyard. Also
        // covers the board, which is equally visible. Multiset arithmetic throughout -- with
        // copiesPerCard = 2 the question is never "is this card present" but "how many are left".
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var (observed, _) = Observe(seed, actions: 300);
            var sampled = Subject.Determinize(observed, new SeededRandom(seed));

            var opponentId = observed.Observer.Opponent();
            var opponent = sampled[opponentId];

            var accounted = new List<string>();
            accounted.AddRange(opponent.Hand);
            accounted.AddRange(opponent.Deck);
            accounted.AddRange(opponent.Discard);

            foreach (var (_, creature) in sampled.Board.CreaturesOf(opponentId))
            {
                if (!creature.IsToken)
                {
                    accounted.AddRange(creature.MergedFrom);
                }
            }

            // If the total across every zone equals the decklist exactly, then no zone can hold
            // a card another zone has already claimed -- the soundness property, stated as
            // conservation rather than as a per-card search.
            AssertSameMultiset(
                Cards.BuildSymmetricDeck(Rules), accounted,
                $"seed {seed}: the sampled opponent's cards");
        }
    }

    [Fact]
    public void Every_sampled_card_id_exists_in_the_card_database()
    {
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var (observed, _) = Observe(seed, actions: 200);
            var sampled = Subject.Determinize(observed, new SeededRandom(seed));

            foreach (var playerId in PlayerIds.All)
            {
                foreach (var cardId in sampled[playerId].Hand.Concat(sampled[playerId].Deck))
                {
                    Assert.True(
                        Cards.Contains(cardId),
                        $"seed {seed}: sampled a card id '{cardId}' that is not a real card.");
                }
            }
        }
    }

    [Fact]
    public void A_card_the_opponent_has_fully_discarded_is_never_dealt_back_to_them()
    {
        // The multiset rule at its sharpest: with copiesPerCard = 2, discarding BOTH copies of a
        // card means the opponent cannot possibly hold one. A set-based implementation passes the
        // conservation test above by luck of totals; this one it cannot pass.
        var cardId = Cards.All[0].Id;
        var decklist = Cards.BuildSymmetricDeck(Rules);

        // The opponent's remaining cards: the decklist minus the copies now in their discard.
        // Built exactly, because the determinizer's own guard rejects a position whose zones do
        // not sum to the decklist -- and rightly so, since such a position cannot arise in play.
        var remaining = decklist.Where(c => c != cardId).ToList();
        var opponentHand = remaining.Take(3).ToArray();
        var opponentDeck = remaining.Skip(3).ToArray();

        var state = new StateBuilder()
            .P1(p => p.Hand([.. decklist.Take(4)]).Deck([.. decklist.Skip(4)]))
            .P2(p => p.Hand(opponentHand).Deck(opponentDeck))
            .Build();

        // Both copies visibly in the opponent's discard, which is what makes holding one
        // impossible rather than merely unlikely.
        state[PlayerId.Two].SendToDiscard(cardId);
        state[PlayerId.Two].SendToDiscard(cardId);

        var observed = new ObservedState(state, PlayerId.One);

        for (ulong seed = 1; seed <= 50; seed++)
        {
            var sampled = Subject.Determinize(observed, new SeededRandom(seed));
            var opponent = sampled[PlayerId.Two];

            Assert.DoesNotContain(cardId, opponent.Hand);
            Assert.DoesNotContain(cardId, opponent.Deck);
        }
    }

    [Fact]
    public void A_custom_deck_ruleset_is_refused_rather_than_sampled_incorrectly()
    {
        // The reconstruction depends on the opponent's decklist being public, which custom decks
        // are not. Failing loudly beats sampling a decklist the opponent never played.
        var state = new StateBuilder()
            .WithRuleSet(RuleSetTestHelper.CustomDeck())
            .P1(p => p.Hand("a"))
            .Build();

        var observed = new ObservedState(state, PlayerId.One);

        var ex = Assert.Throws<InvalidOperationException>(
            () => Subject.Determinize(observed, new SeededRandom(1)));
        Assert.Contains("symmetric", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -- Explicit opponent decklists (non-symmetric play) ----------------------------------------
    //
    // Supplying the opponent's real decklist is a deliberate temporary information cheat that
    // keeps IS-MCTS usable on random/custom decks -- see Determinizer's class note and DESIGN.md
    // D1. These pin that the supplied list is what actually gets sampled from.

    [Fact]
    public void An_explicit_opponent_deck_is_sampled_instead_of_the_symmetric_decklist()
    {
        // The opponent's whole deck is one card id repeated, which the symmetric decklist would
        // never produce -- so if every sampled card is that id, the supplied list is genuinely
        // the source rather than a decoration.
        var only = Cards.All[0].Id;
        var state = NewGame(seed: 5);
        var observed = new ObservedState(state, PlayerId.One);

        // Sized to exactly the opponent's unseen cards: the deal is an accounting identity, not a
        // best effort, and a list that does not cover hand + deck is correctly rejected.
        var unseen = observed.Opponent.HandSize + observed.Opponent.DeckSize;
        var opponentDeck = new Deck("mono", Enumerable.Repeat(only, unseen));
        var subject = new Determinizer(Cards, opponentDeck);

        var sampled = subject.Determinize(observed, new SeededRandom(1));
        var opponent = sampled[PlayerId.Two];

        Assert.NotEmpty(opponent.Hand);
        Assert.All(opponent.Hand, id => Assert.Equal(only, id));
        Assert.All(opponent.Deck, id => Assert.Equal(only, id));
    }

    [Fact]
    public void An_explicit_opponent_deck_lifts_the_symmetric_ruleset_guard()
    {
        // With the decklist in hand there is nothing left to infer from the deck mode, so the
        // guard must apply to the symmetric FALLBACK only -- otherwise custom-deck play could
        // never use IS-MCTS at all, which is the whole reason this path exists.
        var only = Cards.All[0].Id;
        var state = new StateBuilder()
            .WithRuleSet(RuleSetTestHelper.CustomDeck())
            .P1(p => p.Hand(only))
            .P2(p => p.Hand(only))
            .Build();

        var observed = new ObservedState(state, PlayerId.One);

        // One unseen opponent card (their hand), so a one-card decklist covers it exactly.
        var subject = new Determinizer(Cards, new Deck("d", [only]));

        // The point is that this does not throw about symmetry under a custom-deck ruleset.
        var sampled = subject.Determinize(observed, new SeededRandom(1));

        Assert.Equal(only, Assert.Single(sampled[PlayerId.Two].Hand));
    }

    [Fact]
    public void Observed_opponent_hand_and_deck_sizes_still_hold_with_an_explicit_deck()
    {
        // The sizes are observations, not guesses, on this path exactly as on the symmetric one.
        var state = NewGame(seed: 11);
        var observed = new ObservedState(state, PlayerId.One);

        var unseen = observed.Opponent.HandSize + observed.Opponent.DeckSize;
        var opponentDeck = new Deck("mono", Enumerable.Repeat(Cards.All[0].Id, unseen));
        var subject = new Determinizer(Cards, opponentDeck);

        var sampled = subject.Determinize(observed, new SeededRandom(2));

        Assert.Equal(observed.Opponent.HandSize, sampled[PlayerId.Two].Hand.Count);
        Assert.Equal(observed.Opponent.DeckSize, sampled[PlayerId.Two].Deck.Count);
    }

    // -- Non-degeneracy: it is genuinely sampling ------------------------------------------------

    [Fact]
    public void Different_seeds_produce_different_opponent_hands()
    {
        // Guards against the implementation that passes every consistency test by dealing the
        // unseen cards in sorted order: correct, consistent, and useless for search, since every
        // IS-MCTS iteration would examine the identical imagined world.
        var (observed, _) = Observe(seed: 23, actions: 200);

        var hands = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var sampled = Subject.Determinize(observed, new SeededRandom(seed));
            hands.Add(string.Join(",", sampled[observed.Observer.Opponent()].Hand));
        }

        Assert.True(
            hands.Count > 1,
            "Every seed produced the same opponent hand -- the determinizer is not sampling, so "
            + "per-iteration resampling would explore one arbitrary world instead of the space.");
    }

    [Fact]
    public void Different_seeds_produce_different_deck_orders_for_the_observers_own_deck()
    {
        // Own-deck ORDER is hidden even from its owner, so it must be resampled too. Copying
        // Self.DeckComposition verbatim would let a search read its own future draws -- cheating
        // against itself, and in a way that inflates measured play strength.
        var (observed, _) = Observe(seed: 29, actions: 150);

        var orders = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var sampled = Subject.Determinize(observed, new SeededRandom(seed));
            orders.Add(string.Join(",", sampled[observed.Observer].Deck));
        }

        Assert.True(orders.Count > 1, "The observer's own deck order was identical across seeds.");
    }

    [Fact]
    public void The_sampled_own_deck_is_not_simply_the_sorted_composition()
    {
        // The specific lazy implementation: SetDeck(Self.DeckComposition). It would pass the
        // multiset test, and it is exactly the mistake ObservedState's sorted order invites.
        var (observed, _) = Observe(seed: 31, actions: 150);

        var sawUnsorted = false;
        for (ulong seed = 1; seed <= 20 && !sawUnsorted; seed++)
        {
            var sampled = Subject.Determinize(observed, new SeededRandom(seed));
            sawUnsorted = !sampled[observed.Observer].Deck.SequenceEqual(observed.Self.DeckComposition);
        }

        Assert.True(
            sawUnsorted,
            "Every sampled own-deck came out in the composition's sorted order, so the deck is "
            + "being copied rather than shuffled.");
    }

    // -- Determinism -----------------------------------------------------------------------------

    [Fact]
    public void The_same_observation_and_seed_produce_an_identical_sample()
    {
        // Without this a search result cannot be replayed, which is the property the whole engine
        // is built around.
        var (observed, _) = Observe(seed: 37, actions: 200);

        var a = Subject.Determinize(observed, new SeededRandom(99));
        var b = Subject.Determinize(observed, new SeededRandom(99));

        foreach (var playerId in PlayerIds.All)
        {
            Assert.Equal(a[playerId].Hand, b[playerId].Hand);
            Assert.Equal(a[playerId].Deck, b[playerId].Deck);
            Assert.Equal(a[playerId].Discard, b[playerId].Discard);
            Assert.Equal(a[playerId].Resources, b[playerId].Resources);
            Assert.Equal(a[playerId].Score, b[playerId].Score);
        }

        Assert.Equal(a.ActivePlayer, b.ActivePlayer);
        Assert.Equal(a.Phase, b.Phase);
        Assert.Equal(a.TurnNumber, b.TurnNumber);
    }

    // -- The round trip: the defining property ---------------------------------------------------

    [Fact]
    public void Re_observing_a_sampled_world_reproduces_the_original_observation()
    {
        // The strongest statement of correctness, and the one that subsumes most of the
        // consistency tests: if observing the sample from the same seat yields the same
        // observation, then the sample is indistinguishable from reality to that player -- which
        // is precisely what "consistent with all observations" means.
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var (observed, _) = Observe(seed, actions: 250);
            var sampled = Subject.Determinize(observed, new SeededRandom(seed));

            var reobserved = new ObservedState(sampled, observed.Observer);

            Assert.Equal(observed.Self.Hand, reobserved.Self.Hand);
            Assert.Equal(observed.Self.Discard, reobserved.Self.Discard);
            Assert.Equal(observed.Self.DeckComposition, reobserved.Self.DeckComposition);
            Assert.Equal(observed.Self.DeckSize, reobserved.Self.DeckSize);
            Assert.Equal(observed.Self.Resources, reobserved.Self.Resources);
            Assert.Equal(observed.Self.Score, reobserved.Self.Score);

            Assert.Equal(observed.Opponent.HandSize, reobserved.Opponent.HandSize);
            Assert.Equal(observed.Opponent.DeckSize, reobserved.Opponent.DeckSize);
            Assert.Equal(observed.Opponent.Discard, reobserved.Opponent.Discard);
            Assert.Equal(observed.Opponent.Resources, reobserved.Opponent.Resources);
            Assert.Equal(observed.Opponent.Score, reobserved.Opponent.Score);

            Assert.Equal(observed.ActivePlayer, reobserved.ActivePlayer);
            Assert.Equal(observed.Phase, reobserved.Phase);
            Assert.Equal(observed.TurnNumber, reobserved.TurnNumber);
            Assert.Equal(observed.PendingDiscards, reobserved.PendingDiscards);
        }
    }

    [Fact]
    public void Determinizing_from_either_seat_works()
    {
        // Both players' views must be samplable: a search runs on the acting player's view, and
        // over a game that is both seats in turn.
        var (_, real) = Observe(seed: 41, actions: 200);

        foreach (var playerId in PlayerIds.All)
        {
            var observed = new ObservedState(real, playerId);
            var sampled = Subject.Determinize(observed, new SeededRandom(5));

            Assert.Equal(observed.Self.Hand, sampled[playerId].Hand);
            Assert.Equal(observed.Opponent.HandSize, sampled[playerId.Opponent()].Hand.Count);
        }
    }

    // -- Fuzz-backed: every position of many real games ------------------------------------------

    [Fact]
    public void Every_position_of_many_real_games_determinizes_soundly_from_both_seats()
    {
        // The scale-up. Hand-written positions cover the cases anticipated; this covers the ones
        // that arise -- mid-discard-debt states, boards full of merged creatures, near-empty
        // decks, and whatever else real card interactions produce.
        var determinizer = Subject;
        var decklist = Cards.BuildSymmetricDeck(Rules);

        for (ulong seed = 1; seed <= 300; seed++)
        {
            PlayRandomGame(seed, state =>
            {
                foreach (var playerId in PlayerIds.All)
                {
                    var observed = new ObservedState(state, playerId);
                    var sampled = determinizer.Determinize(observed, new SeededRandom(seed));

                    // Sizes right, and every card accounted for, for BOTH players in the sample.
                    Assert.Equal(observed.Opponent.HandSize, sampled[playerId.Opponent()].Hand.Count);
                    Assert.Equal(observed.Opponent.DeckSize, sampled[playerId.Opponent()].Deck.Count);
                    Assert.Equal(observed.Self.Hand, sampled[playerId].Hand);

                    foreach (var side in PlayerIds.All)
                    {
                        AssertSameMultiset(decklist, AllCardsOf(sampled, side), $"seed {seed}: {side}");
                    }
                }
            });
        }
    }

    // -- Helpers ---------------------------------------------------------------------------------

    // Plays `actions` random actions from a fresh game and returns the position, observed from
    // player one. Real positions rather than hand-built ones: the accounting under test only gets
    // interesting once cards have moved between zones.
    // -- The deck actually in play, when it is not the symmetric decklist ------------------------

    [Fact]
    public void A_game_dealt_from_the_default_deck_determinizes_when_given_that_deck()
    {
        // The Godot game deals from DeckBuilder.Default (ONE of every card), not from the
        // ruleset's symmetric decklist (CopiesPerCard = 2, so twice the size). Every other test
        // here deals symmetrically, which is exactly why this gap survived: the determinizer's
        // no-deck fallback rebuilds the SYMMETRIC list, so against a Default-dealt game it thinks
        // twice as many cards are unaccounted for and RestoreOpponent throws.
        //
        // Passing the real deck is the fix, and this pins that it works.
        var deck = DeckBuilder.Default(Cards);
        var (observed, _) = ObserveWithDeck(seed: 21, deck, actions: 120);

        var sampled = new Determinizer(Cards, deck).Determinize(observed, new SeededRandom(1));

        var opponent = sampled[observed.Observer.Opponent()];
        Assert.Equal(observed.Opponent.HandSize, opponent.Hand.Count);
        Assert.Equal(observed.Opponent.DeckSize, opponent.Deck.Count);
    }

    [Fact]
    public void A_game_dealt_from_the_default_deck_fails_loudly_without_that_deck()
    {
        // The bug's own signature, pinned so the fallback can never quietly start "working" by
        // guessing: without the real decklist the accounting cannot be done, and throwing is the
        // designed behaviour (see RestoreOpponent). In Godot this surfaced as the AI silently
        // never moving, because RunAiTurns is `async void`.
        var deck = DeckBuilder.Default(Cards);
        var (observed, _) = ObserveWithDeck(seed: 21, deck, actions: 120);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new Determinizer(Cards).Determinize(observed, new SeededRandom(1)));

        Assert.Contains("card-conservation invariant", ex.Message, StringComparison.Ordinal);
    }

    private static (ObservedState Observed, GameState Real) ObserveWithDeck(
        ulong seed, Deck deck, int actions)
    {
        var random = new SeededRandom(seed);
        var state = new GameState(Rules, random, PlayerId.One);

        foreach (var playerId in PlayerIds.All)
        {
            var player = state[playerId];
            player.SetDeck(deck.Cards);
            player.ShuffleDeck(random);
            player.Draw(Rules.StartingHandSize);
        }

        state.AdvanceToActions();

        for (var i = 0; i < actions && !state.IsOver; i++)
        {
            var legal = ActionGenerator.Generate(state, Cards);
            if (legal.Count == 0)
            {
                break;
            }

            ActionExecutor.Apply(state, Cards, legal[random.Next(legal.Count)]);
        }

        return (new ObservedState(state, PlayerId.One), state);
    }

    private static (ObservedState Observed, GameState Real) Observe(ulong seed, int actions = 100)
    {
        var random = new SeededRandom(seed);
        var state = NewGame(seed);

        for (var i = 0; i < actions && !state.IsOver; i++)
        {
            var legal = ActionGenerator.Generate(state, Cards);
            if (legal.Count == 0)
            {
                break;
            }

            ActionExecutor.Apply(state, Cards, legal[random.Next(legal.Count)]);
        }

        return (new ObservedState(state, PlayerId.One), state);
    }

    private static GameState NewGame(ulong seed)
    {
        var random = new SeededRandom(seed);
        var state = new GameState(Rules, random, PlayerId.One);

        foreach (var playerId in PlayerIds.All)
        {
            var player = state[playerId];
            player.SetDeck(Cards.BuildSymmetricDeck(Rules));
            player.ShuffleDeck(random);
            player.Draw(Rules.StartingHandSize);
        }

        state.AdvanceToActions();
        return state;
    }

    private static void PlayRandomGame(ulong seed, Action<GameState> check)
    {
        var random = new SeededRandom(seed);
        var state = NewGame(seed);

        for (var i = 0; i < 400 && !state.IsOver; i++)
        {
            var legal = ActionGenerator.Generate(state, Cards);
            if (legal.Count == 0)
            {
                break;
            }

            check(state);
            ActionExecutor.Apply(state, Cards, legal[random.Next(legal.Count)]);
        }
    }

    private static List<string> AllCardsOf(GameState state, PlayerId player)
    {
        var all = new List<string>();
        all.AddRange(state[player].Hand);
        all.AddRange(state[player].Deck);
        all.AddRange(state[player].Discard);

        foreach (var (_, creature) in state.Board.CreaturesOf(player))
        {
            if (!creature.IsToken)
            {
                all.AddRange(creature.MergedFrom);
            }
        }

        return all;
    }

    private static IEnumerable<SlotIndex> AllSlots() =>
        PlayerIds.All.SelectMany(SlotIndex.AllFor);

    private static void AssertSameMultiset(
        IReadOnlyList<string> expected, IReadOnlyList<string> actual, string what)
    {
        var expectedCounts = CountBy(expected);
        var actualCounts = CountBy(actual);

        foreach (var (cardId, count) in expectedCounts)
        {
            Assert.True(
                actualCounts.GetValueOrDefault(cardId) == count,
                $"{what}: expected {count} copies of '{cardId}', found "
                + $"{actualCounts.GetValueOrDefault(cardId)}.");
        }

        foreach (var cardId in actualCounts.Keys)
        {
            Assert.True(
                expectedCounts.ContainsKey(cardId),
                $"{what}: found '{cardId}', which the decklist does not contain at all.");
        }
    }

    private static Dictionary<string, int> CountBy(IReadOnlyList<string> cards)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cardId in cards)
        {
            counts[cardId] = counts.GetValueOrDefault(cardId) + 1;
        }

        return counts;
    }
}
