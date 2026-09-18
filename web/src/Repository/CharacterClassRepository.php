<?php

namespace App\Repository;

use App\Entity\CharacterClass;
use App\Entity\ClientBuild;
use App\Entity\TalentTab;
use Doctrine\Bundle\DoctrineBundle\Repository\ServiceEntityRepository;
use Doctrine\Persistence\ManagerRegistry;

/**
 * @extends ServiceEntityRepository<CharacterClass>
 */
class CharacterClassRepository extends ServiceEntityRepository
{
    public function __construct(ManagerRegistry $registry)
    {
        parent::__construct($registry, CharacterClass::class);
    }

    /** @return CharacterClass[] */
    public function findAllOrderedById(): array
    {
        return $this->createQueryBuilder('c')
            ->orderBy('c.id', \SortDirection::Ascending)
            ->getQuery()
            ->getResult();
    }

    public function findOneBySlug(string $slug): ?CharacterClass
    {
        return $this->findOneBy(['slug' => $slug]);
    }

    /** Every (build label, class slug) pair that has at least one talent_tab, across ALL
     * builds at once - lets the landing page filter its class row client-side when the user
     * switches builds, instead of round-tripping to the server (see .claude-docs/gotchas.md
     * on why availability isn't just "all 9 classes" for every build).
     *
     * @return array<string, string[]> build label => class slugs available on it
     */
    public function findAvailableSlugsByBuildLabel(): array
    {
        $rows = $this->createQueryBuilder('c')
            ->select('build.label AS buildLabel', 'c.slug AS classSlug')
            ->innerJoin(TalentTab::class, 'tab', 'WITH', 'tab.characterClass = c')
            ->innerJoin(ClientBuild::class, 'build', 'WITH', 'tab.clientBuild = build')
            ->distinct()
            ->getQuery()
            ->getArrayResult();

        $byBuild = [];
        foreach ($rows as $row) {
            $byBuild[$row['buildLabel']][] = $row['classSlug'];
        }

        return $byBuild;
    }
}
