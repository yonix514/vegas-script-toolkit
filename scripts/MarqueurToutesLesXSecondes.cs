// =============================================================================
//  MarqueurToutesLesXSecondes.cs   --   VEGAS Pro 20
// =============================================================================
//  A QUOI CA SERT :
//    pose un marqueur TOUTES LES X SECONDES sur la timeline.
//    Indispensable pour se reperer dans un montage de 15 minutes.
//
//  PARAMETRES :
//    INTERVALLE_SECONDES_DEFAUT : valeur proposee dans la boite de dialogue
//    (tu peux la changer a chaque lancement ; mettre 0 pour ne pas demander)
//
//  OPTIONS DE LA BOITE :
//    - intervalle en secondes
//    - "commencer a la position du curseur" (sinon : a 0:00)
//    - "limiter a la selection temporelle" (si une selection existe)
//
//  UTILISATION : Outils > Scripts > MarqueurToutesLesXSecondes
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

    const decimal INTERVALLE_SECONDES_DEFAUT = 10m;
    const bool DEMANDER_PAR_BOITE = true;

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
            cheminRapport = Path.Combine(DOSSIER_RAPPORTS, "rapport_marqueurs_reguliers.txt");
            File.WriteAllText(cheminRapport, log.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex) { cheminRapport = "(rapport non ecrit : " + ex.Message + ")"; }
    }

    // ---- boite de dialogue (sans System.Drawing) ---------------------------
    class BoiteIntervalle : Form
    {
        public NumericUpDown Intervalle;
        public CheckBox DepuisCurseur;
        public CheckBox LimiterSelection;

        public BoiteIntervalle(bool selectionExiste)
        {
            Text = "Marqueur toutes les X secondes";
            Width = 380; Height = 210;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;

            Label l1 = new Label();
            l1.Text = "Intervalle entre deux marqueurs (secondes) :";
            l1.Left = 12; l1.Top = 12; l1.Width = 340; l1.Height = 18;
            Controls.Add(l1);

            Intervalle = new NumericUpDown();
            Intervalle.Left = 12; Intervalle.Top = 32; Intervalle.Width = 110;
            Intervalle.Minimum = 0.1m; Intervalle.Maximum = 36000m;
            Intervalle.DecimalPlaces = 1; Intervalle.Increment = 1m;
            Intervalle.Value = INTERVALLE_SECONDES_DEFAUT;
            Controls.Add(Intervalle);

            DepuisCurseur = new CheckBox();
            DepuisCurseur.Text = "Commencer a la position du curseur";
            DepuisCurseur.Left = 12; DepuisCurseur.Top = 64; DepuisCurseur.Width = 340;
            Controls.Add(DepuisCurseur);

            LimiterSelection = new CheckBox();
            LimiterSelection.Text = "Limiter a la selection temporelle";
            LimiterSelection.Left = 12; LimiterSelection.Top = 88; LimiterSelection.Width = 340;
            LimiterSelection.Enabled = selectionExiste;
            LimiterSelection.Checked = selectionExiste;
            Controls.Add(LimiterSelection);

            Button ok = new Button();
            ok.Text = "Poser les marqueurs"; ok.Left = 160; ok.Top = 125; ok.Width = 130;
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button ko = new Button();
            ko.Text = "Annuler"; ko.Left = 296; ko.Top = 125; ko.Width = 60;
            ko.DialogResult = DialogResult.Cancel;
            Controls.Add(ko);

            AcceptButton = ok; CancelButton = ko;
        }
    }

    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        log = new StringBuilder();
        Ligne("RAPPORT - MARQUEURS REGULIERS");
        Ligne("=============================");
        Ligne("Date   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Ligne("Script : MarqueurToutesLesXSecondes.cs  (VEGAS Pro 20)");
        Ligne("");

        // ---- fin de la timeline --------------------------------------------
        long fin = project.Length.Nanos;
        foreach (Track t in project.Tracks)
            foreach (TrackEvent ev in t.Events)
                if (ev.End.Nanos > fin) fin = ev.End.Nanos;
        Ligne("fin de la timeline : " + FmtMs(fin / 10000.0)
              + "   (project.Length = " + FmtTC(project.Length) + ")");

        bool selectionExiste = vegas.SelectionLength.Nanos > 0;
        Ligne("selection temporelle : "
              + (selectionExiste ? FmtTC(vegas.SelectionStart) + " -> "
                  + FmtMs((vegas.SelectionStart.Nanos + vegas.SelectionLength.Nanos) / 10000.0)
                : "aucune"));
        Ligne("marqueurs existants  : " + project.Markers.Count);
        Ligne("");

        // ---- parametres -----------------------------------------------------
        decimal secondes = INTERVALLE_SECONDES_DEFAUT;
        bool depuisCurseur = false, limiter = false;
        if (DEMANDER_PAR_BOITE)
        {
            BoiteIntervalle b = new BoiteIntervalle(selectionExiste);
            if (b.ShowDialog() != DialogResult.OK)
            {
                Ligne("Annule par l'utilisateur : rien n'a ete fait.");
                EcrireRapport();
                return;
            }
            secondes = b.Intervalle.Value;
            depuisCurseur = b.DepuisCurseur.Checked;
            limiter = b.LimiterSelection.Checked && selectionExiste;
        }
        Ligne("intervalle demande : " + secondes.ToString(CultureInfo.InvariantCulture) + " s");
        Ligne("depart             : " + (depuisCurseur ? "curseur (" + FmtTC(vegas.Cursor) + ")" : "0:00"));
        Ligne("limite selection   : " + limiter);
        Ligne("");

        // ---- bornes ---------------------------------------------------------
        long debut = depuisCurseur ? vegas.Cursor.Nanos : 0L;
        long borneFin = fin;
        if (limiter)
        {
            debut = vegas.SelectionStart.Nanos;
            borneFin = vegas.SelectionStart.Nanos + vegas.SelectionLength.Nanos;
            if (depuisCurseur && vegas.Cursor.Nanos > debut) debut = vegas.Cursor.Nanos;
        }
        // 1 ms = 10000 unites de Timecode.Nanos (mesure dans VEGAS Pro 20)
        long pas = (long)Math.Round((double)secondes * 1000.0 * 10000.0);
        if (pas <= 0)
        {
            Ligne("ARRET : intervalle invalide.");
            EcrireRapport();
            MessageBox.Show("Intervalle invalide.", "MarqueurToutesLesXSecondes",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (debut >= borneFin)
        {
            Ligne("ARRET : la timeline (ou la selection) est vide : rien a marquer.");
            EcrireRapport();
            MessageBox.Show("Timeline vide (ou selection vide).\nAucun marqueur pose.",
                "MarqueurToutesLesXSecondes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // ---- pose des marqueurs --------------------------------------------
        Ligne("--- MARQUEURS POSES ---");
        int nb = 0;
        for (long pos = debut; pos < borneFin; pos += pas)
        {
            try
            {
                Timecode tc = Timecode.FromNanos(pos);
                Marker m = new Marker(tc, "T+" + FmtTC(tc));
                project.Markers.Add(m);
                nb++;
                Ligne(string.Format(CultureInfo.InvariantCulture, "  {0,4} : {1}", nb, FmtTC(tc)));
            }
            catch (Exception ex)
            {
                Ligne("  !! echec a " + FmtMs(pos / 10000.0) + " : " + ex.Message);
            }
        }

        Ligne("");
        Ligne("--- RESUME ---");
        Ligne("marqueurs poses      : " + nb);
        Ligne("marqueurs du projet  : " + project.Markers.Count);
        Ligne("");
        Ligne("FIN DU RAPPORT.");
        EcrireRapport();

        MessageBox.Show(
            "Marqueurs poses : " + nb + "\n\n"
            + "Un marqueur toutes les " + secondes.ToString(CultureInfo.InvariantCulture) + " s\n"
            + "de " + FmtMs(debut / 10000.0) + " a " + FmtMs(borneFin / 10000.0) + "\n"
            + "Total de marqueurs dans le projet : " + project.Markers.Count + "\n"
            + "\nRapport :\n" + cheminRapport,
            "MarqueurToutesLesXSecondes", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
