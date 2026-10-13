# Journal des modifications

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/).
Le projet suit [Semantic Versioning](https://semver.org/lang/fr/).

## [1.0.0] — 2026-10-12

Première publication.

### Ajouté

**15 scripts pour VEGAS Pro 20** (testés sur le build 411) :

*Workflow « images sur voix off »*
- `PreparerVoixOff` — met la voix off seule sur une piste, règle le projet,
  et refuse d'écraser une piste audio qui contient déjà des événements.
- `PreparerAlignement` — prépare un projet avec piste vidéo, piste voix,
  marqueurs et régions. Aucun chemin codé en dur : tout passe par des
  boîtes de dialogue.
- `MarquerSelection` — marqueur nommé à la position exacte du curseur,
  avec libellé automatique si on valide à vide.
- `MarqueurToutesLesXSecondes` — marqueurs réguliers pour se repérer.
- `AideRaccourcis` — rappel des raccourcis utiles pendant l'écoute.

*Aligner les images sur les marqueurs*
- `AlignerImagesSurMarqueurs` — **crée** les événements depuis un dossier
  d'images et les colle aux marqueurs (tri alphabétique **naturel**).
- `OrganiserImagesSurMarqueurs` — ne crée rien : **repositionne et
  redimensionne** les images déjà posées.
- `CouperATousLesMarqueurs` — coupe tous les événements à tous les marqueurs.
- `CouperAuCurseur` — coupe sur **toutes** les pistes sélectionnées, pas
  seulement la piste active.
- `AjusterDureeSelection` — pourcentage, ou répartition égale sur un bloc.
- `FonduAuto` — fondus enchaînés entre événements consécutifs, avec vrai
  chevauchement optionnel.

*Voix off : garder les bonnes prises*
- `GarderSegmentsMarques` — garde les intervalles marqués, supprime le reste.
- `FusionnerVoixCollee` — colle bout à bout les segments gardés, sans trou.

*Animation et contrôle*
- `ZoomAutoVegas` — zoom lent 1,00 → 1,06 en deux images clés Pan/Crop.
- `RapportTimeline` — rapport texte de la timeline : pistes, événements,
  fondus, **trous**, longueur du projet.

### Notes

- Le piège API `VideoMotionKeyframe.Bounds` réglé **avant** `Keyframes.Add()`
  est documenté et évité dans `ZoomAutoVegas`.
- Les `.dll` compilés ne sont pas versionnés : chacun compile sur sa machine.
