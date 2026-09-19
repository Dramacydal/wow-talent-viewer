// Wowhead-tooltip-style formatting for a talent_ranks row's active-ability fields - shared
// between talent-tree.js (the single-build tree) and compare-tree.js (the build-diff page),
// so the wording/verification work behind this (see .claude-docs/gotchas.md) lives in one
// place, not two copies that could quietly drift apart.

// The only 3 power types that occur on player talent abilities in vanilla - see
// BuildExtractor.ResolveAbilityFields / .claude-docs/gotchas.md.
export const POWER_TYPE_NAMES = { 0: 'Mana', 1: 'Rage', 3: 'Energy' };

// Trims a fractional value to at most 2 decimals without trailing zeros (1.50 -> "1.5",
// 3.00 -> "3") - cast time/cooldown/range can all carry real fractional parts.
export function trimNumber(n) {
    return Number(n.toFixed(2)).toString();
}

// Each returns null when that side has nothing to show, so the template can render just the
// other side (a lone flex child in a `justify-content: space-between` row naturally sits at
// the start/left - see .tt-tooltip-row CSS).
export function formatAbilityCost(rank) {
    if (!rank.powerCost) return null;
    return `${rank.powerCost} ${POWER_TYPE_NAMES[rank.powerType] ?? rank.powerType}`;
}

export function formatAbilityRange(rank) {
    if (rank.isMeleeRange) return 'Melee Range';
    if (!rank.rangeMaxYards) return null;
    const max = trimNumber(rank.rangeMaxYards);
    return rank.rangeMinYards ? `${trimNumber(rank.rangeMinYards)}-${max} yd range` : `${max} yd range`;
}

export function formatAbilityCastTime(rank) {
    if (rank.castTimeMs === null || rank.castTimeMs === undefined) return null;
    return rank.castTimeMs === 0 ? 'Instant cast' : `${trimNumber(rank.castTimeMs / 1000)} sec cast`;
}

export function formatAbilityCooldown(rank) {
    if (!rank.cooldownMs) return null;
    return rank.cooldownMs > 60000
        ? `${trimNumber(rank.cooldownMs / 60000)} min cooldown`
        : `${trimNumber(rank.cooldownMs / 1000)} sec cooldown`;
}
