-- 0002_add_source_identity.sql — reference copy of the v1 -> v2 step executed by
-- SqliteConnectionFactory.EnsureSchema (PRAGMA user_version 1 -> 2).
--
-- project_id / owner_id become INTERNAL row ids (descriptor band + site id).
-- Mostaql's band is 0, so existing values and every child-table / FTS reference
-- to them stay valid untouched; the site's own numeric id moves to
-- site_project_id, unique per source.

ALTER TABLE projects ADD COLUMN source_id TEXT NOT NULL DEFAULT 'mostaql';
ALTER TABLE projects ADD COLUMN site_project_id INTEGER;
UPDATE projects SET site_project_id = project_id WHERE site_project_id IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_projects_source_site ON projects(source_id, site_project_id);
ALTER TABLE owners ADD COLUMN source_id TEXT NOT NULL DEFAULT 'mostaql';
ALTER TABLE discovery_backlog ADD COLUMN source_id TEXT NOT NULL DEFAULT 'mostaql';
