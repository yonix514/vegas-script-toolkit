# Contribuer

Merci de vouloir contribuer. Ce dépôt est maintenu par un monteur, pas par une
équipe : les réponses peuvent prendre quelques jours.

## Signaler un problème

Dites-moi **la version de VEGAS et le build** (Aide → À propos), et collez le
message d'erreur **exact** du compilateur ou de la boîte de dialogue. Un
rapport sans version est difficile à traiter.

## Proposer un script

- **Un script par *pull request*.** C'est plus facile à relire et à annuler.
- En-tête obligatoire, sur le modèle des scripts existants :

```csharp
// =============================================================================
//  MonScript.cs   --   VEGAS Pro 20
// =============================================================================
//  A QUOI CA SERT :
//    ...
//  UTILISATION : Outils > Scripts > MonScript
// =============================================================================
```

- **Aucun chemin codé en dur** vers une machine. Utilisez une boîte de dialogue
  ou une constante en tête de fichier.
- **Aucun secret** : pas de jeton, pas de mot de passe, pas d'adresse IP
  interne, pas de nom de machine.
- Si votre script règle une propriété de l'API, **ajoutez d'abord l'objet à sa
  collection, réglez ensuite** (voir `ZoomAutoVegas.cs` : c'est le piège
  documenté le plus courant).
- Dites dans la *pull request* **sur quelle version de VEGAS** vous l'avez
  testé, et ce que vous avez observé.

## Style de code

Le style existant : C#, commentaires en français, indentation 4 espaces,
en-têtes de section en `// ===`. Restez cohérent avec les fichiers voisins.

## Ce qui ne sera pas accepté

- Du code copié d'ailleurs sans en indiquer la source et la licence.
- Un script qui détruit des données sans confirmation explicite de
  l'utilisateur.
- Des binaires compilés (`*.dll`). On versionne les sources.
