import { createApp } from 'vue';
import './styles/talent-tree.css';

/**
 * Interactive vanilla talent tree: click-to-spend grid per tab (tier x column, 4 columns
 * wide - see NUM_TALENT_COLUMNS in architecture.md), with the two real vanilla rules:
 * a tier only opens once total points spent in that TAB reach tier*5, and a talent needs
 * its prerequisite talent spent to at least the required rank. Both are enforced on every
 * click, not just visually - see canSpend()/canRemove() below.
 *
 * CELL/GAP here must match the --tt-cell/--tt-gap CSS custom properties in
 * talent-tree.css - the connector-line SVG computes pixel positions from these constants,
 * so the grid's actual CSS layout and the JS-computed line endpoints have to agree exactly.
 */
const CELL = 44;
const GAP = 22; // ~half the icon size, per feedback - was 6 (too tight)

function cellCenter(tier, columnIndex) {
    return {
        x: columnIndex * (CELL + GAP) + CELL / 2,
        y: tier * (CELL + GAP) + CELL / 2,
    };
}

createApp({
    data() {
        return {
            tree: null,
            error: null,
            spent: {},
            hoveredTab: null,
            hoveredTalent: null,
            tooltipPos: { x: 0, y: 0 },
        };
    },
    computed: {
        totalSpent() {
            return Object.values(this.spent).reduce((sum, r) => sum + r, 0);
        },
        // A computed (not a value snapshotted in the mouseenter/mousemove handler) so it
        // reactively tracks `spent` - clicking a cell without moving the mouse afterwards
        // must still update the still-open tooltip for that same cell.
        //
        // Matches the wowhead-calculator convention: nothing spent yet -> show rank 1's
        // text only (a preview of what learning it gives); some ranks spent -> show the
        // CURRENTLY learned rank's text plus a separate "next rank" preview (unless maxed,
        // where there's nothing left to preview).
        tooltip() {
            if (!this.hoveredTalent) return null;
            const talent = this.hoveredTalent;
            const spent = this.spent[talent.id] || 0;
            const mainRank = talent.ranks[spent === 0 ? 0 : spent - 1];
            const nextRank = (spent > 0 && spent < talent.maxRank) ? talent.ranks[spent] : null;
            return {
                name: mainRank.name,
                spent,
                maxRank: talent.maxRank,
                description: mainRank.description,
                nextDescription: nextRank?.description ?? null,
                canLearn: this.canSpend(this.hoveredTab, talent),
                x: this.tooltipPos.x,
                y: this.tooltipPos.y,
            };
        },
    },
    async mounted() {
        const el = document.getElementById('talent-tree-app');
        try {
            const response = await fetch(el.dataset.treeUrl);
            if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
            this.tree = await response.json();
        } catch (e) {
            this.error = e.message;
            return;
        }

        for (const tab of this.tree.tabs) {
            for (const talent of tab.talents) {
                this.spent[talent.id] = 0;
            }
        }
        this.loadFromUrl();

        this.$watch('spent', () => this.saveToUrl(), { deep: true });
    },
    methods: {
        totalSpentInTab(tab) {
            return tab.talents.reduce((sum, t) => sum + (this.spent[t.id] || 0), 0);
        },
        isUnlocked(tab, talent) {
            return this.totalSpentInTab(tab) >= talent.tier * 5;
        },
        prereqsMet(talent) {
            return talent.prerequisites.every((p) => (this.spent[p.requiresTalentId] || 0) >= p.requiresRank);
        },
        canSpend(tab, talent) {
            return (this.spent[talent.id] || 0) < talent.maxRank && this.isUnlocked(tab, talent) && this.prereqsMet(talent);
        },
        // Every already-spent talent in this tab must stay valid (tier unlocked, its own
        // prerequisites still met) after the simulated change - otherwise the change is
        // rejected. Covers both "removes a rank a dependent needs" and "drops the tab's
        // total below a tier threshold another spent talent relies on".
        wouldStayValid(tab) {
            const total = this.totalSpentInTab(tab);
            return tab.talents.every((t) => {
                const r = this.spent[t.id] || 0;
                if (r === 0) return true;
                if (total < t.tier * 5) return false;
                return t.prerequisites.every((p) => (this.spent[p.requiresTalentId] || 0) >= p.requiresRank);
            });
        },
        increment(tab, talent) {
            if (!this.canSpend(tab, talent)) return;
            this.spent[talent.id]++;
        },
        learnAll(tab, talent) {
            while (this.canSpend(tab, talent)) {
                this.spent[talent.id]++;
            }
        },
        onCellClick(tab, talent, event) {
            if (event.shiftKey) this.learnAll(tab, talent);
            else this.increment(tab, talent);
        },
        decrement(tab, talent) {
            if ((this.spent[talent.id] || 0) <= 0) return;
            this.spent[talent.id]--;
            if (!this.wouldStayValid(tab)) {
                this.spent[talent.id]++; // revert - would invalidate something already spent
            }
        },
        resetAll() {
            for (const id in this.spent) this.spent[id] = 0;
        },
        cellClasses(tab, talent) {
            const spent = this.spent[talent.id] || 0;
            return {
                'tt-locked': !this.canSpend(tab, talent) && spent === 0,
                'tt-spent': spent > 0,
                'tt-maxed': spent === talent.maxRank,
            };
        },
        cellStyle(talent) {
            return {
                gridColumn: talent.columnIndex + 1,
                gridRow: talent.tier + 1,
            };
        },
        gridStyle(tab) {
            const maxTier = Math.max(0, ...tab.talents.map((t) => t.tier));
            return {
                gridTemplateColumns: `repeat(4, ${CELL}px)`,
                gridTemplateRows: `repeat(${maxTier + 1}, ${CELL}px)`,
                width: `${4 * CELL + 3 * GAP}px`,
                height: `${(maxTier + 1) * CELL + maxTier * GAP}px`,
            };
        },
        connectors(tab) {
            const byId = Object.fromEntries(tab.talents.map((t) => [t.id, t]));
            const lines = [];
            for (const talent of tab.talents) {
                for (const prereq of talent.prerequisites) {
                    const from = byId[prereq.requiresTalentId];
                    if (!from) continue; // prerequisite outside this tab - not expected in vanilla, skip defensively
                    const a = cellCenter(from.tier, from.columnIndex);
                    const b = cellCenter(talent.tier, talent.columnIndex);
                    // Same column: straight vertical line. Different column: an elbow -
                    // down from the source, across, then down into the target - not a
                    // diagonal, matching how the real client/wowhead draw prerequisite
                    // arrows. The bend sits right below the SOURCE (middle of the gap
                    // right after its row), not at the midpoint of the whole span - when
                    // one source fans out to several targets (straight + elbow), this
                    // keeps every branch forking at the same point directly under the
                    // source instead of at a different height per target.
                    const points = a.x === b.x
                        ? `${a.x},${a.y} ${b.x},${b.y}`
                        : (() => {
                            const bendY = a.y + CELL / 2 + GAP / 2;
                            return `${a.x},${a.y} ${a.x},${bendY} ${b.x},${bendY} ${b.x},${b.y}`;
                        })();
                    lines.push({
                        key: `${prereq.requiresTalentId}-${talent.id}`,
                        points,
                        active: (this.spent[prereq.requiresTalentId] || 0) >= prereq.requiresRank,
                    });
                }
            }
            return lines;
        },
        showTooltip(tab, talent, event) {
            this.hoveredTab = tab;
            this.hoveredTalent = talent;
            this.tooltipPos = { x: event.clientX + 16, y: event.clientY + 16 };
        },
        hideTooltip() {
            this.hoveredTab = null;
            this.hoveredTalent = null;
        },
        loadFromUrl() {
            const raw = new URLSearchParams(location.search).get('build');
            if (!raw) return;
            const byId = {};
            for (const tab of this.tree.tabs) for (const t of tab.talents) byId[t.id] = t;
            for (const pair of raw.split(',')) {
                const [idStr, rankStr] = pair.split(':');
                const id = Number(idStr);
                const rank = Number(rankStr);
                const talent = byId[id];
                if (talent && Number.isInteger(rank) && rank > 0) {
                    this.spent[id] = Math.min(rank, talent.maxRank);
                }
            }
        },
        saveToUrl() {
            const parts = [];
            for (const id in this.spent) {
                if (this.spent[id] > 0) parts.push(`${id}:${this.spent[id]}`);
            }
            const url = new URL(location.href);
            if (parts.length > 0) {
                url.searchParams.set('build', parts.join(','));
            } else {
                url.searchParams.delete('build');
            }
            history.replaceState(null, '', url);
        },
    },
    template: `
        <div class="tt-page">
            <p v-if="error">Failed to load talent tree: {{ error }}</p>
            <template v-else-if="tree">
                <header class="tt-header">
                    <h1>{{ tree.class.name }} - {{ tree.build.label }}</h1>
                    <div class="tt-summary">
                        <span class="tt-summary-item" v-for="tab in tree.tabs" :key="tab.id">
                            <img v-if="tab.iconUrl" :src="tab.iconUrl" width="20" height="20" alt="">
                            {{ tab.name }}: {{ totalSpentInTab(tab) }}
                        </span>
                        <span class="tt-summary-total">Total: {{ totalSpent }}</span>
                        <button class="tt-reset" @click="resetAll">Reset</button>
                    </div>
                </header>
                <div class="tt-tabs">
                    <section class="tt-tab" v-for="tab in tree.tabs" :key="tab.id">
                        <h2 class="tt-tab-title">
                            <img v-if="tab.iconUrl" :src="tab.iconUrl" width="24" height="24" alt="">
                            {{ tab.name }}
                            <span class="tt-tab-points">{{ totalSpentInTab(tab) }}</span>
                        </h2>
                        <div class="tt-grid" :style="gridStyle(tab)">
                            <svg class="tt-connectors">
                                <polyline v-for="c in connectors(tab)" :key="c.key"
                                          :points="c.points"
                                          :class="{ 'tt-connector-active': c.active }" />
                            </svg>
                            <div v-for="talent in tab.talents" :key="talent.id"
                                 class="tt-cell" :class="cellClasses(tab, talent)" :style="cellStyle(talent)"
                                 @click="onCellClick(tab, talent, $event)"
                                 @contextmenu.prevent="decrement(tab, talent)"
                                 @mouseenter="showTooltip(tab, talent, $event)"
                                 @mousemove="showTooltip(tab, talent, $event)"
                                 @mouseleave="hideTooltip">
                                <img v-if="talent.ranks[0].iconUrl" :src="talent.ranks[0].iconUrl" alt="">
                                <span class="tt-cell-rank">{{ spent[talent.id] || 0 }}/{{ talent.maxRank }}</span>
                            </div>
                        </div>
                    </section>
                </div>
            </template>
            <p v-else>Loading...</p>
            <div v-if="tooltip" class="tt-tooltip" :style="{ left: tooltip.x + 'px', top: tooltip.y + 'px' }">
                <div class="tt-tooltip-title">{{ tooltip.name }} ({{ tooltip.spent }}/{{ tooltip.maxRank }})</div>
                <div class="tt-tooltip-desc">{{ tooltip.description }}</div>
                <template v-if="tooltip.nextDescription">
                    <div class="tt-tooltip-next-label">Next rank:</div>
                    <div class="tt-tooltip-desc">{{ tooltip.nextDescription }}</div>
                </template>
                <div v-if="tooltip.canLearn" class="tt-tooltip-actions">
                    Click to learn<template v-if="tooltip.maxRank > 1"> &middot; Shift-click to learn all ranks</template>
                </div>
            </div>
        </div>
    `,
}).mount('#talent-tree-app');
