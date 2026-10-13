// =============================================================================
//  FonduAuto.cs   --   VEGAS Pro 20
// =============================================================================
//  A QUOI CA SERT :
//    pose un FONDU ENCHAINEE (transition) entre TOUS les evenements video
//    consecutifs d'une piste. C'est le script a lancer juste apres avoir
//    aligne les images sur la voix off, pour avoir de belles transitions.
//
//  PARAMETRES (en tete de fichier) :
//    DUREE_FONDU_MS    : duree du fondu (200 ms par defaut)
//    TYPE_TRANSITION   : nom de la transition Vegas a utiliser
//                        ("Cross Fade" = fondu enchainee standard)
//    CREER_LE_CHEVAUCHEMENT : true  = superpose les 2 evenements de la duree
//                                      du fondu -> vrai fondu enchainee
//                             false = simple fondu au noir puis retour
//    PISTES_SELECTIONNEES_SEULEMENT : true = ne traite que la selection
//
//  REMARQUE IMPORTANTE :
//    pour un VRAI fondu enchainee, les deux images doivent se chevaucher.
//    Avec CREER_LE_CHEVAUCHEMENT = true, le script recule le debut de
//    l'evenement suivant de DUREE_FONDU_MS et l'allonge d'autant : la fin de
//    l'evenement (donc la suite du montage) ne bouge pas.
//    La transition Vegas demandee est ensuite posee sur le fondu ; si Vegas
//    refuse (flux non compatible), le fondu simple est conserve et c'est
//    indique dans le rapport.
//
//  UTILISATION : Outils > Scripts > FonduAuto
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
    const double DUREE_FONDU_MS = 200.0;
    const string TYPE_TRANSITION = "Cross Fade";
    const bool CREER_LE_CHEVAUCHEMENT = true;
    static readonly bool PISTES_SELECTIONNEES_SEULEMENT = false;
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
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, "rapport_fondu_auto.txt");
            File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex) { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }
    }

    // recherche d'une transition Vegas par son nom, dans tout l'arbre
    static PlugInNode ChercherTransition(PlugInNode racine, string nom, bool exact)
    {
        if (racine == null || nom == null || nom.Length == 0) return null;
        try
        {
            if (!racine.IsContainer) return null;
            for (int i = 0; i < racine.Count; i++)
            {
                PlugInNode c = racine.GetChild(i);
                if (c == null) continue;
                string n = c.Name == null ? "" : c.Name;
                if (exact ? string.Equals(n, nom, StringComparison.OrdinalIgnoreCase)
                          : n.IndexOf(nom, StringComparison.OrdinalIgnoreCase) >= 0)
                    return c;
                PlugInNode r = ChercherTransition(c, nom, exact);
                if (r != null) return r;
            }
        }
        catch { }
        return null;
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

    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();
        Ligne("RAPPORT DE FONDU AUTOMATIQUE");
        Ligne("============================");
        Ligne("Date   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script : FonduAuto.cs  (VEGAS Pro 20)");
        Ligne("");
        Ligne("DUREE_FONDU_MS          : " + DUREE_FONDU_MS.ToString(CultureInfo.InvariantCulture) + " ms");
        Ligne("TYPE_TRANSITION         : \"" + TYPE_TRANSITION + "\"");
        Ligne("CREER_LE_CHEVAUCHEMENT  : " + CREER_LE_CHEVAUCHEMENT);
        Ligne("");

        // ---- transition Vegas demandee -------------------------------------
        PlugInNode transition = ChercherTransition(vegas.Transitions, TYPE_TRANSITION, true);
        if (transition == null) transition = ChercherTransition(vegas.Transitions, TYPE_TRANSITION, false);
        Ligne("transition trouvee dans Vegas : "
            + (transition == null ? "AUCUNE" : "\"" + transition.Name + "\""));
        if (transition == null)
            Ligne("  -> le script posera seulement les fondus (durcissement/dissolve simple).");
        Ligne("");

        // ---- pistes cibles --------------------------------------------------
        List<Track> cibles = new List<Track>();
        foreach (Track t in project.Tracks)
            if (t.IsVideo() && (!PISTES_SELECTIONNEES_SEULEMENT || t.Selected)) cibles.Add(t);

        Ligne("--- PISTES TRAITEES (" + cibles.Count + ") ---");
        foreach (Track t in cibles) Ligne("  - " + t.Name);
        Ligne("");

        long dureeNanos = (long)Math.Round(DUREE_FONDU_MS * 10000.0);
        int nbFondu = 0, nbChevauchement = 0, nbTransition = 0, nbEchecTrans = 0, nbTropCourt = 0;

        foreach (Track t in cibles)
        {
            List<TrackEvent> evs = new List<TrackEvent>();
            foreach (TrackEvent ev in t.Events) if (ev.IsVideo()) evs.Add(ev);
            evs.Sort(delegate(TrackEvent a, TrackEvent b) { return a.Start.Nanos.CompareTo(b.Start.Nanos); });

            Ligne("--- piste \"" + t.Name + "\" : " + evs.Count + " evenement(s) ---");

            for (int i = 0; i + 1 < evs.Count; i++)
            {
                TrackEvent a = evs[i];
                TrackEvent b = evs[i + 1];

                if (a.Length.Nanos < dureeNanos || b.Length.Nanos < dureeNanos)
                {
                    nbTropCourt++;
                    Ligne("  ignore : \"" + Nom(a) + "\" / \"" + Nom(b) + "\" (evenement plus court que le fondu)");
                    continue;
                }

                long ecart = b.Start.Nanos - a.End.Nanos;   // >0 = trou, 0 = colle, <0 = deja superpose

                // 1) chevauchement
                if (CREER_LE_CHEVAUCHEMENT && ecart >= 0)
                {
                    try
                    {
                        long nouveauDebut = b.Start.Nanos - dureeNanos;
                        long nouvelleDuree = b.Length.Nanos + dureeNanos;
                        b.Start = Timecode.FromNanos(nouveauDebut);
                        b.Length = Timecode.FromNanos(nouvelleDuree);
                        nbChevauchement++;
                    }
                    catch (Exception ex)
                    {
                        Ligne("  chevauchement impossible sur \"" + Nom(b) + "\" : " + ex.Message);
                    }
                }

                // 2) fondus
                try
                {
                    a.FadeOut.Length = new Timecode(DUREE_FONDU_MS);
                    b.FadeIn.Length = new Timecode(DUREE_FONDU_MS);
                    nbFondu++;
                }
                catch (Exception ex)
                {
                    Ligne("  fondu impossible entre \"" + Nom(a) + "\" et \"" + Nom(b) + "\" : " + ex.Message);
                    continue;
                }

                // 3) transition Vegas (facultatif, non bloquant)
                if (transition != null)
                {
                    try
                    {
                        b.FadeIn.Transition = transition;
                        nbTransition++;
                    }
                    catch (Exception ex)
                    {
                        nbEchecTrans++;
                        if (nbEchecTrans <= 3)
                            Ligne("  transition Vegas refusee (" + Nom(b) + ") : " + ex.Message);
                    }
                }

                Ligne("  fondu " + FmtMs(DUREE_FONDU_MS) + " entre \"" + Nom(a) + "\" ("
                      + FmtTC(a.Start) + " -> " + FmtTC(a.End) + ") et \"" + Nom(b) + "\" ("
                      + FmtTC(b.Start) + " -> " + FmtTC(b.End) + ")"
                      + (ecart > 0 ? "   [trou de " + FmtMs(ecart / 10000.0) + " avant]" : ""));
            }
            Ligne("");
        }

        Ligne("--- RESUME ---");
        Ligne("pistes traitees          : " + cibles.Count);
        Ligne("fondus poses             : " + nbFondu);
        Ligne("chevauchements crees     : " + nbChevauchement);
        Ligne("transitions Vegas posees : " + nbTransition);
        Ligne("transitions refusees     : " + nbEchecTrans);
        Ligne("paires ignorees (trop courtes) : " + nbTropCourt);
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        EcrireRapport();

        MessageBox.Show(
            "Fondu automatique termine.\n\n"
            + "Pistes traitees   : " + cibles.Count + "\n"
            + "Fondus poses      : " + nbFondu + "  (" + FmtMs(DUREE_FONDU_MS) + ")\n"
            + "Chevauchements    : " + nbChevauchement + "\n"
            + "Transitions Vegas : " + nbTransition
            + (nbEchecTrans > 0 ? "  (" + nbEchecTrans + " refusee(s))" : "") + "\n"
            + (transition == null ? "\nATTENTION : transition \"" + TYPE_TRANSITION + "\" introuvable dans Vegas.\n" : "")
            + "\nRapport :\n" + cheminRapport,
            "FonduAuto", MessageBoxButtons.OK,
            nbFondu > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }
}
