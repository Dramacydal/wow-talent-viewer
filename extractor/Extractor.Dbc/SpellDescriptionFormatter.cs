using System.Text.RegularExpressions;

namespace Extractor.Dbc;

/// <summary>
/// Resolves the "$s1"-style escape sequences embedded in Spell.Description into readable
/// text — done once at extraction time (not at render time) so the site never needs to
/// touch Spell.dbc/SpellDuration.dbc/SpellRadius.dbc again, and so a build is never
/// re-extracted just to fix text formatting (extraction round-trips to a remote DB are
/// expensive — see architecture.md).
///
/// Reverse-engineered from github.com/sidsukana/QSpellWork (SWObject.cpp's getDescription +
/// RegExpX family), then verified letter-by-letter against every escape sequence that
/// actually appears across all 1357 talent ranks extracted from 1.12.1.5875 — see
/// .claude-docs/gotchas.md for the full catalog and what's deliberately NOT supported
/// (fields that don't exist anywhere in vanilla's Spell.dbc: $u's real StackAmount doesn't
/// exist so it reads CumulativeAura instead — confirmed against known real values like
/// Improved Scorch's "stacks up to 5 times"; $i/$e/$v/$x/$q/$z/$g never appear in vanilla
/// talent text at all and are left as best-effort/unresolved).
///
/// Syntax: $[op N;][spellId]letter[index] — e.g. "$s1" (this spell's effect 1 base points),
/// "$12355d" (spell 12355's duration), "$/1000;S1" (this spell's effect 1 base points,
/// divided by 1000 — casting-time-reduction talents store the value in milliseconds).
/// Also "$lword1:word2;" (singular/plural word choice based on the immediately preceding
/// resolved number) and the combat-rating tokens ($AP, $RAP, $MWB, ...) which scale from
/// live character stats no static extraction can resolve — replaced with a readable
/// "&lt;AP&gt;"-style placeholder instead of QSpellWork's approach of silently blanking them.
/// </summary>
public sealed partial class SpellDescriptionFormatter(DbcClient dbcClient, string build)
{
    // Some $s/$m tokens resolve an effect whose real value scales with the CASTER's level
    // (Spell.EffectRealPointsPerLevel != 0 - e.g. Rogue "Serrated Blades", whose own
    // Description literally says "The amount of Armor reduced increases with your level").
    // This tool has no player character/level anywhere in its data model - a static per-build
    // extraction can't know "your" level. Fixed at 60 (vanilla's level cap for the vast
    // majority of these builds) as the least-wrong single number to show, chosen over leaving
    // the token unresolved. NOT verified against every build back to 0.7.0.3694 - if an early
    // alpha build turns out to have had a different level cap, this constant would need a
    // per-build value instead of one global one.
    private const int ReferenceLevelForPerLevelScaling = 60;

    [GeneratedRegex(@"\$(HND|MWS|mws|MWB|mwb|RWB|rwb|MW|mw|AP|RAP|PL)\b")]
    private static partial Regex CombatRatingRegex();

    [GeneratedRegex(@"\$(?:(?<op>[*/])(?<opval>\d+);)?(?<spellid>\d+)?(?<letter>[A-Za-z])(?:(?<idx>[1-3])|(?<w1>[A-Za-z]+):(?<w2>[A-Za-z]+);)?")]
    private static partial Regex TokenRegex();

    public string Format(SpellRecord spell)
    {
        var text = spell.Description;
        if (string.IsNullOrEmpty(text))
            return text;

        text = CombatRatingRegex().Replace(text, m => $"<{m.Groups[1].Value.ToUpperInvariant()}>");

        double? lastNumericValue = null;
        var sourceForLookahead = text; // Match.Index below refers to THIS string, not the post-replace result
        text = TokenRegex().Replace(text, m => ResolveToken(m, spell, sourceForLookahead, ref lastNumericValue));

        return text;
    }

    private string ResolveToken(Match m, SpellRecord ownerSpell, string sourceText, ref double? lastNumericValue)
    {
        var letter = m.Groups["letter"].Value;
        var idx = m.Groups["idx"].Success ? int.Parse(m.Groups["idx"].Value) : 1;
        var effIdx = idx - 1;

        // "$lsec:secs;" / "$gsec:secs;" — word choice by the most recently resolved number.
        if (m.Groups["w1"].Success)
        {
            var useSingular = lastNumericValue is 1;
            return useSingular ? m.Groups["w1"].Value : m.Groups["w2"].Value;
        }

        var spell = ownerSpell;
        if (m.Groups["spellid"].Success)
        {
            var other = dbcClient.GetSpell(build, int.Parse(m.Groups["spellid"].Value));
            if (other is null)
                return m.Value; // unresolvable cross-reference — leave the raw token rather than guess
            spell = other;
        }

        double? value = char.ToLowerInvariant(letter[0]) switch
        {
            's' or 'm' => Math.Abs(spell.EffectBasePoints[effIdx] + 1
                + ReferenceLevelForPerLevelScaling * spell.EffectRealPointsPerLevel[effIdx]),
            'd' => dbcClient.GetSpellDurationMs(build, spell.DurationIndex) is { } ms ? ms / 1000.0 : null,
            'o' => GetPeriodicTotal(spell, effIdx),
            'h' => spell.ProcChance,
            'a' => dbcClient.GetSpellRadiusYards(build, spell.EffectRadiusIndex[effIdx]),
            'q' => Math.Abs(spell.EffectMiscValue[effIdx]),
            'b' => Math.Abs(spell.EffectPointsPerCombo[effIdx]),
            't' => spell.EffectAuraPeriod[effIdx] / 1000.0,
            'n' => spell.ProcCharges,
            'u' => spell.CumulativeAura,
            'v' => spell.MaxTargetLevel,
            'x' => spell.EffectChainTargets?[effIdx],
            _ => null, // i/e/z/g and anything else: no vanilla field backs this — leave unresolved
        };

        if (value is null)
            return m.Value;

        if (m.Groups["op"].Success)
        {
            var opval = double.Parse(m.Groups["opval"].Value, System.Globalization.CultureInfo.InvariantCulture);
            value = m.Groups["op"].Value == "/" ? value / opval : value * opval;
        }

        lastNumericValue = value;

        // Whole numbers print without a decimal point (matches how these appear in the real
        // client tooltip); non-whole (e.g. "$/1000;S1" producing 0.5 seconds) keep one decimal.
        var rounded = Math.Round(value.Value, 2);
        // InvariantCulture explicitly: this text goes into the database, not straight to a
        // user's screen, and must not depend on the machine's locale — a run on a
        // Russian-locale Windows box (comma decimal separator) produced "0,1 sec" instead of
        // "0.1 sec" before this fix.
        var formatted = rounded == Math.Floor(rounded)
            ? ((long)rounded).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : rounded.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        // Bare "$d"/"$<spellId>d" (no /N or *N modifier) spells out "N seconds" — matches
        // QSpellWork exactly (always "seconds", even for 1 — a real, if grammatically odd,
        // quirk faithfully reproduced rather than "corrected"). A modified duration (e.g.
        // "$/1000;S1 sec" for a cast-time reduction) is left bare: the surrounding sentence
        // already spells out the unit by hand in those cases. Also skipped when the raw text
        // ALREADY writes "seconds" right after the token by hand — real (if inconsistent)
        // vanilla alpha authoring, e.g. Blast Wave's "...dazing them for $d seconds." would
        // otherwise read "...6 seconds seconds.".
        if (char.ToLowerInvariant(letter[0]) == 'd' && !m.Groups["op"].Success && !FollowedByTheWordSeconds(sourceText, m))
            formatted += " seconds";

        return formatted;
    }

    [GeneratedRegex(@"^\s+seconds?\b", RegexOptions.IgnoreCase)]
    private static partial Regex SecondsWordRegex();

    private static bool FollowedByTheWordSeconds(string sourceText, Match m) =>
        SecondsWordRegex().IsMatch(sourceText[(m.Index + m.Length)..]);

    /// <summary>Total effect value over a periodic effect's full duration — QSpellWork's
    /// getRealDuration() * (basePoints+1): duration divided by tick period, times the
    /// per-tick amount. Returns null (leaves the raw token unresolved, same as any other
    /// irresolvable escape) if the effect has no real tick period - deliberately NOT a
    /// hardcoded guess: checked every real `$o` usage in the current dataset (9 spells) and
    /// every one has a genuine nonzero EffectAuraPeriod, so a synthetic fallback would be
    /// dead code for real data and, if it ever DID fire on a spell we haven't seen, would
    /// silently print a made-up number - exactly the class of bug this method used to have
    /// (see the EffectAuraPeriod/EffectAmplitude mixup in git history and gotchas.md).
    ///
    /// The tick period is Spell.EffectAuraPeriod (an int, ms) - NOT EffectAmplitude (a float,
    /// unrelated to timing). QSpellWork's own C++ struct calls the tick-period field
    /// "EffectAmplitude" (declared as an int, right after EffectApplyAuraName) because their
    /// reverse-engineered layout has no separate EffectAuraPeriod field at all - the real
    /// (float) Amplitude value landed under a different name in their code
    /// ("EffectMultipleValue"). WoWDBDefs' newer, cross-referenced schema splits these two
    /// correctly. Caught on real data: Mana Tide Totem's raw description reads
    /// "$16191t1 seconds" - spell 16191's EffectAmplitude is 0 (always has been, it's not a
    /// periodic-timing field), while EffectAuraPeriod is 3000ms, exactly the real, known
    /// 3-second tick. See .claude-docs/gotchas.md.</summary>
    private double? GetPeriodicTotal(SpellRecord spell, int effIdx)
    {
        var durationMs = dbcClient.GetSpellDurationMs(build, spell.DurationIndex);
        if (durationMs is null) return null;
        if (spell.EffectAuraPeriod[effIdx] <= 0) return null;

        var ticks = durationMs.Value / spell.EffectAuraPeriod[effIdx];
        return ticks * (spell.EffectBasePoints[effIdx] + 1);
    }
}
