-- 0004_add_executor.sql — reference copy of the v3 -> v4 step executed by
-- SqliteConnectionFactory.EnsureSchema (PRAGMA user_version 3 -> 4).
--
-- Adds the nafezly-only "executor" (منفذ المشروع) fact: the freelancer a project was awarded
-- to, whose NAME the anonymous HTML exposes on awarded projects (تحت التنفيذ / مكتمل) and which
-- is absent on open projects and on mostaql entirely. Nullable TEXT, additive-only — no rebuild,
-- no drop, no index change. Existing rows keep NULL; enrichment (UpsertDetailsAsync) fills it
-- going forward. FTS is deliberately untouched: the UI never searches executor names, so there is
-- no reason to widen the searchable surface (B4 brief).

ALTER TABLE projects ADD COLUMN executor_name TEXT;
