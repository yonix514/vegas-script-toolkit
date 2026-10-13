// =============================================================================
//  CouperATousLesMarqueurs.cs   --   VEGAS Pro 20
// =============================================================================
//  A QUOI CA SERT :
//    coupe TOUS les evenements video de la timeline a TOUS les marqueurs.
//    Tres utile quand tu as pose tes marqueurs a la main (touche M) pendant
//    l'ecoute de la voix off et que tu veux decouper le montage en une fois.
//
//  OPTIONS (en tete de fichier) :
//    PISTES_SELECTIONNEES_SEULEMENT = true  -> ne traite que les pistes
//                                     selectionnees (si aucune : toutes)
//    MARGE_MS : ignore les marqueurs trop proches d'un bord (0 = aucun)
//
//  UTILISATION : Outils > Scripts > CouperATousLesMarqueurs
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

    static readonly string DOSSIER_RAPPORTS =
        CUTLAB_RAPPORTS;

    static readonly bool PISTES_SELECTIONNEES_SEULEMENT = false;
    const double MARGE_MS = 0.0;   // ex. 1.0 -> ignore un marqueur a moins de 1 ms d'un bord

    static StringBuilder log = new StringBuilder();
    static string cheminRapport = "";
    static void Ligne(string s) { log.AppendLine(s); }

    static string FmtMs(double ms)
    {
        if (ms < 0) ms = 0;
        long t = (long)Math.Round(ms);
        long h = t / 3600000; t %= 3600000;
        long m = t / 60000; t %= 60000;
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00.000}", h, m, t / 1000.0);
    }
    static string FmtTC(Timecode tc) { return FmtMs(tc.Nanos / 10000.0); }

    static void EcrireRapport()
    {
        try
        {
            if (!Directory.Exists(DOSSIER_RAPPORTS)) Directory.CreateDirectory(DOSSIER_RAPPORTS);
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, "rapport_coupe_marqueurs.txt");
            File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex) { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }
    }

    static TrackEvent CouperA(TrackEvent ev, Timecode position)
    {
        if (position.Nanos <= ev.Start.Nanos + (long)(MARGE_MS * 10000.0)) return null;
        if (position.Nanos >= ev.End.Nanos - (long)(MARGE_MS * 10000.0)) return null;
        try
        {
            Timecode offset = Timecode.FromNanos(position.Nanos - ev.Start.Nanos);
            return ev.Split(offset);
        }
        catch (Exception ex)
        {
            Ligne("    !! echec de la coupe a " + FmtTC(position) + " : " + ex.Message);
            return null;
        }
    }

    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();
        Ligne("RAPPORT DE COUPE A TOUS LES MARQUEURS");
        Ligne("=====================================");
        Ligne("Date   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script : CouperATousLesMarqueurs.cs  (VEGAS Pro 20)");
        Ligne("");

        // ---- marqueurs ------------------------------------------------------
        List<Marker> marqueurs = new List<Marker>();
        foreach (Marker m in project.Markers) marqueurs.Add(m);
        marqueurs.Sort(delegate(Marker a, Marker b) { return a.Position.Nanos.CompareTo(b.Position.Nanos); });
        Ligne("marqueurs lus : " + marqueurs.Count);
        for (int i = 0; i < marqueurs.Count; i++)
            Ligne(string.Format(CultureInfo.InvariantCulture, "  marqueur {0,3} : {1}  \"{2}\"",
                i + 1, FmtTC(marqueurs[i].Position), marqueurs[i].Label));

        if (marqueurs.Count == 0)
        {
            Ligne("");
            Ligne("ARRET : aucun marqueur sur la timeline.");
            EcrireRapport();
            MessageBox.Show("Aucun marqueur sur la timeline.\n\n"
                + "Appuie sur M pendant la lecture de la voix off, puis relance le script.",
                "CouperATousLesMarqueurs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // ---- pistes cibles --------------------------------------------------
        List<Track> cibles = new List<Track>();
        foreach (Track t in project.Tracks)
            if (t.IsVideo() && (!PISTES_SELECTIONNEES_SEULEMENT || t.Selected)) cibles.Add(t);

        Ligne("");
        Ligne("--- PISTES TRAITEES (" + cibles.Count + ") ---");
        foreach (Track t in cibles) Ligne("  - " + t.Name);

        // ---- coupes ---------------------------------------------------------
        int total = 0, evCoupes = 0;
        Ligne("");
        Ligne("--- COUPES ---");
        foreach (Track t in cibles)
        {
            List<TrackEvent> evenements = new List<TrackEvent>();
            foreach (TrackEvent ev in t.Events) if (ev.IsVideo()) evenements.Add(ev);
            evenements.Sort(delegate(TrackEvent a, TrackEvent b) { return a.Start.Nanos.CompareTo(b.Start.Nanos); });

            int n = 0;
            foreach (TrackEvent ev in evenements)
            {
                Timecode debut = ev.Start, fin = ev.End;
                TrackEvent courant = ev;
                foreach (Marker m in marqueurs)
                {
                    if (m.Position.Nanos <= debut.Nanos || m.Position.Nanos >= fin.Nanos) continue;
                    TrackEvent droite = CouperA(courant, m.Position);
                    if (droite != null) { total++; n++; courant = droite; }
                }
            }
            evCoupes += n > 0 ? 1 : 0;
            Ligne("piste \"" + t.Name + "\" : " + evenements.Count + " evenement(s), " + n + " coupe(s)");
        }

        Ligne("");
        Ligne("--- RESUME ---");
        Ligne("marqueurs lus        : " + marqueurs.Count);
        Ligne("pistes traitees      : " + cibles.Count);
        Ligne("coupes effectuees    : " + total);
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        EcrireRapport();

        MessageBox.Show(
            "Coupe a tous les marqueurs terminee.\n\n"
            + "Marqueurs lus     : " + marqueurs.Count + "\n"
            + "Pistes traitees   : " + cibles.Count + "\n"
            + "Coupes effectuees : " + total + "\n"
            + "\nRapport :\n" + cheminRapport,
            "CouperATousLesMarqueurs", MessageBoxButtons.OK,
            total > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }
}
