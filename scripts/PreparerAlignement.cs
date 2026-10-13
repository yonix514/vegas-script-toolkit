// =============================================================================
//  PreparerAlignement.cs  --  VEGAS Pro 20
// =============================================================================
//  BUT : preparer le travail d'ALIGNEMENT MANUEL. Le script ne monte rien :
//        il donne une base propre et des reperes, l'utilisateur aligne ensuite
//        chaque image sur la voix off a la main, puis pose ses transitions.
//
//  CE QUE FAIT LE SCRIPT
//    1. projet 1920x1080 / 60 i/s
//    2. piste video "WEBTOON"  : tes PNG dans l'ordre, duree PROVISOIRE
//    3. piste audio "VOIX OFF" : ton fichier audio a 0:00
//    4. marqueurs : un par debut de sequence (lus dans un fichier de sequences)
//                   + un toutes les 10 s (0:00, 0:10, ...) pour se repere
//    5. regions   : une par sequence (navigation dans la voix)
//    6. rapport texte + projet .veg enregistre
//
//  IMPORTANT -- CE SCRIPT TE DEMANDE TES FICHIERS
//    Depuis la version CUTLAB STUDIO, ce script ne contient AUCUN chemin code
//    en dur. Il ouvre des boites de dialogue pour choisir :
//      1) le fichier de sequences (.md)   -- facultatif
//      2) le dossier de tes images PNG     -- obligatoire
//      3) ton fichier audio de voix off    -- facultatif
//      4) ou enregistrer le projet .veg    -- par defaut dans Documents
//    Tu peux tout annuler : dans ce cas rien n'est modifie sur ta timeline.
//
//  FORMAT DU FICHIER DE SEQUENCES (facultatif)
//    Un tableau Markdown, une ligne par sequence, colonnes separees par "|".
//    Les colonnes 2 (numero Sxx), 3 (nom), 5 (debut en s), 6 (fin en s) sont
//    lues. La colonne 8 (nombre de phrases) est ajoutee au nom si presente.
//    Exemple :
//      | seq | nom        | type | debut | fin   | duree | phrases |
//      |-----|------------|------|-------|-------|-------|---------|
//      | S01 | Ouverture  | plan | 0.0   | 12.4  | 12.4  | 3       |
//    Si tu n'as pas ce fichier : annule la 1re boite. Le script pose alors
//    uniquement les marqueurs de repere toutes les 10 s.
//
//  UTILISATION : Outils > Scripts > PreparerAlignement
//
//  CONVENTIONS API (verifiees sur VEGAS Pro 20, build 411) :
//    Timecode en MILLISECONDES, ((TrackEvent)ve).AddTake(stream),
//    project.Markers.Add(new Marker(new Timecode(ms), "...")),
//    project.Regions.Add(new Region(new Timecode(ms), new Timecode(lenMs), "..."))
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    // -- Dossiers -------------------------------------------------------------
    // Ces champs sont REMPLIS au lancement par DemanderLesEntrees().
    // Aucun chemin dependant de la machine de l'auteur.
    static string IMG_DIR    = "";
    static string SEQ_PATH   = "";
    static string AUDIO_FILE = "";
    static string OUT_VEG    = "";
    static string REPORT     = "";

    const double FPS      = 60.0;
    const int    W        = 1920;
    const int    H        = 1080;
    const double PAS_S    = 10.0;      // marqueur tous les 10 s
    const double AUDIO_MS_PAR_DEFAUT = 900000.0; // 900 s, utilise SEULEMENT si aucun audio choisi

    // -- Etat -----------------------------------------------------------------
    static StringBuilder log = new StringBuilder();
    static List<string> erreurs = new List<string>();

    class Img { public int Key; public string File; }
    class Seq { public string Num; public string Nom; public double T0; public double T1; }

    static int nbEvents = 0, nbAudio = 0, nbMarqSeq = 0, nbMarqTps = 0, nbRegions = 0;
    static double evDurMs = 0.0;
    static double[] evStartMs;

    // --- CUTLAB STUDIO : dossier des rapports -------------------------------
    // Choisi une seule fois, ici. Aucun chemin dependant de la machine de l'auteur.
    static string CUTLAB_RAPPORTS
    {
        get
        {
            string d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "CUTLAB STUDIO", "rapports");
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); }
            catch { d = Path.GetTempPath(); }
            return d;
        }
    }
    // -------------------------------------------------------------------------

    // =========================================================================
    //  DEMANDE DES ENTREES A L'UTILISATEUR
    // =========================================================================
    static bool DemanderLesEntrees()
    {
        // 1) fichier de sequences (facultatif)
        OpenFileDialog dlgSeq = new OpenFileDialog();
        dlgSeq.Title = "1/3 - Fichier de sequences (.md) - ANNULER si tu n'en as pas";
        dlgSeq.Filter = "Fichier de sequences (*.md;*.txt)|*.md;*.txt|Tous les fichiers (*.*)|*.*";
        dlgSeq.CheckFileExists = true;
        if (dlgSeq.ShowDialog() == DialogResult.OK) SEQ_PATH = dlgSeq.FileName;
        else SEQ_PATH = "";

        // 2) dossier des images (obligatoire)
        FolderBrowserDialog dlgImg = new FolderBrowserDialog();
        dlgImg.Description = "2/3 - Choisis le dossier qui contient tes images PNG";
        dlgImg.ShowNewFolderButton = false;
        if (dlgImg.ShowDialog() != DialogResult.OK)
        {
            MessageBox.Show("Aucun dossier d'images choisi.\nRien n'a ete modifie.",
                "PreparerAlignement", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        IMG_DIR = dlgImg.SelectedPath;

        // 3) fichier audio de voix off (facultatif)
        OpenFileDialog dlgAudio = new OpenFileDialog();
        dlgAudio.Title = "3/3 - Ton fichier de voix off - ANNULER s'il est deja sur la timeline";
        dlgAudio.Filter = "Audio (*.wav;*.mp3;*.wma;*.m4a;*.aac;*.flac;*.ogg)|*.wav;*.mp3;*.wma;*.m4a;*.aac;*.flac;*.ogg|Tous les fichiers (*.*)|*.*";
        dlgAudio.CheckFileExists = true;
        if (dlgAudio.ShowDialog() == DialogResult.OK) AUDIO_FILE = dlgAudio.FileName;
        else AUDIO_FILE = "";

        // 4) ou enregistrer le projet .veg
        string defaut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CUTLAB STUDIO", "PREPARATION_ALIGNEMENT.veg");
        try
        {
            string dossierDefaut = Path.GetDirectoryName(defaut);
            if (!Directory.Exists(dossierDefaut)) Directory.CreateDirectory(dossierDefaut);
        }
        catch { defaut = Path.Combine(Path.GetTempPath(), "PREPARATION_ALIGNEMENT.veg"); }

        SaveFileDialog dlgVeg = new SaveFileDialog();
        dlgVeg.Title = "Ou enregistrer le projet de preparation ?";
        dlgVeg.Filter = "Projet VEGAS (*.veg)|*.veg";
        dlgVeg.FileName = Path.GetFileName(defaut);
        try { dlgVeg.InitialDirectory = Path.GetDirectoryName(defaut); } catch { }
        if (dlgVeg.ShowDialog() == DialogResult.OK) OUT_VEG = dlgVeg.FileName;
        else OUT_VEG = defaut;

        try
        {
            REPORT = Path.Combine(Path.GetDirectoryName(OUT_VEG), "rapport_preparation.txt");
        }
        catch { REPORT = Path.Combine(CUTLAB_RAPPORTS, "rapport_preparation.txt"); }

        return true;
    }

    // =========================================================================
    public void FromVegas(Vegas vegas)
    {
        log.AppendLine("RAPPORT DE PREPARATION D'ALIGNEMENT MANUEL");
        log.AppendLine("==========================================");
        log.AppendLine("Date           : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        log.AppendLine("Script         : PreparerAlignement.cs (VEGAS Pro 20)");
        log.AppendLine();

        // ---- Choix des entrees par l'utilisateur (aucun chemin code en dur) -
        if (!DemanderLesEntrees()) return;

        Project project = vegas.Project;

        // ---- 1920x1080 / 60 i/s -------------------------------------------
        try
        {
            project.Video.Width  = W;
            project.Video.Height = H;
            project.Video.FrameRate = FPS;
            project.Audio.SampleRate = 48000;
            project.Audio.BitDepth = 16;
        }
        catch (Exception ex) { Erreur("reglage du projet : " + ex.Message); }

        // ---- Lecture des entrees (avant de creer quoi que ce soit) --------
        List<Img> imgs = ListerImages();
        List<Seq> seqs = LireSequences();

        log.AppendLine("--- ENTREES ---");
        log.AppendLine("dossier images : " + IMG_DIR);
        log.AppendLine("images PNG     : " + imgs.Count);
        if (SEQ_PATH.Length > 0)
        {
            log.AppendLine("sequences      : " + seqs.Count + "  lues dans " + SEQ_PATH);
            if (seqs.Count > 0)
                log.AppendLine("voix couverte  : " + Fmt(seqs[0].T0) + " s -> " + Fmt(seqs[seqs.Count - 1].T1) + " s");
        }
        else log.AppendLine("sequences      : aucune (pas de fichier de sequences)");
        log.AppendLine("voix off       : " + (AUDIO_FILE.Length > 0 ? AUDIO_FILE : "(pas de fichier choisi)"));
        log.AppendLine();

        // ---- Pistes : AUDIO d'abord (elle se place en haut de la timeline),
        //      VIDEO ensuite (sous la voix) ---------------------------------
        AudioTrack at = project.AddAudioTrack();
        at.Name = "VOIX OFF";
        VideoTrack vt = project.AddVideoTrack();
        vt.Name = "WEBTOON";
        log.AppendLine("--- PISTES ---");
        log.AppendLine("ajoutees       : VOIX OFF (audio), WEBTOON (video)");
        log.AppendLine("pistes projet  : " + project.Tracks.Count + " au total");
        log.AppendLine();

        // ---- Voix off a 0:00 ---------------------------------------------
        log.AppendLine("--- VOIX OFF ---");
        double audioMs = AUDIO_MS_PAR_DEFAUT;
        if (AUDIO_FILE.Length > 0 && File.Exists(AUDIO_FILE))
        {
            try
            {
                Media am = project.MediaPool.AddMedia(AUDIO_FILE);
                if (am != null && am.Streams != null && am.Streams.Count > 0)
                {
                    Timecode duree = am.Length;
                    double ms = duree.ToMilliseconds();
                    if (ms > 0.0) audioMs = ms;
                    AudioEvent ae = at.AddAudioEvent(new Timecode(0.0), new Timecode(audioMs));
                    if (ae != null)
                    {
                        ((TrackEvent)ae).AddTake(am.Streams[0]);
                        ae.Name = "VOIX OFF (" + Path.GetFileName(AUDIO_FILE) + ") " + Fmt(audioMs / 1000.0) + " s";
                        nbAudio = 1;
                        log.AppendLine("[ok] " + Path.GetFileName(AUDIO_FILE) + "  a 0:00");
                        log.AppendLine("     duree lue  : " + Fmt(ms) + " ms ("
                            + duree.FrameCount + " images @ " + Fmt(duree.FrameRate) + " i/s)");
                        log.AppendLine("     duree posee: " + Fmt(audioMs) + " ms = " + Fmt(audioMs / 1000.0) + " s");
                        log.AppendLine("     fichier    : " + new FileInfo(AUDIO_FILE).Length + " octets");
                    }
                    else Erreur("evenement audio refuse");
                }
                else Erreur("aucun flux audio dans " + AUDIO_FILE);
            }
            catch (Exception ex) { Erreur("import de la voix : " + ex.Message); }
        }
        else if (AUDIO_FILE.Length > 0) Erreur("voix introuvable : " + AUDIO_FILE);
        else
        {
            log.AppendLine("[i] aucun fichier audio choisi : les durees provisoires sont basees");
            log.AppendLine("    sur " + Fmt(audioMs / 1000.0) + " s. Ajuste-les a la main apres avoir");
            log.AppendLine("    pose ta voix off sur la piste VOIX OFF.");
        }
        log.AppendLine();

        // ---- images, duree PROVISOIRE egale -------------------------------
        log.AppendLine("--- IMAGES (duree PROVISOIRE) ---");
        evStartMs = new double[imgs.Count];
        double dureeParImage = audioMs / (imgs.Count > 0 ? imgs.Count : 1);
        evDurMs = dureeParImage;
        log.AppendLine("duree voix     : " + Fmt(audioMs) + " ms");
        log.AppendLine("nb images      : " + imgs.Count);
        log.AppendLine("DUREE PROVISOIRE par image : " + Fmt(dureeParImage) + " ms  ("
            + Fmt(dureeParImage / 1000.0) + " s = " + Fmt(audioMs) + " / " + imgs.Count + ")");

        Dictionary<string, Media> mediaCache = new Dictionary<string, Media>();
        var echecs = new List<string>();
        for (int i = 0; i < imgs.Count; i++)
        {
            double startMs = i * dureeParImage;
            evStartMs[i] = startMs;
            string chemin = Path.Combine(IMG_DIR, imgs[i].File);
            try
            {
                Media media;
                if (!mediaCache.TryGetValue(imgs[i].File, out media))
                {
                    media = project.MediaPool.AddMedia(chemin);
                    if (media == null || media.Streams == null || media.Streams.Count == 0)
                        throw new Exception("media illisible");
                    mediaCache[imgs[i].File] = media;
                }
                VideoEvent ve = vt.AddVideoEvent(new Timecode(startMs), new Timecode(dureeParImage));
                if (ve == null) throw new Exception("evenement refuse");
                ((TrackEvent)ve).AddTake(media.Streams[0]);
                ve.Name = "IMG " + imgs[i].Key.ToString("000") + "  (provisoire)";
                nbEvents++;
            }
            catch (Exception ex)
            {
                echecs.Add(imgs[i].File + " : " + ex.Message);
            }
        }
        log.AppendLine("[ok] evenements video poses : " + nbEvents + " / " + imgs.Count);
        if (echecs.Count > 0)
        {
            log.AppendLine("[!] echecs : " + echecs.Count);
            foreach (string e in echecs) { Erreur("image " + e); if (erreurs.Count > 40) break; }
        }
        log.AppendLine();

        // ---- Marqueurs de sequence (le coeur du script) -------------------
        log.AppendLine("--- MARQUEURS DE SEQUENCE ---");
        foreach (Seq s in seqs)
        {
            try
            {
                project.Markers.Add(new Marker(new Timecode(s.T0 * 1000.0),
                    s.Num + " \u00b7 " + s.Nom));
                nbMarqSeq++;
            }
            catch (Exception ex) { Erreur("marqueur " + s.Num + " : " + ex.Message); }
        }
        log.AppendLine("[ok] marqueurs de debut de sequence : " + nbMarqSeq + " / " + seqs.Count);
        log.AppendLine();

        // ---- Marqueurs toutes les 10 s ------------------------------------
        log.AppendLine("--- MARQUEURS DE REPERE (toutes les " + Fmt(PAS_S) + " s) ---");
        double derniere = (seqs.Count > 0) ? seqs[seqs.Count - 1].T1 : audioMs / 1000.0;
        for (double t = 0.0; t <= derniere + 0.001; t += PAS_S)
        {
            try
            {
                project.Markers.Add(new Marker(new Timecode(t * 1000.0), Mmss(t)));
                nbMarqTps++;
            }
            catch (Exception ex) { Erreur("marqueur temps " + Fmt(t) + " : " + ex.Message); }
        }
        log.AppendLine("[ok] marqueurs de repere : " + nbMarqTps);
        log.AppendLine("     total marqueurs projet : " + (nbMarqSeq + nbMarqTps));
        log.AppendLine();

        // ---- Regions : une par sequence -----------------------------------
        log.AppendLine("--- REGIONS ---");
        foreach (Seq s in seqs)
        {
            double lenMs = (s.T1 - s.T0) * 1000.0;
            if (lenMs <= 0.0) continue;
            try
            {
                project.Regions.Add(new Region(new Timecode(s.T0 * 1000.0), new Timecode(lenMs),
                    s.Num + " \u00b7 " + s.Nom + "  (" + Fmt(s.T1 - s.T0) + " s)"));
                nbRegions++;
            }
            catch (Exception ex) { Erreur("region " + s.Num + " : " + ex.Message); }
        }
        log.AppendLine("[ok] regions : " + nbRegions + " / " + seqs.Count);
        log.AppendLine();

        // ---- Sauvegarde ---------------------------------------------------
        log.AppendLine("--- SAUVEGARDE ---");
        log.AppendLine("fichier        : " + OUT_VEG);
        bool ok = false;
        string methode = "";
        try { ok = project.SaveProject(OUT_VEG); methode = "project.SaveProject"; }
        catch (Exception ex) { Erreur("SaveProject : " + ex.Message); }
        if (!ok)
        {
            try { ok = vegas.SaveProject(OUT_VEG); methode = "vegas.SaveProject"; }
            catch (Exception ex) { Erreur("vegas.SaveProject : " + ex.Message); }
        }
        log.AppendLine("methode        : " + methode);
        log.AppendLine("resultat       : " + (ok ? "OK" : "ECHEC"));
        if (ok && File.Exists(OUT_VEG)) log.AppendLine("taille         : " + new FileInfo(OUT_VEG).Length + " octets");
        log.AppendLine("duree projet   : " + Fmt(project.Length.ToMilliseconds() / 1000.0) + " s ("
            + project.Length.FrameCount + " images @ " + Fmt(FPS) + " i/s)");
        log.AppendLine();

        // ---- Mode d'emploi dans le rapport --------------------------------
        log.AppendLine("==========================================");
        log.AppendLine("COMMENT ALIGNER (a la main)");
        log.AppendLine("==========================================");
        log.AppendLine("1. Ecoute la voix off. Les marqueurs 0:00 / 0:10 / 0:20 ... donnent le");
        log.AppendLine("   repere courant ; les marqueurs S01 / S02 ... sont les sequences.");
        log.AppendLine("2. Repere la phrase a illustrer : clique dans la region (SEQ) correspondante");
        log.AppendLine("   puis lis son debut (le marqueur donne le temps exact).");
        log.AppendLine("3. Sur la piste WEBTOON, glisse l'image voulue pour que son DEBUT tombe sur");
        log.AppendLine("   la frontiere de phrase (aide : Snapping / magnetisme actif).");
        log.AppendLine("4. Ajuste la DUREE de chaque image : etire son bord droit jusqu'au debut de");
        log.AppendLine("   l'image suivante. Les durees " + Fmt(dureeParImage) + " ms sont PROVISOIRES.");
        log.AppendLine("5. Pose les transitions : chevauche deux images (glisse la suivante vers la");
        log.AppendLine("   gauche de ~200 ms) puis Image > Transition, ou utilise la poignee de fondu.");
        log.AppendLine("6. Pour marquer une frontiere pendant l'ecoute : Outils > Scripts >");
        log.AppendLine("   MarquerSelection (marqueur au curseur, libelle demande).");
        log.AppendLine();
        log.AppendLine("[i] RAPPEL : les durees d'images posees par ce script sont PROVISOIRES");
        log.AppendLine("    (" + Fmt(dureeParImage) + " ms chacune = " + Fmt(audioMs / 1000.0) + " s / " + imgs.Count + "). C'est une base de");
        log.AppendLine("    travail : chaque duree est a ajuster a la main phrase par phrase.");
        log.AppendLine();

        EcrireRapport();

        vegas.LogEvent("PreparerAlignement : " + nbEvents + " images, " + nbAudio
            + " audio, " + nbMarqSeq + " marqueurs de sequence, " + nbMarqTps
            + " marqueurs de repere, " + nbRegions + " regions.");

        MessageBox.Show(
            "Preparation d'alignement terminee.\n\n"
            + "Images (WEBTOON)      : " + nbEvents + " evenements\n"
            + "Voix off (VOIX OFF)   : " + Fmt(audioMs / 1000.0) + " s a 0:00\n"
            + "Duree PROVISOIRE/img  : " + Fmt(dureeParImage) + " ms  (a ajuster a la main)\n"
            + "Marqueurs de sequence : " + nbMarqSeq + "\n"
            + "Marqueurs 10 s        : " + nbMarqTps + "\n"
            + "Regions               : " + nbRegions + "\n\n"
            + "Projet   : " + OUT_VEG + "\n"
            + "Rapport  : " + REPORT + "\n"
            + "Erreurs  : " + erreurs.Count,
            "PreparerAlignement.cs");
    }

    // =========================================================================
    //  Entrees
    // =========================================================================
    static List<Img> ListerImages()
    {
        var liste = new List<Img>();
        try
        {
            foreach (string f in Directory.GetFiles(IMG_DIR, "*.png"))
            {
                Match m = Regex.Match(Path.GetFileName(f), "^(\\d+)-");
                if (!m.Success) continue;
                liste.Add(new Img { Key = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                                    File = Path.GetFileName(f) });
            }
            liste.Sort(delegate(Img a, Img b) { return a.Key.CompareTo(b.Key); });
        }
        catch (Exception ex) { Erreur("lecture de " + IMG_DIR + " : " + ex.Message); }
        return liste;
    }

    static List<Seq> LireSequences()
    {
        var liste = new List<Seq>();
        if (SEQ_PATH.Length == 0) return liste;
        try
        {
            if (!File.Exists(SEQ_PATH)) { Erreur("fichier de sequences introuvable : " + SEQ_PATH); return liste; }
            foreach (string ligne in File.ReadAllLines(SEQ_PATH, Encoding.UTF8))
            {
                if (ligne.Length == 0 || ligne[0] != '|') continue;
                string[] c = ligne.Split('|');
                // c[0] vide, c[1] seq, c[2] nom, c[3] type, c[4] debut, c[5] fin,
                // c[6] duree, c[7] phrases, c[8] planches, c[9] debit
                if (c.Length < 7) continue;
                string sn = c[1].Trim();
                if (!Regex.IsMatch(sn, "^S\\d+$")) continue;
                double t0, t1;
                if (!double.TryParse(c[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out t0)) continue;
                if (!double.TryParse(c[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out t1)) continue;
                string nom = Nettoyer(c[2].Trim());
                if (c.Length > 7)
                {
                    string ph = Nettoyer(c[7].Trim());
                    int nb;
                    if (ph.Length > 0 && int.TryParse(ph, NumberStyles.Integer, CultureInfo.InvariantCulture, out nb) && nb > 0)
                        nom = nom + " (" + nb + " phrases)";
                }
                liste.Add(new Seq { Num = sn, Nom = nom, T0 = t0, T1 = t1 });
            }
        }
        catch (Exception ex) { Erreur("lecture du fichier de sequences : " + ex.Message); }
        return liste;
    }

    // =========================================================================
    static void Erreur(string msg) { erreurs.Add(msg); log.AppendLine("[!] " + msg); }

    static void EcrireRapport()
    {
        log.AppendLine("--- ERREURS (" + erreurs.Count + ") ---");
        if (erreurs.Count == 0) log.AppendLine("aucune");
        else foreach (string e in erreurs) log.AppendLine("  " + e);
        try
        {
            string dossier = Path.GetDirectoryName(REPORT);
            if (dossier != null && dossier.Length > 0) Directory.CreateDirectory(dossier);
            File.WriteAllText(REPORT, log.ToString(), Encoding.UTF8);
        }
        catch
        {
            try
            {
                REPORT = Path.Combine(CUTLAB_RAPPORTS, "rapport_preparation.txt");
                File.WriteAllText(REPORT, log.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    static string Nettoyer(string s)
    {
        return s.Replace('\u2019', '\'').Replace('\u2018', '\'').Replace('\u00a0', ' ').Trim();
    }

    static string Mmss(double secondes)
    {
        int t = (int)Math.Round(secondes);
        int m = t / 60;
        int s = t - m * 60;
        return m.ToString(CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture);
    }

    static string Fmt(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    static string Fmt2(double v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }
}
