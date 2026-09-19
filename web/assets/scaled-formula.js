// A description may embed a level-dependent effect as a literal "{BASE + COEFF * Level}"
// formula (generated once by SpellDescriptionFormatter.cs for any Spell.dbc effect whose
// value scales with the caster's level - EffectRealPointsPerLevel != 0). BASE and COEFF are
// plain signed numeric literals - real client tooltips keep the sign all the way through and
// only take the absolute value of the final resolved result, not of BASE/COEFF individually
// (which would be wrong whenever they have opposite signs - see SpellDescriptionFormatter.cs).
// The "BASE + " part is omitted from the formula entirely when BASE is exactly 0 (see
// SpellDescriptionFormatter.cs) - "{1.67 * Level}" instead of "{0 + 1.67 * Level}" - so it's
// an optional group here, defaulting to 0 when absent.
const FORMULA_RE = /\{\s*(?:(-?[\d.]+)\s*\+\s*)?(-?[\d.]+)\s*\*\s*Level\s*\}/g;

// COEFF * Level is rounded to a whole number BEFORE adding it to BASE, not summed as floats
// and rounded once at the end - matches the real client tooltip convention (see gotchas.md):
// the per-level term is what actually varies per character, so it's rounded on its own: BASE
// is already a fixed whole number by construction (Spell.EffectBasePoints is an int), and
// adding an already-rounded integer to it can't reintroduce a fractional result. Math.abs()
// is applied last, to that whole sum, exactly once.
export function resolveScaledFormulas(text, level) {
    if (!text) return text;
    return text.replace(FORMULA_RE, (_, base, coeff) => {
        const value = Math.abs((base === undefined ? 0 : Number(base)) + Math.round(Number(coeff) * level));
        return String(value);
    });
}
