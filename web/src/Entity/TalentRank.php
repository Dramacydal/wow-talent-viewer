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

    /** Whether this rank's spell grants a usable, castable ability (shows up in the
     * spellbook) rather than being a pure passive modifier - Spell.Attributes has
     * SPELL_ATTR_IS_ABILITY (0x10) set and SPELL_ATTR_PASSIVE (0x40) NOT set. Verified
     * against real data across every class (see .claude-docs/gotchas.md). Gates whether
     * the fields below are meaningful. */
    #[ORM\Column]
    private bool $isAbility = false;

    /** 0=Mana, 1=Rage, 3=Energy (the only 3 that occur on player talent abilities in
     * vanilla) - null if isAbility is false OR the ability costs nothing (e.g. Last Stand). */
    #[ORM\Column(nullable: true)]
    private ?int $powerType = null;

    /** Already adjusted for display - Rage is stored divided by 10 internally in Spell.dbc
     * (300 = 30 Rage), Mana/Energy are stored as the real value; see
     * BuildExtractor.ResolveAbilityFields. Null alongside powerType when there's no cost. */
    #[ORM\Column(nullable: true)]
    private ?int $powerCost = null;

    /** True if SpellRange.Flags & 0x1 (the client shows "Melee Range" text, not a numeric
     * range, for these - id=2 is the only such row in vanilla's SpellRange.dbc). Null (not
     * false) when not applicable, to distinguish from "explicitly checked, not melee". */
    #[ORM\Column(nullable: true)]
    private ?bool $isMeleeRange = null;

    /** Null when isMeleeRange is true, or when the spell is self-cast (SpellRange.MaxRange
     * = 0, e.g. Ice Barrier) - no range line is shown in either case. */
    #[ORM\Column(nullable: true)]
    private ?float $rangeMinYards = null;

    #[ORM\Column(nullable: true)]
    private ?float $rangeMaxYards = null;

    /** 0 means instant cast (still populated, not null, when isAbility is true - every
     * ability resolves a CastingTimeIndex, defaulting to instant). Null only when
     * isAbility is false. */
    #[ORM\Column(nullable: true)]
    private ?int $castTimeMs = null;

    /** max(RecoveryTime, CategoryRecoveryTime) - vanilla spells only ever populate one of
     * the two. Null when the ability has no cooldown beyond the global cooldown (e.g.
     * Piercing Howl), not just zero - so the frontend can omit the whole line. */
    #[ORM\Column(nullable: true)]
    private ?int $cooldownMs = null;

    /** Wowhead-tooltip-style "Requires Cat Form" / "Requires Cat Form, Bear Form, Dire Bear
     * Form" line, resolved from Spell.ShapeshiftMask against the real SpellShapeshiftForm.dbc
     * names for this build - see BuildExtractor.ResolveStanceRequirement. Null when the spell
     * has no stance/form restriction. */
    #[ORM\Column(length: 255, nullable: true)]
    private ?string $stanceRequirement = null;

    /** Wowhead-tooltip-style "Requires Melee Weapon" / "Requires Shield" line - see
     * BuildExtractor.ResolveEquipRequirement. Null when the spell has no equipped-item
     * restriction, or one this project hasn't verified real display text for yet. */
    #[ORM\Column(length: 255, nullable: true)]
    private ?string $equipRequirement = null;

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

    public function isAbility(): bool
    {
        return $this->isAbility;
    }

    public function setIsAbility(bool $isAbility): static
    {
        $this->isAbility = $isAbility;

        return $this;
    }

    public function getPowerType(): ?int
    {
        return $this->powerType;
    }

    public function setPowerType(?int $powerType): static
    {
        $this->powerType = $powerType;

        return $this;
    }

    public function getPowerCost(): ?int
    {
        return $this->powerCost;
    }

    public function setPowerCost(?int $powerCost): static
    {
        $this->powerCost = $powerCost;

        return $this;
    }

    public function getIsMeleeRange(): ?bool
    {
        return $this->isMeleeRange;
    }

    public function setIsMeleeRange(?bool $isMeleeRange): static
    {
        $this->isMeleeRange = $isMeleeRange;

        return $this;
    }

    public function getRangeMinYards(): ?float
    {
        return $this->rangeMinYards;
    }

    public function setRangeMinYards(?float $rangeMinYards): static
    {
        $this->rangeMinYards = $rangeMinYards;

        return $this;
    }

    public function getRangeMaxYards(): ?float
    {
        return $this->rangeMaxYards;
    }

    public function setRangeMaxYards(?float $rangeMaxYards): static
    {
        $this->rangeMaxYards = $rangeMaxYards;

        return $this;
    }

    public function getCastTimeMs(): ?int
    {
        return $this->castTimeMs;
    }

    public function setCastTimeMs(?int $castTimeMs): static
    {
        $this->castTimeMs = $castTimeMs;

        return $this;
    }

    public function getCooldownMs(): ?int
    {
        return $this->cooldownMs;
    }

    public function setCooldownMs(?int $cooldownMs): static
    {
        $this->cooldownMs = $cooldownMs;

        return $this;
    }

    public function getStanceRequirement(): ?string
    {
        return $this->stanceRequirement;
    }

    public function setStanceRequirement(?string $stanceRequirement): static
    {
        $this->stanceRequirement = $stanceRequirement;

        return $this;
    }

    public function getEquipRequirement(): ?string
    {
        return $this->equipRequirement;
    }

    public function setEquipRequirement(?string $equipRequirement): static
    {
        $this->equipRequirement = $equipRequirement;

        return $this;
    }
}
