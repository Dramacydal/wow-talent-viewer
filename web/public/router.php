<?php

/**
 * Router for `php -S` (dev only - a real webserver/FrankenPHP handles this natively).
 * Without it, PHP's built-in server would feed every request through index.php, including
 * binary static files (icons, importmap JS/CSS) - which the Symfony kernel doesn't know how
 * to serve and chokes on. Real files on disk are served as-is; everything else goes through
 * the normal front controller.
 *
 * MUST `return` (not just call) require('index.php'): symfony/runtime's
 * autoload_runtime.php does `require $_SERVER['SCRIPT_FILENAME']` internally to get the
 * app-producing closure - under `php -S` with a router script, SCRIPT_FILENAME is THIS
 * file, not index.php, so it re-enters router.php recursively. Without `return` here, that
 * inner require's result (index.php's closure) never propagates back out, and the runtime
 * receives PHP's default "1" (int) instead of the closure - "Invalid return value: callable
 * object expected, int returned" - a real, reproduced failure, not a hypothetical one.
 */
$path = urldecode(parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH));

if ($path !== '/' && file_exists(__DIR__.$path) && !is_dir(__DIR__.$path)) {
    return false;
}

return require __DIR__.'/index.php';
