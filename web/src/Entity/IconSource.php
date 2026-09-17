<?php

namespace App\Entity;

use App\Repository\IconSourceRepository;
use Doctrine\ORM\Mapping as ORM;

/**
 * Global (NOT build-scoped) cache of icon identity between extractor runs — mirrors
 * Extractor.Blp.IIconSourceStore exactly (its JSON-file placeholder implementation will be
 * swapped for this table once the extractor's MySQL write path exists). See
 * .claude-docs/architecture.md for why both sourceHash and iconPath are needed: sourceHash
 * lets a re-run skip decoding an unchanged icon entirely; iconPath (the PNG's own content
 * hash, and its filename under storage/icons/) dedupes on disk even across different source
 * files that happen to decode to the same image.
 */
#[ORM\Entity(repositoryClass: IconSourceRepository::class)]
#[ORM\Table(name: 'icon_sources')]
#[ORM\UniqueConstraint(name: 'uniq_icon_sources_mpq_path', columns: ['mpq_path'])]
class IconSource
{
    #[ORM\Id]
    #[ORM\GeneratedValue]
    #[ORM\Column]
    private ?int $id = null;

    /** MPQ internal path, e.g. "Interface\Icons\Spell_Fire_FlameBolt.blp". */
    #[ORM\Column(length: 255)]
    private string $mpqPath;

    /** sha256 of the raw .blp/.tga source bytes. */
    #[ORM\Column(length: 64)]
    private string $sourceHash;

    /** sha256 of the decoded PNG — also its filename under storage/icons/. */
    #[ORM\Column(length: 64)]
    private string $iconPath;

    public function getId(): ?int
    {
        return $this->id;
    }

    public function getMpqPath(): string
    {
        return $this->mpqPath;
    }

    public function setMpqPath(string $mpqPath): static
    {
        $this->mpqPath = $mpqPath;

        return $this;
    }

    public function getSourceHash(): string
    {
        return $this->sourceHash;
    }

    public function setSourceHash(string $sourceHash): static
    {
        $this->sourceHash = $sourceHash;

        return $this;
    }

    public function getIconPath(): string
    {
        return $this->iconPath;
    }

    public function setIconPath(string $iconPath): static
    {
        $this->iconPath = $iconPath;

        return $this;
    }
}
