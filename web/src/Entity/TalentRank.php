<?php

namespace App\Entity;

use App\Repository\TalentRankRepository;
use Doctrine\ORM\Mapping as ORM;

/**
 * One rank of one talent (e.g. "Improved Fireball, Rank 3/5"). Denormalized from Spell.dbc
 * at extraction time — name/description/icon are copied in rather than looked up at render
 * time, so the site never needs to touch Spell.dbc again after extraction.
 */
#[ORM\Entity(repositoryClass: TalentRankRepository::class)]
#[ORM\Table(name: 'talent_ranks')]
#[ORM\UniqueConstraint(name: 'uniq_talent_ranks_talent_rank', columns: ['talent_id', 'rank_index'])]
class TalentRank
{
    #[ORM\Id]
    #[ORM\GeneratedValue]
    #[ORM\Column]
    private ?int $id = null;

    #[ORM\ManyToOne(targetEntity: Talent::class, inversedBy: 'ranks')]
    #[ORM\JoinColumn(nullable: false, onDelete: 'CASCADE')]
    private Talent $talent;

    /** 1-based rank number (1..5 in vanilla). */
    #[ORM\Column]
    private int $rankIndex;

    /** The real vanilla spell ID (Talent.SpellRank[n]) — kept for traceability even though
     * name/description/icon are denormalized below. */
    #[ORM\Column]
    private int $spellId;

    /** Spell.Name_lang. */
    #[ORM\Column(length: 128)]
    private string $name;

    /** Spell.Description_lang, with $s1/$d/etc. placeholders left as-is (raw client text) — not our job to interpolate at extraction time. */
    #[ORM\Column(type: 'text')]
    private string $description;

    /** PNG content hash (storage/icons/{iconPath}.png). Nullable if resolution/decoding failed. */
    #[ORM\Column(length: 64, nullable: true)]
    private ?string $iconPath = null;

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

    public function getRankIndex(): int
    {
        return $this->rankIndex;
    }

    public function setRankIndex(int $rankIndex): static
    {
        $this->rankIndex = $rankIndex;

        return $this;
    }

    public function getSpellId(): int
    {
        return $this->spellId;
    }

    public function setSpellId(int $spellId): static
    {
        $this->spellId = $spellId;

        return $this;
    }

    public function getName(): string
    {
        return $this->name;
    }

    public function setName(string $name): static
    {
        $this->name = $name;

        return $this;
    }

    public function getDescription(): string
    {
        return $this->description;
    }

    public function setDescription(string $description): static
    {
        $this->description = $description;

        return $this;
    }

    public function getIconPath(): ?string
    {
        return $this->iconPath;
    }

    public function setIconPath(?string $iconPath): static
    {
        $this->iconPath = $iconPath;

        return $this;
    }
}
