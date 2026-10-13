// =============================================================================
//  CouperAuCurseur.cs   --   VEGAS Pro 20
// =============================================================================
//  A QUOI CA SERT :
//    la touche S de Vegas ne coupe que l'evenement sous le curseur de la piste
//    active. Ce script coupe l'evenement video sous le curseur EN DEUX sur
//    TOUTES LES PISTES SELECTIONNEES (et sur toutes les pistes video si
//    aucune piste n'est selectionnee).
//
//  OPTION : "couper aussi a tous les marqueurs de la selection"
//    -> si tu reponds OUI, le script coupe en plus chaque evenement concerne
//       a TOUS les marqueurs qu'il contient.
//
//  UTILISATION : Outils > Scripts > CouperAuCurseur
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

    // false = ne demande rien, coupe seulement au curseur
    // true  = demande aussi "couper a tous les marqueurs de la selection ?"
    const bool DEMANDER_OPTION_MARQUEURS = true;

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
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, "rapport_coupe_curseur.txt");
            File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex) { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }
    }

    // coupe "ev" a la position "position" ; retourne la partie DROITE (ou null)
    static TrackEvent CouperA(TrackEvent ev, Timecode position)
    {
        if (position.Nanos <= ev.Start.Nanos || position.Nanos >= ev.End.Nanos) return null;
        try
        {
            Timecode offset = Timecode.FromNanos(position.Nanos - ev.Start.Nanos);
            TrackEvent droite = ev.Split(offset);
            return droite;
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
        Ligne("RAPPORT DE COUPE AU CURSEUR");
        Ligne("===========================");
        Ligne("Date   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script : CouperAuCurseur.cs  (VEGAS Pro 20)");
        Ligne("");

        Timecode curseur = vegas.Cursor;
        Ligne("position du curseur : " + FmtTC(curseur));

        // ---- marqueurs ------------------------------------------------------
        List<Marker> marqueurs = new List<Marker>();
        foreach (Marker m in project.Markers) marqueurs.Add(m);
        marqueurs.Sort(delegate(Marker a, Marker b) { return a.Position.Nanos.CompareTo(b.Position.Nanos); });
        Ligne("marqueurs du projet  : " + marqueurs.Count);

        // ---- choix des pistes cibles ---------------------------------------
        List<Track> cibles = new List<Track>();
        foreach (Track t in project.Tracks)
            if (t.IsVideo() && t.Selected) cibles.Add(t);

        bool toutesPistes = false;
        if (cibles.Count == 0)
        {
            toutesPistes = true;
            foreach (Track t in project.Tracks) if (t.IsVideo()) cibles.Add(t);
        }
        Ligne("");
        Ligne("--- PISTES CIBLEES (" + cibles.Count + ") ---");
        Ligne(toutesPistes
            ? "aucune piste selectionnee -> TOUTES les pistes video"
            : "pistes selectionnees");
        foreach (Track t in cibles) Ligne("  - " + t.Name);

        if (cibles.Count == 0)
        {
            Ligne("ARRET : aucune piste video dans le projet.");
            EcrireRapport();
            MessageBox.Show("Aucune piste video dans le projet.", "CouperAuCurseur",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // ---- option : couper a tous les marqueurs --------------------------
        bool tousMarqueurs = false;
        if (DEMANDER_OPTION_MARQUEURS && marqueurs.Count > 0)
        {
            DialogResult r = MessageBox.Show(
                "Couper au curseur (" + FmtTC(curseur) + ") sur " + cibles.Count + " piste(s).\n\n"
                + "Veux-tu AUSSI couper chaque evenement concerne a TOUS les marqueurs "
                + "qu'il contient ?\n(" + marqueurs.Count + " marqueur(s) dans le projet)\n\n"
                + "OUI = coupe au curseur + a tous les marqueurs\n"
                + "NON = coupe seulement au curseur",
                "CouperAuCurseur", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            tousMarqueurs = (r == DialogResult.Yes);
        }
        Ligne("");
        Ligne("option tous les marqueurs : " + (tousMarqueurs ? "OUI" : "NON"));

        // ---- travail --------------------------------------------------------
        Ligne("");
        Ligne("--- COUPES ---");
        int nbCurseur = 0, nbMarqueurs = 0, evCurseur = 0;

        foreach (Track t in cibles)
        {
            List<TrackEvent> evenements = new List<TrackEvent>();
            foreach (TrackEvent ev in t.Events) if (ev.IsVideo()) evenements.Add(ev);
            evenements.Sort(delegate(TrackEvent a, TrackEvent b) { return a.Start.Nanos.CompareTo(b.Start.Nanos); });

            Ligne("piste \"" + t.Name + "\" : " + evenements.Count + " evenement(s) video");

            // 1) coupe au curseur
            foreach (TrackEvent ev in evenements)
            {
                if (curseur.Nanos > ev.Start.Nanos && curseur.Nanos < ev.End.Nanos)
                {
                    TrackEvent droite = CouperA(ev, curseur);
                    if (droite != null)
                    {
                        nbCurseur++; evCurseur++;
                        Ligne("  coupe au curseur sur \"" + Nom(ev) + "\" a " + FmtTC(curseur));
                    }
                }
            }

            // 2) coupe a tous les marqueurs
            if (tousMarqueurs)
            {
                foreach (TrackEvent ev in evenements)
                {
                    Timecode debut = ev.Start, fin = ev.End;
                    TrackEvent courant = ev;
                    foreach (Marker m in marqueurs)
                    {
                        if (m.Position.Nanos <= debut.Nanos || m.Position.Nanos >= fin.Nanos) continue;
                        TrackEvent droite = CouperA(courant, m.Position);
                        if (droite != null)
                        {
                            nbMarqueurs++;
                            Ligne("  coupe au marqueur " + FmtTC(m.Position) + " sur \"" + Nom(ev) + "\"");
                            courant = droite;
                        }
                    }
                }
            }
        }

        Ligne("");
        Ligne("--- RESUME ---");
        Ligne("coupes au curseur    : " + nbCurseur);
        Ligne("coupes aux marqueurs : " + nbMarqueurs);
        Ligne("total des coupes     : " + (nbCurseur + nbMarqueurs));
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        EcrireRapport();

        MessageBox.Show(
            "Coupe terminee.\n\n"
            + "Curseur            : " + FmtTC(curseur) + "\n"
            + "Pistes traitees    : " + cibles.Count + "\n"
            + "Coupes au curseur  : " + nbCurseur + "\n"
            + (tousMarqueurs ? "Coupes aux marqueurs : " + nbMarqueurs + "\n" : "")
            + "\nRapport :\n" + cheminRapport,
            "CouperAuCurseur", MessageBoxButtons.OK,
            (nbCurseur + nbMarqueurs) > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    static string Nom(TrackEvent ev)
    {
        try
        {
            if (ev.ActiveTake != null && ev.ActiveTake.MediaPath != null)
                return Path.GetFileName(ev.ActiveTake.MediaPath);
        }
        catch { }
        return string.IsNullOrEmpty(ev.Name) ? "(sans nom)" : ev.Name;
    }
}
