// =============================================================================
//  AideRaccourcis.cs   --   VEGAS Pro 20
// =============================================================================
//  A QUOI CA SERT :
//    affiche une boite de dialogue avec LES RACCOURCIS VEGAS utiles pour le
//    workflow "images sur voix off" (M = marqueur, S = couper, ...), pour
//    avoir les gestes sous les yeux.
//    Une copie du texte est aussi ecrite dans le dossier "rapports" pour
//    pouvoir l'imprimer.
//
//  UTILISATION : Outils > Scripts > AideRaccourcis
// =============================================================================

using System;
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

    static string Texte()
    {
        StringBuilder s = new StringBuilder();
        s.AppendLine("RACCOURCIS VEGAS A CONNAITRE (workflow images sur voix off)");
        s.AppendLine("==========================================================");
        s.AppendLine("");
        s.AppendLine("  M ................. pose un MARQUEUR a la position du curseur");
        s.AppendLine("                      (c'est LA touche du workflow : tu ecoutes la");
        s.AppendLine("                       voix off et tu appuies sur M a chaque");
        s.AppendLine("                       changement d'image)");
        s.AppendLine("  S ................. COUPE l'evenement sous le curseur (en deux)");
        s.AppendLine("  Suppr ............. supprime la selection");
        s.AppendLine("  Ctrl + Z .......... annuler");
        s.AppendLine("  Ctrl + Y .......... retablir (refaire)");
        s.AppendLine("  Ctrl + S .......... sauvegarder le projet");
        s.AppendLine("  G ................. GROUPE les evenements selectionnes");
        s.AppendLine("  Maj + G ........... DEGROUPE");
        s.AppendLine("  Alt + clic-glisser  copie un evenement (au lieu de le deplacer)");
        s.AppendLine("  <- / -> ........... reculer / avancer d'une IMAGE");
        s.AppendLine("  Maj + <- / -> ..... reculer / avancer de 10 images");
        s.AppendLine("  Ctrl + <- / -> .... reculer / avancer d'un point de montage");
        s.AppendLine("  Flèche haut/bas ... passer d'une piste a l'autre");
        s.AppendLine("  Espace ............ lecture / pause");
        s.AppendLine("  L ................. lecture en boucle de la selection");
        s.AppendLine("  Ctrl + Maj + M .... aller au marqueur suivant (Ctrl + M : precedent)");
        s.AppendLine("  Ctrl + molette .... zoomer / dezoomer la timeline");
        s.AppendLine("  Souris milieu...... deplacer la vue de la timeline");
        s.AppendLine("");
        s.AppendLine("POURQUOI CES RACCOURCIS ICI ?");
        s.AppendLine("  1. tu importes / prepares tes images");
        s.AppendLine("  2. tu ECOUTES la voix et tu appuies sur M a chaque changement d'image");
        s.AppendLine("  3. tu lances le script AlignerImagesSurMarqueurs");
        s.AppendLine("     -> chaque image est posee EXACTEMENT entre deux marqueurs");
        s.AppendLine("  4. tu ajustes a la main si besoin (S, Suppr, Ctrl+Z)");
        s.AppendLine("  5. tu lances FonduAuto pour les transitions");
        s.AppendLine("  6. tu lances ZoomAutoVegas pour les animations");
        s.AppendLine("  7. tu lances RapportTimeline pour verifier avant l'export");
        s.AppendLine("  8. tu exportes");
        s.AppendLine("");
        s.AppendLine("POUR DONNER UN RACCOURCI CLAVIER A UN SCRIPT :");
        s.AppendLine("  Options > Preferences > onglet Clavier (ou 'Raccourcis clavier'),");
        s.AppendLine("  cherche le nom du script, choisis une combinaison, valide.");
        s.AppendLine("");
        return s.ToString();
    }

    public void FromVegas(Vegas vegas)
    {
        string texte = Texte();
        string chemin = "";
        try
        {
            if (!Directory.Exists(DOSSIER_RAPPORTS)) Directory.CreateDirectory(DOSSIER_RAPPORTS);
            chemin = Path.Combine(DOSSIER_RAPPORTS, "AIDE-RACCOURCIS.txt");
            File.WriteAllText(chemin, texte, new UTF8Encoding(true));
        }
        catch (Exception ex) { chemin = "(copie non ecrite : " + ex.Message + ")"; }

        MessageBox.Show(texte + "\nCopie texte :\n" + chemin,
            "AideRaccourcis - raccourcis Vegas utiles",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
