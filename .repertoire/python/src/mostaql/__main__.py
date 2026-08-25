"""Allow ``python -m mostaql`` to launch the runtime entry point."""

from mostaql.runtime import cli

if __name__ == "__main__":
    cli()
