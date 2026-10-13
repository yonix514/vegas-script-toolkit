// =============================================================================
//  RapportTimeline.cs   --   VEGAS Pro 20
// =============================================================================
//  A QUOI CA SERT :
//    ecrit un RAPPORT TEXTE de la timeline, pour verifier un montage AVANT
//    l'export :
//      - la liste des pistes (nom, type)
//      - pour chaque evenement : nom, debut, fin, duree, effets, fondus
//      - la liste des TROUS (les endroits ou il n'y a rien)
//      - le total de marqueurs et la longueur du projet
//
//  Le rapport est ecrit dans le dossier "rapports" et son chemin est affiche
//  dans une boite de dialogue.
//
//  UTILISATION : Outils > Scripts > RapportTimeline
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

    const string NOM_FICHIER = "rapport_timeline.txt";

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

    static string Effets(Effects effets)
    {
        string res = "";
        try
        {
            foreach (Effect e in effets)
            {
                string n = "";
                try { n = e.PlugIn != null ? e.PlugIn.Name : e.Description; } catch { }
                if (n == null || n.Length == 0) n = e.Description;
                if (e.Bypass) n += " (desactive)";
                if (res.Length > 0) res += ", ";
                res += n;
            }
        }
        catch { }
        return res.Length == 0 ? "-" : res;
    }

    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();
        Ligne("RAPPORT DE TIMELINE");
        Ligne("===================");
        Ligne("Date        : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script      : RapportTimeline.cs  (VEGAS Pro 20)");
        Ligne("Projet      : " + (project.IsUntitled ? "(sans titre)" : project.FilePath));
        Ligne("Duree projet: " + FmtTC(project.Length));
        Ligne("Pistes      : " + project.Tracks.Count);
        Ligne("Marqueurs   : " + project.Markers.Count);
        Ligne("Regions     : " + project.Regions.Count);
        Ligne("");

        // ---- marqueurs ------------------------------------------------------
        if (project.Markers.Count > 0)
        {
            Ligne("--- MARQUEURS ---");
            List<Marker> marqueurs = new List<Marker>();
            foreach (Marker m in project.Markers) marqueurs.Add(m);
            marqueurs.Sort(delegate(Marker a, Marker b) { return a.Position.Nanos.CompareTo(b.Position.Nanos); });
            for (int i = 0; i < marqueurs.Count; i++)
                Ligne(string.Format(CultureInfo.InvariantCulture, "  {0,4} : {1}   \"{2}\"",
                    i + 1, FmtTC(marqueurs[i].Position), marqueurs[i].Label));
            Ligne("");
        }

        // ---- pistes / evenements -------------------------------------------
        int totalEvents = 0, totalTrous = 0;
        double trousTotalMs = 0;
        double fps = project.Video.FrameRate;
        if (fps <= 0) fps = 30.0;
        // duree d'une image en unites de Timecode.Nanos (1 ms = 10000 unites)
        long uneImage = (long)Math.Round(10000000.0 / fps);

        List<string> lignesTrous = new List<string>();

        foreach (Track t in project.Tracks)
        {
            List<TrackEvent> evs = new List<TrackEvent>();
            foreach (TrackEvent ev in t.Events) evs.Add(ev);
            evs.Sort(delegate(TrackEvent a, TrackEvent b) { return a.Start.Nanos.CompareTo(b.Start.Nanos); });

            Ligne("--- PISTE : \"" + t.Name + "\"  (" + (t.IsVideo() ? "VIDEO" : "AUDIO")
                  + ", index " + t.Index + ", " + evs.Count + " evenement(s)) ---");
            string fxPiste = Effets(t.Effects);
            if (fxPiste != "-") Ligne("  effets de piste : " + fxPiste);

            int i = 0;
            foreach (TrackEvent ev in evs)
            {
                i++;
                totalEvents++;
                string ligne = string.Format(CultureInfo.InvariantCulture,
                    "  {0,4}. {1} | debut {2} | fin {3} | duree {4}",
                    i, Nom(ev), FmtTC(ev.Start), FmtTC(ev.End), FmtMs(ev.Length.Nanos / 10000.0));
                try
                {
                    if (ev.PlaybackRate != 1.0)
                        ligne += " | vitesse " + ev.PlaybackRate.ToString("0.###", CultureInfo.InvariantCulture) + "x";
                    if (ev.Takes.Count > 1) ligne += " | " + ev.Takes.Count + " prises";
                    double fi = ev.FadeIn.Length.Nanos / 10000.0;
                    double fo = ev.FadeOut.Length.Nanos / 10000.0;
                    if (fi > 0 || fo > 0)
                        ligne += " | fondu entree " + FmtMs(fi) + " / sortie " + FmtMs(fo);
                    // les effets d'evenement sont portes par VideoEvent
                    VideoEvent ve = ev as VideoEvent;
                    string fx = (ve != null) ? Effets(ve.Effects) : "-";
                    if (fx != "-") ligne += " | effets : " + fx;
                    if (ev.Mute) ligne += " | MUET";
                }
                catch { }
                Ligne(ligne);
            }

            // trous
            for (int k = 0; k + 1 < evs.Count; k++)
            {
                long trou = evs[k + 1].Start.Nanos - evs[k].End.Nanos;
                if (trou > uneImage)
                {
                    totalTrous++;
                    trousTotalMs += trou / 10000.0;
                    lignesTrous.Add("  piste \"" + t.Name + "\" : trou de " + FmtMs(trou / 10000.0)
                        + " entre " + FmtTC(evs[k].End) + " et " + FmtTC(evs[k + 1].Start)
                        + "   [" + Nom(evs[k]) + " -> " + Nom(evs[k + 1]) + "]");
                }
            }
            if (evs.Count > 0)
            {
                long debut = evs[0].Start.Nanos;
                if (debut > uneImage)
                {
                    totalTrous++;
                    trousTotalMs += debut / 10000.0;
                    lignesTrous.Add("  piste \"" + t.Name + "\" : trou AU DEBUT de " + FmtMs(debut / 10000.0)
                        + " (avant " + FmtTC(evs[0].Start) + ")");
                }
            }
            Ligne("");
        }

        // ---- trous ----------------------------------------------------------
        Ligne("--- TROUS (" + totalTrous + ") ---");
        foreach (string s in lignesTrous) Ligne(s);
        if (totalTrous == 0) Ligne("  aucun trou detecte");
        Ligne("");

        Ligne("--- RESUME ---");
        Ligne("pistes              : " + project.Tracks.Count);
        Ligne("evenements          : " + totalEvents);
        Ligne("trous               : " + totalTrous + "  (total " + FmtMs(trousTotalMs) + ")");
        Ligne("marqueurs           : " + project.Markers.Count);
        Ligne("duree du projet     : " + FmtTC(project.Length));
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        Ligne("");

        try
        {
            if (!Directory.Exists(DOSSIER_RAPPORTS)) Directory.CreateDirectory(DOSSIER_RAPPORTS);
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, NOM_FICHIER);
            File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
            string horo = Path.Combine(DOSSIER_RAPPORTS,
                "rapport_timeline_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(horo, log.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex) { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }

        MessageBox.Show(
            "Rapport de timeline ecrit.\n\n"
            + "Pistes      : " + project.Tracks.Count + "\n"
            + "Evenements  : " + totalEvents + "\n"
            + "Trous       : " + totalTrous + "  (" + FmtMs(trousTotalMs) + ")\n"
            + "Marqueurs   : " + project.Markers.Count + "\n"
            + "Duree       : " + FmtTC(project.Length) + "\n"
            + "\nFichier :\n" + cheminRapport,
            "RapportTimeline", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
