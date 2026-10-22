# MostaqlK Documentation

This site is generated with [DocFX](https://dotnet.github.io/docfx/).

- **API Reference** — generated from the C# XML documentation comments in the codebase.
- **Guides** — architecture docs, ADRs, and postmortems from the `docs/` folder.

## Build the docs

```text
dotnet tool restore
dotnet docfx metadata
dotnet docfx build
```

Or both in one step:

```text
dotnet docfx
```

The static site is written to `_site/`. Serve it locally with:

```text
dotnet docfx serve _site
```
