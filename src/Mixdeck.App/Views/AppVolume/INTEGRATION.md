# Raccordement L13 à réaliser par l'intégrateur

Le contrôle est livré sans insertion dans le panneau rapide. Les contrats partagés et l'enregistrement
des services n'ont pas été modifiés.

Dans `ServiceRegistration`, ajouter les espaces de noms `Mixdeck.Platform.Audio` et
`Mixdeck.App.ViewModels.AppVolume`, puis :

```csharp
services.AddTransient<IAppAudioSessions, AppAudioSessions>();
services.AddSingleton<Func<IAppAudioSessions>>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<AppAudioSessions>>();
    return () => new AppAudioSessions(logger);
});
services.AddTransient<AppVolumeListViewModel>();
```

La fabrique crée une instance possédée par le ViewModel à chaque ouverture. Elle évite de conserver
dans le conteneur racine toutes les instances transitoires déjà libérées. `Start()` est idempotent ;
`Dispose()` ferme la file sans bloquer l'interface. `DisposeAsync()` permet d'attendre la fin du
désabonnement et de la libération COM lorsqu'une attente est nécessaire hors du thread UI.

Dans le XAML du panneau, déclarer `xmlns:appVolume="using:Mixdeck.App.Views.AppVolume"`, puis insérer :

```xml
<appVolume:AppVolumeList x:Name="AppVolumes" />
```

Injecter un `AppVolumeListViewModel` dans `FlyoutWindow`. Dans `Show()`, après avoir montré la fenêtre,
si `AppSettings.ShowAppVolumes` est vrai, rendre le contrôle visible, lui affecter le ViewModel et
appeler `Activate()`. Sinon, laisser `ViewModel` à null et le contrôle masqué. Dans `Hide()`, appeler
`Deactivate()` **et remettre `AppVolumes.ViewModel` à null**, avant de cacher la fenêtre. Appliquer
la même règle si le réglage change pendant l'affichage. À la destruction de la fenêtre, libérer
le ViewModel. Les méthodes sont idempotentes.

`Loaded` et `Unloaded` prennent aussi en charge le cycle de vie dans les conteneurs ordinaires.
La fenêtre actuelle est seulement cachée : ne pas compter sur `Unloaded` dans `FlyoutWindow`.
Ne pas affecter le ViewModel au contrôle dès sa construction, car `Loaded` lancerait le suivi même
si le panneau n'a pas encore été ouvert. Sans instance démarrée, il n'existe ni thread ni objet COM ;
après désactivation, le thread libère les abonnements puis se termine. Aucun sondage périodique.

Les lignes exposent `CanRoute`, faux par défaut, pour rendre visible le bouton « Envoyer vers… »
lors du raccordement L14. Aucun gestionnaire de routage n'est encore installé.
