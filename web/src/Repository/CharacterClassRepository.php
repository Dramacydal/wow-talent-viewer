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

    /** Classes that actually have at least one talent_tab in this build - not every class
     * necessarily does on every build (see gotchas.md: e.g. Warrior's talents were orphaned
     * on 0.7.0.3694, so it has zero real tabs there even though the ChrClasses row exists). */
    public function findAvailableForBuild(ClientBuild $build): array
    {
        return $this->createQueryBuilder('c')
            ->innerJoin(TalentTab::class, 'tab', 'WITH', 'tab.characterClass = c')
            ->where('tab.clientBuild = :build')
            ->setParameter('build', $build)
            ->distinct()
            ->orderBy('c.id', \SortDirection::Ascending)
            ->getQuery()
            ->getResult();
    }

    public function findOneBySlug(string $slug): ?CharacterClass
    {
        return $this->findOneBy(['slug' => $slug]);
    }
}
