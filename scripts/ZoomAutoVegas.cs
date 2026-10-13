// =============================================================================
//  ZoomAutoVegas.cs   --   VEGAS Pro 20
// =============================================================================
//  A QUOI CA SERT :
//    ajoute un ZOOM LENT (1,00 -> 1,06) sur CHAQUE evenement video selectionne,
//    avec 2 images cles Pan/Crop (mouvement video) :
//        image cle 1  au DEBUT  -> zoom 1,00
//        image cle 2  a la FIN  -> zoom 1,06
//    C'est l'animation de base : tu selectionnes tes images, tu lances le
//    script, et ca vit tout seul.
//
//  PARAMETRES (en tete de fichier) :
//    ZOOM_DEBUT / ZOOM_FIN : 1.00 -> 1.06 par defaut
//    IGNORER_SI_DEJA_ANIME  : true = ne touche pas un evenement qui a deja
//                             des images cles (aucune destruction)
//
//  PIEGE API (resolu dans ce script) :
//    il faut AJOUTER l'image cle a la collection AVANT de regler ses bornes,
//    sinon Vegas renvoie "Image cle de mouvement de video non valide".
//    Le script fait donc : new VideoMotionKeyframe(...) -> Keyframes.Add(...)
//    -> puis seulement reglage du zoom.
//
//  UTILISATION : selectionne tes evenements video puis
//                Outils > Scripts > ZoomAutoVegas
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

    // ------------------------- PARAMETRES -----------------------------------
    const double ZOOM_DEBUT = 1.00;
    const double ZOOM_FIN = 1.06;
    const bool IGNORER_SI_DEJA_ANIME = true;
    // ------------------------------------------------------------------------

    static readonly string DOSSIER_RAPPORTS =
        CUTLAB_RAPPORTS;

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
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, "rapport_zoom.txt");
            File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex) { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }
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

    // --- bornes : lecture / comparaison / zoom --------------------------------
    static string FmtB(VideoMotionBounds b)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture,
                "TL({0:0.####},{1:0.####}) TR({2:0.####},{3:0.####}) BR({4:0.####},{5:0.####}) BL({6:0.####},{7:0.####})",
                b.TopLeft.X, b.TopLeft.Y, b.TopRight.X, b.TopRight.Y,
                b.BottomRight.X, b.BottomRight.Y, b.BottomLeft.X, b.BottomLeft.Y);
        }
        catch (Exception ex) { return "(bornes illisibles : " + ex.Message + ")"; }
    }

    static bool MemesBornes(VideoMotionBounds a, VideoMotionBounds b)
    {
        float[] xa = { a.TopLeft.X, a.TopRight.X, a.BottomRight.X, a.BottomLeft.X };
        float[] ya = { a.TopLeft.Y, a.TopRight.Y, a.BottomRight.Y, a.BottomLeft.Y };
        float[] xb = { b.TopLeft.X, b.TopRight.X, b.BottomRight.X, b.BottomLeft.X };
        float[] yb = { b.TopLeft.Y, b.TopRight.Y, b.BottomRight.Y, b.BottomLeft.Y };
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(xa[i] - xb[i]) > 0.0005f) return false;
            if (Math.Abs(ya[i] - yb[i]) > 0.0005f) return false;
        }
        return true;
    }

    static bool BornesDegeneres(VideoMotionBounds b)
    {
        return Math.Abs(b.TopLeft.X - b.TopRight.X) < 0.000001f
            || Math.Abs(b.TopLeft.Y - b.BottomLeft.Y) < 0.000001f;
    }

    // zoom par calcul de bornes : le rectangle VISIBLE est retreci -> l'image grossit
    static VideoMotionBounds Zoomer(VideoMotionBounds b, double facteur)
    {
        float cx = (b.TopLeft.X + b.TopRight.X + b.BottomRight.X + b.BottomLeft.X) / 4f;
        float cy = (b.TopLeft.Y + b.TopRight.Y + b.BottomRight.Y + b.BottomLeft.Y) / 4f;
        float q = (float)(1.0 / facteur);
        return new VideoMotionBounds(
            cx + (b.TopLeft.X - cx) * q, cy + (b.TopLeft.Y - cy) * q,
            cx + (b.TopRight.X - cx) * q, cy + (b.TopRight.Y - cy) * q,
            cx + (b.BottomRight.X - cx) * q, cy + (b.BottomRight.Y - cy) * q,
            cx + (b.BottomLeft.X - cx) * q, cy + (b.BottomLeft.Y - cy) * q);
    }

    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();
        Ligne("RAPPORT DE ZOOM AUTOMATIQUE (images cles Pan/Crop)");
        Ligne("=================================================");
        Ligne("Date   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script : ZoomAutoVegas.cs  (VEGAS Pro 20)");
        Ligne("ZOOM   : " + ZOOM_DEBUT.ToString("0.00", CultureInfo.InvariantCulture)
              + "  ->  " + ZOOM_FIN.ToString("0.00", CultureInfo.InvariantCulture));
        Ligne("");

        // ---- selection -------------------------------------------------------
        List<TrackEvent> sel = new List<TrackEvent>();
        foreach (Track t in project.Tracks)
            foreach (TrackEvent ev in t.Events)
                if (ev.IsVideo() && ev.Selected) sel.Add(ev);
        sel.Sort(delegate(TrackEvent a, TrackEvent b) { return a.Start.Nanos.CompareTo(b.Start.Nanos); });

        Ligne("evenements video selectionnes : " + sel.Count);
        if (sel.Count == 0)
        {
            Ligne("ARRET : aucune selection.");
            EcrireRapport();
            MessageBox.Show("Aucun evenement video selectionne.\n\n"
                + "Selectionne tes images dans Vegas puis relance le script.",
                "ZoomAutoVegas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Ligne("");

        int faits = 0, ignores = 0, echecs = 0, zoomsExplicites = 0;
        Ligne("--- DETAIL ---");

        foreach (TrackEvent ev in sel)
        {
            VideoEvent ve = ev as VideoEvent;
            if (ve == null)
            {
                echecs++;
                Ligne("  \"" + Nom(ev) + "\" : pas un evenement video -> ignore");
                continue;
            }

            try
            {
                VideoMotion vm = ve.VideoMotion;
                VideoMotionKeyframes kfs = vm.Keyframes;

                if (kfs.Count > 0 && IGNORER_SI_DEJA_ANIME)
                {
                    ignores++;
                    Ligne("  \"" + Nom(ev) + "\" : deja " + kfs.Count + " image(s) cle -> IGNORE (rien casse)");
                    continue;
                }

                // ---- image cle 1 : debut, zoom ZOOM_DEBUT -------------------
                VideoMotionKeyframe k1 = new VideoMotionKeyframe(project, ve.Start);
                kfs.Add(k1);                      // AJOUTER AVANT de regler les bornes
                string b1 = FmtB(k1.Bounds);

                // ---- image cle 2 : fin, zoom ZOOM_FIN ----------------------
                VideoMotionKeyframe k2 = new VideoMotionKeyframe(project, ve.End);
                kfs.Add(k2);                      // AJOUTER AVANT de regler les bornes

                string methode = "ScaleBy()";
                try { k2.Smoothness = 0f; } catch { }

                // a) tentative par ScaleBy (API officielle)
                try
                {
                    float d = (float)(ZOOM_FIN - 1.0);
                    k2.ScaleBy(new VideoMotionVertex(d, d));
                }
                catch (Exception ex)
                {
                    Ligne("    ScaleBy a echoue (" + ex.Message + ") -> calcul direct des bornes");
                    methode = "bornes calculees (ScaleBy en echec)";
                }

                // b) verification : si les bornes n'ont pas bouge, on calcule
                bool bouge = false;
                try { bouge = !MemesBornes(k1.Bounds, k2.Bounds); } catch { }
                if (!bouge)
                {
                    try
                    {
                        VideoMotionBounds ref1 = k1.Bounds;
                        if (!BornesDegeneres(ref1))
                        {
                            k2.Bounds = Zoomer(ref1, ZOOM_FIN);
                            methode = "bornes calculees (zoom " + ZOOM_FIN.ToString("0.00", CultureInfo.InvariantCulture) + "x)";
                            zoomsExplicites++;
                        }
                        else
                        {
                            methode = "ECHEC : bornes de reference invalides";
                        }
                    }
                    catch (Exception ex)
                    {
                        methode = "ECHEC : " + ex.Message;
                    }
                }

                // c) zoom de depart sur l'image cle 1 si ZOOM_DEBUT != 1
                if (Math.Abs(ZOOM_DEBUT - 1.0) > 0.0001)
                {
                    try
                    {
                        VideoMotionBounds ref1 = k1.Bounds;
                        if (!BornesDegeneres(ref1)) k1.Bounds = Zoomer(ref1, ZOOM_DEBUT);
                    }
                    catch { }
                }

                string b2 = FmtB(k2.Bounds);
                bool ok = !MemesBornes(k1.Bounds, k2.Bounds);

                if (ok) faits++; else echecs++;

                Ligne("  \"" + Nom(ev) + "\"  " + FmtTC(ve.Start) + " -> " + FmtTC(ve.End)
                      + "  |  " + methode + (ok ? "  [OK]" : "  [ANIMATION NON DETECTEE]"));
                Ligne("      image cle 1 (" + FmtTC(k1.Position) + ") : " + b1);
                Ligne("      image cle 2 (" + FmtTC(k2.Position) + ") : " + b2);
            }
            catch (Exception ex)
            {
                echecs++;
                Ligne("  \"" + Nom(ev) + "\" : ERREUR -> " + ex.Message);
            }
        }

        Ligne("");
        Ligne("--- RESUME ---");
        Ligne("evenements selectionnes : " + sel.Count);
        Ligne("zooms poses             : " + faits);
        Ligne("ignores (deja animes)   : " + ignores);
        Ligne("zooms par calcul direct : " + zoomsExplicites);
        Ligne("echecs                  : " + echecs);
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        EcrireRapport();

        MessageBox.Show(
            "Zoom automatique termine.\n\n"
            + "Selection        : " + sel.Count + " evenement(s)\n"
            + "Zooms poses      : " + faits + "\n"
            + "Ignores (deja animes) : " + ignores + "\n"
            + "Echecs           : " + echecs + "\n"
            + "\nRapport (avec les bornes des images cles) :\n" + cheminRapport,
            "ZoomAutoVegas", MessageBoxButtons.OK,
            faits > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }
}
