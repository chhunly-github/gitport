package gitport;
import javax.swing.*;
import java.awt.*;

final class HintTextField extends JTextField {
    private final String hint;
    HintTextField(String hint){this.hint=hint;getDocument().addDocumentListener(new javax.swing.event.DocumentListener(){public void insertUpdate(javax.swing.event.DocumentEvent e){repaint();}public void removeUpdate(javax.swing.event.DocumentEvent e){repaint();}public void changedUpdate(javax.swing.event.DocumentEvent e){repaint();}});}
    @Override protected void paintComponent(Graphics graphics){super.paintComponent(graphics);if(getText().isEmpty()){Graphics2D g=(Graphics2D)graphics.create();TextRendering.apply(g);g.setColor(AtomTheme.MUTED);g.setFont(getFont());Insets insets=getInsets();g.drawString(hint,insets.left,(getHeight()-g.getFontMetrics().getHeight())/2+g.getFontMetrics().getAscent());g.dispose();}}
}
