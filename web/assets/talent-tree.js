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
const ARROW_LEN = 9; // must match the marker's markerWidth/markerHeight below
const TOOLTIP_MAX_WIDTH = 280; // must match .tt-tooltip's max-width in talent-tree.css

// Vanilla's talent panel is always 7 tier-rows tall (0-6, gated every 5 points up to 30) -
// a fixed structural constant of the client's own talent UI, not talent data: every
// populated tab across the whole extracted dataset has maxTier === 6. Used unconditionally
// by tabArtStyle() (the background art box's height must never depend on a tab's own
// talent count - see that method).
const MIN_TIER_ROWS = 7;

// The only 3 power types that occur on player talent abilities in vanilla - see
// BuildExtractor.ResolveAbilityFields / .claude-docs/gotchas.md.
const POWER_TYPE_NAMES = { 0: 'Mana', 1: 'Rage', 3: 'Energy' };

// Trims a fractional value to at most 2 decimals without trailing zeros (1.50 -> "1.5",
// 3.00 -> "3") - cast time/cooldown/range can all carry real fractional parts.
function trimNumber(n) {
    return Number(n.toFixed(2)).toString();
}

// wowhead-tooltip-style cost/range/cast-time/cooldown lines for an "active ability" rank
// (see talent_ranks.is_ability - a talent that grants a usable, castable ability rather
// than being a pure passive). Each returns null when that side has nothing to show, so the
// template can render just the other side (a lone flex child in a `justify-content:
// space-between` row naturally sits at the start/left - see .tt-tooltip-row CSS).
function formatAbilityCost(rank) {
    if (!rank.powerCost) return null;
    return `${rank.powerCost} ${POWER_TYPE_NAMES[rank.powerType] ?? rank.powerType}`;
}

function formatAbilityRange(rank) {
    if (rank.isMeleeRange) return 'Melee Range';
    if (!rank.rangeMaxYards) return null;
    const max = trimNumber(rank.rangeMaxYards);
    return rank.rangeMinYards ? `${trimNumber(rank.rangeMinYards)}-${max} yd range` : `${max} yd range`;
}

function formatAbilityCastTime(rank) {
    if (rank.castTimeMs === null || rank.castTimeMs === undefined) return null;
    return rank.castTimeMs === 0 ? 'Instant cast' : `${trimNumber(rank.castTimeMs / 1000)} sec cast`;
}

function formatAbilityCooldown(rank) {
    if (!rank.cooldownMs) return null;
    return rank.cooldownMs > 60000
        ? `${trimNumber(rank.cooldownMs / 60000)} min cooldown`
        : `${trimNumber(rank.cooldownMs / 1000)} sec cooldown`;
}

function cellCenter(tier, columnIndex) {
    return {
        x: columnIndex * (CELL + GAP) + CELL / 2,
        y: tier * (CELL + GAP) + CELL / 2,
    };
}

// Settings modal (gear icon) lives in tree/show.html.twig as plain HTML/JS outside this
// Vue app, since it sits in the .tt-picker row alongside the build/class <select>s, not
// inside #talent-tree-app. It reaches these settings by mutating window.ttApp.settings
// directly - same reactive object Vue itself uses, so the change is picked up immediately
// with no event-bus/custom-event plumbing needed.
const SETTINGS_STORAGE_KEY = 'tt-settings';
const DEFAULT_SETTINGS = { sortTabsAlphabetically: false, showTalentIdInTooltip: false };

function loadSettings() {
    try {
        const raw = localStorage.getItem(SETTINGS_STORAGE_KEY);
        return raw ? { ...DEFAULT_SETTINGS, ...JSON.parse(raw) } : { ...DEFAULT_SETTINGS };
    } catch {
        return { ...DEFAULT_SETTINGS };
    }
}

const vueApp = createApp({
    data() {
        return {
            tree: null,
            error: null,
            spent: {},
            hoveredTab: null,
            hoveredTalent: null,
            // A style object, not {x,y} numbers - see showTooltip()/clampTooltipToViewportBottom()
            // for why the horizontal side needs `left` in one case and `right` in the other.
            tooltipStyle: { left: '0px', top: '0px' },
            settings: loadSettings(),
        };
    },
    computed: {
        totalSpent() {
            return Object.values(this.spent).reduce((sum, r) => sum + r, 0);
        },
        // Backend order (talent_tabs.order_index, with an id tie-break - see
        // .claude-docs/gotchas.md) reflects the real client layout; alphabetical is purely
        // an opt-in convenience for players who'd rather scan tabs by name.
        visibleTabs() {
            if (!this.tree) return [];
            if (!this.settings.sortTabsAlphabetically) return this.tree.tabs;
            return [...this.tree.tabs].sort((a, b) => a.name.localeCompare(b.name));
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
            const tab = this.hoveredTab;
            const talent = this.hoveredTalent;
            const spent = this.spent[talent.id] || 0;
            const mainRank = talent.ranks[spent === 0 ? 0 : spent - 1];
            const nextRank = (spent > 0 && spent < talent.maxRank) ? talent.ranks[spent] : null;
            return {
                name: mainRank.name,
                talentId: talent.id,
                spent,
                maxRank: talent.maxRank,
                description: mainRank.description,
                nextDescription: nextRank?.description ?? null,
                canLearn: this.canSpend(tab, talent),
                lockReasons: this.lockReasons(tab, talent),
                isAbility: mainRank.isAbility,
                costText: formatAbilityCost(mainRank),
                rangeText: formatAbilityRange(mainRank),
                castTimeText: formatAbilityCastTime(mainRank),
                cooldownText: formatAbilityCooldown(mainRank),
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
        this.$watch('settings', () => {
            localStorage.setItem(SETTINGS_STORAGE_KEY, JSON.stringify(this.settings));
        }, { deep: true });
    },
    methods: {
        totalSpentInTab(tab) {
            return tab.talents.reduce((sum, t) => sum + (this.spent[t.id] || 0), 0);
        },
        maxPointsInTab(tab) {
            return tab.talents.reduce((sum, t) => sum + t.maxRank, 0);
        },
        isUnlocked(tab, talent) {
            return this.totalSpentInTab(tab) >= talent.tier * 5;
        },
        prereqsMet(talent) {
            return talent.prerequisites.every((p) => (this.spent[p.requiresTalentId] || 0) >= p.requiresRank);
        },
        // "Why is this locked" lines for the tooltip. Tier line shows the ABSOLUTE
        // requirement (total points needed in the tab) - unlike the prereq lines below, this
        // one is not a shortfall. Prereq lines show the SHORTFALL instead (how many more
        // points are still needed): e.g. a prereq needing 3/3 with 2 already spent shows "1
        // more", not "3". Tier line first (if any), then one line per unmet prerequisite -
        // matches the order a player would fix them in (open the tier first, then the
        // specific prereq).
        lockReasons(tab, talent) {
            const reasons = [];
            const tierNeeded = talent.tier * 5;
            if (this.totalSpentInTab(tab) < tierNeeded) {
                reasons.push(`Requires ${tierNeeded} points in ${tab.name} Talents`);
            }
            for (const prereq of talent.prerequisites) {
                const have = this.spent[prereq.requiresTalentId] || 0;
                const shortfall = prereq.requiresRank - have;
                if (shortfall <= 0) continue;
                const prereqTalent = tab.talents.find((t) => t.id === prereq.requiresTalentId);
                const prereqName = prereqTalent ? prereqTalent.ranks[0].name : 'Unknown Talent';
                // Absolute requirement if nothing spent yet (matches the tier line's style);
                // shortfall only once the player has actually started investing in it.
                reasons.push(have === 0
                    ? `Requires ${prereq.requiresRank} point${prereq.requiresRank === 1 ? '' : 's'} in ${prereqName}`
                    : `Needs ${shortfall} more point${shortfall === 1 ? '' : 's'} in ${prereqName}`);
            }
            return reasons;
        },
        canSpend(tab, talent) {
            return (this.spent[talent.id] || 0) < talent.maxRank && this.isUnlocked(tab, talent) && this.prereqsMet(talent);
        },
        // Whether a talent's gate is fully open (tier unlocked AND *every* one of its
        // prerequisites met - not just maxRank), independent of whether it's already
        // maxed. Used to color prerequisite connector lines: a talent can have more than
        // one prerequisite (see gotchas.md, 0.7.0.3694), and a single satisfied edge must
        // NOT turn gold on its own while a sibling prerequisite is still unmet - both (or
        // all) of a target's incoming lines share this same one true/false state, they
        // don't light up independently per edge.
        targetGateOpen(tab, talent) {
            return this.isUnlocked(tab, talent) && this.prereqsMet(talent);
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
        // Three mutually exclusive states: maxed (gold), available to spend right now
        // (green - tier unlocked, all prerequisites met), or locked (default/gray).
        cellClasses(tab, talent) {
            const spent = this.spent[talent.id] || 0;
            if (spent === talent.maxRank) return { 'tt-maxed': true };
            if (this.canSpend(tab, talent)) return { 'tt-available': true };
            return { 'tt-locked': true };
        },
        cellStyle(talent) {
            return {
                gridColumn: talent.columnIndex + 1,
                gridRow: talent.tier + 1,
            };
        },
        // The 4 quadrant textures the client itself composites a tab's panel background
        // from (see architecture.md/gotchas.md). Each corner is its own absolutely-
        // positioned div (.tt-tab-art-corner.{tl,tr,bl,br} in the template, sizing/cropping
        // handled by fixed CSS percentages - see talent-tree.css) - this method only sets
        // which image to show. Missing quadrants (older/incomplete builds) leave that corner
        // blank - the dark ::before vignette still covers the rest.
        cornerStyle(url) {
            return { backgroundImage: url ? `url(${url})` : 'none' };
        },
        // .tt-tab-art's height must be set EXPLICITLY (not left to auto/content-driven
        // block flow) - its 4 corner children are position:absolute with percentage
        // heights, and a percentage height on an absolutely-positioned element only
        // resolves against a containing block that itself has a definite (non-auto)
        // height; against 'auto' it computes to 0, which is exactly what silently made
        // every corner invisible (0 height, still "visible", no error) until caught by
        // inspecting getBoundingClientRect() directly.
        //
        // Deliberately NOT derived from this tab's own talent data (no Math.max over
        // tab.talents): the background quadrant textures are cropped by fixed CSS
        // percentages of THIS box, so the box's proportions must always match the real
        // panel, independent of how many talents this particular tab/build happens to
        // have. Using MIN_TIER_ROWS unconditionally (same formula gridStyle() uses per-tab)
        // is what keeps a sparse/empty tab's art from squashing - a data-driven height
        // collapsed to a single row for Warlock Demonology on 0.11.0.3925 (0 talents,
        // "no demonology talents yet" per that patch), stretching the correctly-sized
        // quadrant PNGs into a fraction of their real height.
        tabArtStyle() {
            const rows = MIN_TIER_ROWS;
            return { height: `${rows * CELL + (rows - 1) * GAP + 20}px` };
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
                    // Same column -> straight vertical. Same tier (rare but real, see
                    // gotchas.md) -> straight horizontal. Otherwise -> a right-angle elbow
                    // that goes RIGHT from the source's own center first, then straight
                    // down into the target - not a diagonal, and not "down then across"
                    // either: a source with two dependents (one directly below, one
                    // diagonal) must not have its straight and elbow lines share ANY
                    // pixels, or they visually read as one line that forks partway down.
                    // Going right first, at the source's own y, means the two paths only
                    // ever share their single common starting POINT, never a segment.
                    // The arrowhead tip should land on the target ICON's edge, not travel
                    // all the way to its center (which the polyline math otherwise uses
                    // throughout, for correct bend geometry). The polyline itself is drawn
                    // even shorter than that - pulled back by the marker's own length on
                    // top of the half-cell edge offset - so the line's stroke ends where
                    // the arrow's WIDE base is (fully covering the line's flat end cap),
                    // and only the solid triangle occupies the last ARROW_LEN px out to the
                    // edge. Ending the line at the tip itself looked "blunt": a line this
                    // thick meeting a mathematically zero-width point leaves the line's own
                    // squared-off cap sticking out past the tip on both sides.
                    const pullback = CELL / 2 + ARROW_LEN;
                    const points = a.x === b.x
                        ? `${a.x},${a.y} ${b.x},${b.y - pullback}`
                        : a.y === b.y
                            ? `${a.x},${a.y} ${b.x - Math.sign(b.x - a.x) * pullback},${a.y}`
                            : `${a.x},${a.y} ${b.x},${a.y} ${b.x},${b.y - pullback}`;
                    lines.push({
                        key: `${prereq.requiresTalentId}-${talent.id}`,
                        points,
                        active: this.targetGateOpen(tab, talent),
                    });
                }
            }
            return lines;
        },
        showTooltip(tab, talent, event) {
            this.hoveredTab = tab;
            this.hoveredTalent = talent;
            // Horizontal side is decided BEFORE render, from TOOLTIP_MAX_WIDTH - not by
            // measuring afterwards like the vertical clamp below. A position:fixed box with
            // only `left` set (no `right`) and no explicit width uses (viewport width - left)
            // as its shrink-to-fit ceiling, so the browser silently narrows it to always fit
            // rather than ever actually overflowing past the right edge - measuring
            // getBoundingClientRect().right after render would basically never see an
            // overflow to react to, it'd just observe an already-squeezed box.
            //
            // When flipped, anchor with `right` (not a computed `left`) - the box's actual
            // width varies with content (a short description renders narrower than
            // TOOLTIP_MAX_WIDTH), so anchoring by `left = mouseX - 16 - TOOLTIP_MAX_WIDTH`
            // left a bigger visible gap before the cursor for shorter tooltips (their right
            // edge fell short of mouseX - 16, since the box never reaches full max-width).
            // `right` keeps that edge - and so the visible gap - the same regardless of how
            // wide the box ends up being.
            const wouldSqueeze = event.clientX + 16 + TOOLTIP_MAX_WIDTH > window.innerWidth;
            this.tooltipStyle = wouldSqueeze
                ? { right: `${window.innerWidth - event.clientX + 16}px`, top: `${event.clientY + 16}px` }
                : { left: `${event.clientX + 16}px`, top: `${event.clientY + 16}px` };
            // Vertical overflow doesn't have the same auto-shrink behavior (height isn't
            // capped the way width is), so it genuinely overflows past the bottom and a
            // post-render measurement is the right tool here.
            this.$nextTick(() => this.clampTooltipToViewportBottom());
        },
        clampTooltipToViewportBottom() {
            const el = this.$refs.tooltipEl;
            if (!el) return;
            const overflow = el.getBoundingClientRect().bottom - window.innerHeight;
            if (overflow > 0) {
                const currentTop = parseFloat(this.tooltipStyle.top);
                this.tooltipStyle = { ...this.tooltipStyle, top: `${currentTop - overflow - 8}px` };
            }
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
            <svg width="0" height="0" style="position: absolute;">
                <defs>
                    <!-- refX=0: the marker anchors at its WIDE base (x=0), not the pointed
                         tip - the polyline's own endpoint (and its stroke's flat end cap)
                         sits under that wide base where it's fully covered, and the tip
                         (x=10) extends ARROW_LEN px further out from there. See the
                         pullback comment in connectors() for why. -->
                    <marker id="tt-arrow-gray" viewBox="0 0 10 10" refX="0" refY="5" markerWidth="9" markerHeight="9" markerUnits="userSpaceOnUse" orient="auto">
                        <path d="M0,0 L10,5 L0,10 z" fill="#6b6b6b" />
                    </marker>
                    <marker id="tt-arrow-gold" viewBox="0 0 10 10" refX="0" refY="5" markerWidth="9" markerHeight="9" markerUnits="userSpaceOnUse" orient="auto">
                        <path d="M0,0 L10,5 L0,10 z" fill="#c9a227" />
                    </marker>
                </defs>
            </svg>
            <p v-if="error">Failed to load talent tree: {{ error }}</p>
            <template v-else-if="tree">
                <header class="tt-header">
                    <h1>{{ tree.class.name }} - {{ tree.build.label }}</h1>
                    <div class="tt-summary">
                        <span class="tt-summary-item" v-for="tab in visibleTabs" :key="tab.id">
                            <img v-if="tab.iconUrl" :src="tab.iconUrl" width="20" height="20" alt="">
                            {{ tab.name }}: {{ totalSpentInTab(tab) }}
                        </span>
                        <span class="tt-summary-total">Total: {{ totalSpent }}</span>
                        <button class="tt-reset" @click="resetAll">Reset</button>
                    </div>
                </header>
                <div class="tt-tabs">
                    <section class="tt-tab" v-for="tab in visibleTabs" :key="tab.id">
                        <h2 class="tt-tab-title">
                            <img v-if="tab.iconUrl" :src="tab.iconUrl" width="24" height="24" alt="">
                            {{ tab.name }}
                            <span class="tt-tab-points">{{ totalSpentInTab(tab) }} / {{ maxPointsInTab(tab) }}</span>
                        </h2>
                        <div class="tt-tab-art" :style="tabArtStyle(tab)">
                        <div class="tt-tab-art-corner tl" :style="cornerStyle((tab.background || {}).topLeft)"></div>
                        <div class="tt-tab-art-corner tr" :style="cornerStyle((tab.background || {}).topRight)"></div>
                        <div class="tt-tab-art-corner bl" :style="cornerStyle((tab.background || {}).bottomLeft)"></div>
                        <div class="tt-tab-art-corner br" :style="cornerStyle((tab.background || {}).bottomRight)"></div>
                        <div class="tt-grid" :style="gridStyle(tab)">
                            <svg class="tt-connectors">
                                <polyline v-for="c in connectors(tab)" :key="c.key"
                                          :points="c.points"
                                          :marker-end="c.active ? 'url(#tt-arrow-gold)' : 'url(#tt-arrow-gray)'"
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
                        </div>
                    </section>
                </div>
            </template>
            <p v-else>Loading...</p>
            <div v-if="tooltip" ref="tooltipEl" class="tt-tooltip" :style="tooltipStyle">
                <div class="tt-tooltip-title">
                    <span>{{ tooltip.name }} ({{ tooltip.spent }}/{{ tooltip.maxRank }})</span>
                    <span v-if="settings.showTalentIdInTooltip" class="tt-tooltip-id">#{{ tooltip.talentId }}</span>
                </div>
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
                <div class="tt-tooltip-desc">{{ tooltip.description }}</div>
                <template v-if="tooltip.nextDescription">
                    <div class="tt-tooltip-next-label">Next rank:</div>
                    <div class="tt-tooltip-desc">{{ tooltip.nextDescription }}</div>
                </template>
                <div v-for="reason in tooltip.lockReasons" :key="reason" class="tt-tooltip-lock-reason">{{ reason }}</div>
                <div v-if="tooltip.canLearn" class="tt-tooltip-actions">
                    Click to learn<template v-if="tooltip.maxRank > 1"> &middot; Shift-click to learn all ranks</template>
                </div>
            </div>
        </div>
    `,
}).mount('#talent-tree-app');
// Exposed so the settings modal (plain HTML/JS in tree/show.html.twig, outside this Vue
// app - it sits in the .tt-picker row, not inside #talent-tree-app) can mutate `settings`
// directly and get Vue's reactivity for free.
window.ttApp = vueApp;
