-- v4 -> v5: persist the listing card's owner sub-line (nafezly's country under the poster name,
-- e.g. "مصر") so the feed card no longer falls back to the descriptor's placeholder meta.
-- Additive-only: nullable TEXT, no rebuild, no index/FTS change (the meta is display-only).
ALTER TABLE projects ADD COLUMN client_meta TEXT;
