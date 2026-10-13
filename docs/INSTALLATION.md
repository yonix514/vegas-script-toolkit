# Installation

Les scripts VEGAS sont des fichiers `.cs` compilés à la volée par VEGAS.
Deux étapes : **trouver le bon dossier**, puis **faire compiler VEGAS**.

---

## 1. Où VEGAS cherche les scripts

VEGAS Pro 20 regarde **plusieurs** dossiers. Les deux qui marchent de façon
fiable :

| Portée | Dossier |
| --- | --- |
| Pour **tous** les utilisateurs de la machine | `C:\Program Files\VEGAS\VEGAS Pro 20.0\Script Menu\` |
| Pour **votre** compte seulement | `%APPDATA%\VEGAS\VEGAS Pro\20.0\Script Menu\` |
| Variante observée sur certaines installations | `%LOCALAPPDATA%\VEGAS\VEGAS Pro\20.0\Script Menu\` |

Le dossier machine demande les **droits administrateur** pour y écrire.
Si vous n'avez pas ces droits, utilisez le dossier du compte.

Ouvrez une invite de commandes et vérifiez ce qui existe déjà :

```bat
dir "%APPDATA%\VEGAS" /s /b | findstr /i "Script Menu"
dir "%LOCALAPPDATA%\VEGAS" /s /b | findstr /i "Script Menu"
```

> ⚠️ Si le dossier n'existe pas, **créez-le** : VEGAS ne le crée pas toujours,
> et il ignore silencieusement un chemin absent.

## 2. Copier les scripts

```bat
set CIBLE=%APPDATA%\VEGAS\VEGAS Pro\20.0\Script Menu
if not exist "%CIBLE%" mkdir "%CIBLE%"
copy /Y scripts\*.cs "%CIBLE%\"
```

## 3. Le dossier `rapports`

Plusieurs scripts écrivent un rapport texte (chemin, liste des événements,
trous, erreurs). Chaque script définit ce dossier dans un bloc
`CUTLAB_RAPPORTS` en tête de fichier. Par défaut il pointe vers un
sous-dossier `rapports` à côté du projet.

**Ouvrez le script et adaptez cette constante avant de compiler.** Un chemin
qui n'existe pas fait échouer l'écriture du rapport — le script le signale
dans sa boîte de dialogue, mais il vaut mieux le régler d'entrée.

## 4. Faire compiler VEGAS

1. Ouvrez VEGAS Pro.
2. Menu **Outils (Tools) → Scripts**.
3. Le nom du script apparaît dans la liste (sans l'extension `.cs`).

**Au premier lancement**, VEGAS compile le `.cs` avec `csc.exe`. Cela prend
une à deux secondes. S'il y a une erreur de compilation, VEGAS affiche le
message du compilateur : lisez-le, il donne le numéro de ligne exact.

Si vous préférez compiler à la main pour voir les erreurs plus clairement :

```bat
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library ^
  /out:"%TEMP%\MonScript.dll" ^
  /reference:"C:\Program Files\VEGAS\VEGAS Pro 20.0\ScriptPortal.Vegas.dll" ^
  /reference:System.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Core.dll ^
  /reference:Microsoft.CSharp.dll ^
  "scripts\MonScript.cs"
```

Les références **obligatoires** :

| Référence | Pourquoi |
| --- | --- |
| `ScriptPortal.Vegas.dll` | l'API de VEGAS (`Vegas`, `TrackEvent`, `Timecode`…) |
| `System.Windows.Forms.dll` | les boîtes de dialogue |
| `System.Drawing.dll` | `VideoMotionBounds`, géométrie |
| `System.Core.dll` | LINQ |
| `Microsoft.CSharp.dll` | nécessaire pour certaines expressions `dynamic` |

Adaptez le chemin de `ScriptPortal.Vegas.dll` à votre version :
`C:\Program Files\VEGAS\VEGAS Pro 20.0\ScriptPortal.Vegas.dll`.
Pour une autre version, remplacez `20.0` par la vôtre.

## 5. Ajouter des raccourcis clavier

Le vrai gain de vitesse est là.

**Options → Raccourcis clavier (Keyboard Shortcuts)**, cherchez le nom du
script, affectez une touche. Suggestions :

| Raccourci | Script |
| --- | --- |
| `Maj+M` | `MarquerSelection` |
| `Maj+C` | `CouperAuCurseur` |
| `Maj+A` | `AlignerImagesSurMarqueurs` |
| `Maj+O` | `OrganiserImagesSurMarqueurs` |

## 6. Vérifier que ça marche

Ouvrez un projet, posez deux marqueurs (touche `M`), lancez
`RapportTimeline`. Vous devez obtenir une boîte de dialogue avec le chemin
d'un fichier `.txt`. **Ouvrez-le.** S'il liste vos pistes et vos
événements, l'installation est bonne.

---

## Désinstaller

Supprimez les `.cs` du dossier `Script Menu`. VEGAS met les `.dll` compilés
en cache ailleurs : si vous modifiez un script et que VEGAS semble ignorer
la modification, videz le cache

```bat
rmdir /s /q "%LOCALAPPDATA%\VEGAS\VEGAS Pro\20.0\Script Menu"
```

puis relancez VEGAS.
