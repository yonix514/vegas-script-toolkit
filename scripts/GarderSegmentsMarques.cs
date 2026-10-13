// =============================================================================
//  GarderSegmentsMarques.cs   --   VEGAS Pro 20   (SCRIPT PRINCIPAL voix off)
// =============================================================================
//  A QUOI CA SERT (le workflow du monteur) :
//
//    1. tu ouvres Vegas avec SEULEMENT la voix off sur une piste
//    2. tu ECOUTES : le narrateur repete la meme phrase 2, 3 ... 12 fois
//    3. tu appuies sur M au DEBUT et a la FIN de chaque prise que tu GARDES
//    4. tu lances ce script : Outils > Scripts > GarderSegmentsMarques
//    5. le script GARDE les intervalles marques et SUPPRIME tout le reste
//
//  QUEL INTERVALLE EST GARDE  (regle simple, a retenir) :
//
//    les marqueurs sont tries par position. Les intervalles sont numerotes
//    DANS L'ORDRE, entre 2 marqueurs consecutifs :
//
//      marqueur1 --> marqueur2  = intervalle 1
//      marqueur2 --> marqueur3  = intervalle 2
//      marqueur3 --> marqueur4  = intervalle 3
//      ...
//
//    MODE = "GARDER"     (defaut)  -> on garde les intervalles IMPAIRS
//                                     1, 3, 5, 7 ... (et on supprime les pairs)
//    MODE = "SUPPRIMER"            -> on SUPPRIME les intervalles IMPAIRS
//                                     1, 3, 5, 7 ... (et on garde les pairs)
//
//    Concretement, avec 6 marqueurs M1..M6 :
//
//      M1--1--M2--2--M3--3--M4--4--M5--5--M6
//         GARDE  SUPPR  GARDE  SUPPR  GARDE        <- MODE="GARDER"
//         SUPPR  GARDE  SUPPR  GARDE  SUPPR        <- MODE="SUPPRIMER"
//
//    MODE="GARDER"    : tu marques les BONNES prises (debut + fin de chaque
//                       bonne prise). Les trous entre les prises marquees
//                       tombent sur les intervalles pairs -> supprimes.
//    MODE="SUPPRIMER" : tu marques les MAUVAISES prises. Tu t'en sers quand
//                       c'est plus rapide de marquer ce qui est rate.
//
//    Un intervalle de duree NULLE (2 marqueurs au meme endroit) est ignore,
//    mais il COMPTE quand meme dans la numerotation : la parite ne se decale
//    jamais.
//
//  HORS MARQUEURS : par defaut (SUPPRIMER_HORS_MARQUEURS = true) tout ce qui
//  est AVANT le 1er marqueur et APRES le dernier marqueur est aussi supprime
//  ("garde ce qui est marque et supprime le reste"). Mets false pour le garder.
//
//  LES CLICS : apres les coupes il reste des coupures franches -> clics.
//  FONDU_MS (defaut 15 ms) pose un fondu d'ENTREE et un fondu de SORTIE de
//  15 ms sur chaque segment garde. Ca supprime les clics.
//
//  NOTE : Vegas NE colle PAS les segments restants bout a bout (il laisse des
//  trous). C'est le script FusionnerVoixCollee.cs qui les colle a partir de 0.
//
//  PARAMETRES : voir le bloc PARAMETRES juste apres les using.
//
//  RAPPORT : un rapport texte est ecrit dans DOSSIER_RAPPORTS (chemin affiche
//  dans la boite de dialogue finale).
//
//  ---------------------------------------------------------------------------
//  CONVENTIONS / PIEGES API MESURES SUR VEGAS PRO 20, BUILD 411 :
//    - new Timecode(x)  ->  x en MILLISECONDES
//    - Timecode.Nanos   ->  ticks de 100 ns  =>  ms = Nanos / 10000.0
//    - TrackEvent.Start / .Length / .End  sont affectables (Timecode)
//    - ev.FadeIn / ev.FadeOut  ->  objets Fade ; la duree est .Length (Timecode)
//    - piste.Events.Remove(ev)      -> supprime un evenement (verifie)
//    - piste.AddAudioEvent(debut, duree) + ev.AddTake(media.GetAudioStreamByIndex(0))
//      -> creation d'un evenement audio (verifie) ; Take.Offset est le point
//         de depart DANS le media et il est affectable (verifie)
//    - PIEGE : TrackEvent.Split(t) ne coupe PAS a la position absolue t.
//      MESURE : sur un evenement [5000 ; 60000], Split(new Timecode(12000))
//      a coupe a 17000 ms (= 5000 + 12000). Le parametre est donc un DECALAGE
//      depuis le debut de l'evenement, pas une position sur la timeline.
//      -> Ce script n'utilise PAS Split : il reconstruit chaque morceau garde
//         avec AddAudioEvent + AddTake + Take.Offset. Le resultat est exact et
//         verifiable (le rapport relit la geometrie ET l'offset de chaque prise).
//    - PIEGE : Project.Length est en LECTURE SEULE (aucun setter, meme via
//      IProjectCOM). La duree du projet est derivee du dernier evenement.
//  ---------------------------------------------------------------------------

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

    // "GARDER"    -> garde les intervalles IMPAIRS (1, 3, 5...)
    // "SUPPRIMER" -> supprime les intervalles IMPAIRS (1, 3, 5...)
    static string MODE = "GARDER";

    // Fondu d'entree + fondu de sortie pose sur CHAQUE segment garde (en ms).
    // 15 ms suffisent a supprimer les clics. 0 = pas de fondu.
    static double FONDU_MS = 15.0;

    // true  = supprime aussi AVANT le 1er marqueur et APRES le dernie
    // false = les garde
    static bool SUPPRIMER_HORS_MARQUEURS = true;

    // Dossier des rapports texte.
    static readonly string DOSSIER_RAPPORTS =
        CUTLAB_RAPPORTS;

    // Tolerance (en ticks de 100 ns) pour dire qu'un morceau est "dans" une plage.
    const long TOLERANCE = 100;

    // =========================================================================
    //  ETAT / OUTILS INTERNES
    // =========================================================================

    static StringBuilder log = new StringBuilder();
    static string cheminRapport = "";

    static void Ligne(string s) { log.AppendLine(s); }

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

    static void EcrireRapport()
    {
        try
        {
            if (!Directory.Exists(DOSSIER_RAPPORTS)) Directory.CreateDirectory(DOSSIER_RAPPORTS);
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, "rapport_garder_segments.txt");
            string texte = log.ToString();
            File.WriteAllText(cheminRapport, texte, new UTF8Encoding(true));
            string horo = Path.Combine(DOSSIER_RAPPORTS,
                "rapport_garder_segments_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(horo, texte, new UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            try
            {
                cheminRapport = Path.Combine(Path.GetTempPath(), "rapport_garder_segments.txt");
                File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
            }
            catch { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }
        }
    }

    // --- structures ----------------------------------------------------------

    class Intervalle
    {
        public int Numero;        // 1-based : intervalle 1, 2, 3 ...
        public Timecode Debut;
        public Timecode Fin;
        public double DureeMs { get { return MsDeNanos(Fin.Nanos - Debut.Nanos); } }
    }

    class Plage
    {
        public Timecode Debut;
        public Timecode Fin;
        public string Origine;   // ex : "intervalle 3" ou "avant le 1er marqueur"
    }

    class Resultat
    {
        public string Mode = "";
        public double FonduMs = 0;
        public int Marqueurs = 0;
        public int IntervallesBruts = 0;
        public int IntervallesImpaire = 0;
        public int IntervallesPair = 0;
        public int IntervallesNuls = 0;
        public List<string> Nuls = new List<string>();
        public int PlagesGardees = 0;
        public List<string> DescriptionPlages = new List<string>();
        public int EvenementsAvant = 0;
        public double DureeAvantMs = 0;
        public int PiecesCreees = 0;
        public List<string> Pieces = new List<string>();
        public int DecalagesFaux = 0;
        public List<string> SansMedia = new List<string>();
        public int EvenementsMultiPrises = 0;
        public List<string> Echecs = new List<string>();
        public int OriginauxSupprimes = 0;
        public int EvenementsGardes = 0;
        public double DureeApresMs = 0;
        public double DureeSupprimeeMs = 0;
        public int FondusPoses = 0;
        public int FondusReduits = 0;
        public int FondusIgnores = 0;
        public bool Erreur = false;
        public bool RienFait = false;
        public string Resume = "";
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

    // =========================================================================
    //  PISTE CIBLE : la piste audio qui contient le plus d'evenements
    // =========================================================================
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

        Ligne("");
        Ligne("--- PISTES AUDIO CONTENANT DES EVENEMENTS : " + candidats.Count + " ---");
        for (int i = 0; i < candidats.Count; i++)
            Ligne("  - \"" + candidats[i].Name + "\" : " + comptes[i] + " evenement(s)");

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
                    + "\" (" + comptes[0] + " evenement(s) sur " + candidats.Count + " piste(s) audio remplie(s))";
        return candidats[0];
    }

    // =========================================================================
    //  RECONSTRUCTION
    //  Pour chaque evenement d'origine et chaque plage a garder, on cree UN
    //  nouvel evenement audio qui contient exactement le morceau a garder, avec
    //  le bon decalage dans le media (Take.Offset). Puis on supprime les
    //  evenements d'origine. Aucun appel a Split : geometrie exacte et verifiee.
    // =========================================================================
    static void Reconstruire(AudioTrack piste, List<TrackEvent> originaux, List<Plage> plages, Resultat r)
    {
        Ligne("");
        Ligne("--- RECONSTRUCTION DES SEGMENTS A GARDER ---");
        Ligne("methode : 1 nouvel evenement par morceau garde (AddAudioEvent + AddTake + Take.Offset)");
        Ligne("          puis suppression des evenements d'origine");

        List<TrackEvent> aSupprimer = new List<TrackEvent>();

        foreach (TrackEvent E in originaux)
        {
            long eStart = E.Start.Nanos;
            long eEnd = E.End.Nanos;

            Take t = null;
            try { t = E.ActiveTake; } catch { }
            if (t == null || t.MediaStream == null)
            {
                r.SansMedia.Add("evenement " + FmtTC(E.Start) + " -> " + FmtTC(E.End)
                    + " : aucune prise utilisable -> NON TOUCHE (laisse tel quel)");
                Ligne("  evenement d'origine " + FmtTC(E.Start) + " -> " + FmtTC(E.End)
                      + " : AUCUNE PRISE -> non touche");
                continue;
            }

            int nbPrises = 0;
            try { nbPrises = E.Takes.Count; } catch { }
            if (nbPrises > 1) r.EvenementsMultiPrises++;

            int cree = 0;
            for (int k = 0; k < plages.Count; k++)
            {
                Plage P = plages[k];
                long s = Math.Max(eStart, P.Debut.Nanos);
                long f = Math.Min(eEnd, P.Fin.Nanos);
                long len = f - s;
                if (len <= 0) continue;

                long delta = s - eStart;             // decalage dans le media
                long offsetAttendu = t.Offset.Nanos + delta;

                try
                {
                    AudioEvent ne = piste.AddAudioEvent(Timecode.FromNanos(s), Timecode.FromNanos(len));
                    Take nt = ne.AddTake(t.MediaStream);
                    if (nt != null)
                    {
                        try { nt.Offset = Timecode.FromNanos(offsetAttendu); }
                        catch (Exception ex) { r.Echecs.Add("offset de prise refuse (" + FmtMs(MsDeNanos(s)) + ") : " + ex.Message); }
                    }
                    try { ne.Start = Timecode.FromNanos(s); } catch { }
                    try { ne.Length = Timecode.FromNanos(len); } catch { }
                    if (ne.End.Nanos != f) { try { ne.End = Timecode.FromNanos(f); } catch { } }

                    double offsetLu = (nt == null) ? -1.0 : nt.Offset.ToMilliseconds();
                    bool offsetOk = (nt != null) && (Math.Abs(nt.Offset.Nanos - offsetAttendu) <= TOLERANCE);
                    if (!offsetOk) r.DecalagesFaux++;

                    cree++;
                    r.PiecesCreees++;
                    r.Pieces.Add(string.Format(CultureInfo.InvariantCulture,
                        "  morceau {0,3} : {1} -> {2}  duree {3,12:0.000} ms | plage \"{4}\" | offset prise {5,12:0.000} ms (attendu {6,12:0.000}) {7} | [{8}]",
                        r.PiecesCreees, FmtTC(ne.Start), FmtTC(ne.End), MsDe(ne.Length), P.Origine,
                        offsetLu, MsDeNanos(offsetAttendu), offsetOk ? "OK" : "<<< FAUX >>>", SourceDe(ne)));

                    if (ne.Start.Nanos != s || ne.End.Nanos != f)
                        r.Echecs.Add("geometrie inexacte sur le morceau " + FmtTC(ne.Start) + " -> " + FmtTC(ne.End)
                            + " (voulu " + FmtMs(MsDeNanos(s)) + " -> " + FmtMs(MsDeNanos(f)) + ")");
                }
                catch (Exception ex)
                {
                    r.Echecs.Add("creation du morceau " + FmtMs(MsDeNanos(s)) + " -> " + FmtMs(MsDeNanos(f))
                        + " impossible : " + ex.Message);
                }
            }

            Ligne("  evenement d'origine " + FmtTC(E.Start) + " -> " + FmtTC(E.End)
                  + " : " + cree + " morceau(x) garde(s)");
            aSupprimer.Add(E);
        }

        // ---- suppression des evenements d'origine ---------------------------
        Ligne("");
        Ligne("--- SUPPRESSION DES EVENEMENTS D'ORIGINE : " + aSupprimer.Count + " ---");
        foreach (TrackEvent E in aSupprimer)
        {
            bool ok = false;
            try { ok = piste.Events.Remove(E); }
            catch (Exception ex) { r.Echecs.Add("suppression refusee : " + ex.Message); }
            if (!ok)
            {
                try
                {
                    int idx = piste.Events.IndexOf(E);
                    if (idx >= 0) { piste.Events.RemoveAt(idx); ok = true; }
                }
                catch (Exception ex) { r.Echecs.Add("suppression par index refusee : " + ex.Message); }
            }
            if (ok) r.OriginauxSupprimes++;
            else r.Echecs.Add("evenement d'origine " + FmtTC(E.Start) + " -> " + FmtTC(E.End) + " NON supprime");
        }

        Ligne("");
        Ligne("--- MORCEAUX GARDES (relus apres creation) : " + r.Pieces.Count + " ---");
        foreach (string s in r.Pieces) Ligne(s);
    }

    // =========================================================================
    //  COEUR DU SCRIPT
    // =========================================================================
    static Resultat Traiter(Project project, AudioTrack piste, string explicationPiste)
    {
        Resultat r = new Resultat();
        r.Mode = MODE;
        r.FonduMs = FONDU_MS;

        Ligne("");
        Ligne("--- PARAMETRES ---");
        Ligne("MODE                       : \"" + MODE + "\"");
        Ligne("FONDU_MS                   : " + FONDU_MS.ToString(CultureInfo.InvariantCulture) + " ms");
        Ligne("SUPPRIMER_HORS_MARQUEURS   : " + SUPPRIMER_HORS_MARQUEURS);

        bool modeGarder = string.Equals(MODE, "GARDER", StringComparison.OrdinalIgnoreCase);
        bool modeSupprimer = string.Equals(MODE, "SUPPRIMER", StringComparison.OrdinalIgnoreCase);
        if (!modeGarder && !modeSupprimer)
        {
            Ligne("");
            Ligne("ARRET : MODE doit valoir \"GARDER\" ou \"SUPPRIMER\" (valeur lue : \"" + MODE + "\").");
            r.Erreur = true;
            r.Resume = "ARRET : MODE invalide (\"" + MODE + "\").\n\n"
                     + "Ouvre le script et mets MODE = \"GARDER\" ou MODE = \"SUPPRIMER\".";
            return r;
        }

        Ligne("");
        Ligne("--- PISTE CIBLE ---");
        Ligne("piste retenue : \"" + piste.Name + "\"  (index " + piste.Index + ")");
        Ligne("pourquoi      : " + explicationPiste);

        // ---- 1) marqueurs ---------------------------------------------------
        List<Marker> marqueurs = LireMarqueurs(project);
        r.Marqueurs = marqueurs.Count;
        Ligne("");
        Ligne("--- MARQUEURS DU PROJET : " + marqueurs.Count + " (tries par position) ---");
        for (int i = 0; i < marqueurs.Count; i++)
            Ligne(string.Format(CultureInfo.InvariantCulture, "  marqueur {0,3} : {1}   \"{2}\"",
                i + 1, FmtTC(marqueurs[i].Position), marqueurs[i].Label));

        if (marqueurs.Count < 2)
        {
            Ligne("");
            Ligne("ARRET : il faut AU MOINS 2 marqueurs (il y en a " + marqueurs.Count + ").");
            r.Erreur = true;
            r.Resume = "ARRET : il faut au moins 2 marqueurs.\n\n"
                     + "Marqueurs trouves : " + marqueurs.Count + "\n\n"
                     + "Pendant l'ecoute, appuie sur M au DEBUT et a la FIN de chaque prise a garder, "
                     + "puis relance le script.";
            return r;
        }

        // ---- 2) evenements de la piste --------------------------------------
        List<TrackEvent> avant = LireEvenements(piste);
        r.EvenementsAvant = avant.Count;
        if (avant.Count == 0)
        {
            Ligne("");
            Ligne("ARRET : la piste \"" + piste.Name + "\" ne contient aucun evenement audio.");
            r.Erreur = true;
            r.Resume = "ARRET : aucun evenement audio sur la piste \"" + piste.Name + "\".\n\n"
                     + "Lance d'abord PreparerVoixOff pour mettre la voix off sur la timeline.";
            return r;
        }
        double finDernierEv = 0;
        foreach (TrackEvent ev in avant)
        {
            r.DureeAvantMs += MsDe(ev.Length);
            if (MsDe(ev.End) > finDernierEv) finDernierEv = MsDe(ev.End);
        }

        Ligne("");
        Ligne("--- EVENEMENTS AUDIO AVANT : " + avant.Count + " ---");
        for (int i = 0; i < avant.Count; i++)
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  evenement {0,3} : {1} -> {2}   duree {3}   [{4}]",
                i + 1, FmtTC(avant[i].Start), FmtTC(avant[i].End), FmtMs(MsDe(avant[i].Length)), SourceDe(avant[i])));
        Ligne("duree audio totale AVANT : " + FmtMs(r.DureeAvantMs));

        // ---- 3) intervalles entre marqueurs (parite = numero d'intervalle) --
        List<Intervalle> intervalles = new List<Intervalle>();
        r.IntervallesBruts = marqueurs.Count - 1;
        for (int i = 0; i + 1 < marqueurs.Count; i++)
        {
            Intervalle it = new Intervalle();
            it.Numero = i + 1;                 // 1-based : PARITE DE REFERENCE
            it.Debut = marqueurs[i].Position;
            it.Fin = marqueurs[i + 1].Position;
            intervalles.Add(it);
            if (it.DureeMs <= 0)
            {
                r.IntervallesNuls++;
                r.Nuls.Add("intervalle " + it.Numero + " : " + FmtTC(it.Debut) + " -> " + FmtTC(it.Fin)
                           + "  duree " + FmtMs(it.DureeMs) + "  -> IGNORE (duree nulle), la parite est conservee");
            }
            if (it.Numero % 2 == 1) r.IntervallesImpaire++; else r.IntervallesPair++;
        }

        Ligne("");
        Ligne("--- INTERVALLES ENTRE MARQUEURS (parite = numero d'intervalle) ---");
        Ligne("intervalles bruts   : " + r.IntervallesBruts);
        Ligne("intervalles impairs : " + r.IntervallesImpaire + "   (1, 3, 5 ...)");
        Ligne("intervalles pairs   : " + r.IntervallesPair + "   (2, 4, 6 ...)");
        Ligne("regle appliquee     : MODE=\"" + MODE + "\" -> "
              + (modeGarder ? "les IMPAIRS sont GARDES, les PAIRS SUPPRIMES"
                            : "les IMPAIRS sont SUPPRIMES, les PAIRS GARDES"));
        Ligne("");
        for (int k = 0; k < intervalles.Count; k++)
        {
            Intervalle it = intervalles[k];
            bool garde = (modeGarder ? (it.Numero % 2 == 1) : (it.Numero % 2 == 0));
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  intervalle {0,3} : {1} -> {2}   duree {3}   -> {4}",
                it.Numero, FmtTC(it.Debut), FmtTC(it.Fin), FmtMs(it.DureeMs),
                garde ? "GARDE" : "SUPPRIME"));
        }
        if (r.Nuls.Count > 0)
        {
            Ligne("");
            Ligne("INTERVALLES IGNORES (duree nulle) : " + r.Nuls.Count);
            foreach (string s in r.Nuls) Ligne("  " + s);
        }

        // ---- 4) plages a garder ---------------------------------------------
        List<Plage> plages = new List<Plage>();
        foreach (Intervalle it in intervalles)
        {
            if (it.DureeMs <= 0) continue;
            bool garde = (modeGarder ? (it.Numero % 2 == 1) : (it.Numero % 2 == 0));
            if (!garde) continue;
            Plage p = new Plage();
            p.Debut = it.Debut; p.Fin = it.Fin; p.Origine = "intervalle " + it.Numero;
            plages.Add(p);
        }

        if (!SUPPRIMER_HORS_MARQUEURS)
        {
            if (MsDe(marqueurs[0].Position) > 0)
            {
                Plage p0 = new Plage();
                p0.Debut = Timecode.FromNanos(0); p0.Fin = marqueurs[0].Position;
                p0.Origine = "avant le 1er marqueur";
                plages.Add(p0);
            }
            if (finDernierEv > MsDe(marqueurs[marqueurs.Count - 1].Position))
            {
                Plage pn = new Plage();
                pn.Debut = marqueurs[marqueurs.Count - 1].Position;
                pn.Fin = Timecode.FromNanos((long)Math.Round(finDernierEv * UNITES_PAR_MS));
                pn.Origine = "apres le dernier marqueur";
                plages.Add(pn);
            }
        }

        plages.Sort(delegate(Plage a, Plage b) { return a.Debut.Nanos.CompareTo(b.Debut.Nanos); });
        r.PlagesGardees = plages.Count;

        Ligne("");
        Ligne("--- PLAGES A GARDER : " + plages.Count + " ---");
        double dureeGardeeCible = 0;
        foreach (Plage p in plages)
        {
            double d = MsDeNanos(p.Fin.Nanos - p.Debut.Nanos);
            dureeGardeeCible += d;
            string s = string.Format(CultureInfo.InvariantCulture,
                "  {0} : {1} -> {2}   duree {3}", p.Origine, FmtTC(p.Debut), FmtTC(p.Fin), FmtMs(d));
            Ligne(s);
            r.DescriptionPlages.Add(s);
        }
        Ligne("duree visee (somme brute) : " + FmtMs(dureeGardeeCible));

        if (plages.Count == 0)
        {
            Ligne("");
            Ligne("SECURITE : aucune plage a garder -> AUCUNE SUPPRESSION effectuee.");
            r.RienFait = true;
            r.Erreur = true;
            r.Resume = "SECURITE : aucune plage a garder.\n\n"
                     + "Le script n'a RIEN supprime.\n"
                     + "Verifie tes marqueurs (il en faut au moins 2 bien places).";
            return r;
        }

        // ---- 5) reconstruction + suppression --------------------------------
        Reconstruire(piste, avant, plages, r);

        // ---- 6) fondus sur les segments gardes ------------------------------
        Ligne("");
        Ligne("--- FONDUS (FONDU_MS = " + FONDU_MS.ToString(CultureInfo.InvariantCulture) + " ms) ---");
        List<TrackEvent> restants = LireEvenements(piste);
        foreach (TrackEvent ev in restants)
        {
            double duree = MsDe(ev.Length);
            double f = FONDU_MS;
            if (f <= 0) { r.FondusIgnores++; continue; }
            if (duree <= 0) { r.FondusIgnores++; continue; }
            if (duree < 2.0 * f)
            {
                f = Math.Max(0.0, duree / 2.0 - 0.5);
                r.FondusReduits++;
            }
            if (f < 0.5) { r.FondusIgnores++; continue; }
            try
            {
                ev.FadeIn.Length = new Timecode(f);
                ev.FadeOut.Length = new Timecode(f);
                r.FondusPoses++;
            }
            catch (Exception ex)
            {
                r.Echecs.Add("fondu refuse sur le segment " + FmtTC(ev.Start) + " : " + ex.Message);
            }
        }

        // ---- 7) relecture finale (preuve) -----------------------------------
        List<TrackEvent> finale = LireEvenements(piste);
        Ligne("");
        Ligne("--- RELECTURE FINALE DANS LE PROJET (piste \"" + piste.Name + "\") : "
              + finale.Count + " evenement(s) ---");
        foreach (TrackEvent ev in finale)
        {
            r.DureeApresMs += MsDe(ev.Length);
            double offsetPrise = -1;
            try { offsetPrise = ev.ActiveTake.Offset.ToMilliseconds(); } catch { }
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  debut {0,12:0.000} ms | duree {1,12:0.000} ms | fin {2,12:0.000} ms | offset prise {3,12:0.000} ms | fondu IN {4,7:0.000} ms | fondu OUT {5,7:0.000} ms | [{6}]",
                ev.Start.ToMilliseconds(), ev.Length.ToMilliseconds(), ev.End.ToMilliseconds(),
                offsetPrise, ev.FadeIn.Length.ToMilliseconds(), ev.FadeOut.Length.ToMilliseconds(), SourceDe(ev)));
        }
        r.EvenementsGardes = finale.Count;
        r.DureeSupprimeeMs = r.DureeAvantMs - r.DureeApresMs;

        // ---- 8) resume ------------------------------------------------------
        Ligne("");
        Ligne("--- RESUME ---");
        Ligne("MODE                        : " + MODE);
        Ligne("FONDU_MS                    : " + FONDU_MS.ToString(CultureInfo.InvariantCulture) + " ms");
        Ligne("SUPPRIMER_HORS_MARQUEURS    : " + SUPPRIMER_HORS_MARQUEURS);
        Ligne("piste traitee               : \"" + piste.Name + "\"");
        Ligne("marqueurs lus               : " + r.Marqueurs);
        Ligne("intervalles bruts           : " + r.IntervallesBruts);
        Ligne("  dont impairs (1,3,5...)   : " + r.IntervallesImpaire);
        Ligne("  dont pairs   (2,4,6...)   : " + r.IntervallesPair);
        Ligne("  dont ignores (duree nulle): " + r.IntervallesNuls);
        Ligne("plages gardees              : " + r.PlagesGardees);
        Ligne("evenements d'origine avant  : " + r.EvenementsAvant);
        Ligne("morceaux crees (gardes)     : " + r.PiecesCreees);
        Ligne("originaux supprimes         : " + r.OriginauxSupprimes);
        Ligne("evenements sans prise (non touches) : " + r.SansMedia.Count);
        Ligne("evenements a prises multiples : " + r.EvenementsMultiPrises);
        Ligne("offsets de prise FAUX       : " + r.DecalagesFaux);
        Ligne("fondus poses                : " + r.FondusPoses);
        Ligne("fondus reduits (segment court) : " + r.FondusReduits);
        Ligne("fondus ignores              : " + r.FondusIgnores);
        Ligne("erreurs                     : " + r.Echecs.Count);
        foreach (string s in r.Echecs) Ligne("  " + s);
        Ligne("DUREE AUDIO AVANT           : " + FmtMs(r.DureeAvantMs));
        Ligne("DUREE AUDIO APRES           : " + FmtMs(r.DureeApresMs));
        Ligne("DUREE SUPPRIMEE             : " + FmtMs(r.DureeSupprimeeMs));
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        Ligne("");

        r.Resume = "MODE \"" + MODE + "\" termine.\n\n"
                 + "Piste traitee        : \"" + piste.Name + "\"\n"
                 + "Marqueurs lus        : " + r.Marqueurs + "\n"
                 + "Intervalles          : " + r.IntervallesBruts
                 + "  (impairs " + r.IntervallesImpaire + " / pairs " + r.IntervallesPair + ")\n"
                 + "Plages gardees       : " + r.PlagesGardees + "\n"
                 + "Segments gardes      : " + r.EvenementsGardes + "\n"
                 + "Originaux supprimes  : " + r.OriginauxSupprimes + "\n"
                 + "Fondus poses         : " + r.FondusPoses
                 + "  (" + FONDU_MS.ToString(CultureInfo.InvariantCulture) + " ms)"
                 + (r.FondusReduits > 0 ? "  dont " + r.FondusReduits + " reduit(s)" : "") + "\n"
                 + (r.DecalagesFaux > 0 ? "OFFSETS DE PRISE FAUX : " + r.DecalagesFaux + "  (voir rapport)\n" : "")
                 + (r.SansMedia.Count > 0 ? "Evenements sans prise non touches : " + r.SansMedia.Count + "\n" : "")
                 + "\nDuree audio AVANT    : " + FmtMs(r.DureeAvantMs)
                 + "\nDuree audio APRES    : " + FmtMs(r.DureeApresMs)
                 + "\nDuree supprimee      : " + FmtMs(r.DureeSupprimeeMs)
                 + "\n"
                 + (r.Echecs.Count > 0 ? "\nATTENTION : " + r.Echecs.Count + " erreur(s) - voir le rapport.\n" : "")
                 + "\nLes segments restent a leur place (Vegas ne les colle pas).\n"
                 + "Pour les coller bout a bout : FusionnerVoixCollee.\n"
                 + "\nRapport :\n" + cheminRapport;

        return r;
    }

    // =========================================================================
    //  POINT D'ENTREE VEGAS
    // =========================================================================
    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();

        Ligne("RAPPORT - GARDER LES SEGMENTS MARQUES (voix off)");
        Ligne("==============================================");
        Ligne("Date   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script : GarderSegmentsMarques.cs  (VEGAS Pro 20)");
        Ligne("Projet : " + (project.IsUntitled ? "(sans titre)" : project.FilePath));
        Ligne("But    : garder les intervalles marques (touche M) et supprimer le reste.");

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
        AudioTrack piste = ChoisirPisteAudio(project, out explication);

        if (piste == null)
        {
            Ligne("");
            Ligne("ARRET : " + explication);
            EcrireRapport();
            MessageBox.Show("Aucune piste audio ne contient d'evenement.\n\n"
                + "Lance d'abord PreparerVoixOff (ou pose ta voix off sur une piste audio), "
                + "place tes marqueurs (touche M), puis relance ce script.\n\nRapport :\n" + cheminRapport,
                "GarderSegmentsMarques", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Resultat r = Traiter(project, piste, explication);
        EcrireRapport();

        MessageBox.Show(r.Resume, "GarderSegmentsMarques", MessageBoxButtons.OK,
            r.Erreur ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    // =========================================================================
    //  MODE TEST  (compile avec /define:MODETEST)
    //  Construit de vrais evenements audio + des marqueurs a positions connues,
    //  applique le traitement, puis RELIT le projet et ecrit les chiffres.
    //  Chaque passe utilise SA piste : les chiffres relus ne peuvent donc pas
    //  etre pollues par une autre passe.
    //  Aucune boite de dialogue ; ferme Vegas a la fin.
    // =========================================================================
#if MODETEST
    // voix_propre.wav : present sur le PC (86 400 044 o, 900 s)
    const string WAV_TEST =
        Path.Combine(Path.GetTempPath(), "audio-test.wav");

    static Dictionary<string, Media> poolTest = new Dictionary<string, Media>(StringComparer.OrdinalIgnoreCase);

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

    static AudioTrack CreerPisteTest(Project project, string nom, double debutMs, double dureeMs)
    {
        AudioTrack at = project.AddAudioTrack();
        at.Name = nom;
        AudioEvent ev = at.AddAudioEvent(new Timecode(debutMs), new Timecode(dureeMs));
        Media m = MediaTest(project, WAV_TEST);
        ev.AddTake(m.GetAudioStreamByIndex(0));
        ev.Start = new Timecode(debutMs);
        ev.Length = new Timecode(dureeMs);
        return at;
    }

    static void AjouterMarqueurs(Project project, double[] positionsMs)
    {
        project.Markers.Clear();
        foreach (double d in positionsMs)
            project.Markers.Add(new Marker(new Timecode(d),
                d.ToString(CultureInfo.InvariantCulture) + "ms"));
    }

    static int NbEvenements(AudioTrack piste)
    {
        int n = 0;
        foreach (TrackEvent ev in piste.Events) n++;
        return n;
    }

    static double TotalMs(AudioTrack piste)
    {
        double s = 0;
        foreach (TrackEvent ev in piste.Events) s += MsDe(ev.Length);
        return s;
    }

    static void TracerPiste(string titre, AudioTrack piste)
    {
        List<TrackEvent> evs = LireEvenements(piste);
        double tot = 0;
        Ligne("");
        Ligne("=== " + titre + " : piste \"" + piste.Name + "\" - " + evs.Count + " evenement(s) ===");
        for (int i = 0; i < evs.Count; i++)
        {
            tot += MsDe(evs[i].Length);
            double offsetPrise = -1;
            try { offsetPrise = evs[i].ActiveTake.Offset.ToMilliseconds(); } catch { }
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  debut {0,12:0.000} ms | duree {1,12:0.000} ms | fin {2,12:0.000} ms | offset prise {3,12:0.000} ms | fondu IN {4,7:0.000} | fondu OUT {5,7:0.000} | [{6}]",
                evs[i].Start.ToMilliseconds(), evs[i].Length.ToMilliseconds(), evs[i].End.ToMilliseconds(),
                offsetPrise, evs[i].FadeIn.Length.ToMilliseconds(), evs[i].FadeOut.Length.ToMilliseconds(),
                SourceDe(evs[i])));
        }
        Ligne("  TOTAL des durees = " + FmtMs(tot) + "  (" + tot.ToString("0.000", CultureInfo.InvariantCulture) + " ms)");
    }

    // VERDICT : on relit la piste APRES traitement (chiffres reels du projet)
    static void Verdict(string nom, AudioTrack piste, string mode, int nbAttendu, double msAttendu)
    {
        int nb = NbEvenements(piste);
        double ms = TotalMs(piste);
        bool ok = (nb == nbAttendu) && (Math.Abs(ms - msAttendu) < 1.0);
        Ligne(string.Format(CultureInfo.InvariantCulture,
            "{0,-22} MODE={1,-10} : {2} evenement(s) (attendu {3}) | total relu {4,12:0.000} ms (attendu {5,12:0.000}) | {6}",
            nom, mode, nb, nbAttendu, ms, msAttendu, ok ? "OK" : "<<< ECHEC >>>"));
    }

    static void TracerFondus(string titre, AudioTrack piste)
    {
        Ligne("");
        Ligne("--- FONDUS RELUS (" + titre + ") ---");
        int i = 0;
        foreach (TrackEvent ev in piste.Events)
        {
            i++;
            Ligne(string.Format(CultureInfo.InvariantCulture,
                "  segment {0,2} : duree {1,10:0.000} ms | FadeIn.Length relu = {2,8:0.000} ms | FadeOut.Length relu = {3,8:0.000} ms",
                i, ev.Length.ToMilliseconds(),
                ev.FadeIn.Length.ToMilliseconds(), ev.FadeOut.Length.ToMilliseconds()));
        }
        if (i == 0) Ligne("  (aucun evenement)");
    }

    static void ModeTest(Project project, Vegas vegas)
    {
        Ligne("");
        Ligne("################ MODE TEST (/define:MODETEST) ################");
        Ligne("Voix de test : " + WAV_TEST);
        Ligne("Existe       : " + File.Exists(WAV_TEST));

        // PASSE 1 : MODE="GARDER" - 7 marqueurs - event 0 -> 60000
        // GARDES : 1 (0-5000), 3 (12000-20000), 5 (25000-32000) = 20000 ms
        Ligne("");
        Ligne("#############################################################");
        Ligne("##### PASSE 1 : MODE=\"GARDER\" - 7 marqueurs / 6 intervalles");
        Ligne("##### attendu : 3 segments, 20000.000 ms, offsets prise 0 / 12000 / 25000");
        Ligne("#############################################################");
        MODE = "GARDER"; FONDU_MS = 15.0; SUPPRIMER_HORS_MARQUEURS = true;
        AudioTrack p1 = CreerPisteTest(project, "TEST_VOIX_1", 0, 60000);
        AjouterMarqueurs(project, new double[] { 0, 5000, 12000, 20000, 25000, 32000, 40000 });
        TracerPiste("AVANT (passe 1)", p1);
        Resultat r1 = Traiter(project, p1, "piste de test passee explicitement");
        TracerPiste("APRES (passe 1)", p1);
        TracerFondus("passe 1", p1);

        // PASSE 2 : MODE="SUPPRIMER" - memes 7 marqueurs
        // GARDES : 2 (5000-12000), 4 (20000-25000), 6 (32000-40000) = 20000 ms
        Ligne("");
        Ligne("#############################################################");
        Ligne("##### PASSE 2 : MODE=\"SUPPRIMER\" - memes 7 marqueurs");
        Ligne("##### attendu : 3 segments, 20000.000 ms, offsets prise 5000 / 20000 / 32000");
        Ligne("#############################################################");
        MODE = "SUPPRIMER"; FONDU_MS = 15.0; SUPPRIMER_HORS_MARQUEURS = true;
        AudioTrack p2 = CreerPisteTest(project, "TEST_VOIX_2", 0, 60000);
        AjouterMarqueurs(project, new double[] { 0, 5000, 12000, 20000, 25000, 32000, 40000 });
        TracerPiste("AVANT (passe 2)", p2);
        Resultat r2 = Traiter(project, p2, "piste de test passee explicitement");
        TracerPiste("APRES (passe 2)", p2);
        TracerFondus("passe 2", p2);

        // PASSE 3 : SUPPRIMER_HORS_MARQUEURS = false
        // event 1000 -> 40000 ; GARDES = 7000 + 5000 + 4000 + 8000 = 24000 ms (4 segments)
        Ligne("");
        Ligne("#############################################################");
        Ligne("##### PASSE 3 : MODE=\"GARDER\" + garder HORS marqueurs");
        Ligne("##### attendu : 4 segments, 24000.000 ms");
        Ligne("#############################################################");
        MODE = "GARDER"; FONDU_MS = 15.0; SUPPRIMER_HORS_MARQUEURS = false;
        AudioTrack p3 = CreerPisteTest(project, "TEST_VOIX_3", 1000, 39000);
        AjouterMarqueurs(project, new double[] { 5000, 12000, 20000, 25000, 32000 });
        TracerPiste("AVANT (passe 3)", p3);
        Resultat r3 = Traiter(project, p3, "piste de test passee explicitement");
        TracerPiste("APRES (passe 3)", p3);

        // PASSE 4 : FONDU_MS trop grand pour un segment court
        // GARDES : 0-1000 (1000 ms) et 1500-2000 (500 ms) = 1500 ms (2 segments)
        // FONDU_MS = 400 -> le segment de 500 ms devient 500/2-0.5 = 249.5 ms
        Ligne("");
        Ligne("#############################################################");
        Ligne("##### PASSE 4 : FONDU_MS=400 sur segments courts");
        Ligne("##### attendu : 2 segments, 1500.000 ms, fondus 400 puis 249.5");
        Ligne("#############################################################");
        MODE = "GARDER"; FONDU_MS = 400.0; SUPPRIMER_HORS_MARQUEURS = true;
        AudioTrack p4 = CreerPisteTest(project, "TEST_VOIX_4", 0, 4000);
        AjouterMarqueurs(project, new double[] { 0, 1000, 1500, 2000, 4000 });
        TracerPiste("AVANT (passe 4)", p4);
        Resultat r4 = Traiter(project, p4, "piste de test passee explicitement");
        TracerPiste("APRES (passe 4)", p4);
        TracerFondus("passe 4", p4);

        // PASSE 5 : un seul marqueur -> ARRET, rien de supprime
        Ligne("");
        Ligne("#############################################################");
        Ligne("##### PASSE 5 : 1 seul marqueur -> ARRET sans rien supprimer");
        Ligne("##### attendu : 1 segment inchange, 8000.000 ms");
        Ligne("#############################################################");
        MODE = "GARDER"; FONDU_MS = 15.0; SUPPRIMER_HORS_MARQUEURS = true;
        AudioTrack p5 = CreerPisteTest(project, "TEST_VOIX_5", 0, 8000);
        AjouterMarqueurs(project, new double[] { 3000 });
        TracerPiste("AVANT (passe 5)", p5);
        Resultat r5 = Traiter(project, p5, "piste de test passee explicitement");
        TracerPiste("APRES (passe 5)", p5);

        // VERDICT : les chiffres sont RELUS dans le projet
        Ligne("");
        Ligne("################ VERDICT DU TEST (chiffres relus dans le projet) ################");
        Verdict("PASSE 1 GARDER", p1, "GARDER", 3, 20000.0);
        Verdict("PASSE 2 SUPPRIMER", p2, "SUPPRIMER", 3, 20000.0);
        Verdict("PASSE 3 HORS MARQ.", p3, "GARDER", 4, 24000.0);
        Verdict("PASSE 4 FONDU 400", p4, "GARDER", 2, 1500.0);
        Verdict("PASSE 5 1 MARQUEUR", p5, "GARDER", 1, 8000.0);
        Ligne("");
        Ligne(string.Format(CultureInfo.InvariantCulture,
            "PASSE 1 offsets de prise FAUX : {0} (attendu 0) | morceaux crees {1} (attendu 3) | originaux supprimes {2} (attendu 1) | fondus {3} (attendu 3)",
            r1.DecalagesFaux, r1.PiecesCreees, r1.OriginauxSupprimes, r1.FondusPoses));
        Ligne(string.Format(CultureInfo.InvariantCulture,
            "PASSE 2 offsets de prise FAUX : {0} (attendu 0) | morceaux crees {1} (attendu 3) | duree supprimee {2:0.000} ms (attendu 40000.000)",
            r2.DecalagesFaux, r2.PiecesCreees, r2.DureeSupprimeeMs));
        Ligne(string.Format(CultureInfo.InvariantCulture,
            "PASSE 3 morceaux crees : {0} (attendu 4) | duree supprimee {1:0.000} ms (attendu 15000.000)",
            r3.PiecesCreees, r3.DureeSupprimeeMs));
        Ligne(string.Format(CultureInfo.InvariantCulture,
            "PASSE 4 fondus reduits : {0} (attendu 1) | fondus poses {1} (attendu 2)",
            r4.FondusReduits, r4.FondusPoses));
        Ligne(string.Format(CultureInfo.InvariantCulture,
            "PASSE 5 arret propre : erreur={0} (attendu True) | rien_fait={1} (attendu True) | morceaux crees {2} (attendu 0)",
            r5.Erreur, r5.RienFait, r5.PiecesCreees));

        Ligne("");
        Ligne("################ FIN DES TESTS ################");
        EcrireRapport();

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
