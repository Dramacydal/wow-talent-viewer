// A description may embed a level-dependent effect as a literal "{BASE + COEFF * Level}"
// formula (generated once by SpellDescriptionFormatter.cs for any Spell.dbc effect whose
// value scales with the caster's level - EffectRealPointsPerLevel != 0). BASE and COEFF are
// plain signed numeric literals - real client tooltips keep the sign all the way through and
// only take the absolute value of the final resolved result, not of BASE/COEFF individually
// (which would be wrong whenever they have opposite signs - see SpellDescriptionFormatter.cs).
// Three shapes, depending on BASE and COEFF's sign (see SpellDescriptionFormatter.cs):
//   "{COEFF * Level}"        - BASE is exactly 0, omitted entirely; COEFF keeps its own sign
//   "{BASE + COEFF * Level}" - COEFF >= 0
//   "{BASE - COEFF * Level}" - COEFF < 0, written as "-" + its magnitude, never "+ -1.11"
// Group 1+2 (base, operator) are only present in the two-term shapes; group 3 is either the
// signed coeff (one-term shape) or the unsigned magnitude paired with the operator.
const FORMULA_RE = /\{\s*(?:(-?[\d.]+)\s*([+-])\s*)?(-?[\d.]+)\s*\*\s*Level\s*\}/g;

// COEFF * Level is rounded to a whole number BEFORE adding it to BASE, not summed as floats
// and rounded once at the end - matches the real client tooltip convention (see gotchas.md):
// the per-level term is what actually varies per character, so it's rounded on its own: BASE
// is already a fixed whole number by construction (Spell.EffectBasePoints is an int), and
// adding an already-rounded integer to it can't reintroduce a fractional result. Math.abs()
// is applied last, to that whole sum, exactly once.
export function resolveScaledFormulas(text, level) {
    if (!text) return text;
    return text.replace(FORMULA_RE, (_, base, op, num) => {
        const coeff = base === undefined ? Number(num) : op === '-' ? -Number(num) : Number(num);
        const baseNum = base === undefined ? 0 : Number(base);
        const value = Math.abs(baseNum + Math.round(coeff * level));
        return String(value);
    });
}
