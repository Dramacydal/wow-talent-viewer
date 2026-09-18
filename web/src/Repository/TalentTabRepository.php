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
            // orderIndex can be NULL (builds before 0.9.0.3807, field didn't exist yet) OR
            // tied (real data - e.g. Mage Fire/Arcane are both 0 on 1.12.1.5875). MySQL's
            // tie-break for equal/NULL values isn't guaranteed, so add `id ASC` (our own
            // autoincrement PK) as an explicit secondary key. NOT `sourceTabId` (TalentTab.
            // dbc's own ID field) - verified wrong for the Fire/Arcane tie above (sourceTabId
            // sorts Fire first, but real vanilla order is Arcane, Fire, Frost). `id` works
            // because BuildExtractor inserts tabs in DbcClient.ReadTalentTabs()'s iteration
            // order, which is the DBC's own physical row order (see DbcClient.
            // LoadIndexedById) - NOT the same as sorting by the ID field's numeric value,
            // and empirically the better proxy for real display order. See gotchas.md.
            ->orderBy('tab.orderIndex', \SortDirection::Ascending)
            ->addOrderBy('tab.id', \SortDirection::Ascending)
            ->getQuery()
            ->getResult();
    }
}
