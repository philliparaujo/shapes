using Shapes.Core.Effects;

namespace Shapes.Tests.Effects;

// The registry is the single source of truth for "what ops exist" -- both the interpreter's
// dispatch and (in step 1.7) card-schema validation query it, so it must be exhaustive and
// fail loudly on an unknown name rather than silently no-op.
public class EffectRegistryTests
{
    [Theory]
    [InlineData("damage")]
    [InlineData("damage_scaled")]
    [InlineData("heal")]
    [InlineData("heal_to_full")]
    [InlineData("set_health")]
    [InlineData("buff_max_health")]
    [InlineData("self_damage")]
    [InlineData("draw")]
    [InlineData("discard")]
    [InlineData("draw_up_to")]
    [InlineData("gain_resource")]
    [InlineData("gain_next_turn")]
    [InlineData("destroy")]
    [InlineData("summon")]
    [InlineData("grant_keyword")]
    [InlineData("stun")]
    [InlineData("next_attack_bonus")]
    [InlineData("next_damage_taken_bonus")]
    [InlineData("conditional")]
    [InlineData("for_each")]
    public void Every_vocabulary_op_is_registered(string opName)
    {
        Assert.True(EffectRegistry.IsKnown(opName));
    }

    [Fact]
    public void Unknown_op_is_reported_as_not_known()
    {
        Assert.False(EffectRegistry.IsKnown("teleport"));
    }

    [Fact]
    public void Resolving_an_unknown_op_fails_loudly_rather_than_returning_null()
    {
        Assert.Throws<UnknownEffectOpException>(() => EffectRegistry.Resolve("teleport"));
    }
}
