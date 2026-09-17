<?php

namespace App\Entity;

use App\Repository\CharacterClassRepository;
use Doctrine\ORM\Mapping as ORM;

/**
 * Static reference table (9 vanilla classes), not build-scoped — a class doesn't stop
 * existing between patches, unlike talent trees. Id intentionally matches the real
 * ChrClasses.dbc ID (1=Warrior, 2=Paladin, ... 11=Druid, with gaps at 6/10) so
 * classMask = 1 << (id - 1) still holds — see .claude-docs/gotchas.md on why that's
 * derived from ChrClasses.ID and NOT the PlayerClass field.
 */
#[ORM\Entity(repositoryClass: CharacterClassRepository::class)]
#[ORM\Table(name: 'classes')]
#[ORM\UniqueConstraint(name: 'uniq_classes_slug', columns: ['slug'])]
class CharacterClass
{
    #[ORM\Id]
    #[ORM\Column]
    private int $id;

    /** Lowercase machine name, e.g. "warrior" — derived from ChrClasses.Filename. */
    #[ORM\Column(length: 32)]
    private string $slug;

    /** Display name, e.g. "Warrior" — from ChrClasses.Name_lang. */
    #[ORM\Column(length: 64)]
    private string $name;

    /** PNG content hash (storage/icons/{iconPath}.png) — cropped from the
     * character-creation-screen class icon sprite sheet (Interface\Glues\CharacterCreate\
     * UI-CharacterCreate-Classes.blp), a one-off global extraction not tied to any build
     * (classes.dbd doesn't carry per-class icon data in vanilla). Nullable until populated. */
    #[ORM\Column(length: 64, nullable: true)]
    private ?string $iconPath = null;

    public function getId(): int
    {
        return $this->id;
    }

    public function setId(int $id): static
    {
        $this->id = $id;

        return $this;
    }

    public function getSlug(): string
    {
        return $this->slug;
    }

    public function setSlug(string $slug): static
    {
        $this->slug = $slug;

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
}
