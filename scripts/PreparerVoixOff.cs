// =============================================================================
//  PreparerVoixOff.cs   --   VEGAS Pro 20   (etape 1 du workflow voix off)
// =============================================================================
//  A QUOI CA SERT :
//    met la VOIX OFF toute seule sur la timeline, prete a etre ecoutee et
//    marquee. Le script :
//      1. ajoute (ou reutilise) UNE SEULE piste audio nommee "VOIX OFF"
//      2. y place le fichier audio choisi, a partir de 0
//      3. regle le projet : 1920 x 1080, 60 i/s
//      4. regle la DUREE DU PROJET = duree de l'audio
//      5. ecrit un rapport et affiche un resume
//
//  REGLE DE SECURITE : si une piste audio contient DEJA des evenements, le
//    script NE LES ECrase PAS : il reutilise cette piste, n'ajoute rien, et
//    le signale clairement dans le rapport et dans la boite de dialogue.
//
//  PARAMETRES : voir le bloc PARAMETRES.
//    CHEMIN_AUDIO : "" = boite de dialogue (choix du fichier)
//                   sinon = chemin complet du fichier a utilise
//
//  UTILISATION : Outils > Scripts > PreparerVoixOff
//
//  CONVENTIONS API (verifiees sur VEGAS Pro 20, build 411) :
//    new Timecode(x)  ->  x en MILLISECONDES ; Timecode.Nanos = ticks de 100 ns
//    project.Video.Width / .Height / .FrameRate   (FrameRate est un Double)
//    project.Length  (Timecode, affectable)
//    project.AddAudioTrack() ; piste.AddAudioEvent(debut, duree)
//    ev.AddTake(media.GetAudioStreamByIndex(0))
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
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
    //  PARAMETRES
    // =========================================================================

    // "" = le script ouvre une boite de dialogue pour choisir le fichier.
    // Sinon, mets ici le chemin complet, par exemple :
    //   static string CHEMIN_AUDIO = @"D:\ma voix.wav";
    static string CHEMIN_AUDIO = "";

    const string NOM_PISTE = "VOIX OFF";

    const int LARGEUR = 1920;
    const int HAUTEUR = 1080;
    const double IMAGES_PAR_SECONDE = 60.0;

    const bool REGLER_PROJET = true;   // false = ne touche pas aux reglages projet

    // Fichiers proposes par defaut dans la boite de dialogue (le 1er qui existe).
    static readonly string[] AUDIO_CONNUS = new string[0];

    static readonly string DOSSIER_RAPPORTS =
        CUTLAB_RAPPORTS;

    // =========================================================================
    //  OUTILS
    // =========================================================================
    static StringBuilder log = new StringBuilder();
    static string cheminRapport = "";
    static void Ligne(string s) { log.AppendLine(s); }

    const long UNITES_PAR_MS = 10000;
    static double MsDe(Timecode tc) { return tc.Nanos / (double)UNITES_PAR_MS; }

    static string FmtMs(double ms)
    {
        if (ms < 0) ms = 0;
        long t = (long)Math.Round(ms);
        long h = t / 3600000; t %= 3600000;
        long m = t / 60000; t %= 60000;
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00.000}", h, m, t / 1000.0);
    }

    static void EcrireRapport()
    {
        try
        {
            if (!Directory.Exists(DOSSIER_RAPPORTS)) Directory.CreateDirectory(DOSSIER_RAPPORTS);
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, "rapport_preparer_voix_off.txt");
            string texte = log.ToString();
            File.WriteAllText(cheminRapport, texte, new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(DOSSIER_RAPPORTS,
                "rapport_preparer_voix_off_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"),
                texte, new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            try
            {
                cheminRapport = Path.Combine(Path.GetTempPath(), "rapport_preparer_voix_off.txt");
                File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
            }
            catch { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }
        }
    }

    static int CompterEvenements(AudioTrack t)
    {
        int n = 0;
        foreach (TrackEvent ev in t.Events) n++;
        return n;
    }

    // =========================================================================
    //  CHOIX DU FICHIER AUDIO
    // =========================================================================
    static string ChoisirFichier()
    {
        if (CHEMIN_AUDIO != null && CHEMIN_AUDIO.Length > 0) return CHEMIN_AUDIO;

        string depart = "";
        foreach (string f in AUDIO_CONNUS)
            if (File.Exists(f)) { depart = f; break; }

        using (OpenFileDialog dlg = new OpenFileDialog())
        {
            dlg.Title = "PreparerVoixOff - choisis le fichier de voix off";
            dlg.Filter = "Fichiers audio (*.wav;*.mp3;*.aac;*.m4a;*.flac;*.wma;*.aif;*.aiff)|*.wav;*.mp3;*.aac;*.m4a;*.flac;*.wma;*.aif;*.aiff"
                       + "|Tous les fichiers (*.*)|*.*";
            if (depart.Length > 0)
            {
                dlg.InitialDirectory = Path.GetDirectoryName(depart);
                dlg.FileName = Path.GetFileName(depart);
            }
            if (dlg.ShowDialog() != DialogResult.OK) return "";
            return dlg.FileName;
        }
    }

    // =========================================================================
    //  POINT D'ENTREE VEGAS
    // =========================================================================
    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();

        Ligne("RAPPORT - PREPARER LA VOIX OFF");
        Ligne("==============================");
        Ligne("Date   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script : PreparerVoixOff.cs  (VEGAS Pro 20)");
        Ligne("Projet : " + (project.IsUntitled ? "(sans titre)" : project.FilePath));
        Ligne("But    : une seule piste audio \"" + NOM_PISTE + "\" avec la voix off, projet en "
              + LARGEUR + "x" + HAUTEUR + " a " + IMAGES_PAR_SECONDE.ToString(CultureInfo.InvariantCulture) + " i/s.");
        Ligne("");

        // ---- 1) etat actuel des pistes --------------------------------------
        Ligne("--- PISTES DU PROJET ---");
        AudioTrack pisteRemplie = null;
        AudioTrack pisteVide = null;
        int nbAudio = 0, nbVideo = 0;
        foreach (Track t in project.Tracks)
        {
            int n = 0;
            foreach (TrackEvent ev in t.Events) n++;
            Ligne("  " + (t.IsAudio() ? "AUDIO" : "VIDEO") + "  \"" + t.Name + "\" : " + n + " evenement(s)");
            if (t.IsAudio())
            {
                nbAudio++;
                AudioTrack at = (AudioTrack)t;
                if (n > 0) { if (pisteRemplie == null) pisteRemplie = at; }
                else if (pisteVide == null || string.Equals(at.Name, NOM_PISTE, StringComparison.OrdinalIgnoreCase))
                    pisteVide = at;
            }
            else nbVideo++;
        }
        Ligne("pistes audio : " + nbAudio + "   pistes video : " + nbVideo);

        string avertissement = "";
        int nbEvenementsAjoutes = 0;
        AudioTrack piste = null;

        if (pisteRemplie != null)
        {
            // ---- SECURITE : ne rien ecraser ---------------------------------
            piste = pisteRemplie;
            Ligne("");
            Ligne("ATTENTION : la piste audio \"" + piste.Name + "\" contient DEJA "
                  + CompterEvenements(piste) + " evenement(s).");
            Ligne("            Le script NE L'ECRASE PAS : il la REUTILISE telle quelle et");
            Ligne("            n'ajoute AUCUN fichier. Rien n'a ete modifie sur la timeline.");
            avertissement = "ATTENTION : une piste audio contient deja des evenements.\n"
                          + "Piste reutilisee : \"" + piste.Name + "\"\n"
                          + "Aucun fichier n'a ete ajoute (rien n'a ete ecrase).\n\n";
        }
        else
        {
            // ---- 2) choix du fichier ----------------------------------------
            string fichier = ChoisirFichier();
            if (fichier == null || fichier.Length == 0)
            {
                Ligne("");
                Ligne("ARRET : aucun fichier choisi (boite de dialogue annulee).");
                EcrireRapport();
                MessageBox.Show("Aucun fichier audio choisi : rien n'a ete fait.\n\nRapport :\n" + cheminRapport,
                    "PreparerVoixOff", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Ligne("");
            Ligne("--- FICHIER AUDIO CHOISI ---");
            Ligne("chemin : " + fichier);
            if (!File.Exists(fichier))
            {
                Ligne("ARRET : ce fichier n'existe pas.");
                EcrireRapport();
                MessageBox.Show("Fichier introuvable :\n" + fichier + "\n\nRien n'a ete fait.\n\nRapport :\n" + cheminRapport,
                    "PreparerVoixOff", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            FileInfo fi = new FileInfo(fichier);
            Ligne("taille : " + fi.Length + " octets");

            // ---- 3) piste cible : une piste audio vide, sinon on en cree une -
            if (pisteVide != null)
            {
                piste = pisteVide;
                Ligne("piste reutilisee (elle etait vide) : \"" + piste.Name + "\"");
            }
            else
            {
                piste = project.AddAudioTrack();
                Ligne("piste creee : \"" + piste.Name + "\"");
            }
            try { piste.Name = NOM_PISTE; }
            catch (Exception ex) { Ligne("nom de piste non modifiable : " + ex.Message); }
            Ligne("piste cible : \"" + piste.Name + "\" (index " + piste.Index + ")");

            // ---- 4) media + evenement a partir de 0 -------------------------
            Media media = null;
            try
            {
                media = project.MediaPool.AddMedia(fichier);
                Ligne("media ajoute au pool : OK (" + (media == null ? "null" : media.Title) + ")");
            }
            catch (Exception ex)
            {
                Ligne("ARRET : ajout du media impossible : " + ex.Message);
                EcrireRapport();
                MessageBox.Show("Impossible d'ouvrir ce fichier dans Vegas :\n" + fichier
                    + "\n\n" + ex.Message + "\n\nRapport :\n" + cheminRapport,
                    "PreparerVoixOff", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            double dureeMediaMs = 0;
            try { dureeMediaMs = MsDe(media.Length); }
            catch { }
            if (dureeMediaMs <= 0)
            {
                try { dureeMediaMs = MsDe(media.GetAudioStreamByIndex(0).Length); }
                catch { }
            }
            Ligne("duree du media   : " + FmtMs(dureeMediaMs)
                  + "  (" + dureeMediaMs.ToString("0.000", CultureInfo.InvariantCulture) + " ms)");
            Ligne("frequence audio du media : "
                  + (media.StreamCount(MediaType.Audio) > 0
                     ? media.GetAudioStreamByIndex(0).StreamInfoText : "(aucun flux audio)"));

            try
            {
                AudioEvent ev = piste.AddAudioEvent(new Timecode(0), new Timecode(Math.Max(1000.0, dureeMediaMs)));
                ev.AddTake(media.GetAudioStreamByIndex(0));
                ev.Start = new Timecode(0);
                if (dureeMediaMs > 0) ev.Length = new Timecode(dureeMediaMs);
                nbEvenementsAjoutes++;
                Ligne("evenement audio pose a 0 : duree lue " + FmtMs(MsDe(ev.Length)));
            }
            catch (Exception ex)
            {
                Ligne("ARRET : pose de l'evenement impossible : " + ex.Message);
                EcrireRapport();
                MessageBox.Show("Impossible de poser la voix off sur la timeline :\n" + ex.Message
                    + "\n\nRapport :\n" + cheminRapport,
                    "PreparerVoixOff", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        // ---- 5) reglages du projet ------------------------------------------
        Ligne("");
        Ligne("--- REGLAGES DU PROJET ---");
        if (REGLER_PROJET)
        {
            try
            {
                project.Video.Width = LARGEUR;
                project.Video.Height = HAUTEUR;
            }
            catch (Exception ex) { Ligne("  largeur/hauteur refusees : " + ex.Message); }
            try
            {
                project.Video.FrameRate = IMAGES_PAR_SECONDE;
            }
            catch (Exception ex) { Ligne("  cadence refusee : " + ex.Message); }
        }
        Ligne("largeur lue     : " + project.Video.Width + "  (demande " + LARGEUR + ")");
        Ligne("hauteur lue     : " + project.Video.Height + "  (demande " + HAUTEUR + ")");
        Ligne("cadence lue     : " + project.Video.FrameRate.ToString(CultureInfo.InvariantCulture)
              + " i/s  (demande " + IMAGES_PAR_SECONDE.ToString(CultureInfo.InvariantCulture) + ")");
        try
        {
            Ligne("frequence audio projet : " + project.Audio.SampleRate + " Hz / " + project.Audio.BitDepth + " bits");
        }
        catch { }

        // ---- 6) duree du projet = duree de l'audio --------------------------
        double finAudioMs = 0;
        int nbEvenements = 0;
        foreach (TrackEvent ev in piste.Events)
        {
            nbEvenements++;
            double f = MsDe(ev.End);
            if (f > finAudioMs) finAudioMs = f;
        }
        Ligne("");
        Ligne("--- PISTE \"" + piste.Name + "\" ---");
        Ligne("evenements sur la piste : " + nbEvenements);
        foreach (TrackEvent ev in piste.Events)
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  debut {0,12:0.000} ms | duree {1,12:0.000} ms | fin {2,12:0.000} ms",
                ev.Start.ToMilliseconds(), ev.Length.ToMilliseconds(), ev.End.ToMilliseconds()));
        Ligne("fin du dernier evenement : " + FmtMs(finAudioMs));

        // PIEGE API VERIFIE : Project.Length est en LECTURE SEULE (aucun setter,
        // ni sur Project ni sur IProjectCOM). La duree du projet est DERIVEE
        // automatiquement de la fin du dernier evenement : elle vaut donc deja
        // la duree de l'audio. On la relit et on la compare.
        double dureeProjetMs = MsDe(project.Length);
        Ligne("duree du projet demandee : " + FmtMs(finAudioMs) + "  (Project.Length est en lecture seule)");
        Ligne("duree du projet lue      : " + FmtMs(dureeProjetMs));
        if (dureeProjetMs + 1.0 < finAudioMs)
            Ligne("ATTENTION : la duree du projet est plus courte que l'audio ("
                  + FmtMs(finAudioMs - dureeProjetMs) + " manquant)");
        else
            Ligne("verification : duree du projet = duree de l'audio  -> OK");

        // ---- 7) resume ------------------------------------------------------
        Ligne("");
        Ligne("--- RESUME ---");
        Ligne("piste                       : \"" + piste.Name + "\"");
        Ligne("evenements ajoutes          : " + nbEvenementsAjoutes);
        Ligne("evenements sur la piste     : " + nbEvenements);
        Ligne("duree audio (fin dernier ev): " + FmtMs(finAudioMs));
        Ligne("duree du projet (relue)     : " + FmtMs(MsDe(project.Length))
              + ((MsDe(project.Length) + 1.0 >= finAudioMs) ? "   (= duree de l'audio)" : "   <<< PLUS COURTE QUE L'AUDIO >>>"));
        Ligne("resolution                  : " + project.Video.Width + "x" + project.Video.Height);
        Ligne("cadence                     : " + project.Video.FrameRate.ToString(CultureInfo.InvariantCulture) + " i/s");
        if (avertissement.Length > 0) Ligne("avertissement               : piste audio existante, rien n'a ete ecrase");
        Ligne("");
        Ligne("SUITE : ecoute la voix off, appuie sur M au DEBUT et a la FIN de chaque");
        Ligne("        prise a garder, puis lance GarderSegmentsMarques.");
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        Ligne("");

        EcrireRapport();

        MessageBox.Show(avertissement
            + "Voix off prete.\n\n"
            + "Piste                : \"" + piste.Name + "\"\n"
            + "Evenements sur piste : " + nbEvenements + "\n"
            + "Duree de l'audio     : " + FmtMs(finAudioMs) + "\n"
            + "Duree du projet      : " + FmtMs(MsDe(project.Length)) + "\n"
            + "Resolution           : " + project.Video.Width + "x" + project.Video.Height
            + "  " + project.Video.FrameRate.ToString(CultureInfo.InvariantCulture) + " i/s\n"
            + "\nEtape suivante : ecoute, pose tes marqueurs (touche M) au debut et a la fin\n"
            + "de chaque BONNE prise, puis lance GarderSegmentsMarques.\n"
            + "\nRapport :\n" + cheminRapport,
            "PreparerVoixOff", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
