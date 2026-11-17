package gitport;

import javax.swing.*;
import javax.swing.border.EmptyBorder;
import java.awt.*;
import java.util.Properties;

/** Preview is local to the dialog. Only accepting Settings persists a choice. */
public final class FontSettings extends JPanel {
    final JComboBox<String> family=new JComboBox<>(EditorFonts.CHOICES);
    final JSpinner size=new JSpinner(new SpinnerNumberModel(13,11,22,1));
    final JComboBox<TextRendering.Mode> rendering=new JComboBox<>(TextRendering.Mode.values());
    final JTextArea preview=new JTextArea("// GitPort editor preview\nclass Workspace {\n    String branch = \"feature/fonts\";\n    int changes = 0123456789;\n    // Il1 O0 {} [] () != =>\n}");
    private final JLabel availability=new JLabel();
    public FontSettings(Properties settings){
        super(new BorderLayout(0,12));setBackground(AtomTheme.EDITOR);setBorder(new EmptyBorder(17,12,12,12));
        JPanel controls=new JPanel(new GridBagLayout());controls.setOpaque(false);
        GridBagConstraints c=new GridBagConstraints();c.gridx=0;c.gridy=0;c.weightx=1;c.fill=GridBagConstraints.HORIZONTAL;c.insets=new Insets(0,0,8,0);
        JLabel heading=new JLabel("Editor fonts");heading.setFont(AtomTheme.UI.deriveFont(Font.BOLD,15f));heading.setForeground(AtomTheme.BRIGHT);controls.add(heading,c);
        c.gridy++;controls.add(new JLabel("Diffs, history, activity and conflict text. Saved per profile."),c);
        JLabel familyLabel=new JLabel("Font family");familyLabel.setLabelFor(family);
        family.setSelectedItem(EditorFonts.selected(settings));family.setRenderer(new DefaultListCellRenderer(){
            @Override public Component getListCellRendererComponent(JList<?> list,Object value,int index,boolean selected,boolean focus){
                JLabel label=(JLabel)super.getListCellRendererComponent(list,value,index,selected,focus);
                String name=value.toString();label.setText(name+(EditorFonts.available(name)?"":" (not installed)"));return label;
            }
        });
        JLabel sizeLabel=new JLabel("Size (11–22 pt)");sizeLabel.setLabelFor(size);size.setValue(EditorFonts.size(settings));
        JPanel fontRow=new JPanel(new BorderLayout(12,0));fontRow.setOpaque(false);
        JPanel families=new JPanel(new BorderLayout(0,5));families.setOpaque(false);families.add(familyLabel,BorderLayout.NORTH);families.add(family,BorderLayout.CENTER);
        JPanel sizes=new JPanel(new BorderLayout(0,5));sizes.setOpaque(false);sizes.add(sizeLabel,BorderLayout.NORTH);sizes.add(size,BorderLayout.CENTER);sizes.setPreferredSize(new Dimension(110,60));
        fontRow.add(families,BorderLayout.CENTER);fontRow.add(sizes,BorderLayout.EAST);c.gridy++;controls.add(fontRow,c);
        JLabel renderingLabel=new JLabel("Text smoothing (whole interface)");renderingLabel.setLabelFor(rendering);c.gridy++;controls.add(renderingLabel,c);
        rendering.setSelectedItem(TextRendering.selected(settings));c.gridy++;controls.add(rendering,c);
        c.gridy++;availability.setForeground(AtomTheme.SECONDARY);controls.add(availability,c);
        add(controls,BorderLayout.NORTH);
        preview.setEditable(false);preview.setBackground(AtomTheme.INPUT);preview.setForeground(AtomTheme.TEXT);preview.setBorder(new EmptyBorder(10,12,10,12));preview.setTabSize(4);
        JScrollPane scroll=new JScrollPane(preview);scroll.setPreferredSize(new Dimension(510,170));add(scroll,BorderLayout.CENTER);
        JLabel note=new JLabel("JetBrains Mono, Fira Code and Source Code Pro are bundled.");note.setForeground(AtomTheme.SECONDARY);add(note,BorderLayout.SOUTH);
        family.addActionListener(e -> refreshPreview());size.addChangeListener(e -> refreshPreview());rendering.addActionListener(e -> refreshPreview());refreshPreview();
    }
    public String selectedFamily(){return (String)family.getSelectedItem();}
    public int selectedSize(){return ((Number)size.getValue()).intValue();}
    public TextRendering.Mode selectedRendering(){return (TextRendering.Mode)rendering.getSelectedItem();}
    public void refreshPreview(){preview.setFont(EditorFonts.resolve(selectedFamily(),selectedSize()));preview.putClientProperty(RenderingHints.KEY_TEXT_ANTIALIASING,TextRendering.hint(selectedRendering()));preview.repaint();availability.setText(EditorFonts.available(selectedFamily())?"Preview · "+selectedSize()+" pt":"Not installed here · preview uses "+AtomTheme.CODE.getFamily());}
}
