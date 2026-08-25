"""Diagnostics sinks: interaction-log-compatible structured logging that never raises."""

from mostaql.diagnostics.interaction_log import InteractionLogger, get_interaction_logger

__all__ = ["InteractionLogger", "get_interaction_logger"]
