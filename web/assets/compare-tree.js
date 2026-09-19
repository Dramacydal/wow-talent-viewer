import { createApp } from 'vue';
import './styles/talent-tree.css';
import './styles/compare-tree.css';
import { formatAbilityCost, formatAbilityRange, formatAbilityCastTime, formatAbilityCooldown } from './ability-format.js';

// Every row in every one of the 3 sections (New/Changed/Removed) is a single RANK, not a
// whole talent - a talent with 5 ranks where only rank 3 changed shows exactly one row
// ("TalentName (Rank 3)"), not the whole talent as one unit with 5 nested sub-rows. This
// mirrors how ranks are actually independent Spell.dbc rows with their own text/cost/range/
// etc. - see .claude-docs/architecture.md. The "(Rank N)" suffix is only shown when the
// talent actually has more than 1 rank (a 1-rank talent's name alone is enough).
function rankLabel(name, rankIndex, maxRank) {
    return maxRank > 1 ? `${name} (Rank ${rankIndex})` : name;
}

// Position/tab/rank-count are properties of the TALENT, not any one rank - attached to rank
// 1's row (which always exists) rather than inventing a separate non-rank row for them, so
// every row in the Changed list still corresponds to exactly one real rank.
function talentLevelFields(a, b) {
    const fields = [];
    if (a.tier !== b.tier || a.columnIndex !== b.columnIndex) {
        fields.push({ label: 'Position', before: `Tier ${a.tier}, Column ${a.columnIndex}`, after: `Tier ${b.tier}, Column ${b.columnIndex}` });
    }
    if (a.tabName !== b.tabName) {
        fields.push({ label: 'Tab', before: a.tabName, after: b.tabName });
    }
    if (a.maxRank !== b.maxRank) {
        fields.push({ label: 'Rank count', before: String(a.maxRank), after: String(b.maxRank) });
    }
    return fields;
}

// Every rank-level field this project already resolves and verifies against real client
// data (see BuildExtractor.ResolveAbilityFields/ResolveStanceRequirement/
// ResolveEquipRequirement) - compared pairwise, only differing ones produce a line.
function rankFields(a, b) {
    const fields = [];
    if (a.name !== b.name) fields.push({ label: 'Name', before: a.name, after: b.name });
    if (a.description !== b.description) fields.push({ label: 'Description', before: a.description, after: b.description });
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
            hoveredRank: null,
            tooltipStyle: { left: '0px', top: '0px' },
        };
    },
    computed: {
        // Classification is per RANK, independent of the matched talent's own overall
        // status - a rank doesn't care what happened to its siblings. This matters for
        // real cases like Hunter "Precision" -> "Humanoid Slaying" (same anchor spell id,
        // see gotchas.md): ranks 1-3 still exist and changed, but ranks 4-5 no longer exist
        // at all - those two individually belong in Removed, not nested inside a "Changed"
        // row for a talent that, as a whole, still exists. Talent-level fields (position/
        // tab/rank-count) have no rank of their own, so they ride along on rank 1's row
        // specifically (forcing rank 1 into Changed even if its own content is identical,
        // since *something* about the talent differs).
        allRows() {
            if (!this.data) return { added: [], changed: [], removed: [] };
            const added = [], changed = [], removed = [];
            for (const t of this.data.talents) {
                if (t.status === 'unchanged') continue;

                if (t.status === 'added') {
                    for (const r of t.b.ranks) {
                        added.push({ anchorSpellId: t.anchorSpellId, rankIndex: r.rankIndex, maxRank: t.b.maxRank, rank: r, tabName: t.b.tabName });
                    }
                    continue;
                }
                if (t.status === 'removed') {
                    for (const r of t.a.ranks) {
                        removed.push({ anchorSpellId: t.anchorSpellId, rankIndex: r.rankIndex, maxRank: t.a.maxRank, rank: r, tabName: t.a.tabName });
                    }
                    continue;
                }

                // status === 'changed': the talent (matched by anchor spell id) exists on
                // both sides, but individual ranks underneath it can still be pure adds/
                // removes/changes of their own.
                const ranksA = ranksByIndex(t.a);
                const ranksB = ranksByIndex(t.b);
                const rankIndices = Object.keys({ ...ranksA, ...ranksB }).map(Number).sort((x, y) => x - y);
                for (const rankIndex of rankIndices) {
                    const ra = ranksA[rankIndex];
                    const rb = ranksB[rankIndex];

                    if (rb && !ra) {
                        added.push({ anchorSpellId: t.anchorSpellId, rankIndex, maxRank: t.b.maxRank, rank: rb, tabName: t.b.tabName });
                        continue;
                    }
                    if (ra && !rb) {
                        removed.push({ anchorSpellId: t.anchorSpellId, rankIndex, maxRank: t.a.maxRank, rank: ra, tabName: t.a.tabName });
                        continue;
                    }

                    const fields = rankIndex === 1 ? talentLevelFields(t.a, t.b) : [];
                    fields.push(...rankFields(ra, rb));
                    if (fields.length === 0) continue;
                    changed.push({ anchorSpellId: t.anchorSpellId, rankIndex, maxRank: t.b.maxRank, rank: rb, tabName: t.b.tabName, fields });
                }
            }
            return { added, changed, removed };
        },
        addedRows() { return this.allRows.added; },
        changedRows() { return this.allRows.changed; },
        removedRows() { return this.allRows.removed; },
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
        rankLabel,
        toggleSection(key) {
            this.collapsed[key] = !this.collapsed[key];
        },
        showTooltip(rank, event) {
            this.hoveredRank = rank;
            const wouldSqueeze = event.clientX + 16 + 280 > window.innerWidth;
            this.tooltipStyle = wouldSqueeze
                ? { right: `${window.innerWidth - event.clientX + 16}px`, top: `${event.clientY + 16}px` }
                : { left: `${event.clientX + 16}px`, top: `${event.clientY + 16}px` };
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
                    New talents ({{ addedRows.length }})
                </button>
                <ul v-if="!collapsed.added" class="cmp-row-list">
                    <li v-for="row in addedRows" :key="'a'+row.anchorSpellId+'-'+row.rankIndex" class="cmp-row cmp-row-added"
                        @mouseenter="showTooltip(row.rank, $event)" @mousemove="showTooltip(row.rank, $event)" @mouseleave="hideTooltip">
                        <img v-if="row.rank.iconUrl" :src="row.rank.iconUrl" class="cmp-row-icon" alt="">
                        <span>{{ rankLabel(row.rank.name, row.rankIndex, row.maxRank) }}</span>
                        <span class="cmp-row-tab">{{ row.tabName }}</span>
                    </li>
                </ul>
            </section>

            <section class="cmp-section">
                <button class="cmp-section-header" @click="toggleSection('changed')">
                    <svg class="cmp-section-toggle" :class="{ 'cmp-section-toggle-open': !collapsed.changed }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="14" height="14"><polyline points="9 6 15 12 9 18"></polyline></svg>
                    Changed talents ({{ changedRows.length }})
                </button>
                <ul v-if="!collapsed.changed" class="cmp-row-list">
                    <li v-for="row in changedRows" :key="'c'+row.anchorSpellId+'-'+row.rankIndex" class="cmp-row-block">
                        <div class="cmp-row cmp-row-changed"
                             @mouseenter="showTooltip(row.rank, $event)" @mousemove="showTooltip(row.rank, $event)" @mouseleave="hideTooltip">
                            <img v-if="row.rank.iconUrl" :src="row.rank.iconUrl" class="cmp-row-icon" alt="">
                            <span>{{ rankLabel(row.rank.name, row.rankIndex, row.maxRank) }}</span>
                            <span class="cmp-row-tab">{{ row.tabName }}</span>
                        </div>
                        <ul class="cmp-field-list">
                            <li v-for="f in row.fields" :key="f.label" class="cmp-field-row">
                                <span class="cmp-field-label">{{ f.label }}:</span>
                                <span class="cmp-field-before">{{ f.before }}</span>
                                <span class="cmp-field-arrow">→</span>
                                <span class="cmp-field-after">{{ f.after }}</span>
                            </li>
                        </ul>
                    </li>
                </ul>
            </section>

            <section class="cmp-section">
                <button class="cmp-section-header" @click="toggleSection('removed')">
                    <svg class="cmp-section-toggle" :class="{ 'cmp-section-toggle-open': !collapsed.removed }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" width="14" height="14"><polyline points="9 6 15 12 9 18"></polyline></svg>
                    Removed talents ({{ removedRows.length }})
                </button>
                <ul v-if="!collapsed.removed" class="cmp-row-list">
                    <li v-for="row in removedRows" :key="'r'+row.anchorSpellId+'-'+row.rankIndex" class="cmp-row cmp-row-removed"
                        @mouseenter="showTooltip(row.rank, $event)" @mousemove="showTooltip(row.rank, $event)" @mouseleave="hideTooltip">
                        <img v-if="row.rank.iconUrl" :src="row.rank.iconUrl" class="cmp-row-icon" alt="">
                        <span>{{ rankLabel(row.rank.name, row.rankIndex, row.maxRank) }}</span>
                        <span class="cmp-row-tab">{{ row.tabName }}</span>
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
