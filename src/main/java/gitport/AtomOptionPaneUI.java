package gitport;
import javax.swing.*;
import javax.swing.plaf.*;
import javax.swing.plaf.basic.*;

/** Applies physical UI fonts to lazily-created dialog labels and buttons as well. */
public final class AtomOptionPaneUI extends BasicOptionPaneUI {
    public static ComponentUI createUI(JComponent component){return new AtomOptionPaneUI();}
    @Override protected void installComponents(){super.installComponents();AtomTheme.polish(optionPane);}
}
