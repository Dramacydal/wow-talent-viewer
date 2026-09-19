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

    /** Same as findForBuildAndClass, but for TWO builds at once (one query instead of two) -
     * built for TalentTreeComparer, where fetching each build separately would double the
     * round-trips to a database that's real network hops away, not something to pay twice
     * for data this small (see .claude-docs/gotchas.md on measured round-trip latency).
     * Also eager-loads the tab (needed for tab-name comparison) for the same N+1 reason.
     *
     * @param int[] $buildIds exactly the two build ids being compared
     */
    public function findForBuildsAndClass(array $buildIds, CharacterClass $class): array
    {
        return $this->createQueryBuilder('t')
            ->addSelect('rank', 'tab')
            ->innerJoin('t.talentTab', 'tab')
            ->leftJoin('t.ranks', 'rank')
            ->where('t.clientBuild IN (:buildIds)')
            ->andWhere('tab.characterClass = :class')
            ->setParameter('buildIds', $buildIds)
            ->setParameter('class', $class)
            ->orderBy('t.tier', \SortDirection::Ascending)
            ->addOrderBy('t.columnIndex', \SortDirection::Ascending)
            ->addOrderBy('rank.rankIndex', \SortDirection::Ascending)
            ->getQuery()
            ->getResult();
    }
}
