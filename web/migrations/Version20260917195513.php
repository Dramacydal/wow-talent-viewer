<?php

declare(strict_types=1);

namespace DoctrineMigrations;

use Doctrine\DBAL\Schema\Schema;
use Doctrine\Migrations\AbstractMigration;

/**
 * Auto-generated Migration: Please modify to your needs!
 */
final class Version20260917195513 extends AbstractMigration
{
    public function getDescription(): string
    {
        return '';
    }

    public function up(Schema $schema): void
    {
        // this up() migration is auto-generated, please modify it to your needs
        $this->addSql('ALTER TABLE talent_tabs ADD background_top_left_path VARCHAR(64) DEFAULT NULL, ADD background_top_right_path VARCHAR(64) DEFAULT NULL, ADD background_bottom_left_path VARCHAR(64) DEFAULT NULL, ADD background_bottom_right_path VARCHAR(64) DEFAULT NULL');
    }

    public function down(Schema $schema): void
    {
        // this down() migration is auto-generated, please modify it to your needs
        $this->addSql('ALTER TABLE talent_tabs DROP background_top_left_path, DROP background_top_right_path, DROP background_bottom_left_path, DROP background_bottom_right_path');
    }
}
