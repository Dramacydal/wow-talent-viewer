<?php

namespace App\Comparison;

use App\Asset\AssetUrlResolver;
use App\Entity\CharacterClass;
use App\Entity\ClientBuild;
use App\Entity\Talent;
use App\Entity\TalentRank;
use App\Repository\TalentRepository;

/**
 * Diffs one class's talent tree between two client builds.
 *
 * Matching key is deliberately the FIRST rank's real Blizzard spell id (Talent.SpellRank[0]),
 * not Talent.dbc's own row ID (`Talent.sourceTalentId`) or TalentTab's row ID
 * (`TalentTab.sourceTabId`) - both are documented as build-local only and get renumbered/
 * reused across builds (see the Talent/TalentTab entity docblocks and the real TabID=24
 * case in .claude-docs/gotchas.md: orphaned on 0.7.0.3694, a completely unrelated "Test" tab
 * on 0.8.0.3734). A spell id is Blizzard's actual persistent identity for an ability's
 * content, which is what "the same talent" needs to mean here - including across a talent
 * moving to a different tab, or a genuine rename (spell id same, text different). A talent
 * whose rank-1 ability was swapped for a completely different one is correctly seen as
 * remove-then-add, not a "change", since that's a different spell id.
 */
final class TalentTreeComparer
{
    public function __construct(
        private readonly TalentRepository $talents,
        private readonly AssetUrlResolver $assets,
    ) {
    }

    public function compare(ClientBuild $buildA, ClientBuild $buildB, CharacterClass $class): array
    {
        $all = $this->talents->findForBuildsAndClass([$buildA->getId(), $buildB->getId()], $class);

        $bySide = ['a' => [], 'b' => []];
        foreach ($all as $talent) {
            $side = $talent->getClientBuild()->getId() === $buildA->getId() ? 'a' : 'b';
            $rank1 = $this->rankByIndex($talent, 1);
            if ($rank1 === null) {
                continue; // shouldn't happen for real data - every talent has a rank 1 - defensive only
            }
            $bySide[$side][$rank1->getSpellId()] = $talent;
        }

        $anchorSpellIds = array_keys($bySide['a'] + $bySide['b']);
        sort($anchorSpellIds);

        $results = [];
        foreach ($anchorSpellIds as $anchorSpellId) {
            $talentA = $bySide['a'][$anchorSpellId] ?? null;
            $talentB = $bySide['b'][$anchorSpellId] ?? null;

            if ($talentA === null) {
                $results[] = ['anchorSpellId' => $anchorSpellId, 'status' => 'added', 'a' => null, 'b' => $this->serializeTalent($talentB)];
                continue;
            }
            if ($talentB === null) {
                $results[] = ['anchorSpellId' => $anchorSpellId, 'status' => 'removed', 'a' => $this->serializeTalent($talentA), 'b' => null];
                continue;
            }

            $changed = $this->talentsDiffer($talentA, $talentB);
            $results[] = [
                'anchorSpellId' => $anchorSpellId,
                'status' => $changed ? 'changed' : 'unchanged',
                // Unchanged talents carry no payload - the frontend doesn't need full rank
                // data for something identical in both builds, and this is exactly the
                // traffic the whole point of a backend diff is meant to avoid sending.
                'a' => $changed ? $this->serializeTalent($talentA) : null,
                'b' => $changed ? $this->serializeTalent($talentB) : null,
            ];
        }

        return $results;
    }

    private function talentsDiffer(Talent $a, Talent $b): bool
    {
        if ($a->getTier() !== $b->getTier()) return true;
        if ($a->getColumnIndex() !== $b->getColumnIndex()) return true;
        if ($a->getMaxRank() !== $b->getMaxRank()) return true;
        if ($a->getTalentTab()->getName() !== $b->getTalentTab()->getName()) return true;

        $ranksA = $this->ranksByIndex($a);
        $ranksB = $this->ranksByIndex($b);
        foreach (array_keys($ranksA + $ranksB) as $rankIndex) {
            $ra = $ranksA[$rankIndex] ?? null;
            $rb = $ranksB[$rankIndex] ?? null;
            if ($ra === null || $rb === null) {
                return true; // rank count mismatch - already caught by maxRank above in the normal case, kept as a defensive fallback
            }
            if ($this->ranksDiffer($ra, $rb)) {
                return true;
            }
        }

        return false;
    }

    private function ranksDiffer(TalentRank $a, TalentRank $b): bool
    {
        return $a->getSpellId() !== $b->getSpellId()
            || $a->getName() !== $b->getName()
            || $a->getIconPath() !== $b->getIconPath()
            || $a->getDescription() !== $b->getDescription()
            || $a->isAbility() !== $b->isAbility()
            || $a->getPowerType() !== $b->getPowerType()
            || $a->getPowerCost() !== $b->getPowerCost()
            || $a->getIsMeleeRange() !== $b->getIsMeleeRange()
            || $a->getRangeMinYards() !== $b->getRangeMinYards()
            || $a->getRangeMaxYards() !== $b->getRangeMaxYards()
            || $a->getCastTimeMs() !== $b->getCastTimeMs()
            || $a->getCooldownMs() !== $b->getCooldownMs()
            || $a->getStanceRequirement() !== $b->getStanceRequirement()
            || $a->getEquipRequirement() !== $b->getEquipRequirement();
    }

    /** @return array<int, TalentRank> rank index => rank */
    private function ranksByIndex(Talent $talent): array
    {
        $out = [];
        foreach ($talent->getRanks() as $rank) {
            $out[$rank->getRankIndex()] = $rank;
        }

        return $out;
    }

    private function rankByIndex(Talent $talent, int $rankIndex): ?TalentRank
    {
        return $this->ranksByIndex($talent)[$rankIndex] ?? null;
    }

    private function serializeTalent(Talent $talent): array
    {
        return [
            'tabName' => $talent->getTalentTab()->getName(),
            'tier' => $talent->getTier(),
            'columnIndex' => $talent->getColumnIndex(),
            'maxRank' => $talent->getMaxRank(),
            'ranks' => array_map(
                fn (TalentRank $r) => [
                    'rankIndex' => $r->getRankIndex(),
                    'spellId' => $r->getSpellId(),
                    'name' => $r->getName(),
                    'description' => $r->getDescription(),
                    'iconUrl' => $this->assets->iconUrl($r->getIconPath()),
                    'isAbility' => $r->isAbility(),
                    'powerType' => $r->getPowerType(),
                    'powerCost' => $r->getPowerCost(),
                    'isMeleeRange' => $r->getIsMeleeRange(),
                    'rangeMinYards' => $r->getRangeMinYards(),
                    'rangeMaxYards' => $r->getRangeMaxYards(),
                    'castTimeMs' => $r->getCastTimeMs(),
                    'cooldownMs' => $r->getCooldownMs(),
                    'stanceRequirement' => $r->getStanceRequirement(),
                    'equipRequirement' => $r->getEquipRequirement(),
                ],
                [...$talent->getRanks()],
            ),
        ];
    }
}
