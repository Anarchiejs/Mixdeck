namespace Mixdeck.Core.Abstractions;

/// <summary>Envoie un processus vers une sortie audio. Les plateformes sans cette capacité répondent false.</summary>
public interface IAppOutputRouter
{
    bool CanRoute { get; }

    /// <summary>endpointId null = sortie par défaut du système.</summary>
    bool RouteProcess(string processName, string? endpointId);
}
