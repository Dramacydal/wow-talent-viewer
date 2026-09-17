<?php

namespace App\Repository;

use App\Entity\TalentPrerequisite;
use Doctrine\Bundle\DoctrineBundle\Repository\ServiceEntityRepository;
use Doctrine\Persistence\ManagerRegistry;

/**
 * @extends ServiceEntityRepository<TalentPrerequisite>
 */
class TalentPrerequisiteRepository extends ServiceEntityRepository
{
    public function __construct(ManagerRegistry $registry)
    {
        parent::__construct($registry, TalentPrerequisite::class);
    }

    /** @param int[] $talentIds @return TalentPrerequisite[] */
    public function findForTalentIds(array $talentIds): array
    {
        if ($talentIds === []) {
            return [];
        }

        return $this->createQueryBuilder('p')
            ->where('p.talent IN (:ids)')
            ->setParameter('ids', $talentIds)
            ->getQuery()
            ->getResult();
    }
}
