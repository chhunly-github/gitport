package gitport;

import javax.swing.*;
import javax.swing.border.*;
import javax.swing.tree.*;
import javax.swing.event.*;
import java.awt.*;
import java.awt.event.*;
import java.nio.file.*;
import java.util.*;
import java.util.function.*;

public final class ProjectExplorer extends JPanel {
    private record Entry(String group,String path,String label,int count){public boolean isGroup(){return path==null;}}
    private final RepositoryCatalog catalog;
    private final Consumer<Path> open;
    private final Consumer<String> error;
    private final JTree tree=new JTree();
    private final JTextField filter=new HintTextField("Find repository...");
    private boolean rebuilding;
    private String active="",branch="";
    private int changeCount;
    public ProjectExplorer(Portable portable,Consumer<Path> open,Runnable add,Consumer<String> error){
        super(new BorderLayout());this.catalog=new RepositoryCatalog(portable);this.open=open;this.error=error;setBackground(AtomTheme.TREE);setMinimumSize(new Dimension(225,200));
        JPanel header=new JPanel(new BorderLayout());header.setBackground(AtomTheme.TREE);header.setBorder(new EmptyBorder(12,14,8,8));JLabel label=new JLabel("PROJECT EXPLORER");label.setFont(AtomTheme.UI.deriveFont(Font.BOLD,12f));label.setForeground(AtomTheme.SECONDARY);header.add(label,BorderLayout.CENTER);
        JPanel tools=new JPanel(new FlowLayout(FlowLayout.RIGHT,3,0));tools.setOpaque(false);tools.add(iconButton("folder","New repository group",this::newGroup));tools.add(iconButton("plus","Open repository",add));header.add(tools,BorderLayout.EAST);
        JPanel search=new JPanel(new BorderLayout(6,0));search.setBackground(AtomTheme.TREE);search.setBorder(new EmptyBorder(0,12,10,12));JLabel glass=new JLabel(Icons.get("search",AtomTheme.MUTED,15));search.add(glass,BorderLayout.WEST);filter.setToolTipText("Filter repositories by name, path or group");filter.putClientProperty("JTextField.placeholderText","Find repository...");filter.setFont(AtomTheme.UI);filter.setBackground(AtomTheme.HEADER);search.add(filter,BorderLayout.CENTER);
        JPanel top=new JPanel(new BorderLayout());top.setOpaque(false);top.add(header,BorderLayout.NORTH);top.add(search,BorderLayout.SOUTH);add(top,BorderLayout.NORTH);
        tree.setRootVisible(false);tree.setShowsRootHandles(true);tree.setRowHeight(32);tree.setBorder(new EmptyBorder(0,6,6,6));tree.setBackground(AtomTheme.TREE);tree.setFont(AtomTheme.UI);tree.getSelectionModel().setSelectionMode(TreeSelectionModel.SINGLE_TREE_SELECTION);tree.setToggleClickCount(2);tree.setCellRenderer(new Renderer());
        JScrollPane scroll=new JScrollPane(tree);scroll.setBorder(new EmptyBorder(0,0,0,0));scroll.getViewport().setBackground(AtomTheme.TREE);add(scroll,BorderLayout.CENTER);
        JLabel tip=new JLabel("Double-click: open  /  Right-click: organize");tip.setFont(AtomTheme.UI.deriveFont(12f));tip.setForeground(AtomTheme.MUTED);tip.setBorder(new EmptyBorder(8,12,8,4));tip.setToolTipText("Double-click a repository to open it. Right-click to organize groups and bookmarks.");add(tip,BorderLayout.SOUTH);
        filter.getDocument().addDocumentListener(new DocumentListener(){public void insertUpdate(DocumentEvent e){reload();}public void removeUpdate(DocumentEvent e){reload();}public void changedUpdate(DocumentEvent e){reload();}});
        ToolTipManager.sharedInstance().registerComponent(tree);
        tree.addMouseListener(new MouseAdapter(){public void mouseClicked(MouseEvent e){if(e.getClickCount()==2&&SwingUtilities.isLeftMouseButton(e))openSelected();}public void mousePressed(MouseEvent e){popup(e);}public void mouseReleased(MouseEvent e){popup(e);}});
        tree.getInputMap().put(KeyStroke.getKeyStroke(KeyEvent.VK_ENTER,0),"openRepo");tree.getActionMap().put("openRepo",new AbstractAction(){public void actionPerformed(ActionEvent e){openSelected();}});
        tree.addTreeExpansionListener(new TreeExpansionListener(){public void treeExpanded(TreeExpansionEvent e){collapse(e,false);}public void treeCollapsed(TreeExpansionEvent e){collapse(e,true);}});
        reload();
    }
    private void collapse(TreeExpansionEvent event,boolean collapsed){if(rebuilding||!filter.getText().isBlank())return;Object node=((DefaultMutableTreeNode)event.getPath().getLastPathComponent()).getUserObject();if(node instanceof Entry entry&&entry.isGroup())try{catalog.portable.settings.setProperty("group."+entry.group+".collapsed",Boolean.toString(collapsed));catalog.portable.save();}catch(Exception ex){error.accept(ex.getMessage());}}
    private JButton iconButton(String icon,String tip,Runnable action){JButton b=new JButton(Icons.get(icon,AtomTheme.SECONDARY,16));AtomTheme.button(b,false);b.setBackground(AtomTheme.TREE);b.setBorder(new EmptyBorder(4,5,4,5));b.setToolTipText(tip);b.addActionListener(e -> action.run());return b;}
    public void active(String path,String branch,int changes){active=path;this.branch=branch;changeCount=changes;reload();}
    public void reload(){rebuilding=true;String query=filter.getText().strip().toLowerCase(Locale.ROOT);DefaultMutableTreeNode root=new DefaultMutableTreeNode("Repositories");
        for(RepositoryCatalog.Group group:catalog.groups()){
            var paths=catalog.repositories().stream().filter(p -> catalog.groupOf(p).equals(group.id())).filter(p -> query.isEmpty()||(catalog.nameOf(p)+" "+p+" "+group.name()).toLowerCase(Locale.ROOT).contains(query)).toList();
            if(!query.isEmpty()&&paths.isEmpty())continue;
            DefaultMutableTreeNode groupNode=new DefaultMutableTreeNode(new Entry(group.id(),null,group.name(),paths.size()));
            for(String path:paths)groupNode.add(new DefaultMutableTreeNode(new Entry(group.id(),path,catalog.nameOf(path),0)));
            root.add(groupNode);
        }
        tree.setModel(new DefaultTreeModel(root));for(int i=0;i<root.getChildCount();i++){DefaultMutableTreeNode node=(DefaultMutableTreeNode)root.getChildAt(i);Entry entry=(Entry)node.getUserObject();if(!query.isEmpty()||!Boolean.parseBoolean(catalog.portable.settings.getProperty("group."+entry.group+".collapsed","false")))tree.expandPath(new TreePath(node.getPath()));}
        rebuilding=false;
    }
    private Entry selected(){Object node=tree.getLastSelectedPathComponent();return node instanceof DefaultMutableTreeNode n&&n.getUserObject() instanceof Entry e?e:null;}
    private void openSelected(){Entry e=selected();if(e!=null&&!e.isGroup())open.accept(catalog.portable.resolve(e.path));}
    private void newGroup(){String name=JOptionPane.showInputDialog(this,"Group name","New repository group",JOptionPane.PLAIN_MESSAGE);if(name!=null)execute(() -> catalog.createGroup(name));}
    @FunctionalInterface private interface Change{void run()throws Exception;}
    private void execute(Change change){try{change.run();reload();}catch(Exception ex){error.accept(ex.getMessage());}}
    private void popup(MouseEvent e){if(!e.isPopupTrigger())return;TreePath row=tree.getPathForLocation(e.getX(),e.getY());if(row!=null)tree.setSelectionPath(row);Entry entry=selected();JPopupMenu menu=new JPopupMenu();
        if(entry!=null&&!entry.isGroup()){
            item(menu,"Open repository",this::openSelected);JMenu move=new JMenu("Move to group");for(var group:catalog.groups()){JMenuItem m=new JMenuItem(group.name(),Icons.get("folder",AtomTheme.GOLD,15));m.addActionListener(event -> execute(() -> catalog.move(entry.path,group.id())));move.add(m);}menu.add(move);
            item(menu,"Rename bookmark...",() -> {String name=JOptionPane.showInputDialog(this,"Display name (folder name stays unchanged)",entry.label);if(name!=null)execute(() -> catalog.renameRepository(entry.path,name));});
            menu.addSeparator();item(menu,"Remove bookmark...",() -> {if(JOptionPane.showConfirmDialog(this,"Remove this bookmark?\nRepository files will remain on disk.","Remove bookmark",JOptionPane.OK_CANCEL_OPTION)==JOptionPane.OK_OPTION)execute(() -> catalog.forget(entry.path));});
        }else if(entry!=null&&!entry.group.equals("ungrouped")){
            item(menu,"Rename group...",() -> {String name=JOptionPane.showInputDialog(this,"Group name",entry.label);if(name!=null)execute(() -> catalog.renameGroup(entry.group,name));});
            item(menu,"Remove group...",() -> {if(JOptionPane.showConfirmDialog(this,"Remove the group and move its bookmarks to Ungrouped?\nRepository files stay in place.","Remove group",JOptionPane.OK_CANCEL_OPTION)==JOptionPane.OK_OPTION)execute(() -> catalog.removeGroup(entry.group));});
        }
        menu.addSeparator();item(menu,"New group...",this::newGroup);menu.show(tree,e.getX(),e.getY());
    }
    private void item(JPopupMenu menu,String label,Runnable action){JMenuItem item=new JMenuItem(label);item.addActionListener(e -> action.run());menu.add(item);}
    private final class Renderer extends JPanel implements TreeCellRenderer{
        private final JLabel icon=new JLabel(),name=new JLabel(),detail=new JLabel();
        Renderer(){super(new BorderLayout(7,0));setBorder(new EmptyBorder(0,4,0,9));add(icon,BorderLayout.WEST);add(name,BorderLayout.CENTER);add(detail,BorderLayout.EAST);}
        public Component getTreeCellRendererComponent(JTree t,Object value,boolean selected,boolean expanded,boolean leaf,int row,boolean focus){
            Object raw=((DefaultMutableTreeNode)value).getUserObject();if(!(raw instanceof Entry e))return new JLabel("");boolean current=e.path!=null&&e.path.equals(active);
            setBackground(selected?AtomTheme.SELECT:current?AtomTheme.c(0x2C333F):AtomTheme.TREE);name.setForeground(selected?AtomTheme.BRIGHT:AtomTheme.TEXT);detail.setForeground(selected?AtomTheme.BRIGHT:AtomTheme.SECONDARY);
            name.setFont(AtomTheme.UI.deriveFont(e.isGroup()||current?Font.BOLD:Font.PLAIN));detail.setFont(AtomTheme.UI.deriveFont(12f));name.setText(e.label);icon.setIcon(Icons.get(e.isGroup()?"folder":"repo",e.isGroup()?AtomTheme.GOLD:current?AtomTheme.BLUE:AtomTheme.SECONDARY,17));
            detail.setText(e.isGroup()?Integer.toString(e.count):current?(changeCount>0?Integer.toString(changeCount):branch):"");setToolTipText(e.isGroup()?e.label:catalog.portable.resolve(e.path).toString());return this;
        }
    }
}
