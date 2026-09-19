<?php

namespace App\Controller;

use App\Repository\CharacterClassRepository;
use App\Repository\ClientBuildRepository;
use Symfony\Bundle\FrameworkBundle\Controller\AbstractController;
use Symfony\Component\HttpFoundation\Request;
use Symfony\Component\HttpFoundation\Response;
use Symfony\Component\HttpKernel\Exception\NotFoundHttpException;
use Symfony\Component\Routing\Attribute\Route;

class HomeController extends AbstractController
{
    #[Route('/', name: 'home', methods: ['GET'])]
    public function index(Request $request, ClientBuildRepository $builds, CharacterClassRepository $classes): Response
    {
        $allBuilds = $builds->findAllOrderedByBuildNumber();
        if ($allBuilds === []) {
            throw new NotFoundHttpException('No client builds extracted yet');
        }

        $requestedLabel = $request->query->get('build');
        $selectedBuild = $requestedLabel !== null ? $builds->findOneByLabel($requestedLabel) : null;
        $selectedBuild ??= $allBuilds[array_key_last($allBuilds)]; // default: latest build

        return $this->render('home/index.html.twig', [
            'builds' => $allBuilds,
            'selectedBuild' => $selectedBuild,
            'allClasses' => $classes->findAllOrderedById(),
            'availableSlugsByBuildLabel' => $classes->findAvailableSlugsByBuildLabel(),
        ]);
    }

    #[Route('/tree/{buildLabel}/{classSlug}', name: 'talent_tree_page', methods: ['GET'])]
    public function tree(
        string $buildLabel,
        string $classSlug,
        ClientBuildRepository $builds,
        CharacterClassRepository $classes,
    ): Response {
        $build = $builds->findOneByLabel($buildLabel) ?? throw new NotFoundHttpException("Unknown build \"$buildLabel\"");
        $class = $classes->findOneBySlug($classSlug) ?? throw new NotFoundHttpException("Unknown class \"$classSlug\"");

        return $this->render('tree/show.html.twig', [
            'build' => $build,
            'class' => $class,
            'allBuilds' => $builds->findAllOrderedByBuildNumber(),
            'allClasses' => $classes->findAllOrderedById(),
        ]);
    }

    /** Placeholder page - just proves the landing page's Compare mode navigates somewhere
     * real and the /api/compare endpoint is reachable from it. The actual visualization is
     * a separate, not-yet-designed pass (see .claude-docs/gotchas.md if that note ever gets
     * added, or just ask - this was deliberately left minimal). */
    #[Route('/compare/{buildLabelA}/{buildLabelB}/{classSlug}', name: 'talent_compare_page', methods: ['GET'])]
    public function compare(
        string $buildLabelA,
        string $buildLabelB,
        string $classSlug,
        ClientBuildRepository $builds,
        CharacterClassRepository $classes,
    ): Response {
        $buildA = $builds->findOneByLabel($buildLabelA) ?? throw new NotFoundHttpException("Unknown build \"$buildLabelA\"");
        $buildB = $builds->findOneByLabel($buildLabelB) ?? throw new NotFoundHttpException("Unknown build \"$buildLabelB\"");
        $class = $classes->findOneBySlug($classSlug) ?? throw new NotFoundHttpException("Unknown class \"$classSlug\"");

        return $this->render('compare/show.html.twig', [
            'buildA' => $buildA,
            'buildB' => $buildB,
            'class' => $class,
        ]);
    }
}
