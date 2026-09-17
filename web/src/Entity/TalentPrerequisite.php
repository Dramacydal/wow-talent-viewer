<?php

namespace App\Entity;

use App\Repository\TalentPrerequisiteRepository;
use Doctrine\ORM\Mapping as ORM;

/**
 * "talent requires >= requiresRank points in requiresTalent" — the actual dependency graph
 * for the click-to-build UI. Both sides are resolved to real Talent rows within the SAME
 * build during extraction; if Talent.PrereqTalent doesn't resolve (shouldn't happen since,
 * unlike TabID, it references another Talent row rather than a possibly-unwired tab, but
 * treat it the same way defensively) the row is skipped, never written with a broken FK.
 */
#[ORM\Entity(repositoryClass: TalentPrerequisiteRepository::class)]
#[ORM\Table(name: 'talent_prerequisites')]
#[ORM\UniqueConstraint(name: 'uniq_talent_prereq_pair', columns: ['talent_id', 'requires_talent_id'])]
class TalentPrerequisite
{
    #[ORM\Id]
    #[ORM\GeneratedValue]
    #[ORM\Column]
    private ?int $id = null;

    /** The talent that has this prerequisite. */
    #[ORM\ManyToOne(targetEntity: Talent::class)]
    #[ORM\JoinColumn(nullable: false, onDelete: 'CASCADE')]
    private Talent $talent;

    /** The talent that must have points spent in it first. */
    #[ORM\ManyToOne(targetEntity: Talent::class)]
    #[ORM\JoinColumn(nullable: false, onDelete: 'CASCADE')]
    private Talent $requiresTalent;

    /** Minimum ranks spent in requiresTalent before talent can be spent in. */
    #[ORM\Column]
    private int $requiresRank;

    public function getId(): ?int
    {
        return $this->id;
    }

    public function getTalent(): Talent
    {
        return $this->talent;
    }

    public function setTalent(Talent $talent): static
    {
        $this->talent = $talent;

        return $this;
    }

    public function getRequiresTalent(): Talent
    {
        return $this->requiresTalent;
    }

    public function setRequiresTalent(Talent $requiresTalent): static
    {
        $this->requiresTalent = $requiresTalent;

        return $this;
    }

    public function getRequiresRank(): int
    {
        return $this->requiresRank;
    }

    public function setRequiresRank(int $requiresRank): static
    {
        $this->requiresRank = $requiresRank;

        return $this;
    }
}
