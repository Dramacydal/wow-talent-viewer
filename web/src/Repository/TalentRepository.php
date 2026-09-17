<?php

namespace App\Repository;

use App\Entity\CharacterClass;
use App\Entity\ClientBuild;
use App\Entity\Talent;
use Doctrine\Bundle\DoctrineBundle\Repository\ServiceEntityRepository;
use Doctrine\Persistence\ManagerRegistry;

/**
 * @extends ServiceEntityRepository<Talent>
 */
class TalentRepository extends ServiceEntityRepository
{
    public function __construct(ManagerRegistry $registry)
    {
        parent::__construct($registry, Talent::class);
    }

    /** All talents for one build+class, with their ranks eager-loaded (one query, avoids
     * N+1 when the controller walks every talent's ranks to build the tree JSON). */
    public function findForBuildAndClass(ClientBuild $build, CharacterClass $class): array
    {
        return $this->createQueryBuilder('t')
            ->addSelect('rank')
            ->innerJoin('t.talentTab', 'tab')
            ->leftJoin('t.ranks', 'rank')
            ->where('t.clientBuild = :build')
            ->andWhere('tab.characterClass = :class')
            ->setParameter('build', $build)
            ->setParameter('class', $class)
            ->orderBy('t.tier', \SortDirection::Ascending)
            ->addOrderBy('t.columnIndex', \SortDirection::Ascending)
            ->addOrderBy('rank.rankIndex', \SortDirection::Ascending)
            ->getQuery()
            ->getResult();
    }
}
