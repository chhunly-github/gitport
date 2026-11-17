package gitport;

import javax.swing.*;
import javax.swing.border.*;
import javax.swing.event.*;
import javax.swing.tree.*;
import java.awt.*;
import java.awt.event.*;
import java.io.IOException;
import java.nio.file.*;
import java.util.*;
import java.util.List;
import java.util.function.*;

/** WebStorm-style Git Branches and Worktrees explorer. */
public final class BranchExplorer extends JPanel {
    public record BranchNode(
        String fullRef,
        String displayName,
        boolean isFolder,
        boolean isLocal,
        boolean isRemote,
        boolean isCurrent,
        Git.Worktree worktree
    ) {
        public boolean isWorktree() {
            return worktree != null || fullRef.contains("worktree-") || fullRef.contains("worktree/") || fullRef.startsWith("agents/") || displayName.contains("worktree-");
        }
    }

    private final Consumer<Path> openRepo;
    private final Consumer<String> switchBranch;
    private final Consumer<String> newBranch;
    private final Consumer<String> newWorktree;
    private final BiConsumer<String, Boolean> mergeBranch;
    private final Consumer<String> deleteBranch;
    private final Consumer<Git.Worktree> removeWorktree;
    private final Runnable fetchRemotes;
    private final BiFunction<String, Path, Path> pickFolder;
    private final Consumer<String> log;
    private final Consumer<String> error;

    private final JTree tree = new JTree(new DefaultTreeModel(new DefaultMutableTreeNode("Root")));
    private final JTextField filter = new HintTextField("Find branch or worktree...");
    private Path currentRepo;
    private String currentBranch = "";
    private List<String> allRefs = new ArrayList<>();
    private List<Git.Worktree> allWorktrees = new ArrayList<>();
    private boolean rebuilding;

    public BranchExplorer(
        Consumer<Path> openRepo,
        Consumer<String> switchBranch,
        Consumer<String> newBranch,
        Consumer<String> newWorktree,
        BiConsumer<String, Boolean> mergeBranch,
        Consumer<String> deleteBranch,
        Consumer<Git.Worktree> removeWorktree,
        Runnable fetchRemotes,
        BiFunction<String, Path, Path> pickFolder,
        Consumer<String> log,
        Consumer<String> error
    ) {
        super(new BorderLayout(0, 6));
        this.openRepo = openRepo;
        this.switchBranch = switchBranch;
        this.newBranch = newBranch;
        this.newWorktree = newWorktree;
        this.mergeBranch = mergeBranch;
        this.deleteBranch = deleteBranch;
        this.removeWorktree = removeWorktree;
        this.fetchRemotes = fetchRemotes;
        this.pickFolder = pickFolder;
        this.log = log;
        this.error = error;

        setBackground(AtomTheme.TREE);
        setBorder(new EmptyBorder(8, 10, 8, 10));

        // Header panel
        JPanel header = new JPanel(new BorderLayout());
        header.setOpaque(false);
        JLabel titleLabel = new JLabel("BRANCHES & WORKTREES");
        titleLabel.setFont(AtomTheme.UI.deriveFont(Font.BOLD, 12f));
        titleLabel.setForeground(AtomTheme.SECONDARY);
        header.add(titleLabel, BorderLayout.CENTER);

        JPanel tools = new JPanel(new FlowLayout(FlowLayout.RIGHT, 2, 0));
        tools.setOpaque(false);
        JButton fetchBtn = iconButton("fetch", "Fetch all remotes", fetchRemotes);
        JButton newBtn = new JButton(Icons.get("plus", AtomTheme.SECONDARY, 15));
        AtomTheme.button(newBtn, false);
        newBtn.setBackground(AtomTheme.TREE);
        newBtn.setBorder(new EmptyBorder(3, 4, 3, 4));
        newBtn.setToolTipText("New branch or worktree...");
        newBtn.addActionListener(e -> showCreateMenu(newBtn));
        tools.add(fetchBtn);
        tools.add(newBtn);
        header.add(tools, BorderLayout.EAST);

        // Search filter panel
        JPanel search = new JPanel(new BorderLayout(6, 0));
        search.setBackground(AtomTheme.TREE);
        search.setBorder(new EmptyBorder(3, 2, 6, 2));
        JLabel glass = new JLabel(Icons.get("search", AtomTheme.MUTED, 14));
        search.add(glass, BorderLayout.WEST);
        filter.setFont(AtomTheme.UI.deriveFont(12f));
        filter.setBackground(AtomTheme.HEADER);
        search.add(filter, BorderLayout.CENTER);

        JPanel top = new JPanel(new BorderLayout(0, 4));
        top.setOpaque(false);
        top.add(header, BorderLayout.NORTH);
        top.add(search, BorderLayout.SOUTH);
        add(top, BorderLayout.NORTH);

        // Tree configuration
        tree.setRootVisible(false);
        tree.setShowsRootHandles(true);
        tree.setRowHeight(28);
        tree.setBorder(new EmptyBorder(0, 2, 4, 2));
        tree.setBackground(AtomTheme.TREE);
        tree.setFont(AtomTheme.UI.deriveFont(12f));
        tree.getSelectionModel().setSelectionMode(TreeSelectionModel.SINGLE_TREE_SELECTION);
        tree.setCellRenderer(new TreeRenderer());

        JScrollPane scroll = new JScrollPane(tree);
        scroll.setBorder(new EmptyBorder(0, 0, 0, 0));
        scroll.getViewport().setBackground(AtomTheme.TREE);
        add(scroll, BorderLayout.CENTER);

        // Bottom quick action toolbar
        JPanel bottomBar = new JPanel(new GridLayout(1, 3, 5, 0));
        bottomBar.setOpaque(false);
        JButton switchBtn = new JButton("Checkout");
        AtomTheme.button(switchBtn, false);
        switchBtn.addActionListener(e -> checkoutSelected());

        JButton worktreeBtn = new JButton("+ Worktree");
        AtomTheme.button(worktreeBtn, false);
        worktreeBtn.addActionListener(e -> newWorktreeFromSelected());

        JButton mergeBtn = new JButton("Merge...");
        AtomTheme.button(mergeBtn, false);
        mergeBtn.addActionListener(e -> mergeSelected(false));

        bottomBar.add(switchBtn);
        bottomBar.add(worktreeBtn);
        bottomBar.add(mergeBtn);
        add(bottomBar, BorderLayout.SOUTH);

        // Events
        filter.getDocument().addDocumentListener(new DocumentListener() {
            public void insertUpdate(DocumentEvent e) { rebuildTree(); }
            public void removeUpdate(DocumentEvent e) { rebuildTree(); }
            public void changedUpdate(DocumentEvent e) { rebuildTree(); }
        });

        tree.addMouseListener(new MouseAdapter() {
            public void mouseClicked(MouseEvent e) {
                if (e.getClickCount() == 2 && SwingUtilities.isLeftMouseButton(e)) {
                    activateSelected();
                }
            }
            public void mousePressed(MouseEvent e) { popup(e); }
            public void mouseReleased(MouseEvent e) { popup(e); }
        });

        tree.getInputMap().put(KeyStroke.getKeyStroke(KeyEvent.VK_ENTER, 0), "activateBranch");
        tree.getActionMap().put("activateBranch", new AbstractAction() {
            public void actionPerformed(ActionEvent e) { activateSelected(); }
        });
    }

    private JButton iconButton(String icon, String tooltip, Runnable action) {
        JButton b = new JButton(Icons.get(icon, AtomTheme.SECONDARY, 15));
        AtomTheme.button(b, false);
        b.setBackground(AtomTheme.TREE);
        b.setBorder(new EmptyBorder(3, 4, 3, 4));
        b.setToolTipText(tooltip);
        b.addActionListener(e -> action.run());
        return b;
    }

    public void update(Path repo, String currentBranch, String rawRefs, List<Git.Worktree> worktrees) {
        this.currentRepo = repo;
        this.currentBranch = currentBranch != null ? currentBranch.strip() : "";
        this.allWorktrees = worktrees != null ? worktrees : Collections.emptyList();
        this.allRefs = new ArrayList<>();
        if (rawRefs != null) {
            for (String r : rawRefs.split("\n")) {
                String line = r.strip();
                if (!line.isEmpty() && !line.endsWith("/HEAD") && !line.equals("origin") && !line.equals("upstream") && !line.equals("HEAD")) {
                    this.allRefs.add(line);
                }
            }
        }
        rebuildTree();
    }

    public String selectedBranch() {
        BranchNode node = selectedNode();
        if (node == null || node.isFolder) return null;
        return node.fullRef;
    }

    private BranchNode selectedNode() {
        Object item = tree.getLastSelectedPathComponent();
        if (item instanceof DefaultMutableTreeNode n && n.getUserObject() instanceof BranchNode b) {
            return b;
        }
        return null;
    }

    private void rebuildTree() {
        rebuilding = true;
        String query = filter.getText().strip().toLowerCase(Locale.ROOT);
        DefaultMutableTreeNode root = new DefaultMutableTreeNode("Root");

        // Map worktrees to branch ref names
        Map<String, Git.Worktree> worktreeByBranch = new HashMap<>();
        for (Git.Worktree wt : allWorktrees) {
            String b = wt.displayBranch();
            if (b != null && !b.isEmpty()) {
                worktreeByBranch.put(b, wt);
                worktreeByBranch.put("refs/heads/" + b, wt);
            }
        }

        // 1. HEAD (Current branch) node
        if (!currentBranch.isEmpty()) {
            boolean isCurMatch = query.isEmpty() || currentBranch.toLowerCase(Locale.ROOT).contains(query);
            if (isCurMatch) {
                DefaultMutableTreeNode headGroup = new DefaultMutableTreeNode(
                    new BranchNode("", "HEAD (Current Branch)", true, false, false, false, null)
                );
                Git.Worktree curWt = worktreeByBranch.get(currentBranch);
                headGroup.add(new DefaultMutableTreeNode(
                    new BranchNode(currentBranch, currentBranch, false, true, false, true, curWt)
                ));
                root.add(headGroup);
            }
        }

        // 2. Local and Remote branches
        List<String> localRefs = new ArrayList<>();
        List<String> remoteRefs = new ArrayList<>();
        for (String raw : allRefs) {
            String ref = raw.strip();
            if (ref.isEmpty() || ref.endsWith("/HEAD") || ref.equals("origin") || ref.equals("upstream") || ref.equals("HEAD")) continue;
            if (ref.startsWith("refs/remotes/")) {
                remoteRefs.add(ref.substring("refs/remotes/".length()));
            } else if (ref.startsWith("refs/heads/")) {
                localRefs.add(ref.substring("refs/heads/".length()));
            } else if (ref.startsWith("origin/") || ref.startsWith("upstream/")) {
                remoteRefs.add(ref);
            } else {
                localRefs.add(ref);
            }
        }

        // Build Local hierarchy
        DefaultMutableTreeNode localGroup = new DefaultMutableTreeNode(
            new BranchNode("", "Local", true, true, false, false, null)
        );
        Map<String, DefaultMutableTreeNode> localFolders = new HashMap<>();

        for (String raw : localRefs) {
            String name = raw.startsWith("refs/heads/") ? raw.substring(11) : raw;
            if (!query.isEmpty() && !name.toLowerCase(Locale.ROOT).contains(query)) continue;

            Git.Worktree wt = worktreeByBranch.get(name);
            boolean isCur = name.equals(currentBranch);

            if (name.contains("/")) {
                int lastSlash = name.lastIndexOf('/');
                String folderPath = name.substring(0, lastSlash);
                String leafName = name.substring(lastSlash + 1);

                DefaultMutableTreeNode parent = getOrCreateFolder(localGroup, localFolders, folderPath, true);
                parent.add(new DefaultMutableTreeNode(
                    new BranchNode(name, leafName, false, true, false, isCur, wt)
                ));
            } else {
                localGroup.add(new DefaultMutableTreeNode(
                    new BranchNode(name, name, false, true, false, isCur, wt)
                ));
            }
        }
        if (localGroup.getChildCount() > 0) root.add(localGroup);

        // 3. Remote branches
        DefaultMutableTreeNode remoteGroup = new DefaultMutableTreeNode(
            new BranchNode("", "Remote", true, false, true, false, null)
        );
        Map<String, DefaultMutableTreeNode> remoteFolders = new HashMap<>();

        for (String raw : remoteRefs) {
            String name = raw.startsWith("refs/remotes/") ? raw.substring(13) : raw;
            if (!query.isEmpty() && !name.toLowerCase(Locale.ROOT).contains(query)) continue;

            if (name.contains("/")) {
                int lastSlash = name.lastIndexOf('/');
                String folderPath = name.substring(0, lastSlash);
                String leafName = name.substring(lastSlash + 1);

                DefaultMutableTreeNode parent = getOrCreateFolder(remoteGroup, remoteFolders, folderPath, false);
                parent.add(new DefaultMutableTreeNode(
                    new BranchNode(name, leafName, false, false, true, false, null)
                ));
            } else {
                remoteGroup.add(new DefaultMutableTreeNode(
                    new BranchNode(name, name, false, false, true, false, null)
                ));
            }
        }
        if (remoteGroup.getChildCount() > 0) root.add(remoteGroup);

        tree.setModel(new DefaultTreeModel(root));

        // Auto-expand all matching branches
        for (int i = 0; i < root.getChildCount(); i++) {
            DefaultMutableTreeNode n = (DefaultMutableTreeNode) root.getChildAt(i);
            expandNode(n);
        }
        rebuilding = false;
    }

    private void expandNode(DefaultMutableTreeNode node) {
        tree.expandPath(new TreePath(node.getPath()));
        for (int i = 0; i < node.getChildCount(); i++) {
            TreeNode child = node.getChildAt(i);
            if (!child.isLeaf()) expandNode((DefaultMutableTreeNode) child);
        }
    }

    private DefaultMutableTreeNode getOrCreateFolder(
        DefaultMutableTreeNode root,
        Map<String, DefaultMutableTreeNode> folderMap,
        String folderPath,
        boolean isLocal
    ) {
        if (folderMap.containsKey(folderPath)) return folderMap.get(folderPath);

        String[] parts = folderPath.split("/");
        DefaultMutableTreeNode current = root;
        StringBuilder accumulated = new StringBuilder();

        for (String part : parts) {
            if (accumulated.length() > 0) accumulated.append("/");
            accumulated.append(part);
            String key = accumulated.toString();

            if (folderMap.containsKey(key)) {
                current = folderMap.get(key);
            } else {
                DefaultMutableTreeNode newNode = new DefaultMutableTreeNode(
                    new BranchNode(key, part, true, isLocal, !isLocal, false, null)
                );
                current.add(newNode);
                folderMap.put(key, newNode);
                current = newNode;
            }
        }
        return current;
    }

    private void activateSelected() {
        BranchNode node = selectedNode();
        if (node == null) return;
        if (node.isFolder) {
            TreePath p = tree.getSelectionPath();
            if (p != null) {
                if (tree.isExpanded(p)) tree.collapsePath(p);
                else tree.expandPath(p);
            }
            return;
        }
        if (node.isWorktree() && node.worktree.path() != null) {
            if (currentRepo != null && node.worktree.path().toAbsolutePath().equals(currentRepo.toAbsolutePath())) {
                log.accept("Worktree " + node.displayName + " is already active in this window.");
                return;
            }
            openRepo.accept(node.worktree.path());
        } else {
            checkoutSelected();
        }
    }

    private void checkoutSelected() {
        BranchNode node = selectedNode();
        if (node == null || node.isFolder) {
            JOptionPane.showMessageDialog(this, "Select a branch to checkout.");
            return;
        }
        if (node.isCurrent) {
            log.accept("Already on branch " + node.fullRef);
            return;
        }
        if (node.isWorktree()) {
            int opt = JOptionPane.showConfirmDialog(
                this,
                "Branch '" + node.displayName + "' is checked out in worktree:\n" + node.worktree.path() + "\n\nSwitch GitPort to that worktree directory?",
                "Open Worktree",
                JOptionPane.YES_NO_OPTION,
                JOptionPane.QUESTION_MESSAGE
            );
            if (opt == JOptionPane.YES_OPTION) {
                openRepo.accept(node.worktree.path());
            }
            return;
        }
        switchBranch.accept(node.fullRef);
    }

    private void newWorktreeFromSelected() {
        BranchNode node = selectedNode();
        String baseRef = node != null && !node.isFolder ? node.fullRef : currentBranch;
        newWorktree.accept(baseRef);
    }

    private void mergeSelected(boolean fetchFirst) {
        BranchNode node = selectedNode();
        if (node == null || node.isFolder) {
            JOptionPane.showMessageDialog(this, "Select a branch to merge into current HEAD.");
            return;
        }
        mergeBranch.accept(node.fullRef, fetchFirst);
    }

    private void showCreateMenu(Component invoker) {
        JPopupMenu menu = new JPopupMenu();
        JMenuItem newBr = new JMenuItem("New Branch...", Icons.get("plus", AtomTheme.BLUE, 15));
        newBr.addActionListener(e -> newBranch.accept(currentBranch));
        menu.add(newBr);

        JMenuItem newWt = new JMenuItem("New Worktree...", Icons.get("worktree", AtomTheme.GOLD, 15));
        newWt.addActionListener(e -> newWorktree.accept(currentBranch));
        menu.add(newWt);

        menu.show(invoker, 0, invoker.getHeight());
    }

    private void popup(MouseEvent e) {
        if (!e.isPopupTrigger()) return;
        TreePath path = tree.getPathForLocation(e.getX(), e.getY());
        if (path != null) tree.setSelectionPath(path);

        BranchNode node = selectedNode();
        JPopupMenu menu = new JPopupMenu();

        if (node != null && !node.isFolder) {
            String name = node.fullRef;
            JLabel heading = new JLabel("  Branch '" + node.displayName + "'");
            heading.setFont(AtomTheme.UI.deriveFont(Font.BOLD, 12f));
            heading.setForeground(AtomTheme.SECONDARY);
            menu.add(heading);
            menu.addSeparator();

            if (node.isWorktree()) {
                JMenuItem openWt = new JMenuItem("Switch to Worktree in GitPort", Icons.get("worktree", AtomTheme.BLUE, 15));
                openWt.addActionListener(ev -> openRepo.accept(node.worktree.path()));
                menu.add(openWt);

                JMenuItem explore = new JMenuItem("Show in Windows Explorer", Icons.get("folder", AtomTheme.SECONDARY, 15));
                explore.addActionListener(ev -> {
                    try {
                        Desktop.getDesktop().open(node.worktree.path().toFile());
                    } catch (Exception ex) {
                        error.accept("Cannot open explorer: " + ex.getMessage());
                    }
                });
                menu.add(explore);
                menu.addSeparator();
            } else {
                JMenuItem checkout = new JMenuItem("Checkout", Icons.get("check", AtomTheme.BLUE, 15));
                checkout.addActionListener(ev -> checkoutSelected());
                menu.add(checkout);
            }

            JMenuItem newBranchFrom = new JMenuItem("New Branch from '" + node.displayName + "'...", Icons.get("plus", AtomTheme.SECONDARY, 15));
            newBranchFrom.addActionListener(ev -> newBranch.accept(name));
            menu.add(newBranchFrom);

            JMenuItem newWtFrom = new JMenuItem("New Worktree...", Icons.get("worktree", AtomTheme.GOLD, 15));
            newWtFrom.addActionListener(ev -> newWorktree.accept(name));
            menu.add(newWtFrom);

            menu.addSeparator();

            // WebStorm-style Fetch & Merge!
            JMenuItem fetchAndMerge = new JMenuItem("Fetch & Merge into '" + currentBranch + "'", Icons.get("fetch", AtomTheme.BLUE, 15));
            fetchAndMerge.addActionListener(ev -> mergeBranch.accept(name, true));
            menu.add(fetchAndMerge);

            JMenuItem merge = new JMenuItem("Merge into '" + currentBranch + "'", Icons.get("merge", AtomTheme.SECONDARY, 15));
            merge.addActionListener(ev -> mergeBranch.accept(name, false));
            menu.add(merge);

            menu.addSeparator();

            if (node.isWorktree() && !node.worktree.isMain()) {
                JMenuItem removeWt = new JMenuItem("Remove Worktree...", Icons.get("minus", AtomTheme.RED, 15));
                removeWt.addActionListener(ev -> removeWorktree.accept(node.worktree));
                menu.add(removeWt);
            }

            if (!node.isCurrent && !node.isWorktree()) {
                JMenuItem del = new JMenuItem("Delete Branch", Icons.get("close", AtomTheme.RED, 15));
                del.addActionListener(ev -> deleteBranch.accept(name));
                menu.add(del);
            }
        } else {
            JMenuItem newBr = new JMenuItem("New Branch...", Icons.get("plus", AtomTheme.BLUE, 15));
            newBr.addActionListener(ev -> newBranch.accept(currentBranch));
            menu.add(newBr);

            JMenuItem newWt = new JMenuItem("New Worktree...", Icons.get("worktree", AtomTheme.GOLD, 15));
            newWt.addActionListener(ev -> newWorktree.accept(currentBranch));
            menu.add(newWt);

            menu.addSeparator();
            JMenuItem fetchAll = new JMenuItem("Fetch all remotes", Icons.get("fetch", AtomTheme.SECONDARY, 15));
            fetchAll.addActionListener(ev -> fetchRemotes.run());
            menu.add(fetchAll);
        }

        menu.show(tree, e.getX(), e.getY());
    }

    private final class TreeRenderer extends JPanel implements TreeCellRenderer {
        private final JLabel iconLabel = new JLabel();
        private final JLabel nameLabel = new JLabel();
        private final JLabel badgeLabel = new JLabel();

        TreeRenderer() {
            super(new BorderLayout(6, 0));
            setOpaque(true);
            setBorder(new EmptyBorder(1, 4, 1, 6));
            add(iconLabel, BorderLayout.WEST);
            add(nameLabel, BorderLayout.CENTER);
            add(badgeLabel, BorderLayout.EAST);
        }

        public Component getTreeCellRendererComponent(
            JTree tree,
            Object value,
            boolean selected,
            boolean expanded,
            boolean leaf,
            int row,
            boolean hasFocus
        ) {
            Object obj = ((DefaultMutableTreeNode) value).getUserObject();
            if (!(obj instanceof BranchNode node)) {
                nameLabel.setText(value != null ? value.toString() : "");
                iconLabel.setIcon(null);
                badgeLabel.setText("");
                return this;
            }

            setBackground(selected ? AtomTheme.SELECT : AtomTheme.TREE);
            nameLabel.setText(node.displayName);

            if (node.isFolder) {
                iconLabel.setIcon(Icons.get("folder", expanded ? AtomTheme.GOLD : AtomTheme.SECONDARY, 16));
                nameLabel.setFont(AtomTheme.UI.deriveFont(Font.BOLD, 12f));
                nameLabel.setForeground(selected ? AtomTheme.BRIGHT : AtomTheme.SECONDARY);
                badgeLabel.setText("");
                setToolTipText(null);
            } else if (node.isWorktree()) {
                iconLabel.setIcon(Icons.get("worktree", node.isCurrent ? AtomTheme.BLUE : AtomTheme.BLUE, 16));
                nameLabel.setFont(AtomTheme.UI.deriveFont(node.isCurrent ? Font.BOLD : Font.PLAIN, 12f));
                nameLabel.setForeground(selected ? AtomTheme.BRIGHT : node.isCurrent ? AtomTheme.BLUE : AtomTheme.TEXT);
                badgeLabel.setFont(AtomTheme.CODE.deriveFont(Font.BOLD, 10.5f));
                badgeLabel.setForeground(AtomTheme.BLUE);
                badgeLabel.setText(node.isCurrent ? "HEAD [wt]" : (node.worktree != null ? "[wt]" : ""));
                setToolTipText(node.worktree != null ? ("Worktree: " + node.worktree.path() + (node.worktree.isLocked() ? " (Locked)" : "")) : "Worktree branch");
            } else {
                boolean isStar = node.displayName.equals("master") || node.displayName.equals("main");
                iconLabel.setIcon(Icons.get("branch", node.isCurrent ? AtomTheme.BLUE : isStar ? AtomTheme.GOLD : AtomTheme.SECONDARY, 15));
                nameLabel.setFont(AtomTheme.UI.deriveFont(node.isCurrent ? Font.BOLD : Font.PLAIN, 12f));
                nameLabel.setForeground(selected ? AtomTheme.BRIGHT : node.isCurrent ? AtomTheme.BLUE : AtomTheme.TEXT);
                badgeLabel.setFont(AtomTheme.CODE.deriveFont(Font.BOLD, 10.5f));
                badgeLabel.setForeground(isStar ? AtomTheme.GOLD : AtomTheme.BLUE);
                badgeLabel.setText(node.isCurrent ? "HEAD" : (isStar ? "★" : ""));
                setToolTipText("Branch: " + node.fullRef);
            }

            return this;
        }
    }
}
