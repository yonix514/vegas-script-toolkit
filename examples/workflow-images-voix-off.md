# Exemple : 526 images sur 30 minutes de voix off

Cet exemple décrit le workflow réel, dans l'ordre, avec ce qu'il faut vérifier
à chaque étape. Les chiffres sont ceux d'une production réelle ; les blocs
`[ ]` sont les contrôles à faire.

---

## Point de départ

- **526 images** PNG nommées `001--Genius.png` … `526--Genius.png`.
- **Une voix off** de 30 minutes, avec des reprises : le narrateur répète
  souvent la même phrase avant de trouver la bonne.
- Le commentaire **va encore changer** : c'est ce qui impose un workflow
  piloté par la voix, et pas un montage figé.

---

## Étape 0 — garder les bonnes prises (`GarderSegmentsMarques`)

Ouvrez un projet VEGAS avec **seulement** la voix off sur une piste.

1. Écoutez. À chaque prise que vous voulez **garder**, appuyez sur `M` au
   **début**, puis sur `M` à la **fin**.
2. `Outils → Scripts → GarderSegmentsMarques`.

Le script numérote les intervalles dans l'ordre entre marqueurs consécutifs :

```
marqueur1 -> marqueur2  = intervalle 1
marqueur2 -> marqueur3  = intervalle 2
marqueur3 -> marqueur4  = intervalle 3
```

En mode `GARDER` (défaut) il conserve les intervalles **impairs** et supprime
les autres. C'est pour ça qu'on marque par **paires** : début et fin de chaque
prise à garder, deux marqueurs par prise.

- [ ] Le rapport texte liste le nombre d'intervalles gardés.
- [ ] **Écoutez le résultat** avant d'aller plus loin. Un décalage d'un
      marqueur se repère en écoutant, pas en relisant un rapport.

Puis `FusionnerVoixCollee` pour supprimer les trous laissés par les segments
supprimés.

- [ ] Après collage : **aucun silence anormal**. Deux segments gardés qui
      n'étaient pas voisins dans la source se retrouvent maintenant bout à
      bout — vérifiez qu'aucune phrase n'est coupée en deux.

---

## Étape 1 — préparer le projet (`PreparerVoixOff`, `PreparerAlignement`)

`PreparerVoixOff` met la voix seule sur une piste `VOIX OFF`, règle le projet en
1920×1080 / 60 i/s, et cale la **durée du projet sur celle de l'audio**.

`PreparerAlignement` ajoute la piste vidéo, importe les images dans l'ordre
**naturel** et pose les marqueurs de repère.

- [ ] La piste vidéo s'appelle `WEBTOON` (ou ce que vous voulez), la piste
      audio `VOIX OFF`.
- [ ] La durée du projet est **exactement** la durée de la voix.

---

## Étape 2 — écouter et marquer

C'est l'étape humaine, il n'y a pas de raccourci.

Écoutez la voix et appuyez sur `M` à **chaque changement d'image**, c'est-à-dire
à chaque frontière de phrase. `MarquerSelection` permet en plus de **nommer** le
marqueur (utile pour s'y retrouver : `intro`, `combat`, `révélation`…).

- [ ] Il faut **au moins 2 marqueurs** pour que l'alignement ait un sens.
- [ ] Le nombre de marqueurs doit être **au moins égal** au nombre d'images à
      placer. S'il y a moins de marqueurs que d'images, le script le signale :
      les images en trop n'ont pas de place.

---

## Étape 3 — aligner (`AlignerImagesSurMarqueurs`)

```
Outils → Scripts → AlignerImagesSurMarqueurs
```

Le script pose :

```
image 1  ->  du marqueur 1 au marqueur 2
image 2  ->  du marqueur 2 au marqueur 3
...
image N  ->  du marqueur N au marqueur N+1
```

Chaque image **coupe exactement sur les marqueurs**. Sa durée est donc celle de
l'intervalle, pas une durée choisie à l'avance.

- [ ] Lisez le rapport texte produit. Il donne le nombre d'images posées et la
      durée de chaque bloc.

Sur la production de référence :

| Mesure | Valeur |
| --- | --- |
| Images posées | 526 |
| Durée du bloc le plus court | 1,567 s (47 images) |
| Durée du bloc le plus long | 6,900 s (207 images) |
| Somme | 53 964 images |
| Frontières sur une vraie frontière de segment | 527 / 527 |

> Si vos images sont **déjà** sur la timeline, utilisez
> `OrganiserImagesSurMarqueurs` : il les redimensionne sur place au lieu d'en
> créer de nouvelles.

---

## Étape 4 — les transitions (`FonduAuto`)

Par défaut : **coupe franche**. C'est le réglage qui correspond à la plupart
des changements d'image.

Sur les **ruptures narratives** — changement de scène, ellipse, retour au
présent — un fondu enchaîné aide le spectateur. `FonduAuto` les pose sur tous
les événements consécutifs d'une piste.

- [ ] Avec `CREER_LE_CHEVAUCHEMENT = true`, vérifiez que **la suite du montage
      n'a pas bougé** : le script recule le début de l'événement suivant et
      l'allonge d'autant, précisément pour ça.
- [ ] Si VEGAS refuse la transition (flux non compatible), le script le dit dans
      le rapport et garde un fondu simple. Ce n'est pas un échec silencieux.

---

## Étape 5 — contrôler avant l'export (`RapportTimeline`)

```
Outils → Scripts → RapportTimeline
```

Le rapport donne la liste des pistes, les événements (nom, début, fin, durée,
effets, fondus), **la liste des trous**, et la longueur totale.

- [ ] **Aucun trou** là où il ne devrait pas y en avoir.
- [ ] La **longueur totale** du projet est celle attendue.
- [ ] Le nombre d'événements vidéo correspond au nombre d'images.

Cette étape a attrapé de vrais problèmes : un trou de deux images au milieu
d'un bloc passe totalement inaperçu à l'œil, et se voit tout de suite dans la
liste des trous.

---

## Ce qui se passe quand le commentaire change

C'est le cas normal, pas l'exception. Si le narrateur réenregistre une phrase :

1. Réécoutez seulement la zone concernée.
2. Déplacez ou supprimez les marqueurs concernés (`MarquerSelection`,
   ou la gestion native des marqueurs de VEGAS).
3. Relancez `OrganiserImagesSurMarqueurs` (vos images existent déjà).
4. Relancez `RapportTimeline`.

Vous ne refaites **que** la zone touchée. C'est tout l'intérêt du workflow
piloté par la voix.

---

## Les pièges, dans l'ordre où on les rencontre

| Piège | Symptôme | Ce qu'il faut faire |
| --- | --- | --- |
| Piste audio écrasée | `PreparerVoixOff` ajoute par-dessus votre voix | Il ne le fait pas : il détecte une piste pleine, la réutilise et le signale. Lisez la boîte de dialogue. |
| Moins de marqueurs que d'images | Les dernières images ne sont pas placées | Ajoutez des marqueurs. Le rapport le dit. |
| Tri alphabétique naïf | `1, 10, 11, 2` | Le script trie en **naturel**. Si vous triez vous-même les fichiers, attention. |
| Chemin de rapport invalide | Pas de fichier rapport | Réglez la constante en tête de script avant de compiler. |
| Images clés écrasées | Le zoom ne s'applique pas | L'API Vegas ignore un réglage fait **avant** l'ajout. `ZoomAutoVegas` ajoute d'abord, règle ensuite. |
