// A description may embed a level-dependent effect as a literal "{BASE + COEFF * Level}"
// formula (generated once by SpellDescriptionFormatter.cs for any Spell.dbc effect whose
// value scales with the caster's level - EffectRealPointsPerLevel != 0). BASE and COEFF are
// already non-negative numeric literals at this point (each Abs()'d independently at
// generation time), so evaluating is plain arithmetic - no eval/Function needed.
const FORMULA_RE = /\{\s*(-?[\d.]+)\s*\+\s*(-?[\d.]+)\s*\*\s*Level\s*\}/g;

// COEFF * Level is rounded to a whole number BEFORE adding it to BASE, not summed as floats
// and rounded once at the end - matches the real client tooltip convention (see gotchas.md):
// the per-level term is what actually varies per character, so it's rounded on its own: BASE
// is already a fixed whole number by construction (Spell.EffectBasePoints is an int), and
// adding an already-rounded integer to it can't reintroduce a fractional result.
export function resolveScaledFormulas(text, level) {
    if (!text) return text;
    return text.replace(FORMULA_RE, (_, base, coeff) => {
        const value = Number(base) + Math.round(Number(coeff) * level);
        return String(value);
    });
}
