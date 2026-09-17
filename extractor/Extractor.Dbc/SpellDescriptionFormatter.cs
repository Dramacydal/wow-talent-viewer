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
        text = TokenRegex().Replace(text, m => ResolveToken(m, spell, ref lastNumericValue));

        return text;
    }

    private string ResolveToken(Match m, SpellRecord ownerSpell, ref double? lastNumericValue)
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
            's' or 'm' => Math.Abs(spell.EffectBasePoints[effIdx] + 1),
            'd' => dbcClient.GetSpellDurationMs(build, spell.DurationIndex) is { } ms ? ms / 1000.0 : null,
            'o' => GetPeriodicTotal(spell, effIdx),
            'h' => spell.ProcChance,
            'a' => dbcClient.GetSpellRadiusYards(build, spell.EffectRadiusIndex[effIdx]),
            'q' => Math.Abs(spell.EffectMiscValue[effIdx]),
            'b' => Math.Abs(spell.EffectPointsPerCombo[effIdx]),
            't' => spell.EffectAmplitude[effIdx] / 1000.0,
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
        // already spells out the unit by hand in those cases.
        if (char.ToLowerInvariant(letter[0]) == 'd' && !m.Groups["op"].Success)
            formatted += " seconds";

        return formatted;
    }

    /// <summary>Total effect value over a periodic effect's full duration — QSpellWork's
    /// getRealDuration() * (basePoints+1): duration divided by tick period (defaulting to a
    /// 5-second tick if the effect isn't periodic), times the per-tick amount.</summary>
    private double? GetPeriodicTotal(SpellRecord spell, int effIdx)
    {
        var durationMs = dbcClient.GetSpellDurationMs(build, spell.DurationIndex);
        if (durationMs is null) return null;

        var tickMs = spell.EffectAmplitude[effIdx] > 0 ? spell.EffectAmplitude[effIdx] : 5000;
        var ticks = durationMs.Value / tickMs;
        return ticks * (spell.EffectBasePoints[effIdx] + 1);
    }
}
