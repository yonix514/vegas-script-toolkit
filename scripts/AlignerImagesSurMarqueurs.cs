// =============================================================================
//  AlignerImagesSurMarqueurs.cs   --   VEGAS Pro 20   (script principal)
// =============================================================================
//  A QUOI CA SERT (le workflow du monteur) :
//
//    1. tu ECOUTES la voix off dans Vegas
//    2. tu appuies sur la touche M a chaque CHANGEMENT D'IMAGE
//       (Vegas pose un marqueur a la position du curseur)
//    3. tu lances ce script :  Outils > Scripts > AlignerImagesSurMarqueurs
//    4. le script pose les images ENTRE LES MARQUEURS :
//          image 1  ->  du marqueur 1 au marqueur 2
//          image 2  ->  du marqueur 2 au marqueur 3
//          ...
//          image N  ->  du marqueur N au marqueur N+1
//       chaque image coupe EXACTEMENT sur les marqueurs
//    5. un rapport texte est ecrit (voir DOSSIER_RAPPORTS)
//
//  ENTREES :
//    - un dossier d'images : choisi par boite de dialogue,
//      OU fige dans la constante DOSSIER_IMAGES ci-dessous.
//    - les marqueurs presents sur la timeline (touche M).
//
//  REGLES :
//    - il faut AU MOINS 2 marqueurs
//    - les images sont prises dans l'ordre alphabetique NATUREL
//      (10 vient APRES 9 et AVANT 100)
//    - s'il y a PLUS d'images que d'intervalles : les premieres sont
//      posees, les autres sont listees dans le rapport ("non placees")
//    - s'il y a MOINS d'images que d'intervalles : la derniere image est
//      REUTILISEE sur les intervalles restants (signale dans le rapport)
//
//  CONVENTIONS API (verifiees sur VEGAS Pro 20, build 411) :
//    new Timecode(x)  ->  x en MILLISECONDES   (mesure : new Timecode(4000).ToMilliseconds() = 4000)
//    Timecode.Nanos   ->  N'EST PAS des nanosecondes mais des ticks de 100 ns
//                         (mesure : new Timecode(4000).Nanos = 40000000)
//                         donc millisecondes = Nanos / 10000.0
//    ((TrackEvent)ve).AddTake(stream)
//    project.Markers.Add(new Marker(new Timecode(ms), "texte"))
//    VideoTrack vt = project.AddVideoTrack();  vt.Name = "WEBTOON";
//    vt.AddVideoEvent(new Timecode(startMs), new Timecode(durMs));
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
    //  PARAMETRES  (modifiables par le monteur)
    // =========================================================================

    // Dossier des images. Laisser VIDE ("") pour que le script demande le
    // dossier par une boite de dialogue a chaque lancement.
#if MODETEST
    static readonly string DOSSIER_IMAGES = CUTLAB_RAPPORTS;
#else
    const string DOSSIER_IMAGES = @"";
#endif

    // Nom de la piste video qui recoit les images (creee si elle n'existe pas).
    const string NOM_PISTE = "WEBTOON";

    // true  = vide la piste AVANT de poser les images (repartir de zero)
    // false = ajoute les images a la suite de ce qui existe deja
    static readonly bool VIDER_LA_PISTE_AVANT = false;

    // Dossier des rapports texte.
    static readonly string DOSSIER_RAPPORTS =
        CUTLAB_RAPPORTS;

    // =========================================================================
    //  ETAT / OUTILS INTERNES
    // =========================================================================

    static StringBuilder log = new StringBuilder();
    static string cheminRapport = "";

    // --- ecriture du rapport -------------------------------------------------
    static void Ligne(string s) { log.AppendLine(s); }

    static void EcrireRapport()
    {
        try
        {
            string dossier = DOSSIER_RAPPORTS;
            if (!Directory.Exists(dossier)) Directory.CreateDirectory(dossier);
            cheminRapport = Path.Combine(dossier, "rapport_alignement.txt");
            string texte = log.ToString();
            File.WriteAllText(cheminRapport, texte, new UTF8Encoding(true));
            // copie horodatee (historique)
            string horo = Path.Combine(dossier,
                "rapport_alignement_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(horo, texte, new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            // dernier recours : dossier temporaire
            try
            {
                cheminRapport = Path.Combine(Path.GetTempPath(), "rapport_alignement.txt");
                File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
            }
            catch { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }
        }
    }

    // --- mise en forme -------------------------------------------------------
    //  PIEGE API VERIFIE DANS VEGAS PRO 20 (mesure reelle) :
    //    new Timecode(4000.0)        -> ToMilliseconds() = 4000   (le constructeur
    //                                   prend bien des MILLISECONDES)
    //    mais Timecode.Nanos         -> 40000000 pour 4000 ms
    //    Timecode.Nanos n'est donc PAS en nanosecondes : c'est un compteur de
    //    ticks de 100 ns (1 ms = 10000). La conversion juste est Nanos / 10000.0
    //    (Nanos / 1000000.0 donnerait un temps 1000 fois trop petit).
    const long UNITES_PAR_MS = 10000;
    static double MsDeNanos(long unites) { return unites / (double)UNITES_PAR_MS; }
    static long NanosDepuisMs(double ms) { return (long)Math.Round(ms * UNITES_PAR_MS); }

    static string FmtMs(double ms)
    {
        if (ms < 0) ms = 0;
        long t = (long)Math.Round(ms);
        long h = t / 3600000; t %= 3600000;
        long m = t / 60000; t %= 60000;
        double s = t / 1000.0;
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00.000}", h, m, s);
    }
    static string FmtTC(Timecode tc) { return FmtMs(tc.Nanos / 10000.0); }
    static double MsDe(Timecode tc) { return tc.Nanos / 10000.0; }

    // --- tri NATUREL des noms de fichiers (10 apres 9, avant 100) -----------
    static int ComparerNaturel(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int i0 = i, j0 = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                string na = a.Substring(i0, i - i0).TrimStart('0');
                string nb = b.Substring(j0, j - j0).TrimStart('0');
                if (na.Length != nb.Length) return na.Length - nb.Length;
                int c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                char ca = char.ToLowerInvariant(a[i]);
                char cb = char.ToLowerInvariant(b[j]);
                if (ca != cb) return ca.CompareTo(cb);
                i++; j++;
            }
        }
        return (a.Length - i) - (b.Length - j);
    }

    static bool EstImage(string f)
    {
        string e = Path.GetExtension(f).ToLowerInvariant();
        return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp"
            || e == ".gif" || e == ".tif" || e == ".tiff" || e == ".webp";
    }

    static List<string> ListerImages(string dossier)
    {
        List<string> res = new List<string>();
        if (dossier == null || dossier.Length == 0 || !Directory.Exists(dossier))
            return res;
        string[] fichiers = Directory.GetFiles(dossier);
        foreach (string f in fichiers) if (EstImage(f)) res.Add(f);
        res.Sort(delegate(string x, string y)
        {
            return ComparerNaturel(Path.GetFileName(x), Path.GetFileName(y));
        });
        return res;
    }

    // =========================================================================
    //  POINT D'ENTREE VEGAS
    // =========================================================================
    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();

        Ligne("RAPPORT D'ALIGNEMENT DES IMAGES SUR LES MARQUEURS");
        Ligne("==================================================");
        Ligne("Date          : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script        : AlignerImagesSurMarqueurs.cs  (VEGAS Pro 20)");
        Ligne("Projet        : " + (project.IsUntitled ? "(sans titre)" : project.FilePath));
        Ligne("");

#if MODETEST
        // ---------------------------------------------------------------------
        //  MODE TEST : 3 passes automatiques, aucune boite de dialogue avant
        //  l'ecriture du rapport. Compile avec /define:MODETEST.
        // ---------------------------------------------------------------------
        string dossier = DOSSIER_IMAGES;

        // passe 1 : 5 marqueurs, 6 images  -> PLUS d'images que d'intervalles
        ViderMarqueurs(project);
        AjouterMarqueursTest(project, new double[] { 0, 4000, 9500, 11000, 21000 });

        // passe 2 : 9 marqueurs, 6 images  -> MOINS d'images que d'intervalles
        List<double> m2 = new List<double>();
        double[] base2 = { 0, 4000, 9500, 11000, 21000 };
        foreach (double d in base2) m2.Add(d);
        m2.Add(22000); m2.Add(23000); m2.Add(24000); m2.Add(30000);

        Ligne("##### PASSE 1 : plus d'images que d'intervalles #####");
        Aligner(project, "WEBTOON", dossier, NOM_PISTE);
        JournalPiste(project, "WEBTOON", "PASSE 1");
        EcrireRapport();

        Ligne("");
        Ligne("##### PASSE 2 : moins d'images que d'intervalles (reutilisation) #####");
        ViderMarqueurs(project);
        foreach (double d in m2) AjouterMarqueur(project, d);
        Aligner(project, "WEBTOON_TEST2", dossier, "WEBTOON_TEST2");
        JournalPiste(project, "WEBTOON_TEST2", "PASSE 2");
        EcrireRapport();

        Ligne("");
        Ligne("##### PASSE 3 : un seul marqueur (cas d'erreur) #####");
        ViderMarqueurs(project);
        AjouterMarqueur(project, 3000);
        Aligner(project, "WEBTOON_TEST3", dossier, "WEBTOON_TEST3");
        JournalPiste(project, "WEBTOON_TEST3", "PASSE 3");
        EcrireRapport();

        Ligne("");
        Ligne("FIN DES TESTS.");
        EcrireRapport();
        MessageBox.Show(
            "MODE TEST termine.\n\nRapport : " + cheminRapport,
            "AlignerImagesSurMarqueurs - test",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
#else
        // ---------------------------------------------------------------------
        //  MODE NORMAL
        // ---------------------------------------------------------------------
        // 1) dossier d'images
        string dossier = DOSSIER_IMAGES;
        if (dossier == null || dossier.Length == 0 || !Directory.Exists(dossier))
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.Description = "Choisis le dossier qui contient les images du montage";
            fbd.ShowNewFolderButton = false;
            if (Directory.Exists(dossier)) fbd.SelectedPath = dossier;
            if (fbd.ShowDialog() != DialogResult.OK)
            {
                Ligne("Aucun dossier choisi : rien n'a ete fait.");
                EcrireRapport();
                MessageBox.Show("Aucun dossier choisi.\nRien n'a ete modifie.",
                    "AlignerImagesSurMarqueurs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            dossier = fbd.SelectedPath;
        }

        // 2) lecture des marqueurs + alignement + rapport
        string nomPiste = Aligner(project, NOM_PISTE, dossier, NOM_PISTE);
        EcrireRapport();

        // 3) message final (APRES le rapport, pour que le fichier existe toujours)
        string resume = DernierResume;
        MessageBox.Show(resume + "\n\nRapport complet :\n" + cheminRapport,
            "AlignerImagesSurMarqueurs", MessageBoxButtons.OK,
            DernierErreur ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
#endif
    }

    static string DernierResume = "";
    static bool DernierErreur = false;

#if MODETEST
    static void ViderMarqueurs(Project project) { project.Markers.Clear(); }
    static void AjouterMarqueur(Project project, double ms)
    {
        project.Markers.Add(new Marker(new Timecode(ms), "M" + (ms / 1000.0).ToString(CultureInfo.InvariantCulture) + "s"));
    }
    static void AjouterMarqueursTest(Project project, double[] ms)
    {
        foreach (double d in ms) AjouterMarqueur(project, d);
    }
    // relecture des evenements REELS du projet Vegas (verification independante
    // de la conversion Nanos : on interroge l'API avec ToMilliseconds())
    static void JournalPiste(Project project, string nom, string passe)
    {
        Ligne("");
        Ligne("--- RELECTURE DANS LE PROJET VEGAS (" + passe + ") : piste \"" + nom + "\" ---");
        foreach (Track t in project.Tracks)
        {
            if (!string.Equals(t.Name, nom, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (TrackEvent ev in t.Events)
            {
                string src = "?";
                try { if (ev.ActiveTake != null) src = Path.GetFileName(ev.ActiveTake.MediaPath); } catch { }
                Ligne(string.Format(CultureInfo.InvariantCulture,
                    "  debut {0,10:0.000} ms | duree {1,10:0.000} ms | fin {2,10:0.000} ms | [{3}]",
                    ev.Start.ToMilliseconds(), ev.Length.ToMilliseconds(), ev.End.ToMilliseconds(), src));
            }
        }
    }
#endif

    // =========================================================================
    //  LECTURE DES MARQUEURS DU PROJET
    // =========================================================================
    static List<Marker> LireMarqueurs(Project project)
    {
        List<Marker> res = new List<Marker>();
        foreach (Marker m in project.Markers) res.Add(m);
        res.Sort(delegate(Marker a, Marker b)
        {
            return a.Position.Nanos.CompareTo(b.Position.Nanos);
        });
        return res;
    }

    // =========================================================================
    //  PISTE CIBLE
    // =========================================================================
    static VideoTrack TrouverOuCreerPiste(Project project, string nom)
    {
        foreach (Track t in project.Tracks)
        {
            if (t.IsVideo() && string.Equals(t.Name, nom, StringComparison.OrdinalIgnoreCase))
                return (VideoTrack)t;
        }
        VideoTrack vt = project.AddVideoTrack();
        vt.Name = nom;
        return vt;
    }

    // =========================================================================
    //  COEUR DU SCRIPT : placer les images entre les marqueurs
    //  retourne le nom de la piste utilisee
    // =========================================================================
    static string Aligner(Project project, string nomPiste, string dossier, string etiquettePiste)
    {
        Ligne("--- PISTE CIBLE ---");
        Ligne("piste demandee : " + etiquettePiste);

        // ---- 1) marqueurs ---------------------------------------------------
        List<Marker> marqueurs = LireMarqueurs(project);
        Ligne("");
        Ligne("--- MARQUEURS LUS SUR LA TIMELINE : " + marqueurs.Count + " ---");
        for (int i = 0; i < marqueurs.Count; i++)
        {
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  marqueur {0,3} : {1}   \"{2}\"", i + 1, FmtTC(marqueurs[i].Position), marqueurs[i].Label));
        }

        // ---- 2) images ------------------------------------------------------
        List<string> images = ListerImages(dossier);
        Ligne("");
        Ligne("--- IMAGES ---");
        Ligne("dossier        : " + dossier);
        Ligne("images trouvees: " + images.Count);
        Ligne("ordre naturel  : " + (images.Count > 0 ? Path.GetFileName(images[0]) : "-")
              + " ... " + (images.Count > 0 ? Path.GetFileName(images[images.Count - 1]) : "-"));

        // ---- 3) verifications ----------------------------------------------
        if (marqueurs.Count < 2)
        {
            Ligne("");
            Ligne("ARRET : il faut AU MOINS 2 marqueurs (il y en a " + marqueurs.Count + ").");
            Ligne("Pendant la lecture de la voix off, appuie sur M a chaque changement d'image.");
            DernierErreur = true;
            DernierResume = "ARRET : il faut au moins 2 marqueurs.\n\n"
                + "Marqueurs trouves : " + marqueurs.Count + "\n\n"
                + "Appuie sur M a chaque changement d'image, puis relance le script.";
            return etiquettePiste;
        }
        if (images.Count == 0)
        {
            Ligne("");
            Ligne("ARRET : aucune image trouvee dans " + dossier);
            DernierErreur = true;
            DernierResume = "ARRET : aucune image (png, jpg, bmp...) dans :\n" + dossier;
            return etiquettePiste;
        }

        int nbIntervalles = marqueurs.Count - 1;   // intervalles "bruts"
        // on ignore les intervalles de duree nulle
        List<int> intervalles = new List<int>();
        for (int i = 0; i < nbIntervalles; i++)
        {
            if (marqueurs[i + 1].Position.Nanos > marqueurs[i].Position.Nanos)
                intervalles.Add(i);
        }
        Ligne("");
        Ligne("--- INTERVALLES ---");
        Ligne("intervalles entre marqueurs : " + nbIntervalles);
        Ligne("intervalles utilisables     : " + intervalles.Count
              + (nbIntervalles - intervalles.Count > 0
                 ? "   (" + (nbIntervalles - intervalles.Count) + " ignore(s) : duree nulle)" : ""));

        // ---- 4) affectation image <-> intervalle ---------------------------
        List<string> imageParIntervalle = new List<string>();
        List<string> nonPlacees = new List<string>();
        bool reutilisation = false;

        if (images.Count >= intervalles.Count)
        {
            // plus d'images que d'intervalles : on prend les premieres
            for (int k = 0; k < intervalles.Count; k++) imageParIntervalle.Add(images[k]);
            for (int k = intervalles.Count; k < images.Count; k++) nonPlacees.Add(images[k]);
        }
        else
        {
            // moins d'images que d'intervalles : on reutilise la derniere
            for (int k = 0; k < intervalles.Count; k++)
                imageParIntervalle.Add(images[Math.Min(k, images.Count - 1)]);
            reutilisation = intervalles.Count > images.Count;
        }

        Ligne("");
        Ligne("--- AFFECTATION ---");
        Ligne("images a placer : " + imageParIntervalle.Count);
        Ligne("images non placees : " + nonPlacees.Count);
        if (reutilisation)
            Ligne("REUTILISATION : la derniere image (" + Path.GetFileName(images[images.Count - 1])
                  + ") est reprise sur " + (intervalles.Count - images.Count) + " intervalle(s) de fin.");
        if (nonPlacees.Count > 0)
            Ligne("PLUS D'IMAGES QUE D'INTERVALLES : " + nonPlacees.Count + " image(s) non placee(s).");

        // ---- 5) piste video ------------------------------------------------
        VideoTrack piste = TrouverOuCreerPiste(project, nomPiste);
        Ligne("");
        Ligne("--- PISTE VIDEO ---");
        Ligne("piste utilisee : \"" + piste.Name + "\"  (index " + piste.Index + ")");

        if (VIDER_LA_PISTE_AVANT)
        {
            int avant = 0;
            List<TrackEvent> aSuppr = new List<TrackEvent>();
            foreach (TrackEvent ev in piste.Events) { aSuppr.Add(ev); avant++; }
            foreach (TrackEvent ev in aSuppr)
            {
                try { piste.Events.Remove(ev); } catch { }
            }
            Ligne("VIDER_LA_PISTE_AVANT = true : " + avant + " evenement(s) retire(s) de la piste.");
        }
        else
        {
            int nb = 0; foreach (TrackEvent ev in piste.Events) nb++;
            Ligne("evenements deja presents sur la piste : " + nb
                  + (nb > 0 ? "   (les images sont ajoutees en plus)" : ""));
        }
        Ligne("");

        // ---- 6) pose des images -------------------------------------------
        Ligne("--- PLACEMENT ---");
        Dictionary<string, Media> dejaVus = new Dictionary<string, Media>(StringComparer.OrdinalIgnoreCase);
        List<double> durees = new List<double>();
        int placees = 0, erreurs = 0;

        for (int k = 0; k < intervalles.Count; k++)
        {
            int i = intervalles[k];
            Timecode debut = marqueurs[i].Position;
            Timecode fin = marqueurs[i + 1].Position;
            double dureeMs = MsDeNanos(fin.Nanos - debut.Nanos);
            string fichier = imageParIntervalle[k];

            try
            {
                Media media;
                if (!dejaVus.TryGetValue(fichier, out media))
                {
                    media = project.MediaPool.AddMedia(fichier);
                    dejaVus[fichier] = media;
                }
                if (media == null) throw new Exception("media non ouvert");

                VideoEvent ve = piste.AddVideoEvent(
                    new Timecode(MsDe(debut)),
                    new Timecode(dureeMs));
                ve.AddTake(media.GetVideoStreamByIndex(0));

                placees++;
                durees.Add(dureeMs);
                Ligne(string.Format(CultureInfo.InvariantCulture,
                    "  image {0,3} : {1}  ->  {2}   duree {3}   [{4}]{5}",
                    placees, FmtTC(debut), FmtTC(fin), FmtMs(dureeMs),
                    Path.GetFileName(fichier),
                    (reutilisation && k >= images.Count) ? "  (reutilisation)" : ""));
            }
            catch (Exception ex)
            {
                erreurs++;
                Ligne("  ERREUR sur " + Path.GetFileName(fichier) + " : " + ex.Message);
            }
        }

        // ---- 7) statistiques ----------------------------------------------
        double min = 0, max = 0, moy = 0, tot = 0;
        if (durees.Count > 0)
        {
            min = durees[0]; max = durees[0];
            foreach (double d in durees) { if (d < min) min = d; if (d > max) max = d; tot += d; }
            moy = tot / durees.Count;
        }

        Ligne("");
        Ligne("--- RESUME ---");
        Ligne("marqueurs lus         : " + marqueurs.Count);
        Ligne("intervalles utilisables: " + intervalles.Count);
        Ligne("images dans le dossier : " + images.Count);
        Ligne("images placees         : " + placees);
        Ligne("erreurs                : " + erreurs);
        Ligne("duree mini             : " + FmtMs(min));
        Ligne("duree maxi             : " + FmtMs(max));
        Ligne("duree moyenne          : " + FmtMs(moy));
        Ligne("duree totale           : " + FmtMs(tot));
        if (nonPlacees.Count > 0)
        {
            Ligne("");
            Ligne("IMAGES NON PLACEES (" + nonPlacees.Count + ") :");
            for (int k = 0; k < nonPlacees.Count; k++)
                Ligne("  " + (k + 1) + ". " + Path.GetFileName(nonPlacees[k]));
        }
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        Ligne("");

        // resume affiche dans la boite de dialogue
        DernierErreur = false;
        DernierResume =
            "Alignement termine.\n\n"
            + "Marqueurs lus          : " + marqueurs.Count + "\n"
            + "Intervalles utilisables: " + intervalles.Count + "\n"
            + "Images dans le dossier : " + images.Count + "\n"
            + "Images placees         : " + placees + "\n"
            + "Duree mini / maxi / moy: " + FmtMs(min) + " / " + FmtMs(max) + " / " + FmtMs(moy) + "\n"
            + (reutilisation ? "Derniere image reutilisee : " + (intervalles.Count - images.Count) + " fois\n" : "")
            + (nonPlacees.Count > 0 ? "Images NON placees     : " + nonPlacees.Count + "\n" : "")
            + (erreurs > 0 ? "Erreurs                : " + erreurs + "\n" : "");

        return piste.Name;
    }
}
