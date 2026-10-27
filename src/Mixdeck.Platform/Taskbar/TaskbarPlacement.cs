namespace Mixdeck.Platform.Taskbar;

public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>Rectangle en pixels physiques de l'écran (coordonnées du bureau virtuel).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <param name="TaskbarBounds">Rectangle de la barre des tâches sur ce moniteur (vide si masquée ou absente).</param>
/// <param name="WorkArea">Zone de travail du moniteur (sans la barre des tâches).</param>
/// <param name="Dpi">DPI effectif du moniteur (96 = 100 %).</param>
public sealed record TaskbarPlacement(TaskbarEdge Edge, PixelRect TaskbarBounds, PixelRect WorkArea, PixelRect MonitorBounds, uint Dpi)
{
    public double Scale => Dpi / 96.0;
}

public interface ITaskbarLocator
{
    /// <summary>Barre des tâches du moniteur qui contient le point (en pratique : la position du curseur au clic sur l'icône).</summary>
    TaskbarPlacement GetForPoint(int x, int y);

    /// <summary>Moniteur principal.</summary>
    TaskbarPlacement GetPrimary();
}
