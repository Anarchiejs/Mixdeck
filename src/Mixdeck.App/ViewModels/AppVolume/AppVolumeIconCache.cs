using System.Diagnostics;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Mixdeck.App.ViewModels.AppVolume;

/// <summary>Icônes Shell des exécutables, chargées sans bloquer l'UI et réutilisées sur le thread UI.</summary>
internal static class AppVolumeIconCache
{
    private static readonly Dictionary<string, Task<BitmapImage?>> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Les appels et les créations d'ImageSource sont tous faits sur le thread UI.
    internal static Task<BitmapImage?> GetAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Task.FromResult<BitmapImage?>(null);
        if (Cache.TryGetValue(path, out var pending)) return pending;
        if (Cache.Count >= 128) Cache.Remove(Cache.Keys.First());
        var result = LoadAsync(path);
        Cache.Add(path, result);
        return result;
    }

    private static async Task<BitmapImage?> LoadAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 32);
            if (thumbnail is null) return null;
            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            return image;
        }
        catch (Exception exception)
        {
            Trace.TraceInformation("Icône d'application indisponible : {0}", exception.Message);
            return null; // Processus protégé ou disparu : le glyphe de secours reste affiché.
        }
    }
}
