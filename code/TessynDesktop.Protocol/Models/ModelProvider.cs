namespace TessynDesktop.Models;

/// <summary>Identifies whether a model is hosted by Anthropic, a local Ollama instance (FR.12.14) or OpenAI through Codex (FR.33.2).</summary>
public enum ModelProvider { Anthropic, Ollama, OpenAI }
