<?php

namespace App\Controller\Api;

use App\Repository\CharacterClassRepository;
use App\Repository\ClientBuildRepository;
use Symfony\Bundle\FrameworkBundle\Controller\AbstractController;
use Symfony\Component\HttpFoundation\JsonResponse;
use Symfony\Component\Routing\Attribute\Route;

/** Static-ish lookups the build/class picker needs: every extracted build, and the 9 classes. */
class CatalogController extends AbstractController
{
    #[Route('/api/builds', name: 'api_builds', methods: ['GET'])]
    public function builds(ClientBuildRepository $builds): JsonResponse
    {
        return $this->json(array_map(
            static fn ($b) => ['id' => $b->getId(), 'label' => $b->getLabel(), 'buildNumber' => $b->getBuildNumber()],
            $builds->findAllOrderedByBuildNumber(),
        ));
    }

    #[Route('/api/classes', name: 'api_classes', methods: ['GET'])]
    public function classes(CharacterClassRepository $classes): JsonResponse
    {
        return $this->json(array_map(
            static fn ($c) => ['id' => $c->getId(), 'slug' => $c->getSlug(), 'name' => $c->getName()],
            $classes->findAllOrderedById(),
        ));
    }
}
