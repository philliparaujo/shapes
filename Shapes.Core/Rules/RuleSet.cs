namespace Shapes.Core.Rules;

// All tunable game rules in one place, loaded from JSON.
//
// Income, scoring, draw, hand limits, and win conditions are expected to change frequently
// during balance work, so none of them may be hard-coded elsewhere in the engine. A balance
// experiment is a named ruleset file; Phase 3 sweeps over them.
//
// Fields land in step 1.4. This placeholder exists so the skeleton compiles and the
// architecture tests have a namespace to assert against.
public sealed class RuleSet
{
}
