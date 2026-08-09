# Roadmap: future (v3, stretch)

[← Back to wiki home](../../base/product/README.md)

Everything in this document is explicitly deferred — it assumes a second full application (mobile) and, for one sub-feature, a small cloud dependency. None of it blocks or is required by v1/v2.

## Table of contents
- [Mobile companion & LAN pairing](#mobile-companion--lan-pairing)
- [Two-way peer sync](#two-way-peer-sync)
- [Push notifications](#push-notifications)
- [Notification collapsing & grouping (mobile)](#notification-collapsing--grouping-mobile)

## Mobile companion & LAN pairing

- Desktop app generates a QR code (in Settings) encoding local connection info: LAN IP, port, short-lived pairing token.
- A future mobile app scans it while on the **same Wi-Fi network** — no internet-facing server, no account system.
- Both required to share the same Wi-Fi for direct LAN communication.

## Two-way peer sync

Both desktop and mobile are **independent peers**, not master/mirror:

- Each holds its own embedded DB and can accumulate data independently (e.g. desktop closed while mobile is open, or vice versa).
- On pairing/reconnection over LAN, both sides exchange a manifest of known `project_id`s and compute the diff in both directions:
  - `desktop_missing = mobile_ids − desktop_ids`
  - `mobile_missing = desktop_ids − mobile_ids`
- Each side requests and inserts the other's missing rows. Result converges to the union: `final_set = desktop_projects ∪ mobile_projects`.
- This is a merge, not an overwrite — safe because of the [no-update policy](../../base/product/architecture-pipeline.md#no-update-policy): a project row is immutable once scraped, so there's no last-write-wins conflict to resolve.
- After merge, each device only fires notifications for rows that are newly-arrived *to it* via sync — not for rows it already had.
- Both apps need the same schema (or a compatible export subset) so a missing row from one is directly insertable into the other.

## Push notifications

LAN sync only works when both devices are online and reachable on the same network. **When the mobile app is fully closed**, reaching it requires the OS-level push service — FCM (Android) or APNs (iOS) — since only those can wake a closed app to show a notification. This is the one part of the design that isn't purely local.

- `push_notifications_enabled` (default: true, inert until a device has paired at least once and a push token is on file).
- Desktop app calls FCM/APNs **directly** using the token captured during QR/LAN pairing — no separate relay server, keeping the "no cloud service to run" property intact; Google's/Apple's push infra is used purely as a delivery pipe.
- If the setting is disabled: mobile only receives updates via LAN sync when actually reachable; no cloud call ever happens.

## Notification collapsing & grouping (mobile)

Distinct from [desktop-side notification grouping](../../v1/product/configuration-reference.md#notification-grouping) — this is about **push delivery** behavior specifically, so duplicate/stacked pushes don't pile up while the phone was offline.

- **FCM:** set `collapse_key` (e.g. `mostaqlk_new_project`) so multiple pending messages collapse to the most recent on delivery. Collapsing affects *delivery*, not *content* — if an aggregate message ("3 new projects") is wanted rather than losing all but the latest, the sender (desktop) must detect the batch and send one summary push itself, not rely on FCM to summarize.
- **APNs:** equivalent via `apns-collapse-id` (same delivery-only caveat). Pair with `thread-id` to visually group notifications in the notification center without dropping any of them (Android equivalent: `group` key).
- **Design takeaway:** use collapse-key/collapse-id defensively to avoid delivery storms on reconnect, but do batching/summarization logic on the sender (desktop) side — the same batching concept as desktop notification grouping, applied to the push payload.
