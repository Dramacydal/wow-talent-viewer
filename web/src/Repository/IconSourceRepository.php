<?php

namespace App\Repository;

use App\Entity\IconSource;
use Doctrine\Bundle\DoctrineBundle\Repository\ServiceEntityRepository;
use Doctrine\Persistence\ManagerRegistry;

/**
 * @extends ServiceEntityRepository<IconSource>
 */
class IconSourceRepository extends ServiceEntityRepository
{
    public function __construct(ManagerRegistry $registry)
    {
        parent::__construct($registry, IconSource::class);
    }

    public function findByMpqPath(string $mpqPath): ?IconSource
    {
        return $this->findOneBy(['mpqPath' => $mpqPath]);
    }
}
