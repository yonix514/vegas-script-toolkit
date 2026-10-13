// =============================================================================
//  AjusterDureeSelection.cs   --   VEGAS Pro 20
// =============================================================================
//  A QUOI CA SERT :
//    agit sur les evenements VIDEO SELECTIONNES pour caler un bloc :
//
//      MODE 1 : "Pourcentage"
//               110 = allonge chaque evenement de 10 %
//                90 = raccourcit chaque evenement de 10 %
//               (chaque evenement garde son propre debut, sauf si tu coches
//                "ancrer sur la fin du bloc")
//
//      MODE 2 : "Repartir la duree totale egalement"
//               tous les evenements selectionnes recoivent la MEME duree,
//               bout a bout, du debut du premier a la fin du dernier.
//               (parfait pour repartir N images sur un bloc de voix)
//
//  UTILISATION : selectionne tes evenements dans Vegas, puis
//                Outils > Scripts > AjusterDureeSelection
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

    // valeurs par defaut de la boite de dialogue
    const decimal POURCENT_DEFAUT = 100m;
    const bool MODE_POURCENT_DEFAUT = true;

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
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, "rapport_ajustement.txt");
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

    // ---- petite boite de dialogue (sans System.Drawing) ---------------------
    class BoiteAjustement : Form
    {
        public NumericUpDown Pourcent;
        public RadioButton ModePourcent;
        public RadioButton ModeRepartir;
        public CheckBox AncrerFin;

        public BoiteAjustement(int nbEvenements)
        {
            Text = "Ajuster la duree de " + nbEvenements + " evenement(s)";
            Width = 430; Height = 260;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;

            Label l1 = new Label();
            l1.Text = "Pourcentage (100 = inchange, 110 = +10 %, 90 = -10 %) :";
            l1.Left = 12; l1.Top = 12; l1.Width = 390; l1.Height = 18;
            Controls.Add(l1);

            Pourcent = new NumericUpDown();
            Pourcent.Left = 12; Pourcent.Top = 32; Pourcent.Width = 90;
            Pourcent.Minimum = 1m; Pourcent.Maximum = 1000m;
            Pourcent.Value = POURCENT_DEFAUT; Pourcent.DecimalPlaces = 0;
            Controls.Add(Pourcent);

            ModePourcent = new RadioButton();
            ModePourcent.Text = "Allonger / raccourcir chaque evenement de ce pourcentage";
            ModePourcent.Left = 12; ModePourcent.Top = 66; ModePourcent.Width = 400;
            ModePourcent.Checked = MODE_POURCENT_DEFAUT;
            Controls.Add(ModePourcent);

            ModeRepartir = new RadioButton();
            ModeRepartir.Text = "Repartir la duree totale du bloc egalement entre les evenements";
            ModeRepartir.Left = 12; ModeRepartir.Top = 90; ModeRepartir.Width = 400;
            ModeRepartir.Checked = !MODE_POURCENT_DEFAUT;
            Controls.Add(ModeRepartir);

            AncrerFin = new CheckBox();
            AncrerFin.Text = "Ancrer sur la FIN du bloc (au lieu du debut)";
            AncrerFin.Left = 12; AncrerFin.Top = 120; AncrerFin.Width = 400;
            Controls.Add(AncrerFin);

            Button ok = new Button();
            ok.Text = "Appliquer"; ok.Left = 210; ok.Top = 160; ok.Width = 95;
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button ko = new Button();
            ko.Text = "Annuler"; ko.Left = 315; ko.Top = 160; ko.Width = 95;
            ko.DialogResult = DialogResult.Cancel;
            Controls.Add(ko);

            AcceptButton = ok; CancelButton = ko;
        }
    }

    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();
        Ligne("RAPPORT D'AJUSTEMENT DE DUREE");
        Ligne("=============================");
        Ligne("Date   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script : AjusterDureeSelection.cs  (VEGAS Pro 20)");
        Ligne("");

        // ---- selection ------------------------------------------------------
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
                + "Selectionne des evenements dans Vegas puis relance le script.",
                "AjusterDureeSelection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        long debutBloc = sel[0].Start.Nanos;
        long finBloc = sel[0].End.Nanos;
        double dureeAvant = 0;
        foreach (TrackEvent ev in sel)
        {
            if (ev.Start.Nanos < debutBloc) debutBloc = ev.Start.Nanos;
            if (ev.End.Nanos > finBloc) finBloc = ev.End.Nanos;
            dureeAvant += ev.Length.Nanos / 10000.0;
        }
        double dureeBlocAvant = (finBloc - debutBloc) / 10000.0;
        Ligne("bloc : " + FmtMs(debutBloc / 10000.0) + " -> " + FmtMs(finBloc / 10000.0)
              + "  (" + FmtMs(dureeBlocAvant) + ")");
        Ligne("somme des durees des evenements : " + FmtMs(dureeAvant));
        Ligne("");

        // ---- dialogue -------------------------------------------------------
        BoiteAjustement boite = new BoiteAjustement(sel.Count);
        if (boite.ShowDialog() != DialogResult.OK)
        {
            Ligne("Annule par l'utilisateur : rien n'a ete modifie.");
            EcrireRapport();
            return;
        }
        bool modeRepartir = boite.ModeRepartir.Checked;
        decimal pct = boite.Pourcent.Value;
        bool ancrerFin = boite.AncrerFin.Checked;

        Ligne("mode choisi      : " + (modeRepartir ? "REPARTIR EGALEMENT" : "POURCENTAGE " + pct + " %"));
        Ligne("ancrage          : " + (ancrerFin ? "fin du bloc" : "debut du bloc"));
        Ligne("");
        Ligne("--- DETAIL ---");

        int faits = 0;
        if (modeRepartir)
        {
            long total = finBloc - debutBloc;
            long parEvent = total / sel.Count;
            long courant = debutBloc;
            foreach (TrackEvent ev in sel)
            {
                long d = parEvent;
                if (courant + d > finBloc) d = finBloc - courant;
                Ligne("  \"" + Nom(ev) + "\" : " + FmtMs(ev.Length.Nanos / 10000.0)
                      + "  ->  " + FmtMs(d / 10000.0) + "   debut " + FmtMs(courant / 10000.0));
                try
                {
                    ev.Start = Timecode.FromNanos(courant);
                    ev.Length = Timecode.FromNanos(d);
                    faits++;
                }
                catch (Exception ex) { Ligne("    !! echec : " + ex.Message); }
                courant += d;
            }
        }
        else
        {
            double f = (double)pct / 100.0;
            if (ancrerFin)
            {
                for (int i = sel.Count - 1; i >= 0; i--)
                {
                    TrackEvent ev = sel[i];
                    long ancienneFin = ev.End.Nanos;
                    long nouvelleDuree = (long)Math.Round(ev.Length.Nanos * f);
                    if (nouvelleDuree < 1) nouvelleDuree = 1;
                    Ligne("  \"" + Nom(ev) + "\" : " + FmtMs(ev.Length.Nanos / 10000.0)
                          + "  ->  " + FmtMs(nouvelleDuree / 10000.0));
                    try
                    {
                        ev.Length = Timecode.FromNanos(nouvelleDuree);
                        ev.Start = Timecode.FromNanos(ancienneFin - nouvelleDuree);
                        faits++;
                    }
                    catch (Exception ex) { Ligne("    !! echec : " + ex.Message); }
                }
            }
            else
            {
                foreach (TrackEvent ev in sel)
                {
                    long nouvelleDuree = (long)Math.Round(ev.Length.Nanos * f);
                    if (nouvelleDuree < 1) nouvelleDuree = 1;
                    Ligne("  \"" + Nom(ev) + "\" : " + FmtMs(ev.Length.Nanos / 10000.0)
                          + "  ->  " + FmtMs(nouvelleDuree / 10000.0));
                    try
                    {
                        ev.Length = Timecode.FromNanos(nouvelleDuree);
                        faits++;
                    }
                    catch (Exception ex) { Ligne("    !! echec : " + ex.Message); }
                }
            }
        }

        // ---- apres ----------------------------------------------------------
        double d2 = 0; long db2 = long.MaxValue, fb2 = long.MinValue;
        foreach (TrackEvent ev in sel)
        {
            d2 += ev.Length.Nanos / 10000.0;
            if (ev.Start.Nanos < db2) db2 = ev.Start.Nanos;
            if (ev.End.Nanos > fb2) fb2 = ev.End.Nanos;
        }
        Ligne("");
        Ligne("--- RESUME ---");
        Ligne("evenements modifies     : " + faits + " / " + sel.Count);
        Ligne("somme des durees apres  : " + FmtMs((double)d2));
        Ligne("bloc apres              : " + FmtMs(db2 / 10000.0) + " -> " + FmtMs(fb2 / 10000.0)
              + "  (" + FmtMs((fb2 - db2) / 10000.0) + ")");
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        EcrireRapport();

        MessageBox.Show(
            "Ajustement termine.\n\n"
            + "Evenements modifies : " + faits + " / " + sel.Count + "\n"
            + "Somme des durees    : " + FmtMs(dureeAvant) + "  ->  " + FmtMs((double)d2) + "\n"
            + "Bloc                : " + FmtMs(dureeBlocAvant) + "  ->  " + FmtMs((fb2 - db2) / 10000.0) + "\n"
            + "\nRapport :\n" + cheminRapport,
            "AjusterDureeSelection", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
