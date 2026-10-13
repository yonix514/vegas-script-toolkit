# vegas-script-toolkit

**15 scripts for VEGAS Pro 20 that automate "images over voice-over" editing.**
Written by a working video editor who builds his own tools, used in production.

[Version française →](README.md)

---

## The problem

Cutting a video where **hundreds of images advance over a voice-over** is
thankless work. When a narrator talks for 30 minutes and the artwork changes
with every sentence, you have to:

- place 500 images on the timeline,
- match **every** image change to **every** sentence boundary,
- stretch or shrink some images by a few tenths of a second,
- drop the transitions in,
- and do it all again every time the commentary is revised.

By hand that is hours of work. It is also exactly the kind of repetitive job a
script does better, faster, and without getting bored.

## What the scripts do

The workflow is **driven by the voice**: you listen, you mark, the script
aligns. That is the only method that stays fast while the commentary is still
changing.

### Step 1 — listen and mark

| Script | What it does |
| --- | --- |
| **`PreparerVoixOff`** | Puts the voice-over alone on a track named `VOIX OFF`, sets the project (1920×1080, 60 fps) and the project length to the audio length. **Refuses to overwrite** a non-empty audio track: it reuses it and says so. |
| **`PreparerAlignement`** | Builds a clean project: video track + voice track + markers + regions. No hard-coded paths, everything through dialogs. |
| **`MarquerSelection`** | Drops a **named** marker at the exact cursor position. If you confirm with an empty label, it generates one. |
| **`AideRaccourcis`** | Shows the useful shortcuts while listening, and writes a printable copy. |
| **`MarqueurToutesLesXSecondes`** | Regular markers at a fixed interval, to keep your bearings in a long edit. |

You listen to the voice and press `M` at each image change. `M` is VEGAS'
native key; the scripts only read those markers.

### Step 2 — align the images

| Script | What it does |
| --- | --- |
| **`AlignerImagesSurMarqueurs`** | **Creates** events from an image folder and pins them to the markers. **Natural** alphabetical sort: `1, 2, …, 10, 11` (not `1, 10, 11, 2`). |
| **`OrganiserImagesSurMarqueurs`** | Creates **nothing**: it **repositions and resizes** images already on the timeline. Use this one when your images are already placed. |
| **`CouperATousLesMarqueurs`** | Splits every video event at every marker in one pass. |
| **`CouperAuCurseur`** | Splits across **all** selected tracks. VEGAS' `S` key only splits the active track — limiting when you have several layers. |
| **`AjusterDureeSelection`** | Two modes: percentage (110 % = +10 %), or evenly **redistribute** a block. |
| **`FonduAuto`** | Cross-fades between consecutive events. Optional real overlap: the script pulls the next event's start back and lengthens it by the same amount, so **the rest of the edit does not move**. |

> ⚠️ `AlignerImagesSurMarqueurs` and `OrganiserImagesSurMarqueurs` are not the
> same tool. The first **creates** events, the second **moves** existing ones.
> Picking the wrong one breaks nothing, it just does not do what you expect.

### Step 3 — keep the good takes

| Script | What it does |
| --- | --- |
| **`GarderSegmentsMarques`** | A narrator repeats the same line 2, 3, 12 times. You mark the start and end of **each take to keep**; the script keeps the marked intervals and **deletes everything else**. |
| **`FusionnerVoixCollee`** | The previous step leaves gaps (VEGAS does not close them by itself). This script butts the kept segments together, with no gap and no overlap. |

### Step 4 — animate and check

| Script | What it does |
| --- | --- |
| **`ZoomAutoVegas`** | Slow 1.00 → 1.06 zoom using two Pan/Crop keyframes. Leaves alone any event that already has keyframes. |
| **`RapportTimeline`** | Text report of the timeline: tracks, events, start/end/duration, effects, fades, **the list of gaps**, and total project length. Run it **before** exporting. |

---

## Installation

→ **[docs/INSTALLATION.md](docs/INSTALLATION.md)** (in French) — the
`Script Menu` path, the `csc.exe` references, keyboard shortcuts, and how to
check that it works.

The short version: copy the `.cs` files into VEGAS' `Script Menu` folder, then
**Tools → Scripts**. VEGAS compiles them on first run.

---

## Example

```
1. PreparerVoixOff            -> voice-over alone on its track
2. listen, press M            -> one marker per image change
3. AlignerImagesSurMarqueurs  -> the 526 images land between the markers
4. FonduAuto                  -> one cross-fade per narrative break
5. RapportTimeline            -> check: no gaps, expected length
```

Full walkthrough: **[examples/workflow-images-voix-off.md](examples/workflow-images-voix-off.md)** (French).

---

## What I measured

Figures taken from a real production (webtoon recap, 526 images, 30-minute
voice-over) — not estimates.

| Measurement | Value |
| --- | --- |
| Images aligned | **526** |
| Boundaries landing on a real segment boundary | **527 / 527** |
| Image block duration | **1.567 s to 6.900 s** (47 to 207 frames) |
| Sum of images | exactly **53,964 frames** (30 fps → 1798.800 s) |
| Assembled voice-over length | **1798.800 s** |
| 16 parts butted together | sum = **1798.799998 s** |

Two findings were **measured**, and one of them corrected a belief:

1. **Check the output duration, not the intention.** After assembling the
   voice, the re-read duration must be **exactly** the target duration. An
   error of a few milliseconds per junction is invisible in isolation and huge
   in aggregate: across hundreds of junctions it adds up to seconds. That is
   why I always measure the duration **of the output**, not just the inputs.

2. **The ordering trap in the Vegas API.** Setting
   `VideoMotionKeyframe.Bounds` **before** `Keyframes.Add()` is **ignored**:
   VEGAS computes the default window at the moment of insertion and overwrites
   your value. Observed result: **72 animated images out of 288**. Adding first
   and setting after: **288 / 288**. `ZoomAutoVegas` uses the correct order.
   Details in [vegas-scripting-notes](https://github.com/youssef-montage) *(repo to be published)*.

> **Not measured here:** the exact time saved versus manual editing. I never
> timed it, so I do not claim it.

---

## Compatibility

| | |
| --- | --- |
| Tested on | **VEGAS Pro 20**, build **411** |
| Should work on | VEGAS Pro 18 and later (the `ScriptPortal.Vegas` API is stable) |
| Platform | Windows only (VEGAS exists nowhere else) |
| Compiler | the one shipped with Windows: `csc.exe`, .NET Framework 4 |

The scripts contain **no machine-specific hard-coded path**: everything goes
through dialogs, or through a constant at the top of the file that you set
yourself.

---

## What this repository is not

- It is **not** a finished product with an installer. These are scripts, to be
  copied into VEGAS' folder.
- It is **not** tested against every VEGAS version. It is tested against the
  one I use, in production.
- There is **no guaranteed support**. Issues are read, contributions welcome.

## License

[MIT](LICENSE) — use it, modify it, redistribute it, including commercially.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). In short: one script per pull request,
state the VEGAS version you tested on, and update the how-to.
