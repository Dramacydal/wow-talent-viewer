<?php

namespace App\Asset;

/** Turns a stored content-hash into the URL Symfony serves it at (see the public/icons,
 * public/backgrounds, public/class-icons symlinks into storage/ - .claude-docs/architecture.md).
 * Shared by any controller that needs to serialize a TalentRank/TalentTab, so the /icons vs
 * /backgrounds prefix can never get mixed up between them again (see gotchas.md). */
final class AssetUrlResolver
{
    public static function iconUrl(?string $iconPath): ?string
    {
        return $iconPath === null ? null : "/icons/$iconPath.png";
    }

    public static function backgroundUrl(?string $iconPath): ?string
    {
        return $iconPath === null ? null : "/backgrounds/$iconPath.png";
    }
}
