<?php

namespace App\Controller\Api;

use App\Comparison\TalentTreeComparer;
use App\Repository\CharacterClassRepository;
use App\Repository\ClientBuildRepository;
use Symfony\Bundle\FrameworkBundle\Controller\AbstractController;
use Symfony\Component\HttpFoundation\JsonResponse;
use Symfony\Component\HttpKernel\Exception\NotFoundHttpException;
use Symfony\Component\Routing\Attribute\Route;

/** Diffs one class's talent tree between two client builds - see TalentTreeComparer for the
 * matching/comparison rules. Both builds' data is fetched in one query each (not two per
 * build) specifically to keep this to as few round-trips as the single-tree endpoint. */
class TalentTreeCompareController extends AbstractController
{
    #[Route('/api/compare/{buildLabelA}/{buildLabelB}/classes/{classSlug}', name: 'api_talent_tree_compare', methods: ['GET'])]
    public function show(
        string $buildLabelA,
        string $buildLabelB,
        string $classSlug,
        ClientBuildRepository $builds,
        CharacterClassRepository $classes,
        TalentTreeComparer $comparer,
    ): JsonResponse {
        $buildA = $builds->findOneByLabel($buildLabelA) ?? throw new NotFoundHttpException("Unknown build \"$buildLabelA\"");
        $buildB = $builds->findOneByLabel($buildLabelB) ?? throw new NotFoundHttpException("Unknown build \"$buildLabelB\"");
        $class = $classes->findOneBySlug($classSlug) ?? throw new NotFoundHttpException("Unknown class \"$classSlug\"");

        return $this->json([
            'buildA' => ['id' => $buildA->getId(), 'label' => $buildA->getLabel()],
            'buildB' => ['id' => $buildB->getId(), 'label' => $buildB->getLabel()],
            'class' => ['id' => $class->getId(), 'slug' => $class->getSlug(), 'name' => $class->getName()],
            'talents' => $comparer->compare($buildA, $buildB, $class),
        ]);
    }
}
