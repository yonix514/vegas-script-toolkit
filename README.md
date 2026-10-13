# vegas-script-toolkit

**15 scripts pour VEGAS Pro 20 qui automatisent le montage « images sur voix off ».**
Écrits par un monteur vidéo qui code ses propres outils, utilisés en production.

[English version →](README.en.md)

---

## Le problème

Monter une vidéo où **des centaines d'images défilent sur une voix off** est un
travail d'esclave. Quand un narrateur parle pendant 30 minutes et que le dessin
change à chaque phrase, il faut :

- placer 500 images sur la timeline,
- faire correspondre **chaque** changement d'image à **chaque** frontière de phrase,
- étirer ou rétrécir certaines images de quelques dixièmes de seconde,
- poser les transitions,
- et recommencer à chaque retouche du commentaire.

À la main, c'est des heures. Et c'est exactement le genre de travail répétitif
qu'un script fait mieux, plus vite, et sans se lasser.

## Ce que ces scripts font

Le workflow est **piloté par la voix** : on écoute, on marque, on laisse le
script aligner. C'est la seule méthode qui reste rapide quand le commentaire
change encore.

### Étape 1 — écouter et marquer

| Script | Ce qu'il fait |
| --- | --- |
| **`PreparerVoixOff`** | Met la voix off seule sur une piste nommée `VOIX OFF`, règle le projet (1920×1080, 60 i/s) et la durée du projet sur celle de l'audio. **Refuse d'écraser** une piste audio déjà remplie : il la réutilise et le signale. |
| **`PreparerAlignement`** | Prépare un projet propre : piste vidéo + piste voix + marqueurs + régions. Aucun chemin codé en dur, tout par boîtes de dialogue. |
| **`MarquerSelection`** | Pose un marqueur **nommé** à la position exacte du curseur. Si vous validez à vide, il fabrique un libellé automatique. |
| **`AideRaccourcis`** | Affiche les raccourcis utiles pendant l'écoute, et en écrit une copie imprimable. |
| **`MarqueurToutesLesXSecondes`** | Pose un marqueur à intervalle régulier, pour se repérer dans un montage long. |

Vous écoutez la voix, vous appuyez sur `M` à chaque changement d'image.
`M` est la touche native de VEGAS ; les scripts ne font que lire ces marqueurs.

### Étape 2 — aligner les images

| Script | Ce qu'il fait |
| --- | --- |
| **`AlignerImagesSurMarqueurs`** | **Crée** les événements depuis un dossier d'images et les colle aux marqueurs. Tri alphabétique **naturel** : `1, 2, …, 10, 11` (pas `1, 10, 11, 2`). |
| **`OrganiserImagesSurMarqueurs`** | Ne crée **rien** : il **repositionne et redimensionne** les images déjà posées. Celui-ci si vos images sont déjà sur la timeline. |
| **`CouperATousLesMarqueurs`** | Coupe tous les événements vidéo à tous les marqueurs, en une fois. |
| **`CouperAuCurseur`** | Coupe à **toutes** les pistes sélectionnées. La touche `S` de VEGAS ne coupe que la piste active — limitant quand on a plusieurs calques. |
| **`AjusterDureeSelection`** | Deux modes : pourcentage (110 % = +10 %), ou répartition **égale** d'un bloc. |
| **`FonduAuto`** | Fondus enchaînés entre événements consécutifs. Avec chevauchement réel optionnel : le script recule le début de l'événement suivant et l'allonge d'autant, donc **la suite du montage ne bouge pas**. |

> ⚠️ `AlignerImagesSurMarqueurs` et `OrganiserImagesSurMarqueurs` ne font pas la
> même chose. Le premier **crée** des événements, le second **déplace** ceux qui
> existent. Choisir le mauvais ne casse rien, mais ne fait pas ce qu'on croit.

### Étape 3 — garder les bonnes prises

| Script | Ce qu'il fait |
| --- | --- |
| **`GarderSegmentsMarques`** | Un narrateur répète la même phrase 2, 3, 12 fois. Vous marquez début et fin de **chaque prise à garder**, le script garde les intervalles marqués et **supprime tout le reste**. |
| **`FusionnerVoixCollee`** | Après l'étape précédente il reste des trous (VEGAS ne colle pas tout seul). Ce script colle les segments bout à bout, sans trou ni chevauchement. |

### Étape 4 — animer et contrôler

| Script | Ce qu'il fait |
| --- | --- |
| **`ZoomAutoVegas`** | Zoom lent 1,00 → 1,06, en deux images clés Pan/Crop. Ne touche pas un événement qui a déjà des images clés. |
| **`RapportTimeline`** | Rapport texte de la timeline : pistes, événements, début/fin/durée, effets, fondus, **la liste des trous**, et la longueur totale du projet. À lancer **avant** l'export. |

---

## Installation

→ **[docs/INSTALLATION.md](docs/INSTALLATION.md)** — le chemin du `Script Menu`,
les références `csc.exe`, les raccourcis clavier, et comment vérifier que ça marche.

Le résumé : copiez les `.cs` dans le dossier `Script Menu` de VEGAS, puis
**Outils → Scripts**. VEGAS compile au premier lancement.

---

## Exemple

```
1. PreparerVoixOff          -> la voix off est seule sur sa piste
2. écoute, touche M         -> un marqueur à chaque changement d'image
3. AlignerImagesSurMarqueurs-> les 526 images se posent entre les marqueurs
4. FonduAuto                -> un fondu à chaque rupture narrative
5. RapportTimeline          -> contrôle : aucun trou, longueur attendue
```

Détail complet : **[examples/workflow-images-voix-off.md](examples/workflow-images-voix-off.md)**.

---

## Ce que j'ai mesuré

Chiffres relevés sur une production réelle (récap webtoon, 526 images,
voix off de 30 minutes), pas des estimations.

| Mesure | Valeur |
| --- | --- |
| Images alignées | **526** |
| Frontières posées sur une vraie frontière de segment | **527 / 527** |
| Durée d'un bloc d'images | **1,567 s à 6,900 s** (47 à 207 images) |
| Somme des images | **53 964 images** exactement (30 i/s → 1798,800 s) |
| Durée de la voix off assemblée | **1798,800 s** |
| Blocs assemblés bout à bout (16 fichiers) | somme = **1798,799998 s** |

Deux découvertes ont été **mesurées**, et l'une a corrigé une croyance :

1. **Vérifier la durée de sortie, pas l'intention.** Après assemblage de la
   voix, la durée relue doit être **exactement** la durée visée. Un écart de
   quelques millisecondes par jonction est invisible à l'unité et énorme en
   agrégat : sur des centaines de jonctions, il se compte en secondes. C'est
   pour cette raison que je mesure systématiquement la durée **en sortie**, et
   pas seulement les durées d'entrée.

2. **Le piège de l'ordre dans l'API Vegas.** Régler
   `VideoMotionKeyframe.Bounds` **avant** `Keyframes.Add()` est **ignoré** :
   VEGAS calcule la fenêtre par défaut au moment de l'ajout et écrase la
   valeur. Résultat observé : **72 images animées sur 288**. En ajoutant
   d'abord et en réglant ensuite : **288 / 288**. `ZoomAutoVegas` applique
   l'ordre correct. Détail dans
   [vegas-scripting-notes](https://github.com/youssef-montage) *(dépôt à publier)*.

> **Non mesuré ici :** le gain de temps exact par rapport au montage manuel. Je
> ne l'ai pas chronométré, donc je ne l'annonce pas.

---

## Compatibilité

| | |
| --- | --- |
| Testé sur | **VEGAS Pro 20**, build **411** |
| Devrait fonctionner sur | VEGAS Pro 18 et suivants (l'API `ScriptPortal.Vegas` est stable) |
| Système | Windows uniquement (VEGAS n'existe pas ailleurs) |
| Compilateur | celui fourni avec Windows : `csc.exe`, .NET Framework 4 |

Les scripts n'ont **aucun chemin codé en dur** dépendant d'une machine :
tout passe par des boîtes de dialogue, ou par une constante en tête de fichier
que vous réglez vous-même.

---

## Ce que ce dépôt n'est pas

- Ce n'est **pas** un produit fini avec installeur. Ce sont des scripts, à
  copier dans le dossier de VEGAS.
- Ce n'est **pas** testé sur toutes les versions de VEGAS. C'est testé sur
  celle que j'utilise, en production.
- Il n'y a **pas de support garanti**. Les *issues* sont lues, les
  contributions sont bienvenues.

## Licence

[MIT](LICENSE) — utilisez, modifiez, redistribuez, y compris commercialement.

## Contribuer

Voir [CONTRIBUTING.md](CONTRIBUTING.md). En résumé : un script par
*pull request*, avec la version de VEGAS testée, et le mode d'emploi mis à jour.
