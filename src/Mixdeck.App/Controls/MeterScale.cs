namespace Mixdeck.App.Controls;

public static class MeterScale
{
    public static double DbToPosition(double db)
    {
        if (double.IsNaN(db)) return 0;
        db = Math.Clamp(db, -60, 12);
        return db switch
        {
            <= -24 => (db + 60) / 36 * .25,
            <= 0 => .25 + (db + 24) / 24 * .55,
            _ => .8 + db / 12 * .2
        };
    }
}
