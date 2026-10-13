// =============================================================================
//  MarquerSelection.cs  --  VEGAS Pro 20
// =============================================================================
//  BUT : poser RAPIDEMENT un marqueur a la position du curseur, avec un libelle
//        demande dans une boite de dialogue. Outil d'ecoute : on lit la voix,
//        on clique dans la timeline a la frontiere de phrase, on lance le script
//        (Outils > Scripts > MarquerSelection) et on tape le libelle.
//
//  - boite de dialogue : le libelle (Entree = valider, Echap = annuler)
//  - si on valide VIDE  : libelle automatique "M001  0:12.500  (750 images)"
//  - le compteur est le nombre de marqueurs deja presents dans le projet + 1
//  - le marqueur est pose a la position EXACTE du curseur (vegas.Cursor)
//
//  Raccourci conseille : Options > Raccourcis clavier, chercher le script.
// =============================================================================

using System;
using System.Globalization;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        Project project = vegas.Project;
        Timecode curseur = vegas.Cursor;
        double ms = curseur.ToMilliseconds();
        double fps = project.Video.FrameRate;
        if (fps <= 0.0) fps = 60.0;

        // Numero de marqueur : nombre de marqueurs deja poses + 1
        int numero = 1;
        try { numero = project.Markers.Count + 1; } catch { }

        string auto = "M" + numero.ToString("000") + "  " + Hms(ms)
                    + "  (" + curseur.FrameCount + " images)";

        string libelle = null;
        try
        {
            using (var f = new Form())
            {
                f.Text = "MarquerSelection - marqueur au curseur";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.ClientSize = new System.Drawing.Size(430, 150);
                f.MinimizeBox = false;
                f.MaximizeBox = false;

                var l1 = new Label();
                l1.Text = "Position du curseur : " + Hms(ms) + "  (" + ms.ToString("0.000", CultureInfo.InvariantCulture)
                        + " ms, image " + curseur.FrameCount + ")";
                l1.SetBounds(12, 12, 406, 18);

                var l2 = new Label();
                l2.Text = "Libelle du marqueur (vide = libelle automatique) :";
                l2.SetBounds(12, 38, 406, 18);

                var t = new TextBox();
                t.SetBounds(12, 60, 406, 22);
                t.Text = "";

                var ok = new Button();
                ok.Text = "Poser le marqueur";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(200, 100, 130, 28);

                var ko = new Button();
                ko.Text = "Annuler";
                ko.DialogResult = DialogResult.Cancel;
                ko.SetBounds(336, 100, 82, 28);

                f.Controls.Add(l1); f.Controls.Add(l2); f.Controls.Add(t);
                f.Controls.Add(ok); f.Controls.Add(ko);
                f.AcceptButton = ok;
                f.CancelButton = ko;

                if (f.ShowDialog() == DialogResult.OK) libelle = t.Text;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Boite de dialogue impossible : " + ex.Message
                + "\n\nUn marqueur automatique va etre pose.", "MarquerSelection.cs");
            libelle = "";
        }

        if (libelle == null) { vegas.LogEvent("MarquerSelection : annule."); return; }   // Echap

        libelle = libelle.Trim();
        if (libelle.Length == 0) libelle = auto;

        try
        {
            project.Markers.Add(new Marker(new Timecode(ms), libelle));
            vegas.LogEvent("MarquerSelection : marqueur \"" + libelle + "\" a " + Hms(ms) + ".");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Marqueur refuse : " + ex.Message, "MarquerSelection.cs");
        }
    }

    static string Hms(double ms)
    {
        long t = (long)Math.Round(ms);
        long h = t / 3600000; t -= h * 3600000;
        long m = t / 60000;   t -= m * 60000;
        double s = t / 1000.0;
        return h.ToString(CultureInfo.InvariantCulture) + ":"
             + m.ToString("00", CultureInfo.InvariantCulture) + ":"
             + s.ToString("00.000", CultureInfo.InvariantCulture);
    }
}
