<?php

declare(strict_types=1);

namespace DoctrineMigrations;

use Doctrine\DBAL\Schema\Schema;
use Doctrine\Migrations\AbstractMigration;

/**
 * Auto-generated Migration: Please modify to your needs!
 */
final class Version20260918140313 extends AbstractMigration
{
    public function getDescription(): string
    {
        return '';
    }

    public function up(Schema $schema): void
    {
        // this up() migration is auto-generated, please modify it to your needs
        $this->addSql('ALTER TABLE talent_ranks ADD is_ability TINYINT NOT NULL, ADD power_type INT DEFAULT NULL, ADD power_cost INT DEFAULT NULL, ADD is_melee_range TINYINT DEFAULT NULL, ADD range_min_yards DOUBLE PRECISION DEFAULT NULL, ADD range_max_yards DOUBLE PRECISION DEFAULT NULL, ADD cast_time_ms INT DEFAULT NULL, ADD cooldown_ms INT DEFAULT NULL');
    }

    public function down(Schema $schema): void
    {
        // this down() migration is auto-generated, please modify it to your needs
        $this->addSql('ALTER TABLE talent_ranks DROP is_ability, DROP power_type, DROP power_cost, DROP is_melee_range, DROP range_min_yards, DROP range_max_yards, DROP cast_time_ms, DROP cooldown_ms');
    }
}
