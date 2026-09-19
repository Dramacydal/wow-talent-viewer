<?php

namespace App\Controller\Api;

use App\Entity\Talent;
use App\Entity\TalentTab;
use App\Repository\CharacterClassRepository;
use App\Repository\ClientBuildRepository;
use App\Repository\TalentPrerequisiteRepository;
use App\Repository\TalentRepository;
use App\Repository\TalentTabRepository;
use Symfony\Bundle\FrameworkBundle\Controller\AbstractController;
use Symfony\Component\HttpFoundation\JsonResponse;
use Symfony\Component\HttpKernel\Exception\NotFoundHttpException;
use Symfony\Component\Routing\Attribute\Route;

/** The one endpoint the Vue talent-tree island needs: everything to render one class's tree
 * for one build in a single request (tabs, talents, ranks, prerequisites) - no follow-up
 * calls back to Spell.dbc/etc. at render time, matching how extraction already denormalized
 * spell text into talent_ranks (see .claude-docs/architecture.md). */
class TalentTreeController extends AbstractController
{
    #[Route('/api/builds/{buildLabel}/classes/{classSlug}/talent-tree', name: 'api_talent_tree', methods: ['GET'])]
    public function show(
        string $buildLabel,
        string $classSlug,
        ClientBuildRepository $builds,
        CharacterClassRepository $classes,
        TalentTabRepository $tabs,
        TalentRepository $talents,
        TalentPrerequisiteRepository $prerequisites,
    ): JsonResponse {
        $build = $builds->findOneByLabel($buildLabel) ?? throw new NotFoundHttpException("Unknown build \"$buildLabel\"");
        $class = $classes->findOneBySlug($classSlug) ?? throw new NotFoundHttpException("Unknown class \"$classSlug\"");

        $tabEntities = $tabs->findForBuildAndClass($build, $class);
        $talentEntities = $talents->findForBuildAndClass($build, $class);

        $talentsByTab = [];
        foreach ($talentEntities as $talent) {
            $talentsByTab[$talent->getTalentTab()->getId()][] = $talent;
        }

        $prereqsByTalent = [];
        foreach ($prerequisites->findForTalentIds(array_map(static fn (Talent $t) => $t->getId(), $talentEntities)) as $prereq) {
            $prereqsByTalent[$prereq->getTalent()->getId()][] = [
                'requiresTalentId' => $prereq->getRequiresTalent()->getId(),
                'requiresRank' => $prereq->getRequiresRank(),
            ];
        }

        return $this->json([
            'build' => ['id' => $build->getId(), 'label' => $build->getLabel()],
            'class' => ['id' => $class->getId(), 'slug' => $class->getSlug(), 'name' => $class->getName()],
            'tabs' => array_map(
                fn (TalentTab $tab) => [
                    'id' => $tab->getId(),
                    'name' => $tab->getName(),
                    'iconUrl' => self::iconUrl($tab->getIconPath()),
                    'background' => [
                        'topLeft' => self::backgroundUrl($tab->getBackgroundTopLeftPath()),
                        'topRight' => self::backgroundUrl($tab->getBackgroundTopRightPath()),
                        'bottomLeft' => self::backgroundUrl($tab->getBackgroundBottomLeftPath()),
                        'bottomRight' => self::backgroundUrl($tab->getBackgroundBottomRightPath()),
                    ],
                    'talents' => array_map(
                        fn (Talent $talent) => [
                            'id' => $talent->getId(),
                            'sourceTalentId' => $talent->getSourceTalentId(),
                            'tier' => $talent->getTier(),
                            'columnIndex' => $talent->getColumnIndex(),
                            'maxRank' => $talent->getMaxRank(),
                            'prerequisites' => $prereqsByTalent[$talent->getId()] ?? [],
                            'ranks' => array_map(
                                static fn ($rank) => [
                                    'rankIndex' => $rank->getRankIndex(),
                                    'name' => $rank->getName(),
                                    'description' => $rank->getDescription(),
                                    'iconUrl' => self::iconUrl($rank->getIconPath()),
                                    'isAbility' => $rank->isAbility(),
                                    'powerType' => $rank->getPowerType(),
                                    'powerCost' => $rank->getPowerCost(),
                                    'isMeleeRange' => $rank->getIsMeleeRange(),
                                    'rangeMinYards' => $rank->getRangeMinYards(),
                                    'rangeMaxYards' => $rank->getRangeMaxYards(),
                                    'castTimeMs' => $rank->getCastTimeMs(),
                                    'cooldownMs' => $rank->getCooldownMs(),
                                    'stanceRequirement' => $rank->getStanceRequirement(),
                                    'equipRequirement' => $rank->getEquipRequirement(),
                                ],
                                $talent->getRanks()->toArray(),
                            ),
                        ],
                        $talentsByTab[$tab->getId()] ?? [],
                    ),
                ],
                $tabEntities,
            ),
        ]);
    }

    private static function iconUrl(?string $iconPath): ?string
    {
        return $iconPath === null ? null : "/icons/$iconPath.png";
    }

    private static function backgroundUrl(?string $iconPath): ?string
    {
        return $iconPath === null ? null : "/backgrounds/$iconPath.png";
    }
}
