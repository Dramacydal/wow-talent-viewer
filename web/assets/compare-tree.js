import { createApp } from 'vue';
import './styles/talent-tree.css';
import './styles/compare-tree.css';
import { formatAbilityCost, formatAbilityRange, formatAbilityCastTime, formatAbilityCooldown } from './ability-format.js';
import { diffWords } from './text-diff.js';

// Position/tab are properties of the TALENT as a whole, not any one rank. Rank COUNT is
// deliberately not listed here even though it's also talent-level - a rank-count change is
// exactly what the per-rank added/removed sub-blocks already show directly (see groups()),
// so a separate "Rank count: 5 -> 3" summary line would just repeat that.
function talentLevelFields(a, b) {
    const fields = [];
    if (a.tier !== b.tier || a.columnIndex !== b.columnIndex) {
        // tier/columnIndex are 0-based internally (matches the grid data everywhere else
        // in this project - see .claude-docs/architecture.md), but a player thinks in
        // 1-based row/column numbers - +1 here for display only.
        fields.push({ label: 'Position', before: `Tier ${a.tier + 1}, Column ${a.columnIndex + 1}`, after: `Tier ${b.tier + 1}, Column ${b.columnIndex + 1}` });
    }
    if (a.tabName !== b.tabName) {
        fields.push({ label: 'Tab', before: a.tabName, after: b.tabName });
    }
    return fields;
}

// Every rank-level field this project already resolves and verifies against real client
// data (see BuildExtractor.ResolveAbilityFields/ResolveStanceRequirement/
// ResolveEquipRequirement) - compared pairwise, only differing ones produce a line. A name
// change is NOT one of these fields - it's shown inline next to the rank's own header
// instead ("NewName (was OldName)"), see groups()/the template.
function rankFields(a, b) {
    const fields = [];
    // Rendered inline (struck-through old word / green new word within the sentence)
    // instead of a before -> after pair - a full-sentence side-by-side reads far worse for a
    // one-word tuning change (see diffWords()/the template).
    if (a.description !== b.description) fields.push({ label: 'Description', diff: diffWords(a.description, b.description) });
    if (a.isAbility !== b.isAbility) {
        fields.push({ label: 'Ability status', before: a.isAbility ? 'Active ability' : 'Passive', after: b.isAbility ? 'Active ability' : 'Passive' });
    }
    const costA = formatAbilityCost(a), costB = formatAbilityCost(b);
    if (costA !== costB) fields.push({ label: 'Cost', before: costA ?? 'None', after: costB ?? 'None' });
    const rangeA = formatAbilityRange(a), rangeB = formatAbilityRange(b);
    if (rangeA !== rangeB) fields.push({ label: 'Range', before: rangeA ?? 'Self', after: rangeB ?? 'Self' });
    const castA = formatAbilityCastTime(a), castB = formatAbilityCastTime(b);
    if (castA !== castB) fields.push({ label: 'Cast time', before: castA ?? 'n/a', after: castB ?? 'n/a' });
    const cdA = formatAbilityCooldown(a), cdB = formatAbilityCooldown(b);
    if (cdA !== cdB) fields.push({ label: 'Cooldown', before: cdA ?? 'None', after: cdB ?? 'None' });
    if (a.stanceRequirement !== b.stanceRequirement) {
        fields.push({ label: 'Stance/form requirement', before: a.stanceRequirement ?? 'None', after: b.stanceRequirement ?? 'None' });
    }
    if (a.equipRequirement !== b.equipRequirement) {
        fields.push({ label: 'Weapon/shield requirement', before: a.equipRequirement ?? 'None', after: b.equipRequirement ?? 'None' });
    }
    return fields;
}

function ranksByIndex(talentSide) {
    const out = {};
    for (const r of talentSide.ranks) out[r.rankIndex] = r;
    return out;
}

const compareApp = createApp({
    data() {
        return {
            data: null,
            error: null,
            collapsed: { added: false, changed: false, removed: false },
            // Per-talent collapse state in the Changed section, keyed by anchorSpellId -
            // absent/falsy means expanded (the default). A plain object works fine here:
            // Vue 3's reactive() proxy tracks new-key assignment, unlike Vue 2.
            collapsedTalents: {},
            hoveredRank: null,
            tooltipStyle: { left: '0px', top: '0px' },
        };
    },
    computed: {
        // Grouped by TALENT (matched across builds by the first rank's real Blizzard spell
        // id - see TalentTreeComparer). New/Removed groups are only ever whole talents (by
        // definition - "added" means every one of its ranks is new). A Changed group's own
        // header always uses rank 1, which is guaranteed to exist on both sides for a
        // "changed" talent (it's literally what the two sides were matched on).
        groups() {
            if (!this.data) return { added: [], changed: [], removed: [] };
            const added = [], changed = [], removed = [];
            for (const t of this.data.talents) {
                if (t.status === 'unchanged') continue;

                if (t.status === 'added') {
                    const rank1 = t.b.ranks.find((r) => r.rankIndex === 1) ?? t.b.ranks[0];
                    added.push({ anchorSpellId: t.anchorSpellId, rank: rank1, maxRank: t.b.maxRank, tabName: t.b.tabName });
                    continue;
                }
                if (t.status === 'removed') {
                    const rank1 = t.a.ranks.find((r) => r.rankIndex === 1) ?? t.a.ranks[0];
                    removed.push({ anchorSpellId: t.anchorSpellId, rank: rank1, tabName: t.a.tabName });
                    continue;
                }

                // status === 'changed'
                const ranksA = ranksByIndex(t.a);
                const ranksB = ranksByIndex(t.b);
                const maxRank = Math.max(t.a.maxRank, t.b.maxRank);
                const talentFields = talentLevelFields(t.a, t.b);

                // Ranks beyond 1 can independently be pure adds/removes/changes even
                // though the talent as a whole still exists (real example: Hunter
                // "Precision" -> "Humanoid Slaying", same anchor spell id, drops from 5
                // ranks to 3 - see gotchas.md). Rank 1 itself can only ever be "changed"
                // here (or absent from this list entirely, if identical) - it's the
                // anchor, so it exists on both sides by construction.
                const rankIndices = Object.keys({ ...ranksA, ...ranksB }).map(Number).sort((x, y) => x - y);
                const rankBlocks = [];
                for (const rankIndex of rankIndices) {
                    const ra = ranksA[rankIndex];
                    const rb = ranksB[rankIndex];
                    if (rb && !ra) {
                        rankBlocks.push({ rankIndex, kind: 'added', rank: rb, fields: [] });
                        continue;
                    }
                    if (ra && !rb) {
                        rankBlocks.push({ rankIndex, kind: 'removed', rank: ra, fields: [] });
                        continue;
                    }
                    const fields = rankFields(ra, rb);
                    if (fields.length === 0 && ra.name === rb.name) continue;
                    rankBlocks.push({ rankIndex, kind: 'changed', rank: rb, oldRank: ra, fields });
                }

                changed.push({
                    anchorSpellId: t.anchorSpellId,
                    rank: ranksB[1] ?? ranksA[1],
                    oldRank: ranksA[1] ?? ranksB[1],
                    maxRank,
                    tabName: t.b.tabName,
                    talentFields,
                    rankBlocks,
                    // A 1-rank talent (on both sides) collapses to the flat single-row +
                    // field-list shape - no point nesting a "(Rank 1)" sub-block under a
                    // talent that only ever has one rank.
                    singleRank: maxRank === 1,
                });
            }
            return { added, changed, removed };
        },
        addedGroups() { return this.groups.added; },
        changedGroups() { return this.groups.changed; },
        removedGroups() { return this.groups.removed; },
        tooltip() {
            if (!this.hoveredRank) return null;
            const rank = this.hoveredRank;
            return {
                name: rank.name,
                description: rank.description,
                isAbility: rank.isAbility,
                costText: formatAbilityCost(rank),
                rangeText: formatAbilityRange(rank),
                castTimeText: formatAbilityCastTime(rank),
                cooldownText: formatAbilityCooldown(rank),
                requirementLines: [rank.stanceRequirement, rank.equipRequirement].filter(Boolean),
            };
        },
    },
    async mounted() {
        const el = document.getElementById('compare-tree-app');
        try {
            const response = await fetch(el.dataset.compareUrl);
            if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
            this.data = await response.json();
        } catch (e) {
            this.error = e.message;
        }
    },
    methods: {
        toggleSection(key) {
            this.collapsed[key] = !this.collapsed[key];
        },
        isTalentCollapsed(anchorSpellId) {
            return !!this.collapsedTalents[anchorSpellId];
        },
        toggleTalent(anchorSpellId) {
            this.collapsedTalents[anchorSpellId] = !this.collapsedTalents[anchorSpellId];
        },
        showTooltip(rank, event) {
            this.hoveredRank = rank;
            const wouldSqueeze = event.clientX + 16 + 280 > window.innerWidth;
            this.tooltipStyle = wouldSqueeze
                ? { right: `${window.innerWidth - event.clientX + 16}px`, top: `${event.clientY + 16}px` }
                : { left: `${event.clientX + 16}px`, top: `${event.clientY + 16}px` };
        },
        // A removed (red) word in the inline description diff shows the OLD rank's tooltip,
        // an added (green) word shows the NEW rank's - an 'equal' word shows nothing (leaves
        // whatever tooltip state was already there, which mouseleave on the previous
        // removed/added span already cleared).
        showDiffTooltip(seg, oldRank, newRank, event) {
            if (seg.type === 'removed') this.showTooltip(oldRank, event);
            else if (seg.type === 'added') this.showTooltip(newRank, event);
        },
        hideTooltip() {
            this.hoveredRank = null;
        },
    },
    template: `
        <p v-if="error">Failed to load comparison: {{ error }}</p>
        <template v-else-if="data">
            <section class="cmp-section">
                <button class="cmp-section-header" @click="toggleSection('added')">
                    <svg class="cmp-section-toggle" :class="{ 'cmp-section-toggle-open': !collapsed.added }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="14" height="14"><polyline points="9 6 15 12 9 18"></polyline></svg>
                    New talents ({{ addedGroups.length }})
                </button>
                <ul v-if="!collapsed.added" class="cmp-row-list">
                    <li v-for="g in addedGroups" :key="g.anchorSpellId" class="cmp-row cmp-row-added">
                        <span class="cmp-row-hoverable" @mouseenter="showTooltip(g.rank, $event)" @mousemove="showTooltip(g.rank, $event)" @mouseleave="hideTooltip">
                            <img v-if="g.rank.iconUrl" :src="g.rank.iconUrl" class="cmp-row-icon" alt="">
                            <span>{{ g.rank.name }}</span>
                        </span>
                        <span v-if="g.maxRank > 1" class="cmp-row-rankcount">({{ g.maxRank }})</span>
                        <span class="cmp-row-tab">{{ g.tabName }}</span>
                    </li>
                </ul>
            </section>

            <section class="cmp-section">
                <button class="cmp-section-header" @click="toggleSection('changed')">
                    <svg class="cmp-section-toggle" :class="{ 'cmp-section-toggle-open': !collapsed.changed }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="14" height="14"><polyline points="9 6 15 12 9 18"></polyline></svg>
                    Changed talents ({{ changedGroups.length }})
                </button>
                <ul v-if="!collapsed.changed" class="cmp-row-list">
                    <li v-for="g in changedGroups" :key="g.anchorSpellId" class="cmp-row-block">
                        <div class="cmp-row cmp-row-changed">
                            <svg class="cmp-section-toggle cmp-talent-toggle" :class="{ 'cmp-section-toggle-open': !isTalentCollapsed(g.anchorSpellId) }" @click="toggleTalent(g.anchorSpellId)" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="14" height="14"><polyline points="9 6 15 12 9 18"></polyline></svg>
                            <span class="cmp-row-hoverable" @mouseenter="showTooltip(g.rank, $event)" @mousemove="showTooltip(g.rank, $event)" @mouseleave="hideTooltip">
                                <img v-if="g.rank.iconUrl" :src="g.rank.iconUrl" class="cmp-row-icon" alt="">
                                <span>{{ g.rank.name }}</span>
                            </span>
                            <span v-if="g.oldRank.name !== g.rank.name" class="cmp-row-was"
                                  @mouseenter="showTooltip(g.oldRank, $event)" @mousemove="showTooltip(g.oldRank, $event)" @mouseleave="hideTooltip">
                                was
                                <img v-if="g.oldRank.iconUrl" :src="g.oldRank.iconUrl" class="cmp-row-icon" alt="">
                                {{ g.oldRank.name }}
                            </span>
                            <span class="cmp-row-tab">{{ g.tabName }}</span>
                        </div>

                        <template v-if="!isTalentCollapsed(g.anchorSpellId)">
                            <ul v-if="g.talentFields.length" class="cmp-field-list">
                                <li v-for="f in g.talentFields" :key="f.label" class="cmp-field-row">
                                    <span class="cmp-field-label">{{ f.label }}:</span>
                                    <span class="cmp-field-before">{{ f.before }}</span>
                                    <span class="cmp-field-arrow">→</span>
                                    <span class="cmp-field-after">{{ f.after }}</span>
                                </li>
                            </ul>

                            <ul v-if="g.singleRank && g.rankBlocks[0]" class="cmp-field-list">
                                <li v-for="f in g.rankBlocks[0].fields" :key="f.label" class="cmp-field-row">
                                    <span class="cmp-field-label">{{ f.label }}:</span>
                                    <template v-if="f.diff">
                                        <span class="cmp-diff-text"><template v-for="(seg, si) in f.diff" :key="si"><span :class="{ 'cmp-diff-removed': seg.type === 'removed', 'cmp-diff-added': seg.type === 'added' }" @mouseenter="showDiffTooltip(seg, g.rankBlocks[0].oldRank, g.rankBlocks[0].rank, $event)" @mousemove="showDiffTooltip(seg, g.rankBlocks[0].oldRank, g.rankBlocks[0].rank, $event)" @mouseleave="hideTooltip">{{ seg.text }}</span><template v-if="si < f.diff.length - 1">{{ ' ' }}</template></template></span>
                                    </template>
                                    <template v-else>
                                        <span class="cmp-field-before">{{ f.before }}</span>
                                        <span class="cmp-field-arrow">→</span>
                                        <span class="cmp-field-after">{{ f.after }}</span>
                                    </template>
                                </li>
                            </ul>

                            <template v-if="!g.singleRank">
                                <div v-for="rb in g.rankBlocks" :key="rb.rankIndex" class="cmp-rank-block">
                                    <div class="cmp-row" :class="{ 'cmp-row-added': rb.kind === 'added', 'cmp-row-removed': rb.kind === 'removed', 'cmp-row-changed': rb.kind === 'changed' }">
                                        <span class="cmp-row-hoverable" @mouseenter="showTooltip(rb.rank, $event)" @mousemove="showTooltip(rb.rank, $event)" @mouseleave="hideTooltip">
                                            <img v-if="rb.rank.iconUrl" :src="rb.rank.iconUrl" class="cmp-row-icon" alt="">
                                            <span>{{ rb.rank.name }} (Rank {{ rb.rankIndex }})</span>
                                        </span>
                                        <span v-if="rb.kind === 'changed' && rb.oldRank.name !== rb.rank.name" class="cmp-row-was"
                                              @mouseenter="showTooltip(rb.oldRank, $event)" @mousemove="showTooltip(rb.oldRank, $event)" @mouseleave="hideTooltip">
                                            was
                                            <img v-if="rb.oldRank.iconUrl" :src="rb.oldRank.iconUrl" class="cmp-row-icon" alt="">
                                            {{ rb.oldRank.name }} (Rank {{ rb.rankIndex }})
                                        </span>
                                    </div>
                                    <ul v-if="rb.kind === 'changed'" class="cmp-field-list">
                                        <li v-for="f in rb.fields" :key="f.label" class="cmp-field-row">
                                            <span class="cmp-field-label">{{ f.label }}:</span>
                                            <template v-if="f.diff">
                                                <span class="cmp-diff-text"><template v-for="(seg, si) in f.diff" :key="si"><span :class="{ 'cmp-diff-removed': seg.type === 'removed', 'cmp-diff-added': seg.type === 'added' }" @mouseenter="showDiffTooltip(seg, rb.oldRank, rb.rank, $event)" @mousemove="showDiffTooltip(seg, rb.oldRank, rb.rank, $event)" @mouseleave="hideTooltip">{{ seg.text }}</span><template v-if="si < f.diff.length - 1">{{ ' ' }}</template></template></span>
                                            </template>
                                            <template v-else>
                                                <span class="cmp-field-before">{{ f.before }}</span>
                                                <span class="cmp-field-arrow">→</span>
                                                <span class="cmp-field-after">{{ f.after }}</span>
                                            </template>
                                        </li>
                                    </ul>
                                </div>
                            </template>
                        </template>
                    </li>
                </ul>
            </section>

            <section class="cmp-section">
                <button class="cmp-section-header" @click="toggleSection('removed')">
                    <svg class="cmp-section-toggle" :class="{ 'cmp-section-toggle-open': !collapsed.removed }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="14" height="14"><polyline points="9 6 15 12 9 18"></polyline></svg>
                    Removed talents ({{ removedGroups.length }})
                </button>
                <ul v-if="!collapsed.removed" class="cmp-row-list">
                    <li v-for="g in removedGroups" :key="g.anchorSpellId" class="cmp-row cmp-row-removed">
                        <span class="cmp-row-hoverable" @mouseenter="showTooltip(g.rank, $event)" @mousemove="showTooltip(g.rank, $event)" @mouseleave="hideTooltip">
                            <img v-if="g.rank.iconUrl" :src="g.rank.iconUrl" class="cmp-row-icon" alt="">
                            <span>{{ g.rank.name }}</span>
                        </span>
                        <span class="cmp-row-tab">{{ g.tabName }}</span>
                    </li>
                </ul>
            </section>

            <div v-if="tooltip" class="tt-tooltip" :style="tooltipStyle">
                <div class="tt-tooltip-title"><span>{{ tooltip.name }}</span></div>
                <template v-if="tooltip.isAbility">
                    <div v-if="tooltip.costText || tooltip.rangeText" class="tt-tooltip-row">
                        <span v-if="tooltip.costText">{{ tooltip.costText }}</span>
                        <span v-if="tooltip.rangeText">{{ tooltip.rangeText }}</span>
                    </div>
                    <div v-if="tooltip.castTimeText || tooltip.cooldownText" class="tt-tooltip-row">
                        <span v-if="tooltip.castTimeText">{{ tooltip.castTimeText }}</span>
                        <span v-if="tooltip.cooldownText">{{ tooltip.cooldownText }}</span>
                    </div>
                </template>
                <div v-for="line in tooltip.requirementLines" :key="line" class="tt-tooltip-requirement">{{ line }}</div>
                <div class="tt-tooltip-desc">{{ tooltip.description }}</div>
            </div>
        </template>
        <p v-else>Loading...</p>
    `,
});

compareApp.mount('#compare-tree-app');
