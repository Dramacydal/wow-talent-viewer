<?php

declare(strict_types=1);

namespace DoctrineMigrations;

use Doctrine\DBAL\Schema\Schema;
use Doctrine\Migrations\AbstractMigration;

/**
 * Auto-generated Migration: Please modify to your needs!
 */
final class Version20260917053409 extends AbstractMigration
{
    public function getDescription(): string
    {
        return 'Initial schema: client_builds, classes, talent_tabs, talents, talent_ranks, talent_prerequisites, icon_sources';
    }

    public function up(Schema $schema): void
    {
        // this up() migration is auto-generated, please modify it to your needs
        $this->addSql('CREATE TABLE classes (id INT NOT NULL, slug VARCHAR(32) NOT NULL, name VARCHAR(64) NOT NULL, UNIQUE INDEX uniq_classes_slug (slug), PRIMARY KEY (id)) DEFAULT CHARACTER SET utf8mb4');
        $this->addSql('CREATE TABLE client_builds (id INT AUTO_INCREMENT NOT NULL, label VARCHAR(32) NOT NULL, build_number INT NOT NULL, notes LONGTEXT DEFAULT NULL, UNIQUE INDEX uniq_client_builds_label (label), PRIMARY KEY (id)) DEFAULT CHARACTER SET utf8mb4');
        $this->addSql('CREATE TABLE icon_sources (id INT AUTO_INCREMENT NOT NULL, mpq_path VARCHAR(255) NOT NULL, source_hash VARCHAR(64) NOT NULL, icon_path VARCHAR(64) NOT NULL, UNIQUE INDEX uniq_icon_sources_mpq_path (mpq_path), PRIMARY KEY (id)) DEFAULT CHARACTER SET utf8mb4');
        $this->addSql('CREATE TABLE talent_prerequisites (id INT AUTO_INCREMENT NOT NULL, requires_rank INT NOT NULL, talent_id INT NOT NULL, requires_talent_id INT NOT NULL, UNIQUE INDEX uniq_talent_prereq_pair (talent_id, requires_talent_id), INDEX IDX_96AC256D18777CEF (talent_id), INDEX IDX_96AC256DF14AEE63 (requires_talent_id), PRIMARY KEY (id)) DEFAULT CHARACTER SET utf8mb4');
        $this->addSql('CREATE TABLE talent_ranks (id INT AUTO_INCREMENT NOT NULL, rank_index INT NOT NULL, spell_id INT NOT NULL, name VARCHAR(128) NOT NULL, description LONGTEXT NOT NULL, icon_path VARCHAR(64) DEFAULT NULL, talent_id INT NOT NULL, UNIQUE INDEX uniq_talent_ranks_talent_rank (talent_id, rank_index), INDEX IDX_3DB6BE9318777CEF (talent_id), PRIMARY KEY (id)) DEFAULT CHARACTER SET utf8mb4');
        $this->addSql('CREATE TABLE talent_tabs (id INT AUTO_INCREMENT NOT NULL, source_tab_id INT NOT NULL, name VARCHAR(64) NOT NULL, icon_path VARCHAR(64) DEFAULT NULL, order_index INT DEFAULT NULL, background_file VARCHAR(64) DEFAULT NULL, client_build_id INT NOT NULL, character_class_id INT NOT NULL, UNIQUE INDEX uniq_talent_tabs_build_source (client_build_id, source_tab_id), INDEX IDX_FBF3BCB639270F4E (client_build_id), INDEX IDX_FBF3BCB6B201E281 (character_class_id), PRIMARY KEY (id)) DEFAULT CHARACTER SET utf8mb4');
        $this->addSql('CREATE TABLE talents (id INT AUTO_INCREMENT NOT NULL, source_talent_id INT NOT NULL, tier INT NOT NULL, column_index INT NOT NULL, max_rank INT NOT NULL, client_build_id INT NOT NULL, talent_tab_id INT NOT NULL, UNIQUE INDEX uniq_talents_build_source (client_build_id, source_talent_id), INDEX IDX_D6CF109A39270F4E (client_build_id), INDEX IDX_D6CF109AFE26D5BE (talent_tab_id), PRIMARY KEY (id)) DEFAULT CHARACTER SET utf8mb4');
        $this->addSql('ALTER TABLE talent_prerequisites ADD CONSTRAINT FK_96AC256D18777CEF FOREIGN KEY (talent_id) REFERENCES talents (id) ON DELETE CASCADE');
        $this->addSql('ALTER TABLE talent_prerequisites ADD CONSTRAINT FK_96AC256DF14AEE63 FOREIGN KEY (requires_talent_id) REFERENCES talents (id) ON DELETE CASCADE');
        $this->addSql('ALTER TABLE talent_ranks ADD CONSTRAINT FK_3DB6BE9318777CEF FOREIGN KEY (talent_id) REFERENCES talents (id) ON DELETE CASCADE');
        $this->addSql('ALTER TABLE talent_tabs ADD CONSTRAINT FK_FBF3BCB639270F4E FOREIGN KEY (client_build_id) REFERENCES client_builds (id) ON DELETE CASCADE');
        $this->addSql('ALTER TABLE talent_tabs ADD CONSTRAINT FK_FBF3BCB6B201E281 FOREIGN KEY (character_class_id) REFERENCES classes (id)');
        $this->addSql('ALTER TABLE talents ADD CONSTRAINT FK_D6CF109A39270F4E FOREIGN KEY (client_build_id) REFERENCES client_builds (id) ON DELETE CASCADE');
        $this->addSql('ALTER TABLE talents ADD CONSTRAINT FK_D6CF109AFE26D5BE FOREIGN KEY (talent_tab_id) REFERENCES talent_tabs (id) ON DELETE CASCADE');
    }

    public function down(Schema $schema): void
    {
        // this down() migration is auto-generated, please modify it to your needs
        $this->addSql('ALTER TABLE talent_prerequisites DROP FOREIGN KEY FK_96AC256D18777CEF');
        $this->addSql('ALTER TABLE talent_prerequisites DROP FOREIGN KEY FK_96AC256DF14AEE63');
        $this->addSql('ALTER TABLE talent_ranks DROP FOREIGN KEY FK_3DB6BE9318777CEF');
        $this->addSql('ALTER TABLE talent_tabs DROP FOREIGN KEY FK_FBF3BCB639270F4E');
        $this->addSql('ALTER TABLE talent_tabs DROP FOREIGN KEY FK_FBF3BCB6B201E281');
        $this->addSql('ALTER TABLE talents DROP FOREIGN KEY FK_D6CF109A39270F4E');
        $this->addSql('ALTER TABLE talents DROP FOREIGN KEY FK_D6CF109AFE26D5BE');
        $this->addSql('DROP TABLE classes');
        $this->addSql('DROP TABLE client_builds');
        $this->addSql('DROP TABLE icon_sources');
        $this->addSql('DROP TABLE talent_prerequisites');
        $this->addSql('DROP TABLE talent_ranks');
        $this->addSql('DROP TABLE talent_tabs');
        $this->addSql('DROP TABLE talents');
    }
}
