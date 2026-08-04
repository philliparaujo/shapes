namespace Shapes.Core.Effects;

// The small targeting-selector language cards use. Automatic selectors (everything but the
// chosen_* pair) resolve without a player decision. chosen_* selectors are what the
// single-target rule counts: a card's effect list may declare at most one across all its
// effects (enforced by card-load validation in step 1.7).
public enum TargetSelector
{
    Self,
    Opposing,
    LeftFriendly,
    RightFriendly,
    AllEnemies,
    AllFriendlies,
    ChosenEnemy,
    ChosenFriendly,
}

public static class TargetSelectors
{
    public static bool IsChosen(this TargetSelector selector) =>
        selector is TargetSelector.ChosenEnemy or TargetSelector.ChosenFriendly;
}
