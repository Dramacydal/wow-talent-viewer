<?php

namespace App\Repository;

use App\Entity\ClientBuild;
use Doctrine\Bundle\DoctrineBundle\Repository\ServiceEntityRepository;
use Doctrine\Persistence\ManagerRegistry;

/**
 * @extends ServiceEntityRepository<ClientBuild>
 */
class ClientBuildRepository extends ServiceEntityRepository
{
    public function __construct(ManagerRegistry $registry)
    {
        parent::__construct($registry, ClientBuild::class);
    }

    /** @return ClientBuild[] */
    public function findAllOrderedByBuildNumber(): array
    {
        return $this->createQueryBuilder('b')
            ->orderBy('b.buildNumber', \SortDirection::Ascending)
            ->getQuery()
            ->getResult();
    }

    public function findOneByLabel(string $label): ?ClientBuild
    {
        return $this->findOneBy(['label' => $label]);
    }
}
