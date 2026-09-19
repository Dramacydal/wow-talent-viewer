<?php

namespace App\Asset;

use Symfony\Component\Asset\Packages;

/** Turns a stored content-hash into the URL Symfony serves it at (see the public/icons,
 * public/backgrounds, public/class-icons symlinks into storage/ - .claude-docs/architecture.md).
 * Shared by any controller that needs to serialize a TalentRank/TalentTab, so the /icons vs
 * /backgrounds prefix can never get mixed up between them again (see gotchas.md).
 *
 * Goes through Symfony's asset Packages service (the same thing Twig's asset() calls) instead
 * of concatenating a leading-slash string by hand - a hardcoded "/icons/$hash.png" is only
 * correct when the app is mounted at the domain root. Deployed at a sub-path (e.g. an Alias
 * "/talents" inside an existing vhost, see .claude-docs/architecture.md) the real URL needs
 * that prefix too, and Packages::getUrl() reads it from the current request automatically -
 * no environment-specific branching needed here. */
final class AssetUrlResolver
{
    public function __construct(
        private readonly Packages $packages,
    ) {
    }

    public function iconUrl(?string $iconPath): ?string
    {
        return $iconPath === null ? null : $this->packages->getUrl("icons/$iconPath.png");
    }

    public function backgroundUrl(?string $iconPath): ?string
    {
        return $iconPath === null ? null : $this->packages->getUrl("backgrounds/$iconPath.png");
    }

    public function classIconUrl(?string $iconPath): ?string
    {
        return $iconPath === null ? null : $this->packages->getUrl("class-icons/$iconPath.png");
    }
}
