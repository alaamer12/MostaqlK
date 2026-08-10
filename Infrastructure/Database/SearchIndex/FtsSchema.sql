-- Full-text search index for bilingual (Arabic/English) project search.
-- Reference copy of the FTS5 statement embedded in `SqliteConnectionFactory`'s bootstrap
-- migration (kept here for readability - this file is not executed directly).
--
-- Standalone (non "external content") FTS5 table: `ProjectRepository.InsertSummaryAsync`
-- inserts a title-only row here at discovery time, and `UpsertDetailsAsync` later deletes
-- then re-inserts the full row (title/description/skills) in the same transaction as the
-- project/skills/assets writes - so no triggers are needed to keep it in sync.
CREATE VIRTUAL TABLE IF NOT EXISTS projects_fts USING fts5(
    project_id UNINDEXED,
    title,
    description,
    skills,
    tokenize = 'unicode61 remove_diacritics 2'
);
