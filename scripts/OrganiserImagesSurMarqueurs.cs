// =============================================================================
//  OrganiserImagesSurMarqueurs.cs   --   VEGAS Pro 20   (script principal)
// =============================================================================
//  A QUOI CA SERT (le workflow du monteur) :
//
//    1. tu poses TOUTES tes images sur la timeline (piste video), dans l'ordre
//    2. tu ECOUTES la voix off et tu appuies sur M a chaque CHANGEMENT D'IMAGE
//    3. tu lances ce script : Outils > Scripts > OrganiserImagesSurMarqueurs
//    4. le script REORGANISE les images DEJA presentes (il ne cree rien) :
//          evenement image 1  ->  du marqueur 1 au marqueur 2
//          evenement image 2  ->  du marqueur 2 au marqueur 3
//          ...
//          evenement image N  ->  du marqueur N au marqueur N+1
//       chaque image est ETIREE ou RETRECIE pour coller EXACTEMENT aux
//       marqueurs (sa duree change).
//    5. un rapport texte est ecrit (voir DOSSIER_RAPPORTS)
//
//  DIFFERENCE AVEC LES SCRIPTS VOISINS (ne pas confondre) :
//    - AlignerImagesSurMarqueurs : prend des FICHIERS d'un dossier et CREE
//      les evenements sur la timeline.
//    - CouperATousLesMarqueurs   : COUPE les evenements aux marqueurs.
//    - OrganiserImagesSurMarqueurs (ce script) : ne cree RIEN et ne coupe RIEN,
//      il REPOSITIONNE et REDIMENSIONNE les evenements video deja poses.
//
//  ENTREES :
//    - les marqueurs du projet (il en faut au moins 2), tries par position
//    - la piste video qui contient les images : celle qui a le PLUS
//      d'evenements ; s'il y a plusieurs pistes video remplies, une boite de
//      dialogue propose la liste (la plus fournie est pre-selectionnee).
//
//  CAS LIMITES GERES (tous signales dans le rapport) :
//    - plus d'evenements que d'intervalles : les M-1 premiers sont places, les
//      autres NE SONT PAS DEPLACES (ils restent ou ils sont) et sont listes
//    - plus d'intervalles que d'evenements : les intervalles restants restent
//      vides (leur nombre exact est signale)
//    - intervalle de duree nulle ou negative (2 marqueurs au meme endroit) :
//      ignore et signale
//    - evenement sans media (aucun take) : ignore et signale
//
//  CONVENTIONS API (verifiees sur VEGAS Pro 20, build 411) :
//    new Timecode(x)  ->  x en MILLISECONDES  (new Timecode(4000).ToMilliseconds() = 4000)
//    Timecode.Nanos   ->  N'EST PAS des nanosecondes mais des ticks de 100 ns
//                         (new Timecode(4000).Nanos = 40000000)
//                         donc millisecondes = Nanos / 10000.0
//    TrackEvent.Start / .Length / .End sont affectables (Timecode)
//    piste.AddVideoEvent(new Timecode(debutMs), new Timecode(dureeMs))
//    une image fixe peut etre etiree bien au-dela de la duree par defaut
//    (mesure : evenement image de 10000 ms obtenu sans probleme)
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
    //  PARAMETRES (modifiables par le monteur)
    // =========================================================================

    // Dossier des rapports texte.
    static readonly string DOSSIER_RAPPORTS =
        CUTLAB_RAPPORTS;

#if MODETEST
    // Dossier des images utilise pour construire le projet de TEST.
    static readonly string DOSSIER_IMAGES =
        CUTLAB_RAPPORTS;
#endif

    // =========================================================================
    //  ETAT / OUTILS INTERNES
    // =========================================================================

    static StringBuilder log = new StringBuilder();
    static string cheminRapport = "";

    static void Ligne(string s) { log.AppendLine(s); }

    // --- temps ---------------------------------------------------------------
    //  PIEGE API VERIFIE DANS VEGAS PRO 20 (mesure reelle) :
    //    new Timecode(4000.0).ToMilliseconds() = 4000
    //    new Timecode(4000.0).Nanos            = 40000000
    //  Timecode.Nanos n'est donc PAS en nanosecondes : ce sont des ticks de
    //  100 ns (1 ms = 10000). Conversion juste : ms = Nanos / 10000.0
    const long UNITES_PAR_MS = 10000;

    static double MsDeNanos(long unites) { return unites / (double)UNITES_PAR_MS; }
    static double MsDe(Timecode tc) { return tc.Nanos / (double)UNITES_PAR_MS; }

    static string FmtMs(double ms)
    {
        if (ms < 0) ms = 0;
        long t = (long)Math.Round(ms);
        long h = t / 3600000; t %= 3600000;
        long m = t / 60000; t %= 60000;
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00.000}", h, m, t / 1000.0);
    }
    static string FmtTC(Timecode tc) { return FmtMs(MsDe(tc)); }

    // --- ecriture du rapport -------------------------------------------------
    static void EcrireRapport()
    {
        try
        {
            if (!Directory.Exists(DOSSIER_RAPPORTS)) Directory.CreateDirectory(DOSSIER_RAPPORTS);
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, "rapport_organisation.txt");
            string texte = log.ToString();
            File.WriteAllText(cheminRapport, texte, new UTF8Encoding(true));
            // copie horodatee (historique)
            string horo = Path.Combine(DOSSIER_RAPPORTS,
                "rapport_organisation_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(horo, texte, new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            try
            {
                cheminRapport = Path.Combine(Path.GetTempPath(), "rapport_organisation.txt");
                File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
            }
            catch { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }
        }
    }

    // --- structures ----------------------------------------------------------
    class Intervalle
    {
        public int Index;        // index du marqueur de debut (0-based)
        public Timecode Debut;
        public Timecode Fin;
        public double DureeMs { get { return MsDeNanos(Fin.Nanos - Debut.Nanos); } }
    }

    class Resultat
    {
        public int Marqueurs;
        public int IntervallesBruts;
        public int IntervallesUtilisables;
        public int IntervallesIgnores;
        public int IntervallesVides;
        public int Evenements;
        public int EvenementsAvecMedia;
        public int EvenementsSansMedia;
        public int Reorganises;
        public int EnTrop;
        public int Echecs;
        public double Min, Max, Moy;
        public List<string> Top10 = new List<string>();
        public List<string> NonDeplaces = new List<string>();
        public List<string> SansMedia = new List<string>();
        public List<string> Ignores = new List<string>();
        public string Resume = "";
        public bool Erreur = false;
    }

    // =========================================================================
    //  LECTURES
    // =========================================================================
    static List<Marker> LireMarqueurs(Project project)
    {
        List<Marker> res = new List<Marker>();
        foreach (Marker m in project.Markers) res.Add(m);
        res.Sort(delegate(Marker a, Marker b) { return a.Position.Nanos.CompareTo(b.Position.Nanos); });
        return res;
    }

    static List<TrackEvent> LireEvenements(VideoTrack piste)
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
            if (t == null) return "(SANS MEDIA)";
            if (t.Media == null) return "(SANS MEDIA)";
            string p = t.MediaPath;
            if (p == null || p.Length == 0) return "(media sans chemin)";
            return Path.GetFileName(p);
        }
        catch { return "(media illisible)"; }
    }

    static bool ASansMedia(TrackEvent ev)
    {
        try { return ev.ActiveTake == null || ev.ActiveTake.Media == null; }
        catch { return true; }
    }

    // =========================================================================
    //  PISTE CIBLE
    //   - une seule piste video avec des evenements -> elle
    //   - plusieurs -> celle qui a le PLUS d'evenements ; si le dialogue est
    //     autorise, l'utilisateur choisit dans une liste
    // =========================================================================
    static VideoTrack ChoisirPiste(Project project, bool autoriserDialogue, out string explication)
    {
        List<VideoTrack> candidats = new List<VideoTrack>();
        List<int> comptes = new List<int>();

        foreach (Track t in project.Tracks)
        {
            if (!t.IsVideo()) continue;
            VideoTrack vt = (VideoTrack)t;
            int n = 0;
            foreach (TrackEvent ev in vt.Events) n++;
            if (n > 0) { candidats.Add(vt); comptes.Add(n); }
        }

        Ligne("");
        Ligne("--- PISTES VIDEO CONTENANT DES EVENEMENTS : " + candidats.Count + " ---");
        for (int i = 0; i < candidats.Count; i++)
            Ligne("  - \"" + candidats[i].Name + "\" : " + comptes[i] + " evenement(s)"
                  + (i == 0 ? "   <-- la plus fournie" : ""));

        // tri par nombre d'evenements decroissant (la plus fournie en tete)
        for (int i = 0; i < candidats.Count; i++)
            for (int j = i + 1; j < candidats.Count; j++)
                if (comptes[j] > comptes[i])
                {
                    VideoTrack tv = candidats[i]; candidats[i] = candidats[j]; candidats[j] = tv;
                    int tc = comptes[i]; comptes[i] = comptes[j]; comptes[j] = tc;
                }

        if (candidats.Count == 0)
        {
            explication = "AUCUNE piste video ne contient d'evenement.";
            return null;
        }
        if (candidats.Count == 1)
        {
            explication = "une seule piste video contient des evenements : \"" + candidats[0].Name
                        + "\" (" + comptes[0] + " evenements)";
            return candidats[0];
        }

        if (!autoriserDialogue)
        {
            explication = "choix automatique : piste la plus fournie \"" + candidats[0].Name
                        + "\" (" + comptes[0] + " evenements sur " + candidats.Count + " pistes remplies)";
            return candidats[0];
        }

        // boite de dialogue : liste des pistes video
        using (Form f = new Form())
        {
            f.Text = "OrganiserImagesSurMarqueurs - piste cible";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.Width = 540;
            f.Height = 340;

            Label l = new Label();
            l.Text = "Plusieurs pistes video contiennent des evenements.\n"
                   + "Choisis celle qui contient les IMAGES a reorganiser :";
            l.Left = 12; l.Top = 12; l.Width = 490; l.Height = 36;
            f.Controls.Add(l);

            ListBox lb = new ListBox();
            lb.Left = 12; lb.Top = 52; lb.Width = 490; lb.Height = 190;
            for (int i = 0; i < candidats.Count; i++)
                lb.Items.Add("\"" + candidats[i].Name + "\"   --   " + comptes[i] + " evenement(s)"
                             + (i == 0 ? "   (la plus fournie)" : ""));
            lb.SelectedIndex = 0;
            f.Controls.Add(lb);

            Button ok = new Button();
            ok.Text = "OK"; ok.DialogResult = DialogResult.OK;
            ok.Left = 330; ok.Top = 254; ok.Width = 80;
            f.Controls.Add(ok);

            Button ko = new Button();
            ko.Text = "Annuler"; ko.DialogResult = DialogResult.Cancel;
            ko.Left = 422; ko.Top = 254; ko.Width = 80;
            f.Controls.Add(ko);

            f.AcceptButton = ok;
            f.CancelButton = ko;

            DialogResult dr = f.ShowDialog();
            int sel = lb.SelectedIndex;
            if (dr != DialogResult.OK || sel < 0)
            {
                explication = "choix annule par l'utilisateur : repli sur la piste la plus fournie \""
                            + candidats[0].Name + "\" (" + comptes[0] + " evenements)";
                return candidats[0];
            }
            explication = "piste choisie dans la liste : \"" + candidats[sel].Name
                        + "\" (" + comptes[sel] + " evenements)";
            return candidats[sel];
        }
    }

    // =========================================================================
    //  PLACEMENT D'UN EVENEMENT :  [debut ; fin]
    //  ordre : Start, puis Length, puis End (repli), puis etirement du media.
    // =========================================================================
    static bool PlacerEvenement(TrackEvent ev, Timecode debut, Timecode fin, out string note)
    {
        note = "";
        Timecode longueur = Timecode.FromNanos(fin.Nanos - debut.Nanos);

        try { ev.Start = debut; }
        catch (Exception ex) { note += " [Start: " + ex.Message + "]"; }

        try { ev.Length = longueur; }
        catch (Exception ex) { note += " [Length: " + ex.Message + "]"; }

        if (ev.End.Nanos != fin.Nanos)
        {
            try { ev.End = fin; }
            catch (Exception ex) { note += " [End: " + ex.Message + "]"; }
        }

        // dernier recours : etirer le media (image fixe) si la longueur est
        // limitee par la duree du media, puis refaire Length / End
        if (ev.End.Nanos != fin.Nanos)
        {
            try
            {
                Take t = ev.ActiveTake;
                if (t != null && t.Media != null)
                {
                    long besoin = t.Offset.Nanos + longueur.Nanos;
                    if (t.Media.Length.Nanos < besoin)
                    {
                        t.Media.Length = Timecode.FromNanos(besoin);
                        note += " [media etire a " + FmtMs(MsDeNanos(besoin)) + "]";
                    }
                    try { ev.Length = longueur; } catch { }
                    if (ev.End.Nanos != fin.Nanos) { try { ev.End = fin; } catch { } }
                }
            }
            catch (Exception ex) { note += " [media: " + ex.Message + "]"; }
        }

        if (ev.Start.Nanos != debut.Nanos)
        {
            try { ev.Start = debut; } catch { }
        }

        return (ev.Start.Nanos == debut.Nanos && ev.End.Nanos == fin.Nanos);
    }

    // =========================================================================
    //  COEUR DU SCRIPT
    // =========================================================================
    static Resultat Organiser(Project project, VideoTrack piste, string explicationPiste)
    {
        Resultat r = new Resultat();

        Ligne("");
        Ligne("--- PISTE CIBLE ---");
        Ligne("piste retenue : \"" + piste.Name + "\"  (index " + piste.Index + ")");
        Ligne("pourquoi      : " + explicationPiste);

        // ---- 1) marqueurs --------------------------------------------------
        List<Marker> marqueurs = LireMarqueurs(project);
        r.Marqueurs = marqueurs.Count;
        Ligne("");
        Ligne("--- MARQUEURS DU PROJET : " + marqueurs.Count + " (tries par position) ---");
        for (int i = 0; i < marqueurs.Count; i++)
            Ligne(string.Format(CultureInfo.InvariantCulture, "  marqueur {0,3} : {1}   \"{2}\"",
                i + 1, FmtTC(marqueurs[i].Position), marqueurs[i].Label));

        // ---- 2) evenements de la piste cible -------------------------------
        List<TrackEvent> evenements = LireEvenements(piste);
        r.Evenements = evenements.Count;
        Ligne("");
        Ligne("--- EVENEMENTS DE LA PISTE (ordre actuel = ordre des images) : " + evenements.Count + " ---");
        for (int i = 0; i < evenements.Count; i++)
        {
            TrackEvent ev = evenements[i];
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  evenement {0,3} : {1} -> {2}  duree {3}   [{4}]",
                i + 1, FmtTC(ev.Start), FmtTC(ev.End), FmtMs(MsDe(ev.Length)), SourceDe(ev)));
        }

        // ---- 3) controles de base ------------------------------------------
        if (marqueurs.Count < 2)
        {
            Ligne("");
            Ligne("ARRET : il faut AU MOINS 2 marqueurs (il y en a " + marqueurs.Count + ").");
            Ligne("Pendant l'ecoute de la voix off, appuie sur M a chaque changement d'image.");
            r.Erreur = true;
            r.Resume = "ARRET : il faut au moins 2 marqueurs.\n\n"
                     + "Marqueurs trouves : " + marqueurs.Count + "\n\n"
                     + "Appuie sur M a chaque changement d'image, puis relance le script.";
            return r;
        }
        if (evenements.Count == 0)
        {
            Ligne("");
            Ligne("ARRET : la piste \"" + piste.Name + "\" ne contient aucun evenement.");
            r.Erreur = true;
            r.Resume = "ARRET : aucun evenement sur la piste \"" + piste.Name + "\".\n\n"
                     + "Pose d'abord tes images sur cette piste, puis relance le script.";
            return r;
        }

        // ---- 4) intervalles entre marqueurs --------------------------------
        List<Intervalle> intervalles = new List<Intervalle>();
        for (int i = 0; i < marqueurs.Count - 1; i++)
        {
            long d = marqueurs[i + 1].Position.Nanos - marqueurs[i].Position.Nanos;
            if (d <= 0)
            {
                r.Ignores.Add(string.Format(CultureInfo.InvariantCulture,
                    "intervalle {0} (marqueur {0} a {1} et marqueur {2} a {3}) : duree {4} -> IGNORE",
                    i + 1, FmtTC(marqueurs[i].Position), i + 2, FmtTC(marqueurs[i + 1].Position), FmtMs(MsDeNanos(d))));
                continue;
            }
            Intervalle it = new Intervalle();
            it.Index = i;
            it.Debut = marqueurs[i].Position;
            it.Fin = marqueurs[i + 1].Position;
            intervalles.Add(it);
        }
        r.IntervallesBruts = marqueurs.Count - 1;
        r.IntervallesUtilisables = intervalles.Count;
        r.IntervallesIgnores = r.Ignores.Count;

        Ligne("");
        Ligne("--- INTERVALLES ENTRE MARQUEURS ---");
        Ligne("intervalles bruts      : " + r.IntervallesBruts);
        Ligne("intervalles utilisables: " + r.IntervallesUtilisables);
        for (int k = 0; k < intervalles.Count; k++)
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  intervalle {0,3} : {1} -> {2}   duree {3}",
                k + 1, FmtTC(intervalles[k].Debut), FmtTC(intervalles[k].Fin), FmtMs(intervalles[k].DureeMs)));
        if (r.Ignores.Count > 0)
        {
            Ligne("INTERVALLES IGNORES (duree nulle ou negative) : " + r.Ignores.Count);
            foreach (string s in r.Ignores) Ligne("  " + s);
        }

        // ---- 5) separation des evenements avec / sans media ----------------
        List<TrackEvent> avecMedia = new List<TrackEvent>();
        foreach (TrackEvent ev in evenements)
        {
            if (ASansMedia(ev))
                r.SansMedia.Add(string.Format(CultureInfo.InvariantCulture,
                    "evenement a {0} (duree {1}) : AUCUN MEDIA -> non deplace",
                    FmtTC(ev.Start), FmtMs(MsDe(ev.Length))));
            else
                avecMedia.Add(ev);
        }
        r.EvenementsAvecMedia = avecMedia.Count;
        r.EvenementsSansMedia = r.SansMedia.Count;

        Ligne("");
        Ligne("--- REPARTITION ---");
        Ligne("evenements avec media : " + r.EvenementsAvecMedia);
        Ligne("evenements sans media : " + r.EvenementsSansMedia);
        if (r.SansMedia.Count > 0)
            foreach (string s in r.SansMedia) Ligne("  " + s);

        // ---- 6) placement ---------------------------------------------------
        int n = Math.Min(avecMedia.Count, intervalles.Count);
        List<double> durees = new List<double>();

        Ligne("");
        Ligne("--- PLACEMENT (evenement i -> intervalle i) ---");
        for (int k = 0; k < n; k++)
        {
            TrackEvent ev = avecMedia[k];
            Intervalle it = intervalles[k];
            string source = SourceDe(ev);

            string note;
            bool ok = PlacerEvenement(ev, it.Debut, it.Fin, out note);

            double debutReel = MsDe(ev.Start);
            double finReelle = MsDe(ev.End);
            double dureeReelle = MsDe(ev.Length);
            durees.Add(dureeReelle);

            if (ok) r.Reorganises++; else r.Echecs++;

            string ligne = string.Format(CultureInfo.InvariantCulture,
                "  image {0,3} : {1} -> {2}   duree {3}   [{4}]   (cible {5} -> {6}, duree {7}){8}{9}",
                k + 1, FmtMs(debutReel), FmtMs(finReelle), FmtMs(dureeReelle), source,
                FmtTC(it.Debut), FmtTC(it.Fin), FmtMs(it.DureeMs),
                ok ? "" : "   <<< NON PLACE EXACTEMENT >>>",
                note);

            Ligne(ligne);
            if (r.Top10.Count < 10)
                r.Top10.Add(string.Format(CultureInfo.InvariantCulture,
                    "  {0,3}. {1} -> {2}   duree {3}   [{4}]",
                    k + 1, FmtMs(debutReel), FmtMs(finReelle), FmtMs(dureeReelle), source));
        }

        // ---- 7) cas limites : trop d'evenements / intervalles vides --------
        r.EnTrop = avecMedia.Count - n;
        r.IntervallesVides = intervalles.Count - n;

        Ligne("");
        Ligne("--- CAS LIMITES ---");
        if (r.EnTrop > 0)
        {
            Ligne("PLUS D'EVENEMENTS QUE D'INTERVALLES : " + r.EnTrop
                  + " evenement(s) NON DEPLACE(S) (laisses a leur place) :");
            for (int k = n; k < avecMedia.Count; k++)
            {
                string s = string.Format(CultureInfo.InvariantCulture,
                    "  image {0,3} : {1} -> {2}   duree {3}   [{4}]",
                    k + 1, FmtMs(MsDe(avecMedia[k].Start)), FmtMs(MsDe(avecMedia[k].End)),
                    FmtMs(MsDe(avecMedia[k].Length)), SourceDe(avecMedia[k]));
                Ligne(s);
                r.NonDeplaces.Add(s);
            }
        }
        else Ligne("plus d'evenements que d'intervalles : non (" + avecMedia.Count + " evenements / " + intervalles.Count + " intervalles)");

        if (r.IntervallesVides > 0)
            Ligne("PLUS D'INTERVALLES QUE D'EVENEMENTS : " + r.IntervallesVides
                  + " intervalle(s) reste(nt) VIDE(S) (aucune image)");
        else Ligne("plus d'intervalles que d'evenements : non");

        if (r.Ignores.Count > 0)
            Ligne("intervalles ignores (duree nulle/negative) : " + r.Ignores.Count);
        else Ligne("intervalles ignores (duree nulle/negative) : aucun");

        if (r.SansMedia.Count > 0)
            Ligne("evenements sans media ignores : " + r.SansMedia.Count);
        else Ligne("evenements sans media ignores : aucun");

        if (r.Echecs > 0) Ligne("placements NON exacts : " + r.Echecs);
        else Ligne("placements non exacts : 0");

        // ---- 8) statistiques de duree (valeurs RELUES dans le projet) ------
        if (durees.Count > 0)
        {
            r.Min = durees[0]; r.Max = durees[0];
            double tot = 0;
            foreach (double d in durees) { if (d < r.Min) r.Min = d; if (d > r.Max) r.Max = d; tot += d; }
            r.Moy = tot / durees.Count;
        }

        Ligne("");
        Ligne("--- DUREES DES IMAGES REORGANISEES (relues dans le projet) ---");
        Ligne("nombre    : " + durees.Count);
        Ligne("duree mini: " + FmtMs(r.Min));
        Ligne("duree maxi: " + FmtMs(r.Max));
        Ligne("duree moy : " + FmtMs(r.Moy));
        Ligne("10 premieres (debut -> fin, duree) :");
        foreach (string s in r.Top10) Ligne(s);
        if (durees.Count == 0) Ligne("  (aucune)");

        // ---- 9) relecture finale depuis le projet (preuve) -----------------
        Ligne("");
        Ligne("--- RELECTURE FINALE DANS LE PROJET VEGAS : piste \"" + piste.Name + "\" ---");
        List<TrackEvent> apres = LireEvenements(piste);
        for (int i = 0; i < apres.Count; i++)
        {
            TrackEvent ev = apres[i];
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  debut {0,10:0.000} ms | duree {1,10:0.000} ms | fin {2,10:0.000} ms | [{3}]",
                ev.Start.ToMilliseconds(), ev.Length.ToMilliseconds(), ev.End.ToMilliseconds(), SourceDe(ev)));
        }

        // ---- 10) resume ----------------------------------------------------
        Ligne("");
        Ligne("--- RESUME ---");
        Ligne("marqueurs lus           : " + r.Marqueurs);
        Ligne("intervalles bruts       : " + r.IntervallesBruts);
        Ligne("intervalles utilisables : " + r.IntervallesUtilisables);
        Ligne("intervalles ignores     : " + r.IntervallesIgnores);
        Ligne("evenements trouves      : " + r.Evenements);
        Ligne("evenements avec media   : " + r.EvenementsAvecMedia);
        Ligne("evenements sans media   : " + r.EvenementsSansMedia);
        Ligne("images REORGANISEES     : " + r.Reorganises);
        Ligne("evenements en trop      : " + r.EnTrop);
        Ligne("intervalles restes vides: " + r.IntervallesVides);
        Ligne("placements non exacts   : " + r.Echecs);
        Ligne("duree mini              : " + FmtMs(r.Min));
        Ligne("duree maxi              : " + FmtMs(r.Max));
        Ligne("duree moyenne           : " + FmtMs(r.Moy));
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        Ligne("");

        r.Resume = r.Reorganises + " image(s) reorganisee(s) sur " + r.Marqueurs + " marqueur(s)"
                 + "  -  durees mini/maxi/moyenne : " + FmtMs(r.Min) + " / " + FmtMs(r.Max) + " / " + FmtMs(r.Moy) + "\n\n"
                 + "Piste ciblee            : \"" + piste.Name + "\"\n"
                 + "Evenements trouves      : " + r.Evenements + "\n"
                 + "Evenements avec media   : " + r.EvenementsAvecMedia + "\n"
                 + "Intervalles utilisables : " + r.IntervallesUtilisables + "\n"
                 + (r.IntervallesIgnores > 0 ? "Intervalles ignores     : " + r.IntervallesIgnores + "\n" : "")
                 + (r.EvenementsSansMedia > 0 ? "Evenements sans media   : " + r.EvenementsSansMedia + "\n" : "")
                 + (r.EnTrop > 0 ? "Evenements NON deplaces : " + r.EnTrop + "\n" : "")
                 + (r.IntervallesVides > 0 ? "Intervalles restes vides: " + r.IntervallesVides + "\n" : "")
                 + (r.Echecs > 0 ? "Placements non exacts   : " + r.Echecs + "\n" : "")
                 + "\nrapport : " + cheminRapport;

        return r;
    }

    // =========================================================================
    //  POINT D'ENTREE VEGAS
    // =========================================================================
    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();

        Ligne("RAPPORT D'ORGANISATION DES IMAGES SUR LES MARQUEURS");
        Ligne("====================================================");
        Ligne("Date   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script : OrganiserImagesSurMarqueurs.cs  (VEGAS Pro 20)");
        Ligne("Projet : " + (project.IsUntitled ? "(sans titre)" : project.FilePath));
        Ligne("But    : repositionner/redimensionner les evenements video DEJA poses");
        Ligne("         pour que chacun occupe l'intervalle entre 2 marqueurs.");

#if MODETEST
        ModeTest(project, vegas);
#else
        ModeNormal(project);
#endif
    }

    // =========================================================================
    //  MODE NORMAL
    // =========================================================================
    static void ModeNormal(Project project)
    {
        string explication;
        VideoTrack piste = ChoisirPiste(project, true, out explication);

        if (piste == null)
        {
            Ligne("");
            Ligne("ARRET : " + explication);
            EcrireRapport();
            MessageBox.Show("Aucune piste video ne contient d'evenement.\n\n"
                + "Pose d'abord tes images sur la timeline, place tes marqueurs (touche M), "
                + "puis relance le script.\n\nRapport :\n" + cheminRapport,
                "OrganiserImagesSurMarqueurs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Resultat r = Organiser(project, piste, explication);
        EcrireRapport();

        MessageBox.Show(r.Resume, "OrganiserImagesSurMarqueurs", MessageBoxButtons.OK,
            r.Erreur ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    // =========================================================================
    //  MODE TEST  (compile avec /define:MODETEST)
    //  Construit lui-meme des evenements + marqueurs, reorganise, puis RELIT
    //  les positions depuis le projet et les ecrit dans le rapport.
    //  Aucune boite de dialogue : le script rend la main et ferme Vegas.
    // =========================================================================
#if MODETEST

    static Dictionary<string, Media> poolTest = new Dictionary<string, Media>(StringComparer.OrdinalIgnoreCase);

    static bool EstImage(string f)
    {
        string e = Path.GetExtension(f).ToLowerInvariant();
        return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp"
            || e == ".gif" || e == ".tif" || e == ".tiff" || e == ".webp";
    }

    static string[] ListerImagesTest()
    {
        List<string> res = new List<string>();
        if (Directory.Exists(DOSSIER_IMAGES))
            foreach (string f in Directory.GetFiles(DOSSIER_IMAGES)) if (EstImage(f)) res.Add(f);
        res.Sort();
        return res.ToArray();
    }

    static Media MediaTest(Project project, string fichier)
    {
        Media m;
        if (!poolTest.TryGetValue(fichier, out m))
        {
            m = project.MediaPool.AddMedia(fichier);
            poolTest[fichier] = m;
        }
        return m;
    }

    // Cree une piste de test : nbEvenements evenements empiles a partir de
    // departMs (comme le monteur qui pose toutes ses images au debut, dans
    // l'ordre). sansMediaIndex = index (0-based) de l'evenement volontairement
    // laisse SANS media (-1 = aucun).
    static VideoTrack CreerPisteTest(Project project, string nom, int nbEvenements,
                                     double departMs, double pasMs, double dureeMs, int sansMediaIndex)
    {
        VideoTrack vt = project.AddVideoTrack();
        vt.Name = nom;
        string[] images = ListerImagesTest();
        for (int i = 0; i < nbEvenements; i++)
        {
            VideoEvent ve = vt.AddVideoEvent(new Timecode(departMs + i * pasMs), new Timecode(dureeMs));
            if (i != sansMediaIndex && images.Length > 0)
            {
                Media m = MediaTest(project, images[i % images.Length]);
                ve.AddTake(m.GetVideoStreamByIndex(0));
            }
        }
        return vt;
    }

    static void AjouterMarqueurs(Project project, double[] positionsMs)
    {
        project.Markers.Clear();
        foreach (double d in positionsMs)
            project.Markers.Add(new Marker(new Timecode(d),
                "M" + d.ToString(CultureInfo.InvariantCulture) + "ms"));
    }

    static void TracerPiste(string titre, VideoTrack piste)
    {
        Ligne("");
        Ligne("=== " + titre + " : piste \"" + piste.Name + "\" ===");
        List<TrackEvent> evs = LireEvenements(piste);
        for (int i = 0; i < evs.Count; i++)
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  debut {0,10:0.000} ms | duree {1,10:0.000} ms | fin {2,10:0.000} ms | [{3}]",
                evs[i].Start.ToMilliseconds(), evs[i].Length.ToMilliseconds(),
                evs[i].End.ToMilliseconds(), SourceDe(evs[i])));
    }

    static void ModeTest(Project project, Vegas vegas)
    {
        Ligne("");
        Ligne("################ MODE TEST (/define:MODETEST) ################");
        Ligne("Projet de test : AUCUNE modification reelle, tout est construit ici.");
        Ligne("Images de test : " + DOSSIER_IMAGES + " (" + ListerImagesTest().Length + " fichier(s))");

        // ---------------------------------------------------------------------
        //  PASSE 1 : cas NORMAL - 5 marqueurs (4 intervalles) / 4 images
        //            + choix AUTOMATIQUE de la piste (la plus fournie)
        // ---------------------------------------------------------------------
        Ligne("");
        Ligne("############################################################");
        Ligne("##### PASSE 1 : cas normal - 5 marqueurs, 4 images ##########");
        Ligne("############################################################");
        VideoTrack p1 = CreerPisteTest(project, "TEST_ORGA_1", 4, 0, 1000, 800, -1);
        AjouterMarqueurs(project, new double[] { 0, 4000, 9500, 11000, 21000 });
        TracerPiste("AVANT (passe 1)", p1);

        string expl1;
        VideoTrack cible1 = ChoisirPiste(project, false, out expl1);
        Ligne("choix automatique de la piste -> \"" + cible1.Name + "\"");
        Organiser(project, cible1, expl1);
        TracerPiste("APRES (passe 1)", cible1);

        // ---------------------------------------------------------------------
        //  PASSE 2 : PLUS D'EVENEMENTS QUE D'INTERVALLES
        //            3 marqueurs (2 intervalles) / 4 images
        // ---------------------------------------------------------------------
        Ligne("");
        Ligne("##################################################################");
        Ligne("##### PASSE 2 : PLUS d'evenements (4) que d'intervalles (2) #####");
        Ligne("##################################################################");
        VideoTrack p2 = CreerPisteTest(project, "TEST_ORGA_2", 4, 0, 1000, 800, -1);
        AjouterMarqueurs(project, new double[] { 1000, 5000, 9000 });
        TracerPiste("AVANT (passe 2)", p2);
        Organiser(project, p2, "piste de test passee explicitement");
        TracerPiste("APRES (passe 2)", p2);

        // ---------------------------------------------------------------------
        //  PASSE 3 : PLUS D'INTERVALLES QUE D'EVENEMENTS
        //            6 marqueurs (5 intervalles) / 3 images
        // ---------------------------------------------------------------------
        Ligne("");
        Ligne("################################################################");
        Ligne("##### PASSE 3 : PLUS d'intervalles (5) que d'evenements (3) #####");
        Ligne("################################################################");
        VideoTrack p3 = CreerPisteTest(project, "TEST_ORGA_3", 3, 500, 700, 600, -1);
        AjouterMarqueurs(project, new double[] { 0, 2000, 4000, 6000, 8000, 10000 });
        TracerPiste("AVANT (passe 3)", p3);
        Organiser(project, p3, "piste de test passee explicitement");
        TracerPiste("APRES (passe 3)", p3);

        // ---------------------------------------------------------------------
        //  PASSE 4 : INTERVALLE NUL + EVENEMENT SANS MEDIA
        //            marqueurs 0 / 2500 / 2500 / 6000  -> 1 intervalle ignore
        //            3 images dont 1 sans media
        // ---------------------------------------------------------------------
        Ligne("");
        Ligne("##################################################################");
        Ligne("##### PASSE 4 : intervalle de duree NULLE + evenement SANS MEDIA ##");
        Ligne("##################################################################");
        VideoTrack p4 = CreerPisteTest(project, "TEST_ORGA_4", 3, 0, 900, 700, 1);
        AjouterMarqueurs(project, new double[] { 0, 2500, 2500, 6000 });
        TracerPiste("AVANT (passe 4)", p4);
        Organiser(project, p4, "piste de test passee explicitement");
        TracerPiste("APRES (passe 4)", p4);

        // ---------------------------------------------------------------------
        //  PASSE 5 : MOINS DE 2 MARQUEURS -> arret propre, rien de deplace
        // ---------------------------------------------------------------------
        Ligne("");
        Ligne("#############################################################");
        Ligne("##### PASSE 5 : un seul marqueur -> ARRET sans rien faire ###");
        Ligne("#############################################################");
        VideoTrack p5 = CreerPisteTest(project, "TEST_ORGA_5", 2, 0, 1000, 800, -1);
        AjouterMarqueurs(project, new double[] { 3000 });
        TracerPiste("AVANT (passe 5)", p5);
        Organiser(project, p5, "piste de test passee explicitement");
        TracerPiste("APRES (passe 5)", p5);

        Ligne("");
        Ligne("################ FIN DES TESTS ################");
        EcrireRapport();

        // fermeture de Vegas (une seule session de test a ete ouverte)
        try
        {
            Ligne("Fermeture de Vegas demandee (vegas.Exit).");
            vegas.Exit();
        }
        catch (Exception ex)
        {
            Ligne("vegas.Exit indisponible : " + ex.Message);
        }
    }
#endif
}
