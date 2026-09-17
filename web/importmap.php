<?php

/**
 * Returns the importmap for this application.
 *
 * - "path" is a path inside the asset mapper system. Use the
 *     "debug:asset-map" command to see the full list of paths.
 *
 * - "entrypoint" (JavaScript only) set to true for any module that will
 *     be used as an "entrypoint" (and passed to the importmap() Twig function).
 *
 * The "importmap:require" command can be used to add new entries to this file.
 */
return [
    'app' => [
        'path' => './assets/app.js',
        'entrypoint' => true,
    ],
    'talent-tree' => [
        'path' => './assets/talent-tree.js',
        'entrypoint' => true,
    ],
    'vue' => [
        'version' => '3.5.43',
    ],
    '@vue/runtime-dom' => [
        'version' => '3.5.43',
    ],
    '@vue/runtime-core' => [
        'version' => '3.5.43',
    ],
    '@vue/shared' => [
        'version' => '3.5.43',
    ],
    '@vue/reactivity' => [
        'version' => '3.5.43',
    ],
];
