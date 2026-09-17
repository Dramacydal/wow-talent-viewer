<?php

namespace App\Repository;

use App\Entity\CharacterClass;
use App\Entity\ClientBuild;
use App\Entity\TalentTab;
use Doctrine\Bundle\DoctrineBundle\Repository\ServiceEntityRepository;
use Doctrine\Persistence\ManagerRegistry;

/**
 * @extends ServiceEntityRepository<TalentTab>
 */
class TalentTabRepository extends ServiceEntityRepository
{
    public function __construct(ManagerRegistry $registry)
    {
        parent::__construct($registry, TalentTab::class);
    }

    /** @return TalentTab[] */
    public function findForBuildAndClass(ClientBuild $build, CharacterClass $class): array
    {
        return $this->createQueryBuilder('tab')
            ->where('tab.clientBuild = :build')
            ->andWhere('tab.characterClass = :class')
            ->setParameter('build', $build)
            ->setParameter('class', $class)
            ->orderBy('tab.orderIndex', \SortDirection::Ascending)
            ->getQuery()
            ->getResult();
    }
}
