<?php

namespace App\Entity;

use App\Repository\ClientBuildRepository;
use Doctrine\ORM\Mapping as ORM;

/**
 * One extracted client version, e.g. "1.12.1.5875". All talent-tree data is scoped to a
 * build — the tree genuinely differs between patches (see .claude-docs/gotchas.md).
 */
#[ORM\Entity(repositoryClass: ClientBuildRepository::class)]
#[ORM\Table(name: 'client_builds')]
#[ORM\UniqueConstraint(name: 'uniq_client_builds_label', columns: ['label'])]
class ClientBuild
{
    #[ORM\Id]
    #[ORM\GeneratedValue]
    #[ORM\Column]
    private ?int $id = null;

    /** Full version string as it appears in the client folder name, e.g. "1.12.1.5875". */
    #[ORM\Column(length: 32)]
    private string $label;

    /** Just the numeric build (the last dot-segment), e.g. 5875 — for sorting/filtering. */
    #[ORM\Column]
    private int $buildNumber;

    #[ORM\Column(type: 'text', nullable: true)]
    private ?string $notes = null;

    public function getId(): ?int
    {
        return $this->id;
    }

    public function getLabel(): string
    {
        return $this->label;
    }

    public function setLabel(string $label): static
    {
        $this->label = $label;

        return $this;
    }

    public function getBuildNumber(): int
    {
        return $this->buildNumber;
    }

    public function setBuildNumber(int $buildNumber): static
    {
        $this->buildNumber = $buildNumber;

        return $this;
    }

    public function getNotes(): ?string
    {
        return $this->notes;
    }

    public function setNotes(?string $notes): static
    {
        $this->notes = $notes;

        return $this;
    }
}
