<?php

namespace App\Controller;

use App\Repository\CharacterClassRepository;
use App\Repository\ClientBuildRepository;
use Symfony\Bundle\FrameworkBundle\Controller\AbstractController;
use Symfony\Component\HttpFoundation\Response;
use Symfony\Component\HttpKernel\Exception\NotFoundHttpException;
use Symfony\Component\Routing\Attribute\Route;

class HomeController extends AbstractController
{
    #[Route('/', name: 'home', methods: ['GET'])]
    public function index(ClientBuildRepository $builds, CharacterClassRepository $classes): Response
    {
        return $this->render('home/index.html.twig', [
            'builds' => $builds->findAllOrderedByBuildNumber(),
            'classes' => $classes->findAllOrderedById(),
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
        ]);
    }
}
