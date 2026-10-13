// =============================================================================
//  FusionnerVoixCollee.cs   --   VEGAS Pro 20   (etape 3, facultative)
// =============================================================================
//  A QUOI CA SERT :
//    apres GarderSegmentsMarques, les segments gardes restent a leur place et
//    il reste des TROUS (Vegas ne colle pas les morceaux tout seul).
//    Ce script COLLE BOUT A BOUT tous les evenements audio de la piste a
//    partir de DEBUT_MS (0 par defaut), sans trou et sans chevauchement.
//    Les evenements gardent leur ordre, leur contenu et leurs fondus.
//
//  QUAND L'UTILISER :
//    - pour verifier que la voix obtenue est bien continue (aucun silence)
//    - pour obtenir une voix off compacte avant l'export
//
//  PARAMETRES :
//    DEBUT_MS : position de depart du 1er segment (0 par defaut)
//    FONDU_MS : 0  = on NE TOUCHE PAS aux fondus deja poses (recommande)
//               >0 = on repose un fondu d'entree/sortie de cette duree su
//                    chaque segment (utile si tu n'as pas lance
//                    GarderSegmentsMarques, ou si les jonctions cliquent)
//
//  UTILISATION : Outils > Scripts > FusionnerVoixCollee
//
//  CONVENTIONS API (verifiees sur VEGAS Pro 20, build 411) :
//    ev.Start / ev.Length / ev.End (Timecode)  ;  ev.FadeIn.Length / ev.FadeOut.Length
//    Timecode.Nanos = ticks de 100 ns  =>  ms = Nanos / 10000.0
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
    static readonly double DEBUT_MS = 0.0;      // ou commence le 1er segment colle
    static readonly double FONDU_MS = 0.0;      // 0 = ne touche pas aux fondus existants

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
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, "rapport_fusionner_voix.txt");
            string texte = log.ToString();
            File.WriteAllText(cheminRapport, texte, new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(DOSSIER_RAPPORTS,
                "rapport_fusionner_voix_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"),
                texte, new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            try
            {
                cheminRapport = Path.Combine(Path.GetTempPath(), "rapport_fusionner_voix.txt");
                File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
            }
            catch { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }
        }
    }

    static List<TrackEvent> LireEvenements(AudioTrack piste)
    {
        List<TrackEvent> res = new List<TrackEvent>();
        foreach (TrackEvent ev in piste.Events) res.Add(ev);
        res.Sort(delegate(TrackEvent a, TrackEvent b) { return a.Start.Nanos.CompareTo(b.Start.Nanos); });
        return res;
    }

    static string SourceDe(TrackEvent ev)
    {
        try
        {
            Take t = ev.ActiveTake;
            if (t == null || t.Media == null) return "(SANS MEDIA)";
            string p = t.MediaPath;
            if (p == null || p.Length == 0) return "(media sans chemin)";
            return Path.GetFileName(p);
        }
        catch { return "(media illisible)"; }
    }

    static AudioTrack ChoisirPisteAudio(Project project, out string explication)
    {
        List<AudioTrack> candidats = new List<AudioTrack>();
        List<int> comptes = new List<int>();
        foreach (Track t in project.Tracks)
        {
            if (!t.IsAudio()) continue;
            AudioTrack at = (AudioTrack)t;
            int n = 0;
            foreach (TrackEvent ev in at.Events) n++;
            if (n > 0) { candidats.Add(at); comptes.Add(n); }
        }
        for (int i = 0; i < candidats.Count; i++)
            for (int j = i + 1; j < candidats.Count; j++)
                if (comptes[j] > comptes[i])
                {
                    AudioTrack ta = candidats[i]; candidats[i] = candidats[j]; candidats[j] = ta;
                    int tc = comptes[i]; comptes[i] = comptes[j]; comptes[j] = tc;
                }
        if (candidats.Count == 0)
        {
            explication = "AUCUNE piste audio ne contient d'evenement.";
            return null;
        }
        explication = "piste audio la plus fournie : \"" + candidats[0].Name
                    + "\" (" + comptes[0] + " evenement(s))";
        return candidats[0];
    }

    // =========================================================================
    //  POINT D'ENTREE VEGAS
    // =========================================================================
    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();

        Ligne("RAPPORT - FUSIONNER (COLLER BOUT A BOUT) LA VOIX");
        Ligne("===============================================");
        Ligne("Date   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script : FusionnerVoixCollee.cs  (VEGAS Pro 20)");
        Ligne("Projet : " + (project.IsUntitled ? "(sans titre)" : project.FilePath));
        Ligne("DEBUT_MS : " + DEBUT_MS.ToString(CultureInfo.InvariantCulture));
        Ligne("FONDU_MS : " + FONDU_MS.ToString(CultureInfo.InvariantCulture)
              + (FONDU_MS <= 0 ? "  (fondus existants conserves)" : "  (fondus reposes)"));
        Ligne("");

        string explication;
        AudioTrack piste = ChoisirPisteAudio(project, out explication);
        if (piste == null)
        {
            Ligne("");
            Ligne("ARRET : " + explication);
            EcrireRapport();
            MessageBox.Show("Aucune piste audio ne contient d'evenement.\n\nRapport :\n" + cheminRapport,
                "FusionnerVoixCollee", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Ligne("piste traitee : \"" + piste.Name + "\" (index " + piste.Index + ")");
        Ligne("pourquoi      : " + explication);
        Ligne("");

        List<TrackEvent> evs = LireEvenements(piste);
        Ligne("--- AVANT : " + evs.Count + " evenement(s) ---");
        double avantDebut = 0, avantFin = 0, sommeAvant = 0, trous = 0;
        for (int i = 0; i < evs.Count; i++)
        {
            double d = MsDe(evs[i].Start), f = MsDe(evs[i].End);
            if (i == 0) avantDebut = d;
            avantFin = f;
            sommeAvant += MsDe(evs[i].Length);
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  {0,3} : debut {1,12:0.000} ms | duree {2,12:0.000} ms | fin {3,12:0.000} ms | fondu IN {4,7:0.000} | fondu OUT {5,7:0.000} | [{6}]",
                i + 1, d, MsDe(evs[i].Length), f,
                evs[i].FadeIn.Length.ToMilliseconds(), evs[i].FadeOut.Length.ToMilliseconds(), SourceDe(evs[i])));
            if (i > 0)
            {
                double e = d - MsDe(evs[i - 1].End);
                if (e > 0) trous += e;
            }
        }
        Ligne("etendue AVANT      : " + FmtMs(avantDebut) + " -> " + FmtMs(avantFin)
              + "  (etendue " + FmtMs(avantFin - avantDebut) + ")");
        Ligne("somme des durees   : " + FmtMs(sommeAvant));
        Ligne("total des TROUS    : " + FmtMs(trous));

        // ---- recollage ------------------------------------------------------
        double curseur = DEBUT_MS;
        int nbFondus = 0;
        List<string> echecs = new List<string>();

        Ligne("");
        Ligne("--- RECOLLAGE A PARTIR DE " + FmtMs(DEBUT_MS) + " ---");
        for (int i = 0; i < evs.Count; i++)
        {
            TrackEvent ev = evs[i];
            Timecode cible = new Timecode(curseur);
            try { ev.Start = cible; }
            catch (Exception ex) { echecs.Add("position du segment " + (i + 1) + " refusee : " + ex.Message); }
            try { ev.End = new Timecode(curseur + MsDe(ev.Length)); }
            catch { }
            double debutReel = MsDe(ev.Start);
            double dureeReelle = MsDe(ev.Length);
            curseur = debutReel + dureeReelle;
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  {0,3} : {1,12:0.000} -> {2,12:0.000} ms   duree {3,12:0.000} ms",
                i + 1, debutReel, debutReel + dureeReelle, dureeReelle));

            if (FONDU_MS > 0)
            {
                double f = FONDU_MS;
                if (dureeReelle < 2.0 * f) f = Math.Max(0.0, dureeReelle / 2.0 - 0.5);
                if (f >= 0.5)
                {
                    try
                    {
                        ev.FadeIn.Length = new Timecode(f);
                        ev.FadeOut.Length = new Timecode(f);
                        nbFondus++;
                    }
                    catch (Exception ex) { echecs.Add("fondu refuse segment " + (i + 1) + " : " + ex.Message); }
                }
            }
        }

        // ---- relecture ------------------------------------------------------
        List<TrackEvent> finale = LireEvenements(piste);
        double sommeApres = 0, finApres = 0, debutApres = 0, trousApres = 0;
        Ligne("");
        Ligne("--- APRES (relu dans le projet) : " + finale.Count + " evenement(s) ---");
        for (int i = 0; i < finale.Count; i++)
        {
            double d = MsDe(finale[i].Start), f = MsDe(finale[i].End);
            if (i == 0) debutApres = d;
            finApres = f;
            sommeApres += MsDe(finale[i].Length);
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  {0,3} : debut {1,12:0.000} ms | duree {2,12:0.000} ms | fin {3,12:0.000} ms | fondu IN {4,7:0.000} | fondu OUT {5,7:0.000} | [{6}]",
                i + 1, d, MsDe(finale[i].Length), f,
                finale[i].FadeIn.Length.ToMilliseconds(), finale[i].FadeOut.Length.ToMilliseconds(),
                SourceDe(finale[i])));
            if (i > 0)
            {
                double e = d - MsDe(finale[i - 1].End);
                if (e > 0) trousApres += e;
            }
        }
        Ligne("etendue APRES      : " + FmtMs(debutApres) + " -> " + FmtMs(finApres)
              + "  (etendue " + FmtMs(finApres - debutApres) + ")");
        Ligne("somme des durees   : " + FmtMs(sommeApres));
        Ligne("total des TROUS    : " + FmtMs(trousApres) + (trousApres <= 0.001 ? "   (aucun trou : COLLE)" : "   <<< il reste des trous >>>"));

        // PIEGE API VERIFIE : Project.Length est en LECTURE SEULE ; la duree du
        // projet est derivee de la fin du dernier evenement. On la relit.
        Ligne("duree du projet (relue, derivee du dernier evenement) : " + FmtMs(MsDe(project.Length)));

        Ligne("");
        Ligne("--- RESUME ---");
        Ligne("piste                  : \"" + piste.Name + "\"");
        Ligne("evenements colles      : " + finale.Count);
        Ligne("etendue avant          : " + FmtMs(avantDebut) + " -> " + FmtMs(avantFin));
        Ligne("etendue apres          : " + FmtMs(debutApres) + " -> " + FmtMs(finApres));
        Ligne("somme des durees avant : " + FmtMs(sommeAvant));
        Ligne("somme des durees apres : " + FmtMs(sommeApres)
              + (Math.Abs(sommeApres - sommeAvant) < 0.5 ? "   (identique : aucun audio perdu)" : "   <<< DIFFERENT >>>"));
        Ligne("trous avant            : " + FmtMs(trous));
        Ligne("trous apres            : " + FmtMs(trousApres));
        Ligne("fondus reposes         : " + nbFondus);
        Ligne("erreurs                : " + echecs.Count);
        foreach (string s in echecs) Ligne("  " + s);
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        Ligne("");

        EcrireRapport();

        MessageBox.Show("Fusion terminee.\n\n"
            + "Piste                : \"" + piste.Name + "\"\n"
            + "Evenements colles    : " + finale.Count + "\n"
            + "Etendue avant        : " + FmtMs(avantFin - avantDebut) + "\n"
            + "Etendue apres        : " + FmtMs(finApres - debutApres) + "\n"
            + "Somme des durees     : " + FmtMs(sommeApres)
            + (Math.Abs(sommeApres - sommeAvant) < 0.5 ? "  (aucun audio perdu)" : "  ATTENTION : different de l'avant") + "\n"
            + "Trous restants       : " + FmtMs(trousApres) + "\n"
            + "Fondus reposes       : " + nbFondus + "\n"
            + (echecs.Count > 0 ? "\nATTENTION : " + echecs.Count + " erreur(s) - voir le rapport.\n" : "")
            + "\nRapport :\n" + cheminRapport,
            "FusionnerVoixCollee", MessageBoxButtons.OK,
            echecs.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }
}
