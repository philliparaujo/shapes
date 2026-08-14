using Shapes.Core.Primitives;
using Shapes.Core.State;
using Shapes.Godot.Adapter;
using Shapes.Tests.Fixtures;

namespace Shapes.Tests.Godot;

// PLAN.md B1b: StatusIcons.Describe must report one badge per active status, and must
// distinguish persistent taunt from the "until your next turn" form, since that's the one status
// whose durability the badge itself has to convey (see CreatureInstance.TauntExpiresNextTurn).
public class StatusIconsTests
{
    private static CreatureInstance Cadet() => new("cadet", 5, TypeMask.Wheel);

    [Fact]
    public void Undamaged_creature_with_no_status_has_no_badges()
    {
        Assert.Empty(StatusIcons.Describe(Cadet()));
    }

    [Fact]
    public void Permanent_taunt_is_not_marked_expiring()
    {
        var creature = Cadet();
        creature.GrantKeyword(KeywordFlags.Taunt);

        var badge = Assert.Single(StatusIcons.Describe(creature));
        Assert.Equal(StatusIcons.TauntGlyph, badge.Glyph);
        Assert.False(badge.IsExpiring);
    }

    [Fact]
    public void Taunt_until_next_turn_is_marked_expiring()
    {
        var creature = Cadet();
        creature.GrantKeyword(KeywordFlags.Taunt, untilNextTurn: true);

        var badge = Assert.Single(StatusIcons.Describe(creature));
        Assert.Equal(StatusIcons.TauntGlyph, badge.Glyph);
        Assert.True(badge.IsExpiring);
    }

    [Fact]
    public void Reflect_and_ricochet_report_their_own_glyphs()
    {
        var creature = Cadet();
        creature.GrantKeyword(KeywordFlags.Reflect);
        creature.GrantRicochet(RicochetDirection.Left);

        var badges = StatusIcons.Describe(creature);

        Assert.Contains(badges, b => b.Glyph == StatusIcons.ReflectGlyph);
        Assert.Contains(badges, b => b.Glyph == StatusIcons.RicochetLeftGlyph);
    }

    [Fact]
    public void Ricochet_right_reports_the_right_glyph()
    {
        var creature = Cadet();
        creature.GrantRicochet(RicochetDirection.Right);

        var badge = Assert.Single(StatusIcons.Describe(creature));
        Assert.Equal(StatusIcons.RicochetRightGlyph, badge.Glyph);
    }

    [Fact]
    public void Stun_reports_its_own_badge()
    {
        var creature = Cadet();
        creature.Stun();

        var badge = Assert.Single(StatusIcons.Describe(creature));
        Assert.Equal(StatusIcons.StunGlyph, badge.Glyph);
    }

    [Fact]
    public void Attack_buff_shows_as_a_plus_n_badge_not_an_icon()
    {
        var creature = Cadet();
        creature.AddAttackBuff(3);

        var badge = Assert.Single(StatusIcons.Describe(creature));
        Assert.Equal("+3 atk", badge.Glyph);
        Assert.True(badge.IsText);
    }

    [Fact]
    public void Other_badges_are_not_marked_as_text()
    {
        var creature = Cadet();
        creature.Stun();

        var badge = Assert.Single(StatusIcons.Describe(creature));
        Assert.False(badge.IsText);
    }

    [Fact]
    public void Zero_attack_buff_reports_no_badge()
    {
        var creature = Cadet();
        creature.AddAttackBuff(0);

        Assert.Empty(StatusIcons.Describe(creature));
    }

    [Fact]
    public void Next_attack_bonus_reports_its_own_badge_with_the_signed_value()
    {
        var creature = Cadet();
        creature.SetNextAttackBonus(2);

        var badge = Assert.Single(StatusIcons.Describe(creature));
        Assert.Equal(StatusIcons.NextAttackBonusGlyph, badge.Glyph);
        Assert.Contains("+2", badge.Tooltip);
    }

    [Fact]
    public void Next_damage_taken_bonus_reports_its_own_badge()
    {
        var creature = Cadet();
        creature.SetNextDamageTakenBonus(-1);

        var badge = Assert.Single(StatusIcons.Describe(creature));
        Assert.Equal(StatusIcons.NextDamageTakenBonusGlyph, badge.Glyph);
        Assert.Contains("-1", badge.Tooltip);
    }

    [Fact]
    public void Pending_on_next_damage_taken_synthesizes_its_armed_effect_via_EffectText()
    {
        var creature = Cadet();
        creature.SetPendingOnNextDamageTaken(Eff.Node("gain_resource", ("type", "anvil"), ("amount", 3)));

        var badge = Assert.Single(StatusIcons.Describe(creature));
        Assert.Equal(StatusIcons.PendingTriggerGlyph, badge.Glyph);
        Assert.Equal("Gain 3 [anvil] next time this takes damage.", badge.Tooltip);
    }

    [Fact]
    public void Pending_on_next_ricochet_synthesizes_its_armed_effect_via_EffectText()
    {
        var creature = Cadet();
        creature.SetPendingOnNextRicochet(Eff.Node("draw", ("amount", 1)));

        var badge = Assert.Single(StatusIcons.Describe(creature));
        Assert.Equal(StatusIcons.PendingTriggerGlyph, badge.Glyph);
        Assert.Equal("Draw 1 next time this ricochets.", badge.Tooltip);
    }

    [Fact]
    public void No_pending_trigger_reports_no_badge()
    {
        Assert.Empty(StatusIcons.Describe(Cadet()));
    }

    [Fact]
    public void All_active_statuses_report_together()
    {
        var creature = Cadet();
        creature.GrantKeyword(KeywordFlags.Taunt);
        creature.GrantKeyword(KeywordFlags.Reflect);
        creature.Stun();
        creature.AddAttackBuff(1);

        Assert.Equal(4, StatusIcons.Describe(creature).Count);
    }
}
