<?php

namespace App\Entity;

use App\Repository\TalentTabRepository;
use Doctrine\ORM\Mapping as ORM;

/**
 * One class's talent tree tab (e.g. Warrior "Arms") for one specific client build.
 * TalentTab.dbc's own ID is NOT reused as our primary key — it's only meaningful within a
 * single build and gets renumbered/reused across builds (see .claude-docs/gotchas.md, the
 * tab=24 "Test" vs "Arms" mix-up). sourceTabId keeps the raw DBC id for traceability and
 * idempotent re-extraction lookups only, never as a cross-build identifier.
 */
#[ORM\Entity(repositoryClass: TalentTabRepository::class)]
#[ORM\Table(name: 'talent_tabs')]
#[ORM\UniqueConstraint(name: 'uniq_talent_tabs_build_source', columns: ['client_build_id', 'source_tab_id'])]
class TalentTab
{
    #[ORM\Id]
    #[ORM\GeneratedValue]
    #[ORM\Column]
    private ?int $id = null;

    #[ORM\ManyToOne(targetEntity: ClientBuild::class)]
    #[ORM\JoinColumn(nullable: false, onDelete: 'CASCADE')]
    private ClientBuild $clientBuild;

    #[ORM\ManyToOne(targetEntity: CharacterClass::class)]
    #[ORM\JoinColumn(nullable: false)]
    private CharacterClass $characterClass;

    /** TalentTab.dbc's own ID for this build — traceability/upsert key only, see class docblock. */
    #[ORM\Column]
    private int $sourceTabId;

    #[ORM\Column(length: 64)]
    private string $name;

    /** PNG content hash (storage/icons/{iconPath}.png), resolved from TalentTab.SpellIconID. Nullable if resolution failed. */
    #[ORM\Column(length: 64, nullable: true)]
    private ?string $iconPath = null;

    /** TalentTab.OrderIndex — missing before 0.9.0.3807 (see gotchas.md), nullable. */
    #[ORM\Column(nullable: true)]
    private ?int $orderIndex = null;

    /** TalentTab.BackgroundFile — missing before 0.8.0.3734, and even when present can be an
     * internal codename that no longer matches the display name (e.g. "WarlockCurses" for the
     * "Affliction" tab) — see gotchas.md. Store as-is, don't try to "fix" it. */
    #[ORM\Column(length: 64, nullable: true)]
    private ?string $backgroundFile = null;

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

    public function getCharacterClass(): CharacterClass
    {
        return $this->characterClass;
    }

    public function setCharacterClass(CharacterClass $characterClass): static
    {
        $this->characterClass = $characterClass;

        return $this;
    }

    public function getSourceTabId(): int
    {
        return $this->sourceTabId;
    }

    public function setSourceTabId(int $sourceTabId): static
    {
        $this->sourceTabId = $sourceTabId;

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

    public function getIconPath(): ?string
    {
        return $this->iconPath;
    }

    public function setIconPath(?string $iconPath): static
    {
        $this->iconPath = $iconPath;

        return $this;
    }

    public function getOrderIndex(): ?int
    {
        return $this->orderIndex;
    }

    public function setOrderIndex(?int $orderIndex): static
    {
        $this->orderIndex = $orderIndex;

        return $this;
    }

    public function getBackgroundFile(): ?string
    {
        return $this->backgroundFile;
    }

    public function setBackgroundFile(?string $backgroundFile): static
    {
        $this->backgroundFile = $backgroundFile;

        return $this;
    }
}
