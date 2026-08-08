# MostaqlK Project Structure

This document describes the intended source structure for MostaqlK. It is a living design reference: the folders are introduced as their responsibilities become necessary, rather than creating empty folders in advance.

## Current application layout

MostaqlK is a single .NET MAUI project. The Windows desktop experience is the MVP priority, while the shared code and platform boundaries remain compatible with the planned Android phase.

```text
MostaqlK/
├── App.xaml                         # Application resources and startup shell
├── App.xaml.cs
├── AppShell.xaml                    # Navigation shell
├── AppShell.xaml.cs
├── MainPage.xaml                    # Initial application page
├── MainPage.xaml.cs
├── MauiProgram.cs                   # Dependency injection and MAUI setup
├── MostaqlK.csproj
│
├── Features/                        # User-facing features, organized by vertical slice
│   ├── Projects/                    # Project list, details, and related view models
│   ├── Notifications/               # Notification presentation and unread state
│   └── Settings/                    # User-facing configuration
│
├── Models/                          # Shared domain and persistence models
├── Services/                        # Application use cases and service interfaces
├── Infrastructure/                 # External I/O and technical implementations
│   ├── Scraping/                    # Feed discovery and project enrichment
│   ├── Storage/                     # SQLite access, repositories, and migrations
│   ├── Notifications/               # Notification delivery implementation
│   └── Networking/                  # HTTP clients and request policies
│
├── Platforms/
│   ├── Windows/                     # Windows tray, lifecycle, and desktop behavior
│   └── Android/                     # Android-specific behavior added in the mobile phase
│
├── Resources/                       # MAUI images, fonts, raw assets, and styles
└── Properties/                      # Launch and build properties
```

The project root remains the home for the MAUI entry points and project file. A separate `src/` or nested `MostaqlK/` directory is not needed for this single-project repository. If the repository later contains multiple projects, the structure can evolve to `src/MostaqlK/` without changing the conceptual layers described here.

## Architectural rules

- Keep domain models, pipeline logic, storage contracts, and service interfaces platform-neutral.
- Put Windows- or Android-only code behind interfaces and implementations under `Platforms/` or the appropriate infrastructure area.
- Organize `Features/` by user capability rather than by one global folder for every page and view model.
- Keep scraping, rate limiting, concurrency, persistence, and notification orchestration out of UI code.
- Use dependency injection from `MauiProgram.cs` to connect interfaces to implementations.
- Keep Arabic-first data handling and bilingual search behavior in shared layers, not in Windows-only code.
- Treat `Resources/` and `Platforms/` as MAUI convention directories; change their project configuration only when the default conventions are insufficient.

## Delivery phases

### MVP — Windows desktop

Implement the first vertical slice with these responsibilities:

```text
Services/              polling and pipeline orchestration
Infrastructure/Scraping/   discover and enrich projects
Infrastructure/Storage/    persist projects in the embedded database
Infrastructure/Notifications/  notify the user
Platforms/Windows/     tray-resident desktop behavior
Features/Projects/      project list and details window
```

The MVP flow is `poll → discover → enrich → store → notify → display`. Projects follow the store-and-forget policy: once stored, they are not re-fetched or updated.

### V2 — richer Windows experience

Extend the existing feature and service boundaries with:

- configurable `query_params` and `include_assets` behavior;
- notification grouping and unread highlighting;
- the full project search and filtering experience;
- additional settings and query-builder UI under `Features/Settings` and `Features/Projects`.

### V3 — Android companion and synchronization

Add Android implementations without moving shared business logic:

```text
Platforms/Android/     notifications, lifecycle, background integration
Infrastructure/Sync/   LAN pairing and peer synchronization
Services/Sync/         synchronization use cases and contracts
```

The existing shared models and service interfaces should support the later mobile companion, LAN peer sync, and push-notification work. Device identity remains a shared contract with platform SecureStorage implementations; full signed peer identity is introduced only when synchronization is implemented.

## Naming and placement guidance

Use conventional PascalCase names for C# types and folders. Prefer a feature folder for code that changes together, and a shared layer only when the code is genuinely reused. Avoid adding empty folders or prematurely splitting the project into multiple assemblies.

The current MAUI template files may remain at the root. As implementation begins, new code should be placed in the responsibility-based folders above, with namespaces matching the folder and project root namespace where practical.

## Source-of-truth documentation

Product scope and behavior are defined under `.repertoire/.steering/product/`; technical constraints are under `.repertoire/.steering/tech/`. The MVP visual references are in `.repertoire/design/mvp/`. Post-MVP designs belong in `.repertoire/design/post-mvp/` and should not be treated as MVP requirements.