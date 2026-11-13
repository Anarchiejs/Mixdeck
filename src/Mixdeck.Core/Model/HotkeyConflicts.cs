namespace Mixdeck.Core.Model;

/// <summary>Deux raccourcis entrent en conflit quand ils partagent la même touche et les mêmes modificateurs.</summary>
public static class HotkeyConflicts
{
    public static IReadOnlyList<HotkeyBinding> Find(IEnumerable<HotkeyBinding> bindings, HotkeyBinding candidate)
    {
        if (candidate.VirtualKey == 0) return [];
        return bindings.Where(other => other.Action != candidate.Action
            && other.VirtualKey == candidate.VirtualKey
            && other.Modifiers == candidate.Modifiers).ToArray();
    }

    public static IReadOnlyList<HotkeyBinding> Conflicting(IReadOnlyList<HotkeyBinding> bindings) =>
        bindings.Where(binding => Find(bindings, binding).Count > 0).ToArray();
}
