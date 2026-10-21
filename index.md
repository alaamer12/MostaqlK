# MostaqlK Documentation

This site is generated with [DocFX](https://dotnet.github.io/docfx/).

- **API Reference** — generated from the C# XML documentation comments in the codebase.
- **Guides** — architecture docs, ADRs, and postmortems from the `docs/` folder.

## Build the docs

```
dotnet tool restore
dotnet docfx metadata
dotnet docfx build
```

Or both in one step:

```
dotnet docfx
```

The static site is written to `_site/`. Serve it locally with:

```
dotnet docfx serve _site
```
