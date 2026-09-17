<?php

namespace App\Entity;

use App\Repository\TalentRepository;
use Doctrine\Common\Collections\ArrayCollection;
use Doctrine\Common\Collections\Collection;
use Doctrine\ORM\Mapping as ORM;

/**
 * One talent (a single cell in the grid — tier x column), for one specific client build.
 * Talent.dbc's own ID is build-local only, never a cross-build identifier (same reasoning
 * as TalentTab — see .claude-docs/gotchas.md). Rows whose raw TabID doesn't resolve to a
 * TalentTab in the same build (the 0.7.0.3694 orphaned-Warrior-talents case) are skipped
 * entirely during extraction, never written with a broken FK — so talentTab here is always
 * a real, valid reference.
 */
#[ORM\Entity(repositoryClass: TalentRepository::class)]
#[ORM\Table(name: 'talents')]
#[ORM\UniqueConstraint(name: 'uniq_talents_build_source', columns: ['client_build_id', 'source_talent_id'])]
class Talent
{
    #[ORM\Id]
    #[ORM\GeneratedValue]
    #[ORM\Column]
    private ?int $id = null;

    #[ORM\ManyToOne(targetEntity: ClientBuild::class)]
    #[ORM\JoinColumn(nullable: false, onDelete: 'CASCADE')]
    private ClientBuild $clientBuild;

    #[ORM\ManyToOne(targetEntity: TalentTab::class)]
    #[ORM\JoinColumn(nullable: false, onDelete: 'CASCADE')]
    private TalentTab $talentTab;

    /** Talent.dbc's own ID for this build — traceability/upsert key + prerequisite resolution during extraction only. */
    #[ORM\Column]
    private int $sourceTalentId;

    /** Talent.dbc TierID — row in the grid, 0-based. */
    #[ORM\Column]
    private int $tier;

    /** Talent.dbc ColumnIndex — column in the grid, 0-based (0-3, see the
     * NUM_TALENT_COLUMNS=4 client constant in architecture.md). */
    #[ORM\Column]
    private int $columnIndex;

    /** Derived: count of non-zero entries in Talent.SpellRank — how many ranks this talent actually has (1-5). */
    #[ORM\Column]
    private int $maxRank;

    /** @var Collection<int, TalentRank> */
    #[ORM\OneToMany(targetEntity: TalentRank::class, mappedBy: 'talent', orphanRemoval: true)]
    private Collection $ranks;

    public function __construct()
    {
        $this->ranks = new ArrayCollection();
    }

    public function getId(): ?int
    {
        return $this->id;
    }

    public function getClientBuild(): ClientBuild
    {
        return $this->clientBuild;
    }

    public function setClientBuild(ClientBuild $clientBuild): static
    {
        $this->clientBuild = $clientBuild;

        return $this;
    }

    public function getTalentTab(): TalentTab
    {
        return $this->talentTab;
    }

    public function setTalentTab(TalentTab $talentTab): static
    {
        $this->talentTab = $talentTab;

        return $this;
    }

    public function getSourceTalentId(): int
    {
        return $this->sourceTalentId;
    }

    public function setSourceTalentId(int $sourceTalentId): static
    {
        $this->sourceTalentId = $sourceTalentId;

        return $this;
    }

    public function getTier(): int
    {
        return $this->tier;
    }

    public function setTier(int $tier): static
    {
        $this->tier = $tier;

        return $this;
    }

    public function getColumnIndex(): int
    {
        return $this->columnIndex;
    }

    public function setColumnIndex(int $columnIndex): static
    {
        $this->columnIndex = $columnIndex;

        return $this;
    }

    public function getMaxRank(): int
    {
        return $this->maxRank;
    }

    public function setMaxRank(int $maxRank): static
    {
        $this->maxRank = $maxRank;

        return $this;
    }

    /** @return Collection<int, TalentRank> */
    public function getRanks(): Collection
    {
        return $this->ranks;
    }
}
